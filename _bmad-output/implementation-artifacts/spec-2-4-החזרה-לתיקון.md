---
title: 'סיפור 2.4: החזרה לתיקון'
type: 'feature'
created: '2026-10-22'
status: 'done'
baseline_revision: '0529c46ef196bc0aa23755c2667bcd7827d1b4be'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-resume_app-2026-10-21/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-2-1-קישור-אישי-במייל.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-2-2-כניסה-ועריכה-כגרסה-חדשה.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-2-3-סקירת-עדכון.md'
warnings: []
deferred:
  - summary: >-
      שינוי מייל או נייד ממתין שנישא על גרסה שהוחזרה אינו ניתן לטיפול על ידי המנהלת עד שהמשתתפת מגישה מחדש
    evidence: |-
      נקודות הניהול לשינוי קשר מקבלות רק גרסה `pending` או `approved`; ה-prefill בטופס מעביר את הבקשה בהגשה מחדש. מתועד ב-README
    location: >-
      server/Infrastructure/SubmissionReviewRepository.cs
    severity: low
  - summary: >-
      הערת ההחזרה נשמרת ללא הגבלת זמן, ואין נקודה שמציגה למנהלת אחרי השליחה מה נשלח
    evidence: |-
      ההערה לא נכנסת ל-`audit_log` ולא ל-Outbox בכוונה; מדיניות שמירה בסיפור 4.3
    location: >-
      server/Domain/Profiles/Profile.cs
    severity: low
  - summary: >-
      באנר ההחזרה וכפתור "עריכה והגשה מחדש" בלקוח, וטופס ההחזרה למנהלת, טרם נבנו
    evidence: |-
      השרת מחזיר `returned: { note, returnedAt }`; סיפורים 5.6 ו-5.8
    location: >-
      client/ (טרם קיים)
    severity: medium---

<intent-contract>

## Intent

**Problem:** המנהלת יכולה רק לאשר הגשה, ולא להחזיר אותה למשתתפת עם הערה כדי שתתקן ותגיש מחדש.

**Approach:** להוסיף ב-`server/` נקודת ניהול `POST /admin/submissions/{revisionId}/return` שמעבירה גרסה `pending` ל-`returned` עם הערה (עדכון מותנה, `AuditLog`), כותבת שורת `EmailOutbox` שמובילה למייל טקסט פשוט עם ההערה וקישור אישי חדש לעריכה, ולהרחיב את `GET /me/profile` כך שיציג את ההערה כשהגרסה האחרונה הוחזרה. הגרסה שהוחזרה אינה מוצגת בציבור, וגרסה מאושרת קודמת נשארת מוצגת. חלק השרת בלבד.

## Boundaries & Constraints

**Always:** הנקודה תחת `/admin/submissions` דורשת `AdminPolicy`, anti-forgery ו-`Origin`. גוף: `{ "note": "..." }`; ההערה חובה, אחרי trim 1 עד 1000 תווים, טקסט פשוט: תווי בקרה (חוץ מ-`\n`, `\t`) ותווים נסתרים מסוכנים (`UnsafeCharacters`) נדחים ב-400 על השדה `note`; שורות מנורמלות ל-`\n`. מזהה הגרסה GUID בצורה `D` בלבד, 404 אחיד לא קיים או פגום. בטרנזקציה אחת עם נעילת שורת הפרופיל (כבאישור): עדכון מותנה של הגרסה `pending` ל-`returned` (עמודות חדשות `return_note` ו-`returned_at` על `ProfileRevision`), כתיבת `AuditLog` (`return-revision`, סוג ישות `profile-revision`, מזהה הגרסה, מזהה המנהלת, בלי הערה או תוכן אישי), וכתיבת שורת `EmailOutbox` (`TemplateKey = "submission-returned"`, מזהה הפרופיל ומזהה הגרסה בעמודה חדשה `revision_id`, `DeduplicationKey = "submission-returned:{revisionId}"`, בלי הערה, אסימון, מייל או נייד בשורה). אם הגרסה אינה `pending` (אושרה, הוחלפה, הוחזרה כבר): 409 `problem+json` עם `code: "revision-not-pending"` ו-`latestPendingRevisionId` (כבאישור), ושום דבר לא משתנה ואין שורת Outbox. מקביליות: אישור והחזרה, או שתי החזרות, מקבילים לאותה גרסה: בדיוק אחת מצליחה. תגובת הצלחה: 200 עם `revisionId` ו-`status: "returned"`, בלי מייל או נייד. המייל (`submission-returned`): תבנית חדשה באותו מנגנון של `edit-link`: טקסט פשוט (`text/plain; charset=utf-8`), נושא קבוע מהתבנית, בגוף ההערה (אחרי ניקוי נוסף: רק טקסט, בלי HTML, מגבלת אורך, שורות חדשות מותרות בגוף בלבד) ושם המחזור, והקישור האישי `{PUBLIC_BASE_URL}/edit#token=...` בפרגמנט `#`. ההערה והאסימון נפתרים רק בזמן השליחה (ההערה נקראת מהגרסה לפי `revision_id`, האסימון מונפק כבסיפור 2.1 עם ביטול קודמים). כל השאר כבסיפור 2.1: claim ו-lease, ניסיונות, `dead` גלוי למנהלת, 429 כדחייה זמנית. פרופיל שהוסר (`Visibility = removed`) או בלי שורת `participant_private`: השורה `dead` עם קטגוריה מתאימה (כבסיפור 2.1). **צד המשתתפת:** `GET /me/profile` מקבל שדה `returned` (`{ note, returnedAt }` או `null`): הוא מוחזר כשהגרסה האחרונה של הפרופיל (בסדר `SubmittedAt`) היא `returned` ואין גרסה `pending` חדשה ממנה; אחרת `null`. מקור העריכה כשאין `pending`: הגרסה `returned` האחרונה אם היא חדשה מהגרסה המאושרת האחרונה, אחרת המאושרת (כך שהמשתתפת עורכת את מה שהוחזר). `PUT /me/profile` ממשיך כבסיפור 2.2 ויוצר גרסה `pending` חדשה; הגרסה שהוחזרה נשארת `returned` (היסטוריה) והערתה לא מוצגת עוד אחרי שיש `pending` חדשה. הגרסה שהוחזרה אינה מופיעה בשום נקודה ציבורית ואינה משנה את הגלריה והפרופיל הציבורי; גרסה מאושרת קודמת נשארת מוצגת. הגרסה שהוחזרה אינה מופיעה בתור הסקירה (`pending` בלבד). שינוי מייל או נייד ממתין שנישא על גרסה שהוחזרה אינו נעלם בשקט: ה-prefill של הטופס כבסיפור 2.2 ממשיך לשאת אותו. ללא PII ביומנים. מיגרציית EF Core.

