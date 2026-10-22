---
title: 'סיפור 3.1: הגשת בקשת פרטי קשר ואימות כתובת'
type: 'feature'
created: '2026-10-22'
status: 'done'
baseline_revision: 'e91aa5c06b263a12405abc330c3d574bce1028d0'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-resume_app-2026-10-21/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-2-1-קישור-אישי-במייל.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-2-5-הסרת-פרופיל-שפורסם.md'
warnings: ['oversized']
deferred:
  - summary: >-
      טופס בקשת פרטי הקשר בעמוד הפרופיל (כפתור כחול), ווידג'ט Turnstile, הודעות "שלחנו אליך מייל" ו"הכתובת אומתה והבקשה נשלחה", ועמוד האימות `/contact/verify` שקורא את האסימון מהפרגמנט ושולח POST טרם נבנו בלקוח
    evidence: |-
      המייל מוביל לנתיב שעדיין אין; סיפור 5.9
    location: >-
      client/ (טרם קיים)
    severity: medium
  - summary: >-
      נתוני המבקר (שם, כתובת וסיבה) נשמרים ללא הגבלת זמן, גם לבקשות שלא אומתו ושפגו; המפתח הזר Restrict חוסם מחיקת פרופיל
    evidence: |-
      מדיניות שמירה ומחיקה טרם נקבעה (שאלה פתוחה); לטפל בסיפור 4.3
    location: >-
      server/Domain/Contacts/ContactRequest.cs
    severity: medium
  - summary: >-
      מכסת הבקשות לפי פרופיל וכתובת נספרת לכל ניסיון ולכן צד שלישי יכול למצות אותה; אין קנוניזציה של plus-addressing; תזמון התגובה עשוי לגלות מצב פרופיל
    evidence: |-
      פשרה מכוונת להסתרת קיום; הקשחה בסיפור 4.1
    location: >-
      server/Api/Endpoints/ContactRequestEndpoints.cs
    severity: low
  - summary: >-
      מסנן הקישורים בסיבה הוא best-effort (דומיינים חשופים ו-hxxp לא נדחים); ו-`ContactRequest.Create` ו-`CanTransition` בדומיין אינם בשימוש בקוד הייצור
    evidence: |-
      הכתיבה נעשית ב-SQL גולמי; לשקול ניתוב דרך כללי הדומיין
    location: >-
      server/Features/Contacts/ContactRequestService.cs
    severity: low---

<intent-contract>

## Intent

**Problem:** מבקר שרוצה ליצור קשר עם משתתפת לא יכול להגיש בקשה, ואין דרך לוודא שהכתובת שלו אמיתית לפני שהבקשה מגיעה למנהלת.

**Approach:** להוסיף ב-`server/` את `ContactRequest` (מכונת מצבים של AD-3, כרגע עד `submitted` ו-`expired`), נקודה ציבורית מוגנת `POST /profiles/{profileId}/contact-requests` שיוצרת בקשה `awaiting_email_verification` ושורת `EmailOutbox` למייל אימות למבקר, ונקודה `POST /contact-requests/verify` שמחליפה אסימון אימות חד-פעמי ומעבירה את הבקשה ל-`submitted` ונועלת את כתובת המבקר. חלק השרת בלבד; אין חשיפת פרטי קשר.

## Boundaries & Constraints

