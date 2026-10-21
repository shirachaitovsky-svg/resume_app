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
    public const string TotpSetupRequired = "יש להפעיל אימות דו-שלבי לפני המשך";
    public const string TotpCodeInvalid = "הקוד שגוי";
    public const string TotpAlreadyEnabled = "האימות הדו-שלבי כבר מופעל";
    public const string TotpNotStarted = "יש להתחיל את ההרשמה לאימות דו-שלבי";
    public const string FieldRequired = "שדה חובה";
    public const string CohortNameTooLong = "שם המחזור ארוך מדי (עד 100 תווים)";
    public const string CohortAlreadyRevoked = "הקישור כבר בוטל";
    public const string NotFound = "לא נמצא";
    public const string InviteNotFound = "הקישור אינו תקף";
}
