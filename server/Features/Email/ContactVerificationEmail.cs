using ResumeApp.Domain.Email;
using ResumeApp.Features.PublicForms;

namespace ResumeApp.Features.Email;

/// <summary>
/// The "contact-verification" template (story 3.1), sent to the visitor. A fixed subject, the visitor's own (cleaned) name, and a link with
/// the token in the URL fragment (AD-6). It never names the participant and carries no contact detail or request text.
/// </summary>
public static class ContactVerificationEmail
{
    public const string Subject = "אימות כתובת המייל לבקשת יצירת קשר";

    public static string Link(string publicBaseUrl, string token)
        => $"{publicBaseUrl.TrimEnd('/')}/contact/verify#token={token}";

    public static string Body(string publicBaseUrl, string token, string visitorName)
        => string.Join("\n",
            $"שלום {EmailHeaderSanitizer.Sanitize(visitorName)},",
            "",
            "התקבלה בקשה ליצירת קשר שנשלחה מכתובת המייל הזו.",
            $"כדי לאשר את הכתובת ולהעביר את הבקשה להמשך טיפול, יש ללחוץ על הקישור (תקף ל-{(int)PersonalToken.VerifyContactEmailLifetime.TotalHours} שעות):",
            "",
            Link(publicBaseUrl, token),
            "",
            "אם לא שלחת בקשה, אפשר להתעלם מהמייל.",
            "");
}
