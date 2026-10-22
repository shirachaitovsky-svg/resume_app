using System.Globalization;
using ResumeApp.Features.PublicForms;

namespace ResumeApp.Features.Email;

/// <summary>
/// The "contact-request-notification" template (story 3.2), sent to the manager. A fixed subject; the body names the verification time, the
/// cohort and the requested profile and links to the queue. It carries no visitor name, address or reason and no participant contact detail.
/// </summary>
public static class ContactRequestNotificationEmail
{
    public const string Subject = "התקבלה בקשת פרטי קשר חדשה";

    public const string Placeholder = "(ללא שם)";

    /// <summary>A cleaned name, or a placeholder when it is empty (never a blank line).</summary>
    private static string Shown(string? name)
    {
        var clean = EmailHeaderSanitizer.Sanitize(name ?? string.Empty).Trim();
        return clean.Length == 0 ? Placeholder : clean;
    }

    public static string QueueLink(string publicBaseUrl) => $"{publicBaseUrl.TrimEnd('/')}/admin/contact-requests";

    public static string Body(string publicBaseUrl, ContactNotificationInfo info)
        => string.Join("\n",
            "התקבלה בקשת פרטי קשר חדשה שאומתה.",
            "",
            $"זמן האימות: {info.VerifiedAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} (UTC)",
            $"מחזור: {Shown(info.CohortName)}",
            $"פרופיל מבוקש: {Shown(info.ProfileName)}",
            "",
            "לצפייה בבקשה ובתור הבקשות:",
            QueueLink(publicBaseUrl),
            "");
}