**Always:** **הגשה** `POST /profiles/{profileId}/contact-requests` (`[AllowAnonymous]`; מזהה GUID בצורה `D`; גוף `{ "name", "email", "reason" }`; מוגנת ב-anti-forgery, `Origin`, הגבלת IP, Turnstile (כותרת `X-Turnstile-Token`), הגבלה לפי כתובת המבקר המנורמלת, והגבלה לפי פרופיל; ספי ברירת מחדל לשעה בהגדרה במשתני סביבה: `RATE_LIMIT_CONTACT_REQUEST_IP` 10, `RATE_LIMIT_CONTACT_REQUEST_EMAIL` 3, `RATE_LIMIT_CONTACT_REQUEST_PROFILE` 10). התגובה לפרופיל `Active`, לפרופיל שאינו `Active`, ולפרופיל שאינו קיים, זהה בגוף, בסטטוס ובכותרות: 202 עם `{ "status": "verification-sent" }`, ואין עבודה כבדה שמבדילה בנתיב. ולידציה (קודם לבדיקת קיום הפרופיל, בלי חשיפה): `name` חובה 1 עד 100 אחרי trim, `email` תקין כבהגשת פרופיל (נורמליזציה trim ואותיות קטנות), `reason` חובה 1 עד 1000, טקסט פשוט: בלי קישורים (נדחה כל טקסט שמכיל `http://`, `https://`, `www.`, או סכמה כגון `javascript:` או `data:`, בלי תלות באותיות גדולות), בלי תווי בקרה חוץ מ-`\n` ו-`\t`, ובלי תווים נסתרים מסוכנים (`UnsafeCharacters`); כשל: 400 `problem+json` עם שגיאה לכל שדה. פרופיל `Active` בלבד יוצר: `ContactRequest` חדשה במצב `awaiting_email_verification` (טבלה `contact_requests`: `id` GUID v4, `profile_id`, `visitor_name`, `visitor_email` מנורמל, `reason`, `status`, `created_at`, `expires_at` (24 שעות), `verified_at`) ושורת `EmailOutbox` (`TemplateKey = "contact-verification"`, עמודה חדשה `request_id` שמצביעה על הבקשה, `DeduplicationKey = "contact-verification:{requestId}"`, בלי אסימון, כתובת, שם או טקסט בשורה). אם כבר קיימת בקשה פתוחה (מצב שאינו `expired` או `participant_declined` או `delivered`) לאותו (פרופיל, כתובת מבקר מנורמלת), לא נוצרת נוספת ועדיין מוחזרת אותה תגובה (אינדקס ייחודי חלקי על `(profile_id, visitor_email)` למצבים הפתוחים). ה-worker מכליל את מנגנון התבניות ל-`contact-verification`: קורא את כתובת המבקר והשם מהבקשה בזמן השליחה, מנפיק אסימון `PersonalToken` (`Purpose = "verify-contact-email"`, 256 ביט, hash בלבד, תקף 24 שעות, חד-פעמי, עמודה חדשה `request_id` בישות האסימון, אסימון קודם לאותה בקשה מתבטל) ושולח מייל טקסט פשוט בעברית למבקר עם נושא קבוע, שם המבקר מנוקה (`EmailHeaderSanitizer`) והקישור `{PUBLIC_BASE_URL}/contact/verify#token=...` (אסימון בפרגמנט `#`); בלי שם המשתתפת ובלי כל פרט קשר או תוכן הבקשה; כשל הופך `dead` כבסיפור 2.1; בקשה שהוסרה או הפכה `expired` לפני השליחה: השורה `dead` עם קטגוריה `request-not-awaiting`. **אימות** `POST /contact-requests/verify` (`[AllowAnonymous]`, גוף `{ "token" }`, anti-forgery, `Origin`, הגבלת IP `RATE_LIMIT_CONTACT_VERIFY_IP` ברירת מחדל 30): פענוח אסימון בעדכון מותנה אטומי אחד (לא נוצל, לא בוטל, לא פג, `Purpose = verify-contact-email`) ולאחר מכן, באותה טרנזקציה עם נעילת הבקשה, `UPDATE contact_requests SET status = 'submitted', verified_at = now WHERE id = @id AND status = 'awaiting_email_verification' AND expires_at > now`. הצלחה: 200 `{ "status": "submitted" }` בלי מידע על הפרופיל או המשתתפת, והכתובת המאומתת ננעלת (אין אף נקודה שמשנה `visitor_email` של בקשה). כל כשל (אסימון פג, נוצל, בוטל, לא קיים, פגום, מטרה אחרת, בקשה שכבר אומתה, פרופיל שאינו `Active`) מחזיר אותה תגובה בדיוק: 401 `problem+json` עם `code: "link-invalid"`; בכשל שבו האסימון תקין אך הבקשה פגה (`expires_at` עבר) או הפרופיל אינו `Active`, הבקשה (אם עדיין `awaiting_email_verification`) עוברת ל-`expired` באותה טרנזקציה. סורק מייל: הקישור הוא GET לעמוד לקוח; הצריכה היא POST בלבד. **תפוגה ברקע:** `EmailOutboxWorker` מעביר מדי מחזור בקשות `awaiting_email_verification` ש-`expires_at` שלהן עבר ל-`expired` (עדכון מותנה, סוויפר) ומבטל את אסימוני האימות הפתוחים שלהן. **הסרת פרופיל (חוב מסיפור 2.5):** `RemoveAsync` מעביר באותה טרנזקציה את כל הבקשות הפתוחות של הפרופיל (`awaiting_email_verification` ו-`submitted`) ל-`expired` ומבטל אסימוני `verify-contact-email` פתוחים שלהן, ובמטריצה בדיקה לכך; (מצבי המשך של אפיק 3 יתווספו לרשימה באותו מקום בסיפורים 3.3 ו-3.4). **פרטיות:** בקשה שלא אומתה אינה נחשפת בשום נקודה (אין עדיין נקודת מנהלת שמציגה בקשות; סיפור 3.2 יציג `submitted` ואילך בלבד); אין קריאה מ-`ParticipantPrivate`; אין בתגובות, ב-Outbox, ביומנים או בהודעות שגיאה כתובת מבקר, שם, סיבה או פרט קשר של משתתפת. מיגרציית EF Core: `contact_requests`, `email_outbox.request_id`, `personal_tokens.request_id` ואינדקסים. טבלאות snake_case.

