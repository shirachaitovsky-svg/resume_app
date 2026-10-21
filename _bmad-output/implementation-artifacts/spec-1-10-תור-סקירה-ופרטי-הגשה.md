---
title: 'סיפור 1.10: תור סקירה ופרטי הגשה'
type: 'feature'
created: '2026-10-21'
status: 'done'
baseline_revision: '2dce93d9849f1ea5feb73882ac31192ef4638b36'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-resume_app-2026-10-21/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-1-9-קורות-חיים.md'
warnings: []
deferred:
  - summary: >-
      אין רישום ביומן ביקורת של צפיית אדמין בפרטי קשר (מייל ונייד) של הגשה
    evidence: |-
      AD-22 מגדיר ביומן רק פעולות כתיבה; צפייה לא נרשמת. להחליט בסיפור 4.2 אם נדרש
    location: >-
      server/Api/Endpoints/SubmissionEndpoints.cs
    severity: low
  - summary: >-
      תצוגת עדכון לא מציגה את הגרסה המאושרת לצד המוצעת ואת השדות ששונו
    evidence: |-
      מתוכנן בסיפור 2.3 (סקירת עדכון); כאן מוחזר סימון kind בלבד
    location: >-
      server/Features/Profiles/SubmissionReviewService.cs
    severity: low
  - summary: >-
      מסך תור הסקירה, טבלה עם מצב ריק, קישור מוצר בלשונית חדשה עם rel="noopener" ותצוגת תמונה או אווטאר נמצאים בלקוח וטרם נבנו
    evidence: |-
      השרת מחזיר נתונים בלבד; סיפור 5.6 בלקוח
    location: >-
      client/ (טרם קיים)
    severity: medium---

<intent-contract>

## Intent

**Problem:** המנהלת לא יכולה לראות אילו הגשות ממתינות ולעיין בפרטיהן (כולל מייל, נייד, קישור למוצר ותמונה), ולכן אי אפשר להחליט אם לפרסם.

**Approach:** להוסיף ב-`server/` נקודות ניהול (אדמין בלבד): `GET /admin/submissions` שמחזירה את תור הגרסאות הממתינות, `GET /admin/submissions/{revisionId}` עם כל פרטי ההגשה, ו-`GET /admin/submissions/{revisionId}/photo` שמגישה את תמונת הגרסה הממתינה למנהלת בלבד. קורות החיים ושאר הטקסטים מוחזרים לאחר ניקוי באותה פונקציה כמו בציבור. חלק השרת בלבד.

## Boundaries & Constraints

**Always:** כל הנקודות תחת `/admin/submissions` דורשות `AdminPolicy` (ולכן גם TOTP שהושלם); אנונימי 401, מחובר שאינו אדמין 403. התור כולל רק גרסאות במצב `pending`, מהישנה לחדשה (לפי `SubmittedAt` ואז מזהה), בצורת `{ items, total }` עם `limit` (ברירת מחדל 50, מקסימום 100) ו-`offset`. פריט בתור: `revisionId`, שם, שם מוצר, שם המחזור, `kind` (`new` כשלפרופיל אין גרסה מאושרת, אחרת `update`) ותאריך הגשה. פרטי הגשה כוללים: כל שדות הגרסה, קישור המוצר, מייל ונייד מ-`ParticipantPrivate`, מזהה האווטאר, והאם יש תמונה (בולאני `hasPhoto`, בלי הבייטים), וקורות החיים ושדות הטקסט החופשי. קורות החיים מוחזרים אחרי `SafeMarkdown.Clean` (אותה פונקציה כמו בציבור); ערך שנדחה על ידה מוחזר כריק עם סימון `cvInvalid` ולא מפיל את הבקשה. תגובות `Admin*` DTO ייעודיות בלבד (לא ישות EF). מזהה גרסה שאינו GUID, אינו קיים או אינה `pending`: 404 `problem+json` אחיד. התמונה: `Content-Type` מהגרסה, `X-Content-Type-Options: nosniff`, `Cache-Control: no-store`, ו-404 אחיד כשאין תמונה. כותרות `no-store` על כל התגובות (הקיים). לא נרשם מייל, נייד או קורות חיים ביומן.

