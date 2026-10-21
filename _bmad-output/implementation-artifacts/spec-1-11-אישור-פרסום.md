---
title: 'סיפור 1.11: אישור פרסום'
type: 'feature'
created: '2026-10-21'
status: 'done'
baseline_revision: 'd5edf9d2900dbbf50e5389748272eb303d1b7046'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-resume_app-2026-10-21/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-1-10-תור-סקירה-ופרטי-הגשה.md'
warnings: []
deferred:
  - summary: >-
      `audit_log` ללא הגנת append-only (אפשר לעדכן ולמחוק), בלי אינדקס לפי ישות או מבצע ובלי נקודת קריאה
    evidence: |-
      AD-22 מגדיר רישום בלבד; הקשחה (הרשאות DB או trigger), אינדקסים ומדיניות שמירה בסיפור 4.2
    location: >-
      server/Infrastructure/AppDbContext.cs
    severity: medium
  - summary: >-
      אין בדיקת כשל באמצע הטרנזקציה (rollback של כל הכתיבות) כי אין seam להזרקת תקלה
    evidence: |-
      הטרנזקציה נבדקת רק בהצלחה, 409 ומרוץ; להוסיף הזרקת תקלה לבדיקה
    location: >-
      server/Infrastructure/SubmissionReviewRepository.cs
    severity: low
  - summary: >-
      בהרצה מלאה אחת מתוך ארבע נכשלה בדיקה אחת באופן שלא שוחזר בשתי הרצות חוזרות
    evidence: |-
      הכשל לא תועד (הפלט נחתך); לעקוב אם חוזר ולהוסיף בידוד לבדיקות מקביליות
    location: >-
      server/Tests/
    severity: low
  - summary: >-
      אישור נעשה בלי בדיקת שינוי מייל או נייד ממתין (AR17) ובלי הצגת גרסה מפורסמת לצד המוצעת
    evidence: |-
      מטופל בסיפורים 2.2 ו-2.3
    location: >-
      server/Features/Profiles/SubmissionReviewService.cs
    severity: low---

<intent-contract>

## Intent

**Problem:** הגשות נשארות ממתינות לנצח: אין דרך שהמנהלת תאשר הגשה כך שהפרופיל יהפוך לפומבי, ואין רישום של מי אישר מה.

**Approach:** להוסיף ב-`server/` נקודת ניהול `POST /admin/submissions/{revisionId}/approve` שמעבירה גרסה ממתינה ל-`approved`, מצביעה את `ApprovedRevisionId` של הפרופיל אליה ומגדירה `Visibility = active`, כעדכון מותנה בטרנזקציה אחת, וכותבת שורה ל-`AuditLog` בלי תוכן אישי. חלק השרת בלבד.

## Boundaries & Constraints

**Always:** הנקודה דורשת `AdminPolicy` (ולכן גם TOTP שהושלם) ואת הגנות anti-forgery ו-`Origin` הקיימות. מזהה הגרסה מפורש בנתיב (GUID בצורה `D` בלבד). המעבר הוא `UPDATE ... WHERE status = 'pending'` על גרסה אחת בדיוק; אם לא עודכנה שורה: 409 `problem+json` כשהגרסה קיימת ואינה `pending`, ו-404 אחיד כשהמזהה אינו תקין או אינו קיים. בהצלחה, באותה טרנזקציה: הגרסה הופכת `approved`; `Profile.ApprovedRevisionId` מצביע עליה; `Profile.Visibility = active`; אם הייתה לפרופיל גרסה מאושרת קודמת היא הופכת `superseded` (עדכון מותנה על `status = 'approved'`). כשל באמצע מבטל הכול. מקביליות: שתי בקשות אישור במקביל לאותה גרסה - רק אחת מצליחה (200) והאחרות 409, והמצב בסיום עקבי. נוצרת שורת `AuditLog` (טבלה חדשה, מיגרציה) באותה טרנזקציה עם: מזהה המנהלת, זמן, פעולה (`approve-revision`), סוג ישות ומזהה הגרסה בלבד, בלי שם, מייל, נייד או טקסט. אישור הפרסום אינו נוגע ב-`ParticipantPrivate` ואינו מאשר מסירת פרטי קשר. תגובת הצלחה: `200` עם `revisionId`, `profileId` ו-`status: "approved"`. אין החזרת מייל או נייד.

