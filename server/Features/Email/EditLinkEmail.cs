namespace ResumeApp.Features.Email;

/// <summary>The "edit-link" template. A fixed subject (no free text) and the token in the URL fragment (AD-6).</summary>
public static class EditLinkEmail
{
    public const string Subject = "הקישור האישי שלך לעריכת הפרופיל";

    public static string Link(string publicBaseUrl, string token)
        => $"{publicBaseUrl.TrimEnd('/')}/edit#token={token}";

    /// <summary>The cohort name tells apart links for the same address in different cohorts. It is in the body only, never a header.</summary>
    public static string Body(string publicBaseUrl, string token, string cohortName)
        => string.Join("\n",
            "שלום,",
            "",
            $"התקבלה בקשה לקישור אישי לעריכת הפרופיל שלך במחזור: {cohortName}",
            "הקישור תקף ל-24 שעות:",
            "",
            Link(publicBaseUrl, token),
            "",
            "אם לא ביקשת קישור, אפשר להתעלם מהמייל.",
            "אין להעביר את הקישור לאחרים.",
            "");
}
