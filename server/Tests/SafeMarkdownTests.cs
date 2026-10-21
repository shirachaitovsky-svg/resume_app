using ResumeApp.Features.Markdown;

namespace ResumeApp.Tests;

public class SafeMarkdownTests
{
    private const int Max = ResumeApp.Domain.Profiles.ProfileRevision.CvMaxLength;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n ")]
    public void Blank_is_absent(string? input)
    {
        var r = SafeMarkdown.Clean(input, Max);
        Assert.Equal(MarkdownOutcome.Ok, r.Outcome);
        Assert.Null(r.Text);
    }

    [Fact]
    public void Valid_markdown_is_kept_with_normalised_line_breaks()
    {
        var md = "# כותרת\r\n\r\n- **מודגש** ו-*נטוי*\r\n- [אתר](https://a.co) ו-[אחר](http://a.co/x)\r\n\r\n1. אחד\r\n2. שניים\r\n\r\n<https://a.co>";
        var r = SafeMarkdown.Clean(md, Max);
        Assert.Equal(MarkdownOutcome.Ok, r.Outcome);
        Assert.Equal(md.Replace("\r\n", "\n"), r.Text);
    }

    [Theory]
    [InlineData("<b>x</b> שלום", "x שלום")]
    [InlineData("שלום <img src=x onerror=alert(1)> עולם", "שלום  עולם")]
    [InlineData("שלום <!-- הערה --> עולם", "שלום  עולם")]
    public void Inline_html_tags_are_removed(string input, string expected)
    {
        var r = SafeMarkdown.Clean(input, Max);
        Assert.Equal(MarkdownOutcome.Ok, r.Outcome);
        Assert.Equal(expected, r.Text);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>\n\nפסקה")]
    [InlineData("<iframe src=\"https://evil\"></iframe>\n\nפסקה")]
    [InlineData("<div onclick=\"x()\">\nטקסט\n</div>\n\nפסקה")]
    [InlineData("<scr<script>ipt>alert(1)</script>\n\nפסקה")]
    [InlineData("<script\n\nפסקה")]
    [InlineData("- <img src=x onerror=alert(1)>\n\nפסקה")]
    public void Html_blocks_and_xss_vectors_leave_no_tags(string input)
    {
        var r = SafeMarkdown.Clean(input, Max);
        Assert.Equal(MarkdownOutcome.Ok, r.Outcome);
        Assert.DoesNotContain("<", r.Text ?? "");
        // An unterminated <script swallows the rest of the document as one HTML block, which is dropped whole.
        if (!input.StartsWith("<script\n")) Assert.Contains("פסקה", r.Text);
    }

    [Theory]
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("[x](JaVaScRiPt:alert(1))")]
    [InlineData("[x](java<b></b>script:alert(1))")]
    [InlineData("[x](&#106;avascript:alert(1))")]
    [InlineData("[x](data:text/html;base64,AAAA)")]
    [InlineData("[x](vbscript:msgbox(1))")]
    [InlineData("[x](mailto:a@b.co)")]
    [InlineData("[x](//evil.com)")]
    [InlineData("[x](/relative)")]
    [InlineData("[x](evil.com)")]
    [InlineData("[x]()")]
    [InlineData("<data:text/html,hi>")]
    [InlineData("<javascript:alert(1)>")]
    [InlineData("<a@b.co>")]
    [InlineData("[x][r]\n\n[r]: javascript:alert(1)")]
    [InlineData("[r]: javascript:alert(1)")]
    [InlineData("![x](https://a.co/i.png)")]
    [InlineData("![x][r]\n\n[r]: https://a.co/i.png")]
    public void Bad_links_and_images_are_rejected(string input)
    {
        var r = SafeMarkdown.Clean(input, Max);
        Assert.Equal(MarkdownOutcome.Invalid, r.Outcome);
        Assert.Null(r.Text);
    }

    [Fact]
    public void Length_boundary()
    {
        Assert.Equal(MarkdownOutcome.Ok, SafeMarkdown.Clean(new string('א', 20_000), Max).Outcome);
        Assert.Equal(MarkdownOutcome.TooLong, SafeMarkdown.Clean(new string('א', 20_001), Max).Outcome);
    }

    [Theory]
    [InlineData("a\0b")]
    [InlineData("a\u202Eb")]
    [InlineData("a\u200Bb")]
    [InlineData("a\u2028b")]
    [InlineData("a\u0007b")]
    public void Unsafe_characters_are_rejected(string input)
        => Assert.Equal(MarkdownOutcome.Invalid, SafeMarkdown.Clean(input, Max).Outcome);

