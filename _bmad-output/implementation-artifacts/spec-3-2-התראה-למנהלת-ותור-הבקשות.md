---
title: 'סיפור 3.2: התראה למנהלת ותור הבקשות'
type: 'feature'
created: '2026-10-22'
status: 'done'
baseline_revision: 'b14f6b13eed0485d5af0d0e3c370e0eae14d25e5'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-resume_app-2026-10-21/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-3-1-הגשת-בקשת-פרטי-קשר-ואימות-כתובת.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-2-1-קישור-אישי-במייל.md'
warnings: []
deferred:
  - summary: >-
      מסך "בקשות פרטי קשר" לניהול בלקוח, והקישור במייל ההתראה (`/admin/contact-requests`) מוביל לנתיב לקוח שעדיין לא קיים
    evidence: |-
      השרת מחזיר נתונים בלבד; סיפור 5.9
    location: >-
      client/ (טרם קיים)
    severity: medium
  - summary: >-
      שליחת ההתראה למספר כתובות מנהלת היא at-least-once ועלולה לשלוח שוב לכתובת שכבר קיבלה; ללא שורת Outbox נפרדת לכל נמען
    evidence: |-
      מקסימום 5 כתובות; מפתח אידמפוטנטיות יציב לכל נמען מצמצם כפילויות רק אצל ספקים שתומכים; לשקול שורה לנמען אם יידרש
    location: >-
      server/Features/Email/EmailOutboxProcessor.cs
    severity: low
  - summary: >-
      אין חיפוש, מיון או סינון לפי מבקר או פרופיל בתור הבקשות, והזמן במייל ההתראה ב-UTC בלבד
    evidence: |-
      התור מוגבל לעימוד וסינון לפי סטטוס; לשקול בסיפור 5.9 או 4.1
    location: >-
      server/Api/Endpoints/ContactRequestAdminEndpoints.cs
    severity: low---

<intent-contract>

## Intent

**Problem:** בקשת פרטי קשר שאומתה (`submitted`) לא מגיעה לידיעת המנהלת, ואין לה מסך או נקודה שמציגים את הבקשות המאומתות ומצב האישורים שלהן.

**Approach:** להוסיף ב-`server/` שורת `EmailOutbox` להתראה למנהלת באותה טרנזקציה של המעבר ל-`submitted` (תבנית `contact-request-notification`, טקסט פשוט, בלי פרטי קשר של משתתפת), ו-`GET /admin/contact-requests` לתור הבקשות המאומתות עם אישור המשתתפת ואישור המנהלת בנפרד. חלק השרת בלבד.

## Boundaries & Constraints