**Never:** אין אישור שינוי קשר, אין הסרת פרופיל (2.5), אין החלת ההחזרה על גרסה שאינה `pending`, אין מסכי לקוח, אין שליחת מייל מנתיב הבקשה (רק כתיבת שורת Outbox), אין הערה או אסימון ב-Outbox, אין שינוי של הגרסה המאושרת או של ה-`Visibility`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| החזרה | אדמין, גרסה `pending`, הערה תקינה | 200; הגרסה `returned` עם `return_note`; שורת Outbox ו-`AuditLog` | N/A |
| הערה חסרה | ריקה או רווחים בלבד | 400 על `note`; לא נוצר דבר | ולידציה |
| הערה ארוכה | 1001 תווים | 400 על `note` | ולידציה |
| תווים מסוכנים | NUL, bidi, zero-width | 400 על `note` | ולידציה |
| לא ממתינה | `approved`, `superseded`, `returned` | 409 `revision-not-pending` עם `latestPendingRevisionId`; בלי Outbox | 409 |
| לא קיים | GUID אקראי או פורמט לא קנוני | 404 אחיד | N/A |
| מקביליות | אישור והחזרה במקביל; חמש החזרות במקביל | בדיוק הצלחה אחת; שורת Outbox ו-`AuditLog` אחת | נעילה ועדכון מותנה |
| המייל | הרצת ה-worker | טקסט פשוט עם ההערה, שם המחזור וקישור אישי עם אסימון בפרגמנט; אסימון חדש מבטל קודם | N/A |
| פרטיות Outbox | שורת `email_outbox` | בלי הערה, אסימון, מייל או נייד | N/A |
| צד משתתפת | הגרסה האחרונה `returned` | `GET /me/profile` מחזיר `returned: { note, returnedAt }` ומקור העריכה הוא הגרסה שהוחזרה | N/A |
| הגשה מחדש | `PUT /me/profile` אחרי החזרה | גרסה `pending` חדשה; `returned` נשארת היסטוריה; `GET` מחזיר `returned: null` | N/A |
| תצוגה ציבורית | פרופיל עם גרסה מאושרת וגרסה שהוחזרה | הגלריה והפרופיל מציגים את המאושרת | N/A |
| פרופיל בלי מאושרת | הגשה ראשונה שהוחזרה | לא מוצג בציבור | N/A |
| תור | גרסה `returned` | אינה בתור הסקירה | N/A |
| פרופיל שהוסר | החזרה ואז הוסר לפני שליחה | שורת Outbox `dead` עם קטגוריה | N/A |
| הרשאה | אנונימי: 401; מחובר שאינו אדמין: 403 | נדחה, ללא שינוי | N/A |
| הגנת בקשה | בלי anti-forgery או Origin זר | נדחית | N/A |

