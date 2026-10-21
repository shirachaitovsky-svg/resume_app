---
title: 'סיפור 2.1: קישור אישי במייל'
type: 'feature'
created: '2026-10-21'
status: 'done'
baseline_revision: 'ab62b7ecefb908b1f7a20bd33ea70bff9970e411'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-resume_app-2026-10-21/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-1-7-הגנה-על-טפסים-ציבוריים.md'
warnings: ['oversized']
deferred:
  - summary: >-
      אין ניקוי (retention) לשורות `email_outbox` ו-`personal_tokens`, ואין פעולת ניהול לשליחה חוזרת או ביטול של שורת `dead`
    evidence: |-
      הטבלאות ימשיכו לגדול; מחיקת פרופיל בפועל תיכשל בגלל מפתח זר Restrict. לטפל במדיניות שמירה בסיפורים 4.2 ו-4.3
    location: >-
      server/Infrastructure/EmailOutboxRepository.cs
    severity: medium
  - summary: >-
      שליחה אמיתית מול Mailpit (קונטיינר) ומול Resend החי לא נבדקה; SMTP נבדק מול שרת בבדיקה בלבד
    evidence: |-
      בדיקה ידנית ב-`docker compose -f docker-compose.dev.yml up -d mailpit`; Resend נבדק מול stub
    location: >-
      server/Infrastructure/SmtpEmailSender.cs
    severity: medium
  - summary: >-
      תזמון התגובה מגלה אם הכתובת רשומה (הכנסת שורה), ומי שמכיר כתובת יכול לנצל את מכסת הבקשות שלה
    evidence: |-
      התגובה אחידה בגוף וכותרות; שיוויון תזמון ומכסה עצמאית לפי IP בסיפור 4.1
    location: >-
      server/Api/Endpoints/EditLinkEndpoints.cs
    severity: low
  - summary: >-
      מסך "עריכת הפרופיל שלי" (הזנת מייל והודעה אחידה), ועמוד `/edit` שאליו מובילים הקישורים, טרם נבנו בלקוח
    evidence: |-
      המייל מכיל קישור `/edit#token=...` לנתיב שעדיין אין; סיפורים 2.2 ו-5.8
    location: >-
      client/ (טרם קיים)
    severity: medium---

<intent-contract>

## Intent

**Problem:** משתתפת לא יכולה לחזור לפרופיל שלה כי אין סיסמה, ואין עדיין שום תשתית לשליחת מיילים אמינה (שתשמש גם את אפיקים 2 ו-3).

**Approach:** להוסיף ב-`server/` את `POST /profiles/edit-link` (ציבורי, מוגן) שמחזיר תמיד תגובה אחידה, כותב שורת `EmailOutbox` ללא סודות, ותהליך רקע שתופס שורות עם claim ו-lease, מנפיק בזמן השליחה אסימון קישור אישי אקראי (נשמר כ-hash) ושולח מייל טקסט פשוט דרך `IEmailSender`. בפיתוח המיילים נקלטים בתפסן מקומי. חלק השרת בלבד; פתיחת הקישור והעריכה הם סיפור 2.2.

## Boundaries & Constraints