**Always:** **התראה:** בעדכון המותנה של `VerifyAsync` (סיפור 3.1) שמעביר `awaiting_email_verification` ל-`submitted`, ובאותה טרנזקציה, נכתבת שורת `EmailOutbox` עם `TemplateKey = "contact-request-notification"`, `request_id` של הבקשה, ו-`DeduplicationKey = "contact-notification:{requestId}"` ייחודי (`ON CONFLICT DO NOTHING`, כך שאימות חוזר או מרוץ לא יוצרים שורה נוספת); בשורה אין שם, כתובת, סיבה או פרט קשר. המעבר ל-`submitted` ללא שורת התראה לא מתאפשר (אם הכתיבה נכשלת, הטרנזקציה כולה מתבטלת). ה-worker מכליל את מנגנון התבניות ל-`contact-request-notification`: שולח מייל טקסט פשוט (`text/plain; charset=utf-8`), בעברית, נושא קבוע, לכתובות המנהלת שבתצורה (`ADMIN_NOTIFICATION_EMAIL`: כתובת אחת או כמה מופרדות בפסיק, עד 5, כל אחת מנותחת ותקפה, מנורמלת; מנותח באתחול בכל סביבה כשהמשתנה מוגדר; חובה מחוץ ל-Development, וב-Development ברירת המחדל היא `admin@example.test`). גוף המייל: הודעה קבועה שהתקבלה בקשת פרטי קשר חדשה שאומתה, זמן האימות, שם המחזור ושם הפרופיל המבוקש (שם התצוגה של הגרסה המאושרת, ללא פרטי קשר), וקישור לתור `{PUBLIC_BASE_URL}/admin/contact-requests`; בלי שם המבקר, כתובתו או הסיבה, ובלי כל פרט קשר של משתתפת. כל שדה משתנה שנכנס לכותרת עובר `EmailHeaderSanitizer` (הנושא קבוע). כשל, ניסיונות, backoff, `dead`, 429 וכל שאר מנגנון המיילים כבסיפור 2.1; שורה `dead` מופיעה למנהלת ב-`GET /admin/email-outbox` והבקשה ממשיכה להופיע בתור (כי התור אינו תלוי במייל). בקשה ששינתה מצב אחרי האימות (למשל `expired` בהסרת הפרופיל): ההתראה עדיין נשלחת כל עוד `verified_at` קיים; אם `verified_at` ריק: `dead` עם קטגוריה `request-not-verified`. אם שליחה לאחת הכתובות נכשלת, השורה כולה מנוסה שוב (at-least-once; כתובת שכבר קיבלה עלולה לקבל שוב); אם אין כתובות מנהלת בתצורה: `dead` עם `no-recipient`. **תור הבקשות:** `GET /admin/contact-requests` (`AdminPolicy`, `Cache-Control: no-store`): מציג אך ורק בקשות שאומתו (`verified_at` לא ריק), לעולם לא `awaiting_email_verification` ולעולם לא בקשה שפגה בלי אימות. פרמטרים: `status` (`open` (ברירת מחדל: `submitted`, `sent_to_participant`, `participant_approved`), `submitted`, `sent_to_participant`, `participant_approved`, `participant_declined`, `delivered`, `expired`, `all`; ערך אחר: 400), `limit` (1 עד 100, ברירת מחדל 50, מעל 100 נחתך), `offset` (0 ומעלה; שלילי: 400); סדר: הישנה ביותר שאומתה קודם (`verified_at` ואז מזהה); `{ items, total }`. פריט: `requestId`, `visitorName`, `visitorEmail` (הכתובת המאומתת), `reason` (טקסט פשוט כפי שנשמר), `profileId`, `profileName` ו-`productName` (מהגרסה המאושרת האחרונה, ואם אין, מהגרסה האחרונה), `cohortName`, `profileVisibility`, `createdAt`, `verifiedAt`, `status`, ושני אישורים נפרדים: `participantDecision` (`approved`, `declined`, או `null` טרם התקבלה החלטה), `participantDecidedAt`, ו-`managerApprovedAt` (או `null`); בלי שום פרט קשר של המשתתפת (לא מייל ולא נייד, ולא קריאה מ-`ParticipantPrivate`). עמודות חדשות ב-`contact_requests` ובמיגרציה: `participant_decision` (עם CHECK `approved`/`declined`), `participant_decided_at`, `manager_approved_at` (nullable; ימולאו בסיפורים 3.3 ו-3.4). `GET /admin/contact-requests/{requestId}` (GUID `D`; 404 אחיד לא קיים, פגום, או בקשה שלא אומתה) מחזיר את אותו פריט. שני ה-`GET` דורשים `AdminPolicy`; אנונימי: 401, מחובר שאינו אדמין: 403. אין עדיין פעולות כתיבה על בקשות. ללא PII ביומנים. עמודות ואינדקסים בהתאם (למשל `(verified_at, id)` למצבים הפתוחים).