</intent-contract>

## Code Map

- `server/Api/Endpoints/SubmissionEndpoints.cs` -- קבוצת `/admin/submissions`, `approve` (409 `revision-not-pending` עם `latestPendingRevisionId`, אחסון `NameIdentifier`, `AdminPolicy`, ניתוח GUID `D`); להוסיף `return` באותו דפוס.
- `server/Features/Profiles/SubmissionReviewService.cs`, `server/Infrastructure/SubmissionReviewRepository.cs` -- `ApproveAsync` (טרנזקציה, נעילת שורת הפרופיל, עדכון מותנה, `AuditLog`); להוסיף `ReturnAsync` בדפוס זהה כולל כתיבת שורת Outbox בטרנזקציה.
- `server/Domain/Profiles/Profile.cs` -- `ProfileRevision`, `RevisionStatus.Returned` (כבר קיים); להוסיף `ReturnNote`, `ReturnedAt` ומיגרציה (README: `dotnet-ef`). `server/Domain/Audit/AuditLogEntry.cs` -- קבועי פעולות; להוסיף `return-revision`.
- `server/Domain/Email/EmailOutboxEntry.cs`, `server/Infrastructure/EmailOutboxRepository.cs` -- ישות Outbox ו-`PersonalTokenIssuer`, `EnqueueAsync` (דוגמה ב-`EditLinkService`); להוסיף `RevisionId` ומיגרציה ותבנית חדשה `submission-returned`.
- `server/Features/Email/EmailOutboxProcessor.cs`, `EditLinkEmail.cs`, `EditLinkService.cs` -- מנגנון התבניות כרגע מטפל רק ב-`edit-link` (`CategoryUnknownTemplate` לכל השאר); להכליל לרישום תבניות (`TemplateKey` לבונה הודעה) ולהוסיף את `submission-returned`.
- `server/Api/Endpoints/ParticipantEndpoints.cs`, `server/Infrastructure/ProfileRepository.cs` -- `GET /me/profile` ומקור העריכה (pending אחרת מאושרת); להרחיב לשדה `returned` ולבחירת המקור.
- `server/Features/Profiles/ProfileService.cs` -- ולידציה משותפת; `UnsafeCharacters` לבדיקת ההערה.
- `server/Tests/SubmissionReviewTests.cs`, `ContactChangeReviewTests.cs`, `ParticipantEditTests.cs`, `EditLinkTests.cs`, `AuthTestSupport.cs` -- אדמין עם TOTP ו-anti-forgery, יצירת גרסאות במצבים שונים, `FakeEmailSender`, הרצת worker בבדיקות; להרחיב.
- אין לשנות: `client/`, הנקודות הציבוריות, התנהגות האישור וסשן המשתתפת מעבר לנדרש.

## Tasks & Acceptance

**Execution:**
- `server/Domain/` ו-`server/Infrastructure/` -- `ReturnNote`, `ReturnedAt` ב-`ProfileRevision`, `RevisionId` ב-`EmailOutboxEntry`, ומיגרציה -- AR8, FR-18
- `server/Features/Email/` -- רישום תבניות ל-`TemplateKey`, ותבנית `submission-returned` (טקסט פשוט, הערה, מחזור, קישור בפרגמנט) -- AD-4, AD-15
- `server/Features/Profiles/` ו-`server/Infrastructure/` -- `ReturnAsync` בטרנזקציה עם נעילה, עדכון מותנה, `AuditLog` ושורת Outbox -- AR6, AD-14, AD-22
- `server/Api/Endpoints/` -- `POST /admin/submissions/{revisionId}/return` ו-`returned` ב-`GET /me/profile` ובחירת מקור העריכה -- FR-18
- `server/Tests/` -- בדיקות אינטגרציה לשורות המטריצה כולל מרוץ, פרטיות Outbox, שליחת המייל בפועל דרך ה-worker, וצד המשתתפת והציבור -- AR6, AR21
- `README.md` -- הנקודה, השדה `returned`, התבנית והתנהגות העריכה -- תפעול

**Acceptance Criteria:**
- Given הגשה ממתינה, when המנהלת בוחרת "החזרה לתיקון" וכותבת הערה, then הגרסה הופכת `returned` בעדכון מותנה, והפעולה נרשמת ב-`AuditLog`.
- Given הגשה שהוחזרה, when המערכת ממשיכה, then נכתבת שורת `EmailOutbox` והמשתתפת מקבלת מייל עם ההערה (טקסט פשוט, מנוקה) וקישור אישי חדש לעריכה.
- Given פרופיל שהוחזר, when המשתתפת פותחת את הקישור, then `GET /me/profile` מחזיר את הערת המנהלת ומקור העריכה הוא הגרסה שהוחזרה, וההגשה מחדש יוצרת גרסה `pending`.
- Given הגשה שהוחזרה, when מבקר פותח את האתר, then היא אינה מוצגת, ואם הייתה גרסה מאושרת קודמת היא נשארת מוצגת.
- Given משתמש שאינו אדמין, when מנסה להחזיר הגשה, then 403.

