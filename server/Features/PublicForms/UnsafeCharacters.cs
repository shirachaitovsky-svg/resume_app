using System.Globalization;

namespace ResumeApp.Features.PublicForms;

/// <summary>The one character set rejected or stripped from free text: control, line/paragraph separators, bidi controls, zero-width.</summary>
public static class UnsafeCharacters
{
    public static bool IsUnsafe(char c)
        => char.IsControl(c)
           || c is >= '\u200B' and <= '\u200F'   // zero-width space/joiners and LRM/RLM
           || c is >= '\u202A' and <= '\u202E'   // bidi embeddings and overrides
           || c is >= '\u2066' and <= '\u2069'   // bidi isolates
           || c == '\uFEFF'                       // zero-width no-break space / BOM
           || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
}