**Never:** אין החזרה לתיקון (2.4), אין הסרה (2.5), אין שינוי מייל או נייד ממתין (2.3), אין שליחת מיילים, אין שאילתה ציבורית (1.12), אין מסכי לקוח. אין אישור המוני.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| אישור תקין | אדמין, גרסה `pending` | 200; הגרסה `approved`, `ApprovedRevisionId` מצביע אליה, `Visibility = active`, שורת `AuditLog` | N/A |
| אישור עדכון | לפרופיל גרסה מאושרת קודמת | הקודמת הופכת `superseded`, החדשה `approved`, המצביע מוחלף | N/A |
| כבר מאושרת | אישור חוזר לאותה גרסה | 409, שום דבר לא משתנה, אין שורת `AuditLog` נוספת | 409 |
| לא ממתינה | גרסה `returned` או `superseded` | 409 | 409 |
| לא קיים | GUID אקראי או פורמט לא קנוני | 404 אחיד | N/A |
| מקביליות | חמש בקשות מקבילות לאותה גרסה | בדיוק 200 אחד ו-409 לשאר; מצב עקבי; שורת `AuditLog` אחת | אטומיות |
| פרופיל שהוסר | `Visibility = removed` וגרסה ממתינה | האישור מחזיר `Visibility = active` | N/A |
| הרשאה | אנונימי: 401; מחובר שאינו אדמין: 403 | נדחה, שום דבר לא משתנה | N/A |
| הגנת בקשה | בלי אסימון anti-forgery או עם Origin זר | נדחית | N/A |
| פרטיות | תוכן `AuditLog` ותגובה | בלי מייל, נייד או טקסט אישי | N/A |
| פרטי קשר | אחרי אישור | `ParticipantPrivate` ללא שינוי | N/A |

</intent-contract>

## Code Map

- `server/Api/Endpoints/SubmissionEndpoints.cs` -- קבוצת `/admin/submissions` עם `AdminPolicy`, ניתוח GUID בצורה `D`, 404 אחיד; להוסיף את `POST .../approve`.
- `server/Features/Profiles/SubmissionReviewService.cs`, `server/Infrastructure/SubmissionReviewRepository.cs` -- תבנית שירות ומאגר לשאילתות הסקירה; מוסיפים שם או בשירות חדש `ApproveAsync` עם טרנזקציה.
- `server/Domain/Profiles/Profile.cs` -- `Profile.ApprovedRevisionId`, `Visibility`, `RevisionStatus` (`Pending`, `Approved`, ...); סטטוסים נשמרים כמחרוזות בטבלה.
- `server/Infrastructure/ProfileRepository.cs` -- דוגמה ל-`ExecuteSqlInterpolated` ול-`ON CONFLICT` ולהימנעות מ-PII ביומן; `AppDbContext.cs` למיפוי snake_case ולמיגרציה (README מסביר `dotnet-ef`).
- `server/Domain/` -- מקום לישות `AuditLogEntry` (ללא תלות); `server/Infrastructure/AppDbContext.cs` -- `DbSet` ומיפוי; מזהה המנהלת מ-`ClaimsPrincipal` (`NameIdentifier`).
- `server/Tests/SubmissionReviewTests.cs`, `AuthTestSupport.cs` -- יצירת גרסאות במצבים שונים ישירות בבסיס הנתונים ושליחת POST עם anti-forgery ו-Origin כאדמין שהשלים TOTP; להשתמש בהם.
- אין לשנות: `client/`, ההגנות מסיפורים 1.3 עד 1.10, הנקודות הציבוריות.

## Tasks & Acceptance

**Execution:**
- `server/Domain/` ו-`server/Infrastructure/` -- ישות `AuditLog` ומיגרציה (טבלה `audit_log`, אינדקס לפי זמן) -- AR19, AD-22
- `server/Features/Profiles/` ו-`server/Infrastructure/` -- שירות אישור בטרנזקציה אחת עם עדכונים מותנים ו-`AuditLog` -- AD-2, AD-14
- `server/Api/Endpoints/` -- `POST /admin/submissions/{revisionId}/approve` עם 200, 404 ו-409 -- FR-7
- `server/Tests/` -- בדיקות אינטגרציה לשורות המטריצה, כולל מרוץ אוטומטי והרשאות -- AR6, AR21
- `README.md` -- הנקודה, קודי התגובה ויומן הביקורת -- תפעול