**Never:** אין פנייה למשתתפת והחלטתה (3.3), אין מסירת פרטי קשר ואישור מנהלת (3.4), אין מסכי לקוח, אין חשיפת בקשה שלא אומתה, אין פרטי קשר של משתתפת בשום תגובה או מייל, אין שליחת מייל ישירה מנתיב האימות, אין כתובת מנהלת בקוד או בריפו.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| אימות בקשה | המעבר ל-`submitted` | בטרנזקציה אחת נכתבת שורת Outbox אחת עם `DeduplicationKey` ייחודי, בלי PII | N/A |
| אימות חוזר ומרוץ | חמש אימותים מקבילים | שורת Outbox אחת | `ON CONFLICT DO NOTHING` |
| כשל בכתיבת ההתראה | שורה לא ניתנת לכתיבה | הבקשה נשארת `awaiting` והטרנזקציה מתבטלת | N/A |
| מייל התראה | worker | טקסט פשוט למנהלת, עם מחזור, שם פרופיל וקישור לתור, בלי שם מבקר וסיבה ובלי פרטי משתתפת | N/A |
| כמה כתובות מנהלת | `a@x.co,b@x.co` | נשלח לכל כתובת (מייל אחד לכל כתובת) ושורה אחת מסומנת `sent` רק כששני המיילים נשלחו | N/A |
| תצורה פגומה | כתובת לא תקינה או יותר מ-5 | האתחול נכשל בשגיאה ברורה | פתיחה |
| בלי כתובות | המשתנה חסר ב-Development | ברירת מחדל `admin@example.test`; מחוץ ל-Development האתחול נכשל | N/A |
| כשל סופי | חמישה ניסיונות כושלים | `dead` גלוי ב-`/admin/email-outbox`; הבקשה עדיין בתור | N/A |
| תור | אדמין, בקשות `submitted` ואילך | הבקשות המאומתות עם הסדר והשדות לפי המפרט | N/A |
| לא מאומתת | `awaiting_email_verification` או `expired` שלא אומתה | אינה בתור ולא ב-`GET` לפי מזהה (404) | N/A |
| סינון סטטוס | `status=expired`, `all`, `participant_declined` | סינון נכון; ערך מוזר: 400 | N/A |
| עימוד | `limit=2&offset=1` | חלק נכון עם `total` כולל; `limit` מעל 100 נחתך | `limit` קטן מ-1 או `offset` שלילי: 400 |
| שני אישורים | בקשה `submitted` | `participantDecision: null`, `managerApprovedAt: null`, ושני האישורים בשדות נפרדים | N/A |
| פרופיל שהוסר | בקשה שאומתה ואז הפכה `expired` בהסרה | מופיעה עם `status: expired` ו-`profileVisibility: removed` רק ב-`all` או `expired` | N/A |
| פרטיות | תגובות, מייל, Outbox, יומן | אין פרט קשר של משתתפת; Outbox בלי PII | N/A |
| הרשאה | אנונימי: 401; מחובר שאינו אדמין: 403 | נדחה בשני ה-`GET` | N/A |
| `GET` לפי מזהה | GUID `D` של בקשה מאומתת | הפריט המלא; מזהה פגום או לא קיים: 404 אחיד | N/A |

</intent-contract>

## Code Map

