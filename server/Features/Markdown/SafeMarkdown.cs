using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using ResumeApp.Features.PublicForms;

namespace ResumeApp.Features.Markdown;

public enum MarkdownOutcome { Ok, TooLong, Invalid }

/// <summary>Text is the cleaned Markdown (null when absent or rejected).</summary>
public sealed record MarkdownResult(MarkdownOutcome Outcome, string? Text);

/// <summary>
/// The single shared cleaner for restricted free-text Markdown (AD-10). Used when saving a CV (story 1.9) and
/// reusable by the admin and public read paths. It parses to an AST (Markdig), strips every HTML tag from the text,
/// allows only paragraphs, headings, lists, emphasis, line breaks and links (code, quotes, rules, tables and images are rejected), rejects any link whose URL is not an absolute http or https URL, and limits length and characters.
/// The result is Markdown (never rendered HTML) and the function is idempotent: cleaning its own output returns it unchanged.
/// </summary>
public static class SafeMarkdown
{
    private const int MaxStripPasses = 5;
    private static readonly TimeSpan Deadline = TimeSpan.FromMilliseconds(250);

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().Build();

    // Linear-time matching: no backtracking, so adversarial input cannot blow up.
    private static readonly Regex HtmlTagPattern = new(
        @"<!--.*?-->|<\?.*?\?>|<!\[CDATA\[.*?\]\]>|<![A-Za-z][^>]*>|</?[A-Za-z][^>]*>",
        RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(250));

    private static readonly HashSet<Type> Allowed =
    [
        typeof(ParagraphBlock), typeof(HeadingBlock), typeof(ListBlock), typeof(ListItemBlock),
        typeof(LinkReferenceDefinitionGroup), typeof(LinkReferenceDefinition),
        typeof(ContainerInline), typeof(LiteralInline), typeof(HtmlEntityInline), typeof(EmphasisInline),
        typeof(LineBreakInline), typeof(LinkInline), typeof(AutolinkInline),
    ];

    public static MarkdownResult Clean(string? input, int maxLength)
    {
        if (string.IsNullOrEmpty(input)) return new(MarkdownOutcome.Ok, null);

        // Checked on the raw input, so nothing unsafe can be hidden by trimming.
        if (input.Any(c => UnsafeCharacters.IsUnsafe(c) && c is not ('\n' or '\r' or '\t'))) return new(MarkdownOutcome.Invalid, null);

        var text = input.Replace("\r\n", "\n").Replace('\r', '\n').Trim(' ', '\n', '\t');
        if (text.Length == 0) return new(MarkdownOutcome.Ok, null);
        if (text.Length > maxLength) return new(MarkdownOutcome.TooLong, null);

        var clock = Stopwatch.StartNew();
        try
        {
            // Stripping can expose new constructs (a tag removed from inside a URL), so repeat until no HTML remains.
            for (var pass = 0; ; pass++)
            {
                var doc = Markdig.Markdown.Parse(text, Pipeline);
                if (clock.Elapsed > Deadline) return new(MarkdownOutcome.Invalid, null);
                var edits = HtmlEdits(doc, text);
                if (edits.Count == 0)
                    return Validate(doc) && clock.Elapsed <= Deadline ? new(MarkdownOutcome.Ok, text) : new(MarkdownOutcome.Invalid, null);
                if (pass >= MaxStripPasses) return new(MarkdownOutcome.Invalid, null);

                var sb = new StringBuilder(text);
                foreach (var (start, end, replacement) in edits.OrderByDescending(e => e.Start))
                    sb.Remove(start, end - start + 1).Insert(start, replacement);
                text = sb.ToString().Trim(' ', '\n', '\t');
                if (text.Length == 0) return new(MarkdownOutcome.Ok, null);
            }
        }
        catch (Exception ex) when (ex is RegexMatchTimeoutException or ArgumentException)
        {
            return new(MarkdownOutcome.Invalid, null);
        }
    }

    private static List<(int Start, int End, string Replacement)> HtmlEdits(MarkdownDocument doc, string text)
    {
        var edits = new List<(int, int, string)>();
        // HtmlInline nodes carry no usable source span, so inline HTML is stripped from the source of the leaf block holding it.
        var leaves = doc.Descendants<HtmlBlock>().Cast<LeafBlock>()
            .Concat(doc.Descendants<LeafBlock>().Where(l => l is not HtmlBlock && l.Inline is { } inl && inl.Descendants<HtmlInline>().Any()));
        foreach (var block in leaves)
        {
            var (s, e) = (block.Span.Start, block.Span.End);
            if (s < 0 || e < s || e >= text.Length) continue;
            var original = text.Substring(s, e - s + 1);
            var stripped = HtmlTagPattern.Replace(original, "");
            // Keep the text between the tags; when nothing can be stripped (an unterminated tag) drop the whole block.
            edits.Add((s, e, stripped == original ? "" : stripped));
        }
        return edits;
    }

    private static bool Validate(MarkdownDocument doc)
    {
        // The allow-list: every other node type (code, quotes, rules, tables, images, raw HTML...) is rejected.
        foreach (var node in doc.Descendants())
            if (!Allowed.Contains(node.GetType())) return false;
        foreach (var link in doc.Descendants<LinkInline>())
        {
            if (link.IsImage) return false;
            if (!IsHttpUrl(link.Url)) return false;
        }
        foreach (var auto in doc.Descendants<AutolinkInline>())
        {
            if (auto.IsEmail || !IsHttpUrl(auto.Url)) return false;
        }
        foreach (var definition in doc.Descendants<LinkReferenceDefinition>())
        {
            if (!IsHttpUrl(definition.Url)) return false;
        }
        return true;
    }

    private static bool IsHttpUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (url.Any(c => char.IsWhiteSpace(c) || UnsafeCharacters.IsUnsafe(c))) return false;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
               && uri.Scheme is "http" or "https"
               && !string.IsNullOrEmpty(uri.Host)
               && string.IsNullOrEmpty(uri.UserInfo)
               && url.StartsWith(uri.Scheme + "://", StringComparison.OrdinalIgnoreCase);
    }
}