**Acceptance Criteria:**
- Given גרסה `pending` שהמנהלת רואה, when היא מאשרת אותה, then `ApprovedRevisionId` של הפרופיל מצביע עליה, סטטוס הגרסה `approved` ו-`Visibility = active`, בטרנזקציה אחת.
- Given גרסה שכבר אינה `pending` או שתי בקשות אישור במקביל, when מבקשים לאשר, then 409 ולא משתנה כלום (ורק אחת מצליחה במקביל).
- Given אישור, when הוא בוצע, then נרשמת שורה ב-`AuditLog` עם זהות המנהלת, זמן, פעולה ומזהה הגרסה בלי תוכן אישי, והאישור אינו נוגע בפרטי הקשר.

## Spec Change Log

## Review Triage Log

### 2026-10-21 — Review pass
- verdicts: כ-30 ממצאים מארבע שכבות (רבים כפולים) — high 0, medium 3, low 5, false 1
- findings:
  - `[medium]` `[patch]` המצביע המאושר הקודם נקרא בלי נעילה, ולכן אישור מקביל של שתי גרסאות שונות עלול להשאיר שתיים מאושרות — נעילת שורת הפרופיל לפני הקריאה
  - `[medium]` `[patch]` תוצאת ה-supersede לא נבדקת — נבדק מספר השורות ונוסף כשל מכוון עם בדיקה לנתון לא עקבי
  - `[medium]` `[patch]` אין בדיקה לאישור מקביל של שתי גרסאות ולמבצע וזמן ב-AuditLog — נוספו
  - `[low]` `[patch]` קוד מכונה ל-409, כותרת ל-403 ו-README עם טבלה שבורה — תוקנו
  - `[low]` `[defer]` הגנת append-only ב-audit_log, בדיקת כשל באמצע טרנזקציה, כשל בודד לא משוחזר, שינוי מייל או נייד ממתין — נרשמו ב-deferred
  - `[low]` `[reject]` אישור גרסה עם cv שנדחה, Execution strategy, בדיקת קיום מחוץ לטרנזקציה, SingleAsync, Visibility מכל מצב, UTC Kind — לא סביר שייפגעו בשימוש רגיל
  - `[false]` `[reject]` Designer ו-snapshot חסרים — הקבצים קיימים (נבדק ב-grep על `audit_log`)
## Verification

**Commands:**
- `dotnet build server` -- expected: 0 שגיאות
- `dotnet test server` -- expected: כל הבדיקות עוברות (דורש Docker)

## Auto Run Result

- **שינוי:** `POST /admin/submissions/{revisionId}/approve` (אדמין בלבד): בטרנזקציה אחת עדכון מותנה של גרסה `pending` ל-`approved`, העברת הקודמת (אם יש) ל-`superseded`, הצבעת `ApprovedRevisionId` ו-`Visibility = active`, וכתיבת שורה ל-`audit_log` (מבצע, זמן, פעולה ומזהה גרסה בלבד). שורת הפרופיל ננעלת לפני קריאת המצביע הקודם. 409 עם `code: revision-not-pending` לגרסה שאינה ממתינה, 404 אחיד למזהה לא קיים, 500 לנתון לא עקבי. האישור אינו נוגע בפרטי הקשר.
- **קבצים:** `server/Domain/Audit/AuditLogEntry.cs`, מיגרציית `AuditLog`, `server/Infrastructure/SubmissionReviewRepository.cs`, `server/Features/Profiles/SubmissionReviewService.cs`, `server/Api/Endpoints/SubmissionEndpoints.cs`, `Strings.cs`, `server/Tests/SubmissionReviewTests.cs`, `README.md`.
- **סקירה:** 3 תיקוני medium (נעילת הפרופיל מפני אישור מקביל של שתי גרסאות, בדיקת תוצאת ה-supersede, בדיקות מקביליות ומבצע) ו-2 low הוחלו. 4 פריטים נדחו ל-`deferred` (הגנת append-only ב-`audit_log`, בדיקת כשל באמצע טרנזקציה, כשל בודד לא משוחזר בבדיקות, שינוי מייל או נייד ממתין). השאר נדחו כמתועד ביומן.
- **המלצה לסקירת המשך:** כן. סיכונים שלא אומתו: אין בדיקה שכשל באמצע הטרנזקציה מבטל את כל הכתיבות, ובהרצה מלאה אחת נכשלה בדיקה שלא שוחזרה בשתי הרצות נוספות.
- **אימות:** `dotnet build server` ללא שגיאות, `dotnet test server`: 338 מתוך 338 עוברות מול Postgres אמיתי.
- **סיכונים שנותרו:** אין עדיין מסך אישור בלקוח (סיפור 5.6).
