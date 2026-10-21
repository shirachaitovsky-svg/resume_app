---
title: 'סיפור 1.7: הגנה על טפסים ציבוריים'
type: 'feature'
created: '2026-10-21'
status: 'done'
baseline_revision: 'a26b012f6a8417232e43095b67091418de5e5622'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-resume_app-2026-10-21/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-1-6-הגשת-פרופיל-שדות-חובה.md'
warnings: []
deferred:
  - summary: >-
      אכיפת "כל המיילים טקסט פשוט" ושימוש ב-EmailHeaderSanitizer בפועל אפשריים רק כשיש שליחת מיילים
    evidence: |-
      אין עדיין IEmailSender או Outbox; הסניטייזר קיים ונבדק אך אין לו קורא. לאכוף בסיפור 2.1
    location: >-
      server/Features/PublicForms/EmailHeaderSanitizer.cs
    severity: medium
  - summary: >-
      מגבלת המייל נספרת לפני ולידציה, ולכן מי שמכיר קישור הזמנה יכול לנצל 5 הגשות לשעה לכתובת של אחרת; גם plus-addressing עוקף אותה
    evidence: |-
      הסדר נקבע במפרט (ספירה לפני יצירה); לשקול ספירה לפי (IP, מחזור, מייל) או רק אחרי ולידציה
    location: >-
      server/Api/Security/PublicFormGuard.cs
    severity: medium
  - summary: >-
      אימות Turnstile לא בודק hostname ו-action, וקריאת Cloudflare האמיתית לא רצה מול השירות החי
    evidence: |-
      נבדק רק מול handler מדומה; לאמת מול Cloudflare בסביבה אמיתית ולהוסיף בדיקת hostname
    location: >-
      server/Infrastructure/CloudflareTurnstileVerifier.cs
    severity: low
  - summary: >-
      בקשת GET לבדיקת קישור דורשת אסימון Turnstile חד-פעמי, ולכן הלקוח חייב להביא אסימון חדש בכל קריאה
    evidence: |-
      נובע מקריטריוני הקבלה; מתועד ב-README, יש לממש בסיפורי הלקוח
    location: >-
      server/Api/Endpoints/CohortEndpoints.cs
    severity: low---

<intent-contract>

## Intent

**Problem:** הטפסים הציבוריים (הגשת פרופיל, ובהמשך בקשת קישור אישי ובקשת פרטי קשר) פתוחים לבוטים, להצפה ולהזרקת שורות חדשות לכותרות מיילים.

**Approach:** להוסיף ב-`server/` רכיבי הגנה משותפים שנקודות ציבוריות מפעילות: אימות Cloudflare Turnstile שנכשל סגור, הגבלת קצב שהמונים שלה בבסיס הנתונים (לפי IP ולפי כתובת מייל ופרופיל), וניקוי תווי שורה חדשה לפני שדה שנכנס לכותרת מייל. להפעיל אותם על `POST /invites/{token}/profiles` ועל בדיקת הקישור `GET /invites/{token}`. חלק השרת בלבד.

## Boundaries & Constraints

**Always:** אסימון Turnstile מגיע בכותרת `X-Turnstile-Token`; האימות מול `https://challenges.cloudflare.com/turnstile/v0/siteverify` עם `TURNSTILE_SECRET` מסביבה ובקשת HTTP עם timeout קצר. נכשל סגור: אסימון חסר, לא תקין, שגיאת רשת, timeout או תגובה לא צפויה דוחים את הבקשה (403 `problem+json`). `TURNSTILE_SECRET` חובה מחוץ לסביבת Development; ב-Development ברירת המחדל היא מפתח הבדיקה של Cloudflare שתמיד מצליח. המונים בטבלה בבסיס הנתונים (עדכון אטומי `INSERT ... ON CONFLICT DO UPDATE ... RETURNING`), לא בזיכרון, בחלונות קבועים. מפתחות המונים הם hash של ה-IP ושל המייל, לא הערך הגולמי. ה-IP נלקח מ-`HttpContext.Connection.RemoteIpAddress` אחרי `UseForwardedHeaders` בלבד, כלומר רק אחרי שהסוד המשותף אומת. חריגה מהמגבלה: 429 `problem+json` עם `Retry-After`, ובלי לחשוף פרטים. סף ושעון הנתונים ניתנים להגדרה (ברירות מחדל: הגשת פרופיל 20 בקשות לשעה ל-IP ו-5 לשעה ל-(מחזור, מייל); בדיקת קישור 120 לשעה ל-IP). חלונות ישנים נמחקים. הסדר: מגבלת IP, אחר כך Turnstile, אחר כך מגבלת מייל (אחרי קריאת הגוף ולפני יצירה). כל ניסיון נספר גם אם נכשל באימות.

