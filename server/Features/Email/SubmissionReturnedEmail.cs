using ResumeApp.Domain.Profiles;
using ResumeApp.Features.PublicForms;

namespace ResumeApp.Features.Email;

/// <summary>The "submission-returned" template (story 2.4): fixed subject, the admin's note in the body, and a plain link to the site (story 6.4: no token; she signs in with Google to fix and resubmit).</summary>
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

    /// <summary>The page where the participant, once signed in with Google, edits and resubmits her profile.</summary>
    public static string Link(string publicBaseUrl) => $"{publicBaseUrl.TrimEnd('/')}/me";

    public static string Body(string publicBaseUrl, string cohortName, string? note)
        => string.Join("\n",
            "שלום,",
            "",
            $"הפרופיל שלך במחזור: {cohortName} הוחזר לתיקון.",
            "הערת המנהלת:",
            "",
            CleanNote(note),
            "",
            "אפשר לתקן ולהגיש מחדש באתר, אחרי כניסה עם חשבון הגוגל שלך:",
            "",
            Link(publicBaseUrl),
            "");
}
