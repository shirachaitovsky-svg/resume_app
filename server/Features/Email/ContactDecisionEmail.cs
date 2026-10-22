using ResumeApp.Domain.Email;
using ResumeApp.Features.Contacts;
using ResumeApp.Features.PublicForms;

namespace ResumeApp.Features.Email;

/// <summary>
/// The "contact-decision-request" template (story 3.3), sent to the participant. A fixed subject, the visitor's cleaned name and reason, and a
/// link with the token in the URL fragment (AD-6). It carries no contact detail of the participant and not the visitor's address.
/// </summary>
public static class ContactDecisionEmail
{
    public const string Subject = "בקשה לקבלת פרטי הקשר שלך";

    public static string Link(string publicBaseUrl, string token)
        => $"{publicBaseUrl.TrimEnd('/')}/contact/decide#token={token}";

    public static string Body(string publicBaseUrl, string token, string visitorName, string reason)
        => string.Join("\n",
            "שלום,",
            "",
            "התקבלה בקשה לקבלת פרטי הקשר שלך (מייל ונייד). הפרטים לא יישלחו בלי הסכמתך.",
            $"שם המבקר: {EmailHeaderSanitizer.Sanitize(visitorName)}",
            "הסיבה לפנייה:",
            ContactRequestService.CleanFreeText(reason),
            "",
            $"כדי לאשר או לסרב, יש ללחוץ על הקישור (תקף ל-{(int)PersonalToken.ContactDecisionLifetime.TotalDays} ימים, ואפשר להחליט פעם אחת):",
            "",
            Link(publicBaseUrl, token),
            "",
            "גם אחרי הסכמתך, הפרטים יישלחו רק לאחר אישור מנהלת הקורס. אם אינך מעוניינת, אפשר לסרב או להתעלם מהמייל.",
            "");
}
