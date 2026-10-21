using System.Globalization;

namespace ResumeApp.Features.PublicForms;

/// <summary>Strips line breaks and control characters from any value that may end up in an email header (header injection).</summary>
public static class EmailHeaderSanitizer
{
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var chars = value.Select(c => IsUnsafe(c) ? ' ' : c).ToArray();
        return new string(chars).Trim();
    }

    private static bool IsUnsafe(char c)
        => char.IsControl(c)
           || c is >= '\u200B' and <= '\u200F'   // zero-width space/joiners and LRM/RLM
           || c is >= '\u202A' and <= '\u202E'   // bidi embeddings and overrides
           || c is >= '\u2066' and <= '\u2069'   // bidi isolates
           || c == '\uFEFF'                       // zero-width no-break space / BOM
           || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
}