**Never:** אין שליחת מיילים (סיפור 2.1) ואין Outbox. אין שמירת IP או מייל גולמיים בטבלת המונים או ביומן. אין מצב עקיפה שניתן להפעיל בייצור. אין שינוי בנקודות הניהול.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| אסימון תקף | `X-Turnstile-Token` שאומת | הבקשה ממשיכה לטיפול הרגיל | N/A |
| אסימון חסר | בלי כותרת | 403 `problem+json`, לא נוצר דבר | נכשל סגור |
| אסימון נדחה | Cloudflare מחזירה `success:false` | 403 | נכשל סגור |
| שירות לא זמין | timeout, שגיאת רשת או תגובה לא צפויה | 403 | נכשל סגור |
| חריגה לפי IP | מעבר לסף מאותו IP בחלון | 429 עם `Retry-After`; המונה בבסיס הנתונים | N/A |
| חריגה לפי מייל | אותו (מחזור, מייל) מעבר לסף | 429 | N/A |
| כתובת אחרת | IP או מייל אחר | לא מושפע מהחריגה | N/A |
| שרת הופעל מחדש | הספירה נשמרת | המונים נשמרים בבסיס הנתונים | N/A |
| מקביליות | חמש בקשות מקבילות | המונה מדויק, בלי הפסד עדכונים | עדכון אטומי |
| ניקוי כותרת | שדה עם `\r`, `\n` או תו בקרה | הם מוסרים או מוחלפים ברווח | N/A |
| IP מזויף | `X-Forwarded-For` בבקשה בלי הסוד | נדחית כבר ב-403 של הסוד, ולא נספרת ל-IP | N/A |

</intent-contract>

## Code Map

- `server/Api/Endpoints/ProfileEndpoints.cs`, `CohortEndpoints.cs` -- נקודות ציבוריות קיימות (`POST /invites/{token}/profiles`, `GET /invites/{token}`) שמקבלות את ההגנות; הטיפול בגוף ובאסימון קיים כבר שם (404 אחיד לפני קריאת הגוף).
- `server/Api/Program.cs` -- סדר ה-middleware (סוד מקור, `UseForwardedHeaders`, אימות, `RequestIntegrityMiddleware`) ורישום שירותים; כאן רישום `HttpClient` ל-Turnstile.
- `server/Api/Security/RequestIntegrityMiddleware.cs` -- תבנית middleware קיימת (anti-forgery ו-Origin); ההגנות החדשות הן filter או שירות לנקודה, לא שינוי בו.
- `server/Features/` -- מקום לממשקים (`ITurnstileVerifier`, `IRateLimiter`) ולכללי המדיניות; `server/Infrastructure/` למימושים (HTTP, EF/SQL גולמי) ולמיגרציה.
- `server/Infrastructure/ProfileRepository.cs` -- דוגמה ל-`INSERT ... ON CONFLICT` גולמי ולמניעת PII ביומן (ראו הגדרות Serilog ב-`appsettings.json`).
- `server/Tests/AuthTestSupport.cs`, `ProfileSubmissionTests.cs` -- `AuthApiFactory`, `ApiClient`; להוסיף דרך להחליף `ITurnstileVerifier` בבדיקות. לשים לב: בדיקות קיימות של ההגשה והקישור חייבות להמשיך לעבור (המפעל מספק אסימון תקף וסף גבוה).
- אין לשנות: `client/`, `vercel.json`, `middleware.js`.

## Tasks & Acceptance

**Execution:**
- `server/Features/` ו-`server/Infrastructure/` -- `ITurnstileVerifier` והמימוש מול Cloudflare (timeout, נכשל סגור) -- AR15, AD-12
- `server/Features/` ו-`server/Infrastructure/` -- `IRateLimiter` עם מונים בבסיס הנתונים, טבלה, מיגרציה וניקוי חלונות ישנים -- AD-12
- `server/Api/` -- filter או רכיב משותף שמפעיל את ההגנות בסדר הנדרש על שתי הנקודות הציבוריות, ומחזיר 403/429 כ-`problem+json` -- FR-2
- `server/Features/` -- `EmailHeaderSanitizer` (הסרת `\r`, `\n` ותווי בקרה) -- AD-12
- `server/Tests/` -- בדיקות אינטגרציה ויחידה לשורות המטריצה, כולל התמדה בהפעלה מחדש ומקביליות -- AR21
- `README.md` -- `TURNSTILE_SECRET`, מפתחות הבדיקה, ספי המגבלות והשיטה -- תפעול