**Always:** נקודה: `POST /profiles/edit-link` עם גוף `{ "email": "..." }`, `[AllowAnonymous]`, מוגנת ב-anti-forgery, `Origin` ובהגנות מסיפור 1.7 (הגבלת IP, Turnstile, ומגבלת קצב לפי כתובת מייל מנורמלת באותה מדיניות עם סף ברירת מחדל 5 לשעה, ניתן להגדרה ב-`RATE_LIMIT_EDIT_LINK_EMAIL`, ו-`RATE_LIMIT_EDIT_LINK_IP` ברירת מחדל 20). התגובה זהה (202 עם `{ "status": "ok" }`) בין אם המייל קיים ובין אם לא, ובין אם נכתבה שורת Outbox ובין אם לא, גם בגוף, כותרות ותזמון בקירוב (אין עבודה כבדה בנתיב). מייל לא תקין: 400 ולידציה (כבסיפור 1.6) ולא נחשף קיום. נבחרים פרופילים של אותו מייל מנורמלי (`ParticipantPrivate`) רק פרופילים ב-`Visibility = active` (גם אם עדיין אין להם גרסה מאושרת); פרופיל שהוסר לא נבחר. לכל פרופיל תואם נכתבת שורת `EmailOutbox` (`TemplateKey = "edit-link"`, מזהה הפרופיל, `DeduplicationKey = "edit-link:{profileId}:{yyyyMMddHH}"` ייחודי, כך שבקשות חוזרות באותה שעה לא יוצרות כפילות). בשורה: `TemplateKey`, מזהי ישויות, `DeduplicationKey`, סטטוס, ניסיונות, `NextAttemptAt`, `LeaseUntil`, קטגוריית שגיאה אחרונה, זמני יצירה ושליחה; **לא** אסימון גולמי, מייל, נייד או תוכן מייל. תהליך רקע (`BackgroundService`) תופס שורות `pending` שהגיע זמנן עם `FOR UPDATE SKIP LOCKED`, קובע lease (ברירת מחדל 2 דקות) ומבצע את השליחה מחוץ לטרנזקציית התפיסה: קורא את המייל מ-`ParticipantPrivate`, מנפיק אסימון קישור אישי, בונה את ההודעה, שולח דרך `IEmailSender`, ורק אחר כך מסמן `sent`. שורה שהמופע שתפס אותה קרס חוזרת לתפיסה כשה-lease פג. כישלון זמני (שגיאת רשת, 5xx, או 429 מהספק) מתזמן ניסיון נוסף עם backoff אקספוננציאלי וקבוע מקסימלי; 429 אינו נספר במספר הניסיונות הכושלים. אחרי 5 ניסיונות כושלים: `dead`. שורות `dead` ומספרי השורות לפי סטטוס גלויים למנהלת ב-`GET /admin/email-outbox` (`AdminPolicy`; סטטוסים וספירות, ורשימת `dead` עם `templateKey`, ניסיונות, קטגוריית שגיאה, זמן יצירה; בלי כתובת, טקסט או אסימון). אסימון קישור אישי (`PersonalToken`): 32 בתים אקראיים מקריפטוגרפיה (256 ביט) בקידוד base64url; נשמר רק כ-hash (SHA-256) יחד עם `Purpose = "edit-profile"`, `ProfileId`, `ExpiresAt` (24 שעות), `UsedAt`, `RevokedAt`, זמני יצירה; הנפקת אסימון חדש לאותו פרופיל ומטרה מבטלת (`RevokedAt`) את הקודמים שטרם נוצלו, באותה טרנזקציה. אסימון אינו נרשם ביומן ואינו נשמר בשום מקום מלבד ה-hash ובגוף המייל שנשלח. המייל: טקסט פשוט בלבד (`text/plain; charset=utf-8`, בלי HTML), עברית, נושא קבוע מהתבנית (בלי טקסט חופשי), והקישור בצורה `{PUBLIC_BASE_URL}/edit#token={token}` (האסימון בפרגמנט `#`, לא בנתיב או בפרמטר). כל שדה משתנה שנכנס לכותרת עובר `EmailHeaderSanitizer`. `IEmailSender` (ב-`Features`) הוא נקודת ההחלפה היחידה של ספק המיילים; מימושים ב-`Infrastructure`: `SmtpEmailSender` (MailKit, MIT) שנבחר ב-`EMAIL_PROVIDER=smtp` עם `SMTP_HOST`, `SMTP_PORT`, `SMTP_USER`, `SMTP_PASSWORD`, `SMTP_SECURE`, `EMAIL_FROM`; ו-`ResendEmailSender` (HTTP, `EMAIL_PROVIDER=resend`, `RESEND_API_KEY`, `EMAIL_FROM`) שמטפל ב-429 וב-5xx ככישלון זמני. בסביבת Development ברירת המחדל היא SMTP אל `localhost:1025` (תפסן מקומי Mailpit, ראו `docker-compose.dev.yml` חדש ו-README); מחוץ ל-Development, ספק חייב להיות מוגדר ואחרת האתחול נכשל בשגיאה ברורה. שליחה אידמפוטנטית: ל-`IEmailSender` מועבר מפתח אידמפוטנטיות (מזהה השורה). טבלאות בסכמה snake_case ומיגרציית EF Core. תהליך הרקע לא רץ כשאין בסיס נתונים; ניתן לכבותו ב-`EMAIL_WORKER=false`. בלי PII ביומנים.

