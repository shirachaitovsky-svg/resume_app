using ResumeApp.Features.PublicForms;

namespace ResumeApp.Features.Email;

/// <summary>
/// The "contact-delivery" template (story 3.4), sent only to the visitor's verified address. Plain text: the participant's display name,
/// email and phone, read at send time. Nothing else about the request.
/// </summary>
public static class ContactDeliveryEmail
{
    public const string Subject = "פרטי הקשר שביקשת";

    public static string Body(string participantName, string email, string phone)
    {
        var name = EmailHeaderSanitizer.Sanitize(participantName).Trim();
        return string.Join("\n",
            "שלום,",
            "",
            $"המשתתפת {(name.Length == 0 ? ContactRequestNotificationEmail.Placeholder : name)} אישרה למסור לך את פרטי הקשר שלה:",
            "",
            $"מייל: {EmailHeaderSanitizer.Sanitize(email)}",
            $"נייד: {EmailHeaderSanitizer.Sanitize(phone)}",
            "",
            "הפרטים נמסרים פעם אחת בלבד, לכתובת שאימתת.",
            "");
    }
}