- `server/Infrastructure/ContactRequestRepository.cs` -- `VerifyAsync` (הטרנזקציה שמעבירה ל-`submitted`, נעילות לפי הסדר פרופיל, בקשה, אסימונים) ו-`OpenStatesSql`; כאן מוסיפים את כתיבת שורת ההתראה באותה טרנזקציה. `server/Features/Contacts/ContactRequestService.cs` -- שירות הבקשות; להוסיף את שאילתות התור והפריט.
- `server/Domain/Contacts/ContactRequest.cs` -- `ContactRequest` ו-`ContactRequestStatus`; להוסיף `ParticipantDecision`, `ParticipantDecidedAt`, `ManagerApprovedAt` ומיגרציה (README: `dotnet-ef`). `server/Domain/Email/EmailOutboxEntry.cs` -- `ContactVerificationTemplate`, `RequestId`, עוזר מפתח dedup; להוסיף `ContactRequestNotificationTemplate`.
- `server/Features/Email/EmailOutboxProcessor.cs`, `ContactVerificationEmail.cs`, `IEmailOutbox.cs`, `server/Infrastructure/EmailOutboxRepository.cs`, `EmailOptions.cs`, `EmailOutboxWorker.cs` -- מנגנון התבניות והשליחה (claim ו-lease, `RecordFailure`, dead); להכליל ל-`contact-request-notification` ולנתח את `ADMIN_NOTIFICATION_EMAIL` באותו דפוס של `EMAIL_FROM`.
- `server/Api/Endpoints/ProfileAdminEndpoints.cs`, `SubmissionEndpoints.cs` -- תבנית נקודות אדמין: `AdminPolicy`, `{ items, total }`, פרמטרי `limit`/`offset` עם 400, GUID `D` ו-404 אחיד, `no-store`.
- `server/Infrastructure/ProfileAdminRepository.cs` -- דוגמה להקרנת שם ומוצר מהגרסה המאושרת האחרונה ושם מחזור בשאילתה אחת.
- `server/Api/Endpoints/EmailOutboxEndpoints.cs` -- `GET /admin/email-outbox` להצגת שורות `dead`.
- `server/Tests/ContactRequestTests.cs`, `EditLinkTests.cs`, `ProfileRemovalTests.cs`, `AuthTestSupport.cs` -- בקשות ופרופילים וירידה ל-`FakeEmailSender`, הרצת worker, אדמין עם TOTP ו-anti-forgery; להרחיב.
- אין לשנות: `client/`, נקודות הבקשה והאימות הציבוריות מעבר לכתיבת ההתראה, נקודות המשתתפת.

## Tasks & Acceptance

**Execution:**
- `server/Domain/` ו-`server/Infrastructure/` -- עמודות `participant_decision`, `participant_decided_at`, `manager_approved_at`, CHECK ואינדקס, תבנית `ContactRequestNotificationTemplate`, ומיגרציה -- AR7
- `server/Infrastructure/` -- כתיבת שורת Outbox להתראה בטרנזקציית המעבר ל-`submitted` -- AR8, AD-4
- `server/Features/Email/` ו-`server/Infrastructure/` -- תבנית `contact-request-notification`, ניתוח `ADMIN_NOTIFICATION_EMAIL`, שליחה לכל כתובת, וקטגוריות כשל -- FR-14, AR15
- `server/Features/Contacts/` ו-`server/Api/Endpoints/` -- `GET /admin/contact-requests` ו-`GET /admin/contact-requests/{requestId}` עם סינון, עימוד והקרנה -- FR-14, UX-DR13
- `server/Tests/` -- בדיקות אינטגרציה לשורות המטריצה, כולל אטומיות ההתראה, אחידות הכתיבה במרוץ, תצורה, והעדר PII -- AR21
- `README.md` -- הנקודות, `ADMIN_NOTIFICATION_EMAIL`, שדות התור והעמודות החדשות -- תפעול

**Acceptance Criteria:**
- Given בקשה שעברה ל-`submitted`, when המעבר נשמר, then באותה טרנזקציה נכתבת שורת `EmailOutbox` עם `DeduplicationKey` ייחודי, והמנהלת מקבלת מייל התראה בטקסט פשוט, בלי פרטי קשר של משתתפת.
- Given מסך הניהול, when המנהלת פותחת את הבקשות, then מוצגות הבקשות המאומתות עם שם המבקר, כתובתו המאומתת, הסיבה, הפרופיל המבוקש, התאריך והמצב, ולכל בקשה אישור המשתתפת ואישור המנהלת בנפרד.
- Given משתמש שאינו אדמין, when פונה לנקודות הבקשות, then 403, וקריאה אנונימית נדחית (401).
- Given מייל התראה שנכשל סופית, when הוא עובר ל-`dead`, then הבקשה עדיין מופיעה בתור והכשל גלוי למנהלת.

