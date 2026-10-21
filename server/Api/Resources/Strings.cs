namespace ResumeApp.Api.Resources;

/// <summary>Single place for user-facing strings (Hebrew), not inline in code.</summary>
public static class Strings
{
    public const string InvalidCredentialsTitle = "פרטי הכניסה שגויים";
    public const string PasswordChangeRequired = "יש להחליף את הסיסמה לפני המשך";
    public const string CsrfRejected = "הבקשה נדחתה";
    public const string ValidationTitle = "הנתונים אינם תקינים";

    public const string PasswordTooShort = "הסיסמה חייבת להכיל לפחות 12 תווים";
    public const string PasswordCommon = "הסיסמה נפוצה מדי, יש לבחור סיסמה אחרת";
    public const string PasswordContainsUsername = "הסיסמה אינה יכולה להכיל את שם המשתמש";
    public const string PasswordSameAsCurrent = "הסיסמה החדשה חייבת להיות שונה מהנוכחית";
    public const string CurrentPasswordWrong = "הסיסמה הנוכחית שגויה";
    public const string PasswordInvalid = "הסיסמה אינה עומדת בדרישות";
    public const string FieldRequired = "שדה חובה";
}