**Never:** אין התראה למנהלת ותור בקשות (3.2), אין פנייה למשתתפת והחלטתה (3.3), אין מסירת פרטי קשר (3.4), אין מסכי לקוח או כפתור בעמוד הפרופיל (בלקוח), אין שינוי כתובת המבקר המאומתת, אין חשיפת פרטי קשר של משתתפת, אין קישורים בתוכן הבקשה, אין שליחת מייל מנתיב הבקשה.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| הגשה תקינה | פרופיל `Active`, Turnstile תקף | 202 `verification-sent`; נוצרת `awaiting_email_verification` ושורת Outbox אחת; בלי אסימון | N/A |
| פרופיל לא פעיל או לא קיים | `removed`, לא קיים, מזהה פגום | אותה 202 בדיוק (גוף וכותרות); לא נוצר דבר | אחיד |
| ולידציה | שם, מייל או סיבה חסרים או לא תקינים | 400 עם שגיאה לשדה, לפני בדיקת הפרופיל | ולידציה |
| קישור בסיבה | `http://x`, `www.x.co`, `javascript:` | 400 על `reason` | ולידציה |
| תווים מסוכנים | NUL, bidi, zero-width בשם או בסיבה | 400 על השדה | ולידציה |
| בלי Turnstile או Origin | חסר | נדחה כבסיפור 1.7 | N/A |
| קצב | חריגה לפי IP, כתובת או פרופיל | 429 עם `Retry-After` ו-`code` | N/A |
| בקשה כפולה | אותה כתובת ופרופיל כשיש בקשה פתוחה | אותה 202; בלי בקשה נוספת ובלי שורת Outbox נוספת | אינדקס חלקי |
| מקביליות | חמש הגשות זהות במקביל | בקשה אחת ושורת Outbox אחת | אינדקס ייחודי |
| מייל אימות | ה-worker | טקסט פשוט למבקר עם קישור באסימון בפרגמנט `#`; אסימון חדש מבטל קודם; בלי פרטי משתתפת | N/A |
| פרטיות Outbox | שורת `email_outbox` | בלי אסימון, כתובת, שם או סיבה | N/A |
| אימות תקין | POST עם אסימון תקף | 200 `submitted`; `verified_at` נקבע; האסימון נוצל; הכתובת ננעלת | N/A |
| שימוש חוזר | אותו אסימון שוב | 401 `link-invalid` | אחיד |
| אסימון לא תקף | פג, בוטל, לא קיים, פגום, מטרה אחרת (`edit-profile`) | אותו 401 בדיוק | אחיד |
| בקשה שפגה | אסימון תקף אבל `expires_at` עבר | 401 אחיד והבקשה הופכת `expired` | אחיד |
| פרופיל הוסר לפני האימות | אסימון תקף | 401 אחיד והבקשה `expired` | אחיד |
| מקביליות אימות | חמש בקשות אימות לאותו אסימון | בדיוק 200 אחד | עדכון אטומי |
| סוויפר | בקשה `awaiting` שחלף 24 שעות | `expired` ואסימוניה בוטלו | N/A |
| הסרת פרופיל | בקשות `awaiting` ו-`submitted` של פרופיל | עוברות `expired` בטרנזקציית ההסרה | N/A |
| לא נחשפת | בקשה שלא אומתה | אינה מופיעה בשום נקודה | N/A |
| הגנת אימות | בלי anti-forgery או Origin זר | נדחה | N/A |
| קצב אימות | חריגה מהסף | 429 | N/A |