**Never:** אין שינוי מצב (אישור והחזרה הם בסיפורים 1.11 ו-2.4), אין החזרת מייל או נייד בנקודות ציבוריות, אין מסכי לקוח, אין החזרת תמונת גרסה ממתינה בנקודה הציבורית (נשאר 404). אין פעולות כתיבה בסיפור זה.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| תור עם הגשות | אדמין, כמה גרסאות `pending` | 200 `{ items, total }` מהישנה לחדשה; שדות הפריט לפי ההגדרה | N/A |
| תור ריק | אין גרסאות `pending` | 200 עם `items: []` ו-`total: 0` | N/A |
| הגשה חדשה | פרופיל בלי גרסה מאושרת | `kind: "new"` | N/A |
| עדכון | פרופיל עם `ApprovedRevisionId` וגרסה ממתינה נוספת (נקבע בבדיקה בבסיס הנתונים) | `kind: "update"` | N/A |
| עמוד תוצאות | `limit=2&offset=1` | חלק נכון, `total` כולל; `limit` מעל 100 נחתך | `limit` או `offset` שליליים: 400 |
| פרטי הגשה | `revisionId` של ממתינה | כל הפרטים כולל מייל ונייד, `hasPhoto`, וקורות חיים מנוקים | N/A |
| קורות חיים מסוכנים בבסיס הנתונים | `cv` שנכנס ישירות עם `<script>` או קישור `javascript:` | מוחזר מנוקה או ריק עם `cvInvalid: true` | לא נכשל |
| גרסה לא קיימת או לא ממתינה | מזהה אחר, מאושרת או `returned` | 404 אחיד | N/A |
| תמונת הגרסה | `hasPhoto` | 200 עם סוג התמונה, `nosniff`, `no-store` | אין תמונה: 404 אחיד |
| הרשאה | אנונימי או מחובר שאינו אדמין | 401 או 403 בכל שלוש הנקודות | N/A |
| פרטיות | נקודה ציבורית | מייל ונייד אינם מופיעים בה | N/A |

</intent-contract>

## Code Map

- `server/Api/Endpoints/CohortEndpoints.cs` -- תבנית `MapGroup("/admin/cohorts")` עם `RequireAuthorization(AdminPolicy)`, DTO ו-`{ items, total }`; לשכפל את הדפוס ל-`/admin/submissions` בקובץ חדש.
- `server/Domain/Profiles/Profile.cs` -- `Profile`, `ProfileRevision` (סטטוס, שדות, `Photo`, `PhotoContentType`, `Cv`), `ParticipantPrivate`; `ProfileRevision.SubmittedAt`.
- `server/Features/Profiles/ProfileService.cs`, `server/Infrastructure/ProfileRepository.cs` -- מאגר קיים עם `GetApprovedPhotoAsync`; להוסיף שם שאילתות תור ופרטים עם הקרנה (בלי טעינת `Photo` בתור וברשימה).
- `server/Features/Markdown/SafeMarkdown.cs` -- `Clean(text, maxLength)` ללא תלות; לקרוא בה בקריאה.
- `server/Infrastructure/AppDbContext.cs` -- יחסי `Cohort`, `Profile`, `ProfileRevision`, `ParticipantPrivate`.
- `server/Tests/ProfileOptionalFieldsTests.cs`, `CohortTests.cs`, `AuthTestSupport.cs` -- תבניות לבדיקות אדמין (כולל השלמת TOTP) ויצירת הגשות; להוסיף עזר שיוצר גרסאות במצבים שונים ישירות בבסיס הנתונים.
- אין לשנות: `client/`, ההגנות מסיפורים 1.3 עד 1.9, הנקודה הציבורית של התמונה.

## Tasks & Acceptance

**Execution:**
- `server/Features/Profiles/` ו-`server/Infrastructure/` -- שאילתות תור, פרטים ותמונת גרסה ממתינה, עם הקרנה -- FR-6
- `server/Api/Endpoints/` -- `SubmissionEndpoints` עם שלוש הנקודות ו-DTO `Admin*`, עמוד תוצאות ו-404 אחיד -- FR-6, AD-5
- `server/Tests/` -- בדיקות אינטגרציה לשורות המטריצה, כולל ניקוי CV מסוכן שנכנס ישירות לבסיס הנתונים והרשאות -- AR13, AR21
- `README.md` -- הנקודות והשדות -- תפעול