**Never:** אין פתיחת קישור או כניסה (2.2), אין מיילים של אפיקים 2 ו-3 מלבד `edit-link`, אין HTML או קבצים מצורפים, אין שליחת מייל מנתיב הבקשה, אין החזרת מידע על קיום הכתובת, אין מסכי לקוח, אין שמירת סיסמה כלשהי בתצורה בקוד.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| מייל רשום | כתובת של פרופיל פעיל | 202 `{status:"ok"}`; נכתבת שורת Outbox אחת; בלי אסימון עדיין | N/A |
| מייל לא רשום | כתובת לא קיימת | אותה תגובה בדיוק; לא נכתבה שורה | N/A |
| מייל לא תקין | פורמט שגוי | 400 ולידציה | N/A |
| בקשות חוזרות | חמש בקשות מקבילות לאותה כתובת באותה שעה | 202 לכולן; שורת Outbox אחת | `DeduplicationKey` ייחודי |
| חריגת קצב | מעל הסף לאותה כתובת או IP | 429 עם `Retry-After` ו-`code` | N/A |
| בלי Turnstile או Origin | חסר | נדחה כבסיפור 1.7 | N/A |
| שליחה | שורה `pending` | נשלח מייל טקסט פשוט עם קישור בפרגמנט; הנפקת אסימון; הקודם מבוטל; השורה `sent` | N/A |
| הנפקה חוזרת | שני מיילים לאותו פרופיל בשעות שונות | רק האסימון האחרון תקף | N/A |
| כישלון זמני | שגיאת רשת או 5xx | `NextAttemptAt` נדחה; ניסיון נספר | backoff |
| 429 מהספק | `Retry-After` | נדחה בלי ספירת ניסיון כושל | N/A |
| כישלון סופי | חמישה ניסיונות כושלים | `dead`, גלוי ב-`/admin/email-outbox` | N/A |
| קריסה אחרי תפיסה | lease שפג | השורה נתפסת שוב | N/A |
| שני מופעים | שני workers במקביל | כל שורה נשלחת פעם אחת | `SKIP LOCKED` |
| פרטיות | Outbox, תגובות ולוגים | אין אסימון גולמי, מייל או נייד | N/A |
| תפסן מקומי | Development | המייל נקלט ב-Mailpit | N/A |

</intent-contract>

## Code Map

- `server/Api/Endpoints/ProfileEndpoints.cs` -- תבנית נקודה ציבורית מוגנת: `PublicFormGuard` (IP ו-Turnstile ומגבלת מייל), קריאת גוף ידנית, `Origin` ו-anti-forgery; נקודת `edit-link` החדשה בקובץ חדש לפי אותו דפוס.
- `server/Api/Security/PublicFormGuard.cs`, `server/Features/PublicForms/IRateLimiter.cs` -- מדיניות קצב ל-IP ולמייל (hash עם pepper); להוסיף scopes `edit-link-ip` ו-`edit-link-email`.
- `server/Features/Profiles/ProfileService.cs` -- `NormalizeEmail`, קריאת פרופילים לפי מייל מנורמלי דרך `ParticipantPrivate` (אינדקס `(cohort_id, email)` קיים; להוסיף אינדקס על `email` אם נדרש).
- `server/Domain/Profiles/Profile.cs` -- `Profile`, `ParticipantPrivate`; `server/Domain/Audit/AuditLogEntry.cs` -- דוגמה לישות ללא תלות.
- `server/Infrastructure/AppDbContext.cs`, `DependencyInjection.cs`, `ProfileRepository.cs` -- מיפוי snake_case, רישום שירותים ודוגמאות ל-`ExecuteSqlInterpolated` ול-`FOR UPDATE` (ראו `SubmissionReviewRepository`); מיגרציה (README: `dotnet-ef`).
- `server/Features/PublicForms/EmailHeaderSanitizer.cs` -- ניקוי ערכים לכותרת.
- `server/Api/Endpoints/SubmissionEndpoints.cs` -- תבנית `AdminPolicy` ו-`{ items, total }` לנקודת `GET /admin/email-outbox`.
- `server/Api/Program.cs` -- רישום שירותי רקע; `server/Tests/AuthTestSupport.cs` -- `AuthApiFactory` (החלפת שירותים, משתני סביבה, `FakeTurnstileVerifier`); להוסיף `FakeEmailSender` ולכבות את ה-worker בבדיקות שאינן שלו.
- אין לשנות: `client/`, התנהגות ההגנות מסיפורים 1.3 עד 1.13, הנקודות הציבוריות והניהול הקיימות.