## Spec Change Log

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: כ-45 ממצאים מארבע שכבות (רבים כפולים) — high 0, medium 4, low 6, false 0
- findings:
  - `[medium]` `[patch]` ניקוי ההערה במייל מוחק `<` ו-`>` ומשנה משמעות בעוד שהקלט מקבל אותם — ההערה נשלחת כפי שנכתבה, ומקור יחיד לאורך
  - `[medium]` `[patch]` כל כשלי המייל מתכנסים לקטגוריה אחת — קטגוריות נפרדות (`revision-missing`, `revision-not-returned`, `empty-note`), ו-"24 שעות" נגזר מתוקף האסימון
  - `[medium]` `[patch]` חסרות בדיקות: כשלי worker לשורות `submission-returned`, שמירת תמונה בהגשה מחדש אחרי החזרה, וקלט תווים נסתרים ב-escape — נוספו
  - `[low]` `[patch]` טבלת README שבורה ותיעוד שינוי קשר שנישא, אורך בתווי UTF-16 ושמירת ההערה — עודכנו
  - `[low]` `[defer]` שינוי קשר על גרסה שהוחזרה, שמירת ההערה, מסכי הלקוח — נרשמו ב-deferred
  - `[low]` `[reject]` 400 לפני 404, אורך בתווי UTF-16, שאילתות מרובות במקור העריכה, מרוץ approve מול return שמקבל כל מנצח, תיאום זמנים שווה בין returned ל-approved — לא סביר שייפגעו בשימוש רגיל או שהתנהגות מתועדת
## Verification

**Commands:**
- `dotnet build server` -- expected: 0 שגיאות
- `dotnet test server` -- expected: כל הבדיקות עוברות (דורש Docker)

## Auto Run Result

- **שינוי:** `POST /admin/submissions/{revisionId}/return` (אדמין בלבד, גוף `{ note }`): בטרנזקציה אחת עם נעילת הפרופיל, גרסה `pending` הופכת `returned` (עם `return_note` ו-`returned_at`), נכתבים `AuditLog` (`return-revision`) ושורת `EmailOutbox` (`submission-returned`, בלי הערה או אסימון). ה-worker שולח מייל טקסט פשוט עם ההערה כפי שנכתבה, שם המחזור וקישור אישי חדש `/edit#token=...` (אסימון חדש מבטל קודמים), וכשלים מובילים ל-`dead` עם קטגוריות מפורשות. `GET /me/profile` מחזיר `returned: { note, returnedAt }` כשהגרסה האחרונה הוחזרה, ומקור העריכה הוא הגרסה שהוחזרה; הגשה מחדש יוצרת `pending` חדשה. הגרסה שהוחזרה אינה מוצגת בציבור ואינה בתור, וגרסה מאושרת קודמת נשארת מוצגת. 409 `revision-not-pending` עם `latestPendingRevisionId` כשהגרסה אינה ממתינה.
- **קבצים:** `server/Features/Profiles/SubmissionReviewService.cs`, `server/Infrastructure/{SubmissionReviewRepository,ProfileRepository,EmailOutboxRepository}.cs`, `server/Features/Email/{EmailOutboxProcessor,SubmissionReturnedEmail}.cs`, `server/Api/Endpoints/{SubmissionEndpoints,ParticipantEndpoints}.cs`, `server/Domain/{Profiles,Email,Audit}/*`, מיגרציית `SubmissionReturn`, `server/Tests/ReturnForCorrectionTests.cs`, `README.md`.
- **סקירה:** 3 תיקוני medium (ניקוי ההערה במייל שהסיר `<` ו-`>`, קטגוריות כשל נפרדות ו-TTL מהתצורה, בדיקות חסרות) ו-1 low הוחלו. 3 פריטים נדחו ל-`deferred` (שינוי קשר על גרסה שהוחזרה, שמירת ההערה, מסכי לקוח). השאר נדחו כמתועד ביומן.
- **המלצה לסקירת המשך:** כן. סיכונים שלא אומתו: שינוי מייל או נייד ממתין שנישא על גרסה שהוחזרה אינו ניתן לטיפול על ידי המנהלת עד שהמשתתפת מגישה מחדש; לא נבדקה שליחה אמיתית מול Mailpit או Resend.
- **אימות:** `dotnet build server` ללא שגיאות, `dotnet test server`: 578 מתוך 578 עוברות מול Postgres אמיתי.
- **סיכונים שנותרו:** אין עדיין טופס החזרה וטופס עריכה בלקוח (סיפורים 5.6 ו-5.8).