    [Theory]
    [InlineData("# כותרת\n\n- [a](https://a.co) **b**")]
    [InlineData("<b>x</b> <script>alert(1)</script>\n\nפסקה")]
    [InlineData("<scr<script>ipt>alert(1)</script>\n\nפסקה")]
    [InlineData("- <img src=x>\n\n<div>\ny\n</div>")]
    [InlineData("a <i>b</i> [c](https://a.co) <!-- d -->")]
    public void Cleaning_is_idempotent(string input)
    {
        var once = SafeMarkdown.Clean(input, Max);
        Assert.Equal(MarkdownOutcome.Ok, once.Outcome);
        var twice = SafeMarkdown.Clean(once.Text, Max);
        Assert.Equal(MarkdownOutcome.Ok, twice.Outcome);
        Assert.Equal(once.Text, twice.Text);
    }

    [Theory]
    [InlineData("`code`")]
    [InlineData("a `<script>alert(1)</script>` b")]
    [InlineData("```\n<script>alert(1)</script>\n```")]
    [InlineData("~~~\ncode\n~~~")]
    [InlineData("x\n\n    indented code")]
    [InlineData("> quote")]
    [InlineData("---")]
    [InlineData("* * *")]
    public void Unsupported_constructs_are_rejected(string input)
    {
        var r = SafeMarkdown.Clean(input, Max);
        Assert.Equal(MarkdownOutcome.Invalid, r.Outcome);
        Assert.Null(r.Text);
    }

    [Fact]
    public void Escaped_html_entities_are_plain_text_and_kept()
    {
        var r = SafeMarkdown.Clean("&lt;script&gt;alert(1)&lt;/script&gt;", Max);
        Assert.Equal(MarkdownOutcome.Ok, r.Outcome);
        Assert.Equal("&lt;script&gt;alert(1)&lt;/script&gt;", r.Text);
    }

    [Fact]
    public void Script_tags_are_stripped_and_inner_text_is_plain_text()
    {
        var r = SafeMarkdown.Clean("a <script>alert(1)</script> b", Max);
        Assert.Equal("a alert(1) b", r.Text);
    }

    [Fact]
    public void A_single_tag_alone_leaves_no_text()
    {
        var r = SafeMarkdown.Clean("<b></b>", Max);
        Assert.Equal(MarkdownOutcome.Ok, r.Outcome);
        Assert.Null(r.Text);
    }

    [Theory]
    [InlineData("[x](https://user:pass@a.co/)")]
    [InlineData("[x](https://user@a.co/)")]
    [InlineData("<https://user:pass@a.co/>")]
    public void Links_with_credentials_are_rejected(string input)
        => Assert.Equal(MarkdownOutcome.Invalid, SafeMarkdown.Clean(input, Max).Outcome);

    [Theory]
    [InlineData("\u2028a")]
    [InlineData("a\u2029")]
    [InlineData("\u0085a")]
    [InlineData("\va")]
    [InlineData("a\f")]
    [InlineData("\u001Fa")]
    [InlineData("\u202E")]
    public void Unsafe_characters_at_the_edges_are_rejected_not_trimmed(string input)
        => Assert.Equal(MarkdownOutcome.Invalid, SafeMarkdown.Clean(input, Max).Outcome);

    public static TheoryData<string> Adversarial() => new()
    {
        string.Concat(Enumerable.Repeat("<!--", 5000)),
        string.Concat(Enumerable.Repeat("<a ", 6666)),
        string.Concat(Enumerable.Repeat("- ", 10_000)),
        string.Concat(Enumerable.Repeat("  - ", 5000)),
        string.Concat(Enumerable.Repeat("*", 20_000)),
        string.Concat(Enumerable.Repeat("*a _b ", 3333)),
        string.Concat(Enumerable.Repeat("[", 20_000)),
        string.Concat(Enumerable.Repeat("[a](", 5000)),
        string.Concat(Enumerable.Repeat("<!-- <b> ", 2222)),
    };

    [Theory]
    [MemberData(nameof(Adversarial))]
    public void Adversarial_input_finishes_quickly(string input)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var r = SafeMarkdown.Clean(input.Length > Max ? input[..Max] : input, Max);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"took {clock.Elapsed}");
        Assert.True(r.Outcome is MarkdownOutcome.Ok or MarkdownOutcome.Invalid);
    }
}