</intent-contract>

## Code Map

- `server/Api/Endpoints/EditLinkEndpoints.cs`, `server/Api/Security/PublicFormGuard.cs` -- תבנית נקודה ציבורית מוגנת: IP, Turnstile, מגבלת מייל, תגובה אחידה, ולידציית מייל משותפת; להוסיף scopes `contact-request-ip`, `contact-request-email`, `contact-request-profile`, `contact-verify-ip` ומשתני `RateLimitOptions`.
- `server/Api/Endpoints/ParticipantEndpoints.cs` -- `POST /profiles/session`: תבנית החלפת אסימון בעדכון אטומי עם 401 `link-invalid` אחיד ו-`Referrer-Policy: no-referrer`; לחקות באימות המבקר (בלי עוגיית סשן).
- `server/Features/Email/EmailOutboxProcessor.cs`, `SubmissionReturnedEmail.cs`, `EditLinkEmail.cs`, `IEmailOutbox.cs` -- מנגנון התבניות (`edit-link`, `submission-returned`) ומחזור חיי שורה; להוסיף `contact-verification` ותבנית; `server/Infrastructure/EmailOutboxRepository.cs` -- `PersonalTokenIssuer`, claim, `MarkSent`, `RecordFailure`; להוסיף הנפקה לבקשה (`request_id`).
- `server/Infrastructure/EmailOutboxWorker.cs` -- לולאת ה-worker; כאן הסוויפר של בקשות שפגו.
- `server/Domain/Email/EmailOutboxEntry.cs` -- `EmailOutboxEntry` (להוסיף `RequestId` ו-`ContactVerificationTemplate`), `PersonalToken` (להוסיף `RequestId` ו-`VerifyContactEmailPurpose`); `server/Domain/` -- מקום לישות `ContactRequest` (`Contacts/`) עם `ContactRequestStatus` לפי AD-3.
- `server/Infrastructure/ProfileAdminRepository.cs` -- `RemoveAsync` (טרנזקציה עם נעילת שורת הפרופיל): כאן מוסיפים את הפיכת הבקשות הפתוחות ל-`expired` וביטול אסימוניהן.
- `server/Infrastructure/AppDbContext.cs`, `DependencyInjection.cs`, `Program.cs` -- מיפוי snake_case, אינדקסים, מיגרציה (README: `dotnet-ef`) ורישום שירותים וקצב.
- `server/Features/Profiles/ProfileService.cs` -- `NormalizeEmail`, `ValidateEmail`; `server/Features/PublicForms/UnsafeCharacters.cs`, `EmailHeaderSanitizer.cs`.
- `server/Tests/EditLinkTests.cs`, `EditLinkRobustnessTests.cs`, `ProfileRemovalTests.cs`, `PublicFormProtectionTests.cs`, `AuthTestSupport.cs` -- `AuthApiFactory`, `FakeEmailSender`, `FakeTurnstileVerifier`, הרצת worker בבדיקות, יצירת פרופילים ואסימונים; להרחיב.
- אין לשנות: `client/`, נקודות האדמין והמשתתפת הקיימות מעבר להסרה, התנהגות מנגנון המיילים ל-`edit-link` ו-`submission-returned`.