**Acceptance Criteria:**
- Given מנהלת מחוברת עם TOTP, when קוראת לתור, then היא רואה את הגרסאות הממתינות עם שם, מוצר, מחזור, סוג (חדש או עדכון) ותאריך; ומשתמש שאינו אדמין לא רואה אותן.
- Given הגשה בתור, when נפתחת, then היא כוללת מייל, נייד, קישור למוצר, אווטאר או תמונה וקורות חיים מנוקים.
- Given תור ריק, when נקרא, then מוחזר מצב ריק תקין.

## Spec Change Log

## Review Triage Log

### 2026-10-21 — Review pass
- verdicts: כ-30 ממצאים מארבע שכבות (רבים כפולים) — high 0, medium 3, low 5, false 1
- findings:
  - `[medium]` `[patch]` התור מציג גרסה שאי אפשר לפתוח (ללא שורת ParticipantPrivate) כי השאילתות משתמשות בצירופים שונים — אוחדו הצירופים וה-total
  - `[medium]` `[patch]` חסרות בדיקות: פרטי עדכון (`kind: update`), כל שדות הפרטים עם ערכים נבדלים, ותוצאת קורות חיים מסוכנים מדויקת — נוספו
  - `[low]` `[patch]` `limit=0`, ניתוח GUID לא קנוני, הערך הגולמי של cv בשורת הפרטים ו-README לא מדויק — תוקנו
  - `[low]` `[defer]` רישום צפייה בפרטי קשר, הצגת הבדל בעדכון, מסך הלקוח — נרשמו ב-deferred
  - `[low]` `[reject]` ניקוי שאר שדות הטקסט (טקסט פשוט, הלקוח מציג כטקסט), רשימת לבנה לסוגי תמונה ו-CSP בתמונת האדמין (מקודדת תמיד ל-JPEG), consistency של count ו-page, שורה ריקה — לא סביר שייפגעו בשימוש רגיל
  - `[false]` `[reject]` no-store חסר בתשובות הרשימה והפרטים — מוגדר ב-middleware כללי לכל תגובה מאומתת, והבדיקות מאמתות
## Verification

**Commands:**
- `dotnet build server` -- expected: 0 שגיאות
- `dotnet test server` -- expected: כל הבדיקות עוברות (דורש Docker)

## Auto Run Result

- **שינוי:** שלוש נקודות ניהול לאדמין בלבד: `GET /admin/submissions` (תור גרסאות ממתינות, מהישנה לחדשה, `{ items, total }` עם עימוד, `kind` חדש או עדכון), `GET /admin/submissions/{revisionId}` (כל פרטי ההגשה כולל מייל, נייד, קישור מוצר, אווטאר, `hasPhoto`, וקורות חיים מנוקים דרך `SafeMarkdown.Clean`, עם `cvInvalid` כשנדחו) ו-`GET /admin/submissions/{revisionId}/photo` (תמונת גרסה ממתינה, `nosniff`, `no-store`).
- **קבצים:** `server/Features/Profiles/SubmissionReviewService.cs`, `server/Infrastructure/SubmissionReviewRepository.cs`, `server/Api/Endpoints/SubmissionEndpoints.cs`, `Program.cs`, `DependencyInjection.cs`, `server/Tests/SubmissionReviewTests.cs`, `README.md`.
- **סקירה:** 2 תיקוני medium (אחידות הצירופים בין התור לפרטים, בדיקות חסרות) ו-3 תיקוני low הוחלו. 3 פריטים נדחו ל-`deferred` (רישום צפייה בפרטי קשר, הצגת הבדל בעדכון, מסך הלקוח). השאר נדחו כמתועד ביומן.
- **המלצה לסקירת המשך:** כן. הסיכון שלא אומת: שאילתת התמונה אינה דורשת שורת `ParticipantPrivate`, ומסכי הניהול בלקוח טרם קיימים.
- **אימות:** `dotnet build server` ללא שגיאות, `dotnet test server`: 329 מתוך 329 עוברות מול Postgres אמיתי.
- **סיכונים שנותרו:** אין עדיין מסך תור בלקוח (סיפור 5.6).
