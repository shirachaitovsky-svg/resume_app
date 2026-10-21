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

    private static bool IsUnsafe(char c) => UnsafeCharacters.IsUnsafe(c);
}