## Tasks & Acceptance

**Execution:**
- `server/Domain/` ו-`server/Infrastructure/` -- ישות `ContactRequest` ומכונת מצבים, `RequestId` בשורת Outbox ובאסימון, אינדקס ייחודי חלקי, מיגרציה -- AR7, AR16
- `server/Features/` -- שירות בקשות: ולידציה, יצירה אחידה, אימות אטומי ו-`expired`, והכללת מנגנון התבניות ל-`contact-verification` -- AD-3, AD-4, AD-6, AD-12
- `server/Api/Endpoints/` -- `POST /profiles/{profileId}/contact-requests` ו-`POST /contact-requests/verify` עם הגנות 1.7 וקצב לפי IP, כתובת ופרופיל -- FR-13, AR15
- `server/Infrastructure/` -- סוויפר תפוגה ב-worker והפיכת בקשות פתוחות ל-`expired` בהסרת פרופיל -- AD-13
- `server/Tests/` -- בדיקות אינטגרציה לשורות המטריצה, כולל אחידות תגובות, מקביליות, פרטיות Outbox והלוג, ושליחת המייל דרך ה-worker -- AR21
- `README.md` -- הנקודות, משתני הסביבה, מכונת המצבים והתנהגות הסרה ותפוגה -- תפעול

**Acceptance Criteria:**
- Given טופס תקין שנשלח אחרי אימות Turnstile, when נשלח, then נוצרת `ContactRequest` במצב `awaiting_email_verification` ומוחזר אישור זהה בין פרופיל פעיל, לא פעיל ולא קיים, ונכתבת שורת `EmailOutbox` עם `TemplateKey` ומזהים בלבד.
- Given המייל שנשלח, when המבקר מחליף את האסימון (POST, חד-פעמי, אטומי), then הבקשה עוברת ל-`submitted` בעדכון מותנה והכתובת ננעלת.
- Given קישור אימות שפג, נוצל או לא תקף, when נפתח, then מוחזרת תגובה אחידה, הבקשה שטרם אומתה עוברת ל-`expired`, ולא נחשף אם הפרופיל קיים.
- Given בקשה שלא אומתה, when מנהלת מסתכלת, then היא אינה נחשפת; והסרת פרופיל מעבירה את הבקשות הפתוחות שלו ל-`expired`.

## Spec Change Log

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: כ-50 ממצאים מארבע שכבות (רבים כפולים) — high 0, medium 6, low 6, false 0
- findings:
  - `[medium]` `[patch]` שדה `name` שמוד במייל האימות אינו עובר סינון קישורים — ניתן להשתמש בדומיין כדי לשלוח פיתוי; נוסף סינון, NFKC ונרמול `\r`
  - `[medium]` `[patch]` מבקר יכול להינעל עד 24 שעות כשבקשה תקועה (worker כבוי, מייל שנכשל, בקשה שפגה) — פקיעה לפני יצירה, ובכשל סופי של המייל הבקשה פוקעת
  - `[medium]` `[patch]` שליחה לא בודקת שהפרופיל `Active`; רשימת המצבים הפתוחים בשלושה מקומות בלי CHECK — נוספו בדיקה ו-CHECK
  - `[medium]` `[patch]` סוויפר לא חסום, רץ בכל poll ללא נעילה — חסום, מוגבל לדקה, עם SKIP LOCKED ובדיקת worker
  - `[medium]` `[patch]` חסרות בדיקות: 413/415/JSON פגום, משתני סביבה, מרוץ מול הסרה, שורה בלי `request_id`, ניסיון חוזר זמני — נוספו; התאמת נתיב ה-middleware המדויק; README
  - `[low]` `[defer]` מסך הלקוח, שמירת נתונים, מכסות שניתנות לניצול ותזמון, מסנן קישורים best-effort ודומיין לא בשימוש — נרשמו ב-deferred
  - `[low]` `[reject]` ביטול בקשה במקום ניסיון חוזר כשהשליחה נכשלה בגלל המבקר, ביקורת תכנון על העמודה `profile_id` באסימון, שדה `HeaderLines` — לא סביר שייפגעו בשימוש רגיל או מטופלים בתיקונים