**Acceptance Criteria:**
- Given נקודה ציבורית, when נשלחת בלי אסימון Turnstile תקף (גם כשהשירות לא זמין), then הבקשה נדחית ולא נוצר דבר.
- Given בקשות מאותו IP או לאותה כתובת מעבר לסף, when מגיעה בקשה נוספת, then 429 והמונה שמור בבסיס הנתונים, והמפתח אינו ה-IP או המייל הגולמיים.
- Given שדה טקסט חופשי שייכנס לכותרת מייל, when מנקים אותו, then תווי שורה חדשה אינם נשארים.

## Spec Change Log

## Review Triage Log

### 2026-10-21 — Review pass
- verdicts: כ-45 ממצאים מארבע שכבות (רבים כפולים) — high 0, medium 6, low 6, false 1
- findings:
  - `[medium]` `[patch]` README ההרצה המקומית נשבר מחוץ ל-Development — עודכן
  - `[medium]` `[patch]` תגובות 403/429 ללא קוד קריא למכונה; כשל מסד הנתונים של מגביל הקצב ללא מדיניות; IP חסר חולק מכל; IPv6 לפי /128 — נוספו קודים, כשל סגור 503, כשל סגור ל-IP חסר ומפתח /64
  - `[medium]` `[patch]` כשל Turnstile שקט — נרשם אזהרה לפי קטגוריה, בלי אסימון
  - `[medium]` `[patch]` חלון שאינו מחלק של 1440 גורם לסחיפה; ניקוי כושל הופך בקשה ל-500 — נוסף אימות ו-try/catch
  - `[medium]` `[patch]` בדיקות חסרות: ערכי הגדרה לא תקינים ו-DI אמיתי של Turnstile; נרמול מייל כפול — נוספו ושותף
  - `[low]` `[patch]` סניטייזר בלי bidi ו-zero-width, README פגום בשורת הסניטייזר ו-line endings — תוקנו
  - `[medium]` `[defer]` מיילים טקסט פשוט, נעילת מייל של קורבן, hostname ב-Turnstile, GET עם אסימון חד-פעמי — נרשמו ב-deferred
  - `[low]` `[reject]` שימוש כפול בפתרון מחזור, using יתר, idempotency key, הערות על בדיקות קוסמטיות, סף עליון נוסף — לא סביר שייפגעו בשימוש רגיל
  - `[false]` `[reject]` Designer ו-snapshot חסרים — הקבצים קיימים והוחרגו מה-diff
## Verification

**Commands:**
- `dotnet build server` -- expected: 0 שגיאות
- `dotnet test server` -- expected: כל הבדיקות עוברות (דורש Docker)

## Auto Run Result

- **שינוי:** הגנה משותפת על הנקודות הציבוריות `POST /invites/{token}/profiles` ו-`GET /invites/{token}`: אימות Cloudflare Turnstile שנכשל סגור (כותרת `X-Turnstile-Token`), הגבלת קצב בבסיס הנתונים לפי IP (IPv6 לפי /64) ולפי (מחזור, מייל) עם מפתחות HMAC, תגובות 403/429/503 עם `code` קריא למכונה, ו-`EmailHeaderSanitizer` (כולל bidi ו-zero-width).
- **קבצים:** `server/Features/PublicForms/*`, `server/Infrastructure/CloudflareTurnstileVerifier.cs`, `RateLimiter.cs`, מיגרציית `RateLimits`, `server/Api/Security/PublicFormGuard.cs`, `ProfileEndpoints.cs`, `CohortEndpoints.cs`, `Program.cs`, `server/Tests/PublicFormProtectionTests.cs`, `AuthTestSupport.cs`, `README.md`, `render.yaml`.
- **סקירה:** 6 תיקוני medium ו-2 low הוחלו (קודי שגיאה, כשל סגור של המגביל וה-IP החסר, /64 ל-IPv6, לוג כשל Turnstile, אימות חלון המגבלה, בדיקות חסרות, README). 4 פריטים נדחו ל-`deferred` (מיילים טקסט פשוט עד סיפור 2.1, נעילת מייל של קורבן, hostname ב-Turnstile, GET עם אסימון חד-פעמי). השאר נדחו כמתועד ביומן.
- **המלצה לסקירת המשך:** כן. הסיכון שלא אומת: קריאת Cloudflare האמיתית לא הורצה מול השירות החי, ואכיפת "מיילים טקסט פשוט" לא ניתנת לבדיקה עד שיש שליחת מיילים.
- **אימות:** `dotnet build server` ללא שגיאות, `dotnet test server`: 179 מתוך 179 עוברות מול Postgres אמיתי.
- **סיכונים שנותרו:** בייצור השרת לא יעלה בלי `TURNSTILE_SECRET`; נעילת מייל של קורבן על ידי מי שמחזיק קישור הזמנה; ראו `deferred`.