## Tasks & Acceptance

**Execution:**
- `server/Domain/` ו-`server/Infrastructure/` -- ישויות `EmailOutboxEntry` ו-`PersonalToken`, מיפוי, אינדקסים (ייחודי על `DeduplicationKey`, על `TokenHash`, ועל `(Status, NextAttemptAt)`) ומיגרציה -- AR8, AR10
- `server/Features/Email/` -- `IEmailSender`, תבנית `edit-link`, שירות הנפקת אסימון, ו-`IEmailOutbox` לכתיבה ולתפיסה -- AD-4, AD-6, AD-15
- `server/Infrastructure/` -- `SmtpEmailSender`, `ResendEmailSender`, `EmailOutboxWorker` (claim, lease, backoff, dead), בחירת ספק ואימות תצורה -- AD-4, AD-16
- `server/Api/Endpoints/` -- `POST /profiles/edit-link` ו-`GET /admin/email-outbox` -- FR-11
- `docker-compose.dev.yml` -- שירות Mailpit לפיתוח -- תפעול
- `server/Tests/` -- בדיקות אינטגרציה ויחידה לשורות המטריצה: תגובה אחידה, אידמפוטנטיות, הנפקה וביטול, backoff ו-429, `dead`, lease ושני workers, פרטיות הטבלה והלוגים, ומייל טקסט פשוט עם פרגמנט -- AR8, AR21
- `README.md` -- הנקודות, משתני הסביבה, Mailpit והתנהגות הנפקה -- תפעול

**Acceptance Criteria:**
- Given כתובת מייל, when נשלחת בקשת קישור אישי (עם Turnstile), then מוחזרת תמיד אותה תגובה, בין אם המייל קיים ובין אם לא, והמגבלות לפי כתובת ו-IP פעילות.
- Given מייל קיים, when הבקשה מתקבלת, then נכתבת שורת `EmailOutbox` עם `TemplateKey` ומזהים בלבד (בלי אסימון גולמי), ובשליחה נוצר אסימון 256 ביט שנשמר כ-hash עם `Purpose = edit-profile`, תקף 24 שעות, ואסימון קודם לאותה מטרה מתבטל.
- Given שורה ב-`EmailOutbox`, when תהליך הרקע רץ, then הוא תופס אותה עם claim ו-lease, שולח מחוץ לטרנזקציה דרך `IEmailSender`, ניסיונות מוגבלים עם 429 כדחייה זמנית, `dead` גלוי למנהלת, ו-`DeduplicationKey` מונע כפילות.
- Given סביבת פיתוח, when נשלח מייל, then הוא נקלט בתפסן מקומי, ו-`IEmailSender` הוא נקודת ההחלפה היחידה של הספק; כל המיילים טקסט פשוט והקישור בנוי עם האסימון בפרגמנט `#`.

## Spec Change Log

## Review Triage Log

### 2026-10-21 — Review pass
- verdicts: כ-60 ממצאים מארבע שכבות (רבים כפולים) — high 0, medium 7, low 8, false 1
- findings:
  - `[medium]` `[patch]` כשל ב-QUIT אחרי שליחה מוצלחת מסומן ככשל ומוביל למייל כפול; כשלים קבועים (5xx, כתובת פגומה) מנוסים 5 פעמים — תוקנו
  - `[medium]` `[patch]` batch של 10 שורות עם lease יחיד גורם לשליחה כפולה; עדכוני סטטוס לא מותנים בבעלות על ה-lease — נתפסת שורה אחת, העדכונים מותנים
  - `[medium]` `[patch]` כשל ב-MarkSent אחרי שליחה, דחיית 429 ללא תקרה, שורה רעילה שקורסת בכל תפיסה, מפתח אידמפוטנטיות זהה לגוף שונה — תוקנו
  - `[medium]` `[patch]` תצורה בייצור: https, EMAIL_FROM, אימות SMTP לא מוצפן, EMAIL_WORKER — אימות באתחול; render.yaml חסר משתני SMTP
  - `[medium]` `[patch]` בדיקות חסרות: הנפקה מקבילה לאותו פרופיל, TLS, עימוד האדמין, ניקוי משתני סביבה — נוספו; שם המחזור במייל לבהירות
  - `[low]` `[patch]` ביטול בכיבוי נספר כניסיון, README — תוקנו
  - `[medium]` `[defer]` retention ופעולת ניהול ל-dead, שליחה אמיתית מול Mailpit ו-Resend, תזמון תגובה ומכסת מייל חיצונית, מסכי לקוח — נרשמו ב-deferred
  - `[low]` `[reject]` ביטול טוקן קודם בכל ניסיון כושל (מוגדר בסיפור: הנפקה חדשה מבטלת את הקודם; מתועד), מפתח dedup לפי שעה (מתועד), תרבות לא גרגוריאנית, גבול שעה בבקשות מקבילות — לא סביר שייפגעו בשימוש רגיל או ההחלטה מתועדת
  - `[false]` `[reject]` Designer ו-snapshot חסרים — קיימים והוחרגו מה-diff