## Verification

**Commands:**
- `dotnet build server` -- expected: 0 שגיאות
- `dotnet test server` -- expected: כל הבדיקות עוברות (דורש Docker; כ-5 דקות, מומלץ להריץ ברקע)

## Auto Run Result

- **שינוי:** `ContactRequest` עם מכונת המצבים של AD-3 (כרגע עד `submitted` ו-`expired`); `POST /profiles/{profileId}/contact-requests` (ציבורי, anti-forgery, Origin, Turnstile והגבלת קצב לפי IP, כתובת ופרופיל) שמחזיר תמיד אותה 202 `verification-sent` לפרופיל פעיל, לא פעיל ולא קיים; מייל אימות דרך ה-Outbox (תבנית `contact-verification`, אסימון `verify-contact-email` בפרגמנט `#`, אסימון נוצר בזמן השליחה); `POST /contact-requests/verify` שמחליף אסימון חד-פעמי בעדכון אטומי ומעביר ל-`submitted` ונועל את הכתובת, עם 401 `link-invalid` אחיד לכל כשל. סוויפר תפוגה חסום ב-worker, והסרת פרופיל מעבירה בקשות פתוחות ל-`expired` (החוב מסיפור 2.5).
- **קבצים:** `server/Domain/Contacts/ContactRequest.cs`, `server/Features/Contacts/ContactRequestService.cs`, `server/Infrastructure/ContactRequestRepository.cs`, `server/Api/Endpoints/ContactRequestEndpoints.cs`, `server/Features/Email/ContactVerificationEmail.cs`, `EmailOutboxProcessor.cs`, `EmailOutboxWorker.cs`, `ProfileAdminRepository.cs`, `RequestIntegrityMiddleware.cs`, מיגרציות `ContactRequests` ו-`ContactRequestStatusCheck`, `server/Tests/ContactRequestTests.cs`, `README.md`.
- **סקירה:** 5 תיקוני medium (סינון קישורים בשדה השם, נעילת מבקר כשבקשה תקועה, בדיקת Active בשליחה ו-CHECK, סוויפר חסום, בדיקות חסרות) ו-1 low קבוצתי הוחלו. 4 פריטים נדחו ל-`deferred` (מסך הלקוח, שמירת נתונים, מכסות שניתנות לניצול ותזמון, מסנן קישורים best-effort). השאר נדחו כמתועד ביומן.
- **המלצה לסקירת המשך:** כן. סיכונים שלא אומתו: לא נבדקה שליחה אמיתית מול Mailpit או Resend; מסנן הקישורים בסיבה הוא best-effort; תזמון התגובה עשוי לגלות מצב פרופיל.
- **אימות:** `dotnet build server` ללא שגיאות, `dotnet test server`: 680 מתוך 680 עוברות מול Postgres אמיתי.
- **סיכונים שנותרו:** אין עדיין טופס ועמוד אימות בלקוח (סיפור 5.9); נתוני מבקר נשמרים ללא הגבלת זמן עד מדיניות שמירה.