## Spec Change Log

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: כ-50 ממצאים מארבע שכבות (רבים כפולים) — high 0, medium 5, low 7, false 0
- findings:
  - `[medium]` `[patch]` שליחה למספר נמענים: עצירה בכשל קבוע או 429, מפתח אידמפוטנטיות יציב לכל נמען, חריגה לכל נמען, ובדיקות איחוד — תוקנו
  - `[medium]` `[patch]` `total` בתור אינו תואם לפריטים (צירוף חסר בספירה), ושם הפרופיל נופל למחרוזת ריקה — אוחדו הצירופים ונוסף fallback
  - `[medium]` `[patch]` כשל בהתראה גלוי רק ב-`/admin/email-outbox` — נוסף `notificationStatus` בפריט התור
  - `[medium]` `[patch]` `ADMIN_NOTIFICATION_EMAIL` חובה גם במופעי web בלי worker — נדרש רק כשה-worker רץ; ניתוח סובלני לפסיק מסיים
  - `[low]` `[patch]` אילוצי CHECK לעמודות ההחלטה, ניקוי trigger בבדיקה, ובדיקות tie-break ו-`request_id` ריק, README — תוקנו
  - `[low]` `[defer]` מסך לקוח וקישור במייל, at-least-once לכמה כתובות, חיפוש ומיון — נרשמו ב-deferred
  - `[low]` `[reject]` שני אינדקסים חופפים (בהיקף הנוכחי אינו משפיע), ניקוי שמות פרופיל (כבר מוגבלים ובלי תווי בקרה), `count` ו-`page` בשתי שאילתות, `offset` ללא תקרה, שינוי UTC — לא סביר שייפגעו בשימוש רגיל
## Verification

**Commands:**
- `dotnet build server` -- expected: 0 שגיאות
- `dotnet test server` -- expected: כל הבדיקות עוברות (דורש Docker; כ-5 דקות, מומלץ להריץ ברקע)

## Auto Run Result

- **שינוי:** בטרנזקציית המעבר ל-`submitted` נכתבת שורת `EmailOutbox` להתראה (`contact-request-notification`, מפתח dedup ייחודי, בלי PII); ה-worker שולח מייל טקסט פשוט למנהלת (`ADMIN_NOTIFICATION_EMAIL`, עד 5 כתובות) עם מחזור, שם פרופיל וקישור לתור, בלי פרטי מבקר או משתתפת. `GET /admin/contact-requests` ו-`GET /admin/contact-requests/{id}` (אדמין בלבד) מציגים רק בקשות שאומתו, עם סינון סטטוס, עימוד, אישור משתתפת ואישור מנהלת בשדות נפרדים (עמודות חדשות), ו-`notificationStatus` שמבהיר אם ההתראה נכשלה.
- **קבצים:** `server/Features/Contacts/ContactRequestQueueService.cs`, `server/Infrastructure/{ContactRequestQueueRepository,ContactRequestRepository,EmailOutboxRepository,EmailOptions}.cs`, `server/Features/Email/{EmailOutboxProcessor,ContactRequestNotificationEmail}.cs`, `server/Api/Endpoints/ContactRequestAdminEndpoints.cs`, מיגרציות `ContactRequestQueue` ו-`ContactRequestDecisionChecks`, `server/Tests/ContactRequestQueue*Tests.cs`, `render.yaml`, `README.md`.
- **סקירה:** 4 תיקוני medium (שליחה למספר נמענים, `total` מול פריטים, `notificationStatus`, חובת `ADMIN_NOTIFICATION_EMAIL` למופעי worker בלבד) ו-1 low קבוצתי הוחלו. 3 פריטים נדחו ל-`deferred` (מסך לקוח וקישור במייל, at-least-once לכמה כתובות, חיפוש ומיון). השאר נדחו כמתועד ביומן.
- **המלצה לסקירת המשך:** כן. סיכונים שלא אומתו: לא נבדקה שליחה אמיתית מול Mailpit או Resend; במקרה של כשל קבוע בחלק מהנמענים השורה נחשבת נשלחה כל עוד לפחות נמען אחד קיבל.
- **אימות:** `dotnet build server` ללא שגיאות, `dotnet test server`: 749 מתוך 749 עוברות מול Postgres אמיתי (ריצת הסוכן).
- **סיכונים שנותרו:** אין מסך ניהול בלקוח (סיפור 5.9); `ADMIN_NOTIFICATION_EMAIL` חובה במופעי worker מחוץ ל-Development.
