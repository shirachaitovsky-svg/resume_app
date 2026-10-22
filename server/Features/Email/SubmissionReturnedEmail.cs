using ResumeApp.Domain.Email;
using ResumeApp.Domain.Profiles;
using ResumeApp.Features.PublicForms;

namespace ResumeApp.Features.Email;

/// <summary>The "submission-returned" template (story 2.4): fixed subject, the admin's note in the body, a fresh personal link in the URL fragment.</summary>
public static class SubmissionReturnedEmail
{
    public const string Subject = "הפרופיל שלך הוחזר לתיקון";

    /// <summary>
    /// Body-only cleaning of the note, independent of what was validated on entry. The mail is plain text (not an HTML surface), so the note
    /// is sent as written: only unsafe and invisible characters are removed, line breaks are normalised to LF (allowed in a body, never in a header),
    /// and the length is capped at the same maximum as entry validation.
    /// </summary>
    public static string CleanNote(string? note)
    {
        if (string.IsNullOrEmpty(note)) return string.Empty;
        var text = note.Replace("\r\n", "\n").Replace('\r', '\n');
        var kept = text.Where(c => c is '\n' or '\t' || !UnsafeCharacters.IsUnsafe(c));
        var cleaned = new string(kept.ToArray()).Trim();
        return cleaned.Length > ProfileRevision.ReturnNoteMaxLength ? cleaned[..ProfileRevision.ReturnNoteMaxLength] : cleaned;
    }

    public static string Body(string publicBaseUrl, string token, string cohortName, string? note)
        => string.Join("\n",
            "שלום,",
            "",
            $"הפרופיל שלך במחזור: {cohortName} הוחזר לתיקון.",
            "הערת המנהלת:",
            "",
            CleanNote(note),
            "",
            $"אפשר לתקן ולהגיש מחדש דרך הקישור האישי (תקף ל-{(int)PersonalToken.EditProfileLifetime.TotalHours} שעות):",
            "",
            EditLinkEmail.Link(publicBaseUrl, token),
            "",
            "אין להעביר את הקישור לאחרים.",
            "");
}