## Verification

**Commands:**
- `dotnet build server` -- expected: 0 שגיאות
- `dotnet test server` -- expected: כל הבדיקות עוברות (דורש Docker)
- (אופציונלי) `docker compose -f docker-compose.dev.yml up -d mailpit` ושליחה אמיתית ל-`localhost:1025` -- expected: המייל מופיע ב-Mailpit ב-`http://localhost:8025`

## Auto Run Result

- **שינוי:** תשתית המיילים וקישור אישי: `POST /profiles/edit-link` (ציבורי, מוגן ב-anti-forgery, Origin, Turnstile והגבלת קצב לפי IP ולפי מייל) שמחזיר תמיד `202 {status:"ok"}` וכותב שורת `email_outbox` בלי סודות; `EmailOutboxWorker` שתופס שורה אחת בכל פעם עם claim ו-lease (`FOR UPDATE SKIP LOCKED`), מנפיק אסימון 256 ביט (נשמר כ-hash, 24 שעות, ביטול קודמים) ושולח מייל טקסט פשוט בעברית עם הקישור `{PUBLIC_BASE_URL}/edit#token=...` דרך `IEmailSender` (SMTP עם MailKit או Resend). כישלון זמני עם backoff, 429 כדחייה בלי ספירה (עם תקרת גיל), כישלון קבוע ו-5 ניסיונות כושלים מובילים ל-`dead`, גלוי למנהלת ב-`GET /admin/email-outbox`. בפיתוח Mailpit ב-`docker-compose.dev.yml`.
- **קבצים:** `server/Domain/Email/*`, `server/Features/Email/*`, `server/Infrastructure/{EmailOutboxRepository,EmailOutboxWorker,SmtpEmailSender,ResendEmailSender,EmailOptions}.cs`, `server/Api/Endpoints/{EditLinkEndpoints,EmailOutboxEndpoints}.cs`, מיגרציות `EmailOutbox` ו-`EmailOutboxClaimCount`, `docker-compose.dev.yml`, `render.yaml`, `server/Tests/*`, `README.md`.
- **סקירה:** 7 תיקוני medium (שליחה כפולה בניתוק או ב-lease משותף, MarkSent אחרי שליחה, דחיית 429 ללא תקרה, שורה רעילה, אימות תצורה בייצור, בדיקות חסרות) ו-2 low הוחלו. 4 פריטים נדחו ל-`deferred` (retention ופעולות ניהול ל-dead, שליחה אמיתית מול Mailpit ו-Resend, תזמון תגובה ומכסת מייל, מסכי לקוח). השאר נדחו כמתועד ביומן. לאחר התיקונים התגלה ותוקן דליפת משתני סביבה בין בדיקות.
- **המלצה לסקירת המשך:** כן. סיכונים שלא אומתו: שליחה אמיתית מול Mailpit ו-Resend החי, והתנהגות העבודה ברקע בהרצה ארוכה ובשני מופעים.
- **אימות:** `dotnet build server` ללא שגיאות, `dotnet test server`: 476 מתוך 476 עוברות מול Postgres אמיתי (הורצו שלוש פעמים).
- **סיכונים שנותרו:** `PUBLIC_BASE_URL` (https), `EMAIL_PROVIDER` ו-`EMAIL_FROM` חובה מחוץ ל-Development (ב-Render יש להזין); הקישור במייל מוביל לנתיב `/edit` שעוד לא קיים בלקוח.
