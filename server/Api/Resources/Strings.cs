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
    public const string CohortFilterInvalid = "מזהה המחזור אינו תקין";
    public const string PagingInvalid = "פרמטרי העימוד אינם תקינים";
    public const string RevisionNotPending = "ההגשה כבר אינה ממתינה";
    public const string ReturnNoteTooLong = "ההערה ארוכה מדי (עד 1000 תווים)";
    public const string Forbidden = "אין הרשאה לפעולה";
    public const string NoContactChange = "אין שינוי מייל או נייד ממתין";
    public const string ContactChangeStale = "השינוי הממתין הוחלף, יש לרענן";
    public const string RevisionNotEligible = "ההגשה הוחלפה או הוחזרה";
    public const string EmailTaken = "כתובת המייל כבר בשימוש במחזור";
    public const string ApproveInconsistent = "לא ניתן לאשר: נתוני הפרופיל אינם עקביים";
    public const string InviteNotFound = "הקישור אינו תקף";
    public const string NameTooLong = "השם ארוך מדי (עד 100 תווים)";
    public const string ProductNameTooLong = "שם המוצר ארוך מדי (עד 100 תווים)";
    public const string ProductUrlTooLong = "הקישור ארוך מדי (עד 2048 תווים)";
    public const string ProductUrlInvalid = "יש להזין קישור מלא שמתחיל ב-http או https";
    public const string EmailTooLong = "כתובת המייל ארוכה מדי (עד 254 תווים)";
    public const string EmailInvalid = "כתובת המייל אינה תקינה";
    public const string PhoneTooLong = "מספר הנייד ארוך מדי";
    public const string FieldInvalid = "הערך אינו תקין";
    public const string BodyMalformed = "גוף הבקשה אינו תקין";
    public const string BodyTooLarge = "גוף הבקשה גדול מדי";
    public const string UnsupportedMediaType = "יש לשלוח JSON";
    public const string BotCheckFailed = "אימות האבטחה נכשל, יש לנסות שוב";
    public const string RateLimitUnavailable = "השירות אינו זמין כרגע, יש לנסות שוב מאוחר יותר";
    public const string TooManyRequests = "בוצעו יותר מדי ניסיונות, יש לנסות שוב מאוחר יותר";
    public const string DescriptionTooLong = "התיאור ארוך מדי (עד 120 תווים)";
    public const string RoleTooLong = "התפקיד ארוך מדי (עד 100 תווים)";
    public const string WorkplaceTooLong = "מקום העבודה ארוך מדי (עד 100 תווים)";
    public const string IntroTooLong = "הטקסט ארוך מדי (עד 1000 תווים)";
    public const string CvTooLong = "קורות החיים ארוכים מדי (עד 20,000 תווים)";
    public const string CvInvalid = "קורות החיים מכילים תוכן שאינו נתמך: מותרים רק קישורים מוחלטים ב-http או https; תמונות, קוד וציטוטים אינם נתמכים, וגם תווים נסתרים אסורים";
    public const string AvatarInvalid = "הדמות שנבחרה אינה קיימת";
    public const string PhotoInvalid = "הקובץ אינו תמונה תקינה (JPEG, PNG או WebP)";
    public const string PhotoTooLarge = "התמונה גדולה מדי (עד 300KB)";
    public const string LinkInvalid = "הקישור אינו תקף או שכבר נעשה בו שימוש";
    public const string SessionInvalid = "ההתחברות פגה, יש להיכנס שוב דרך הקישור האישי";
    public const string PhoneInvalid = "מספר הנייד אינו תקין (9 עד 15 ספרות)";
}
