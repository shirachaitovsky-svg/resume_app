---
title: 'סיפור 1.6: הגשת פרופיל (שדות חובה)'
type: 'feature'
created: '2026-10-21'
status: 'done'
baseline_revision: '29ee33ab5f22a8b05950b97e571776bc34e34a40'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-resume_app-2026-10-21/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-1-5-יצירת-מחזור-וקישור-הזמנה.md'
warnings: []
deferred:
  - summary: >-
      נוסחי הפרטיות המדויקים מהבריף והודעת ההצלחה "הפרופיל הוגש וממתין לאישור המנהלת" לא קיימים בשרת
    evidence: |-
      הם טקסטי ממשק; השרת מחזיר `{status:"pending"}` בלבד. יש לממש אותם בסיפור לקוח (טופס ההגשה)
    location: >-
      client/ (טרם קיים)
    severity: medium
  - summary: >-
      חוסרי שלמות בסכמה: אין FK מ-`approved_revision_id` לגרסאות, אין אילוץ CHECK על `status`, ו-`participant_private.cohort_id` מועתק ללא אילוץ מרוכב
    evidence: |-
      עלול לגרום לסטייה בין הטבלאות; לטפל בסיפור 1.11 (אישור פרסום) שנוגע במצביע
    location: >-
      server/Infrastructure/AppDbContext.cs
    severity: low
  - summary: >-
      תגובת הכפילות עשויה להיות שונה בזמן התגובה מהצלחה
    evidence: |-
      הגבלת קצב והגנת הטפסים בסיפור 1.7 מצמצמות את האפשרות לאיסוף כתובות
    location: >-
      server/Infrastructure/ProfileRepository.cs
    severity: low---

<intent-contract>

## Intent

**Problem:** משתתפת שקיבלה קישור הזמנה לא יכולה להגיש פרופיל, ואין מקום מאובטח לשמור את המייל והנייד שלה בנפרד מהמידע הציבורי.

**Approach:** להוסיף ב-`server/` נקודת הגשה ציבורית לפי קישור הזמנה שיוצרת `Profile`, `ProfileRevision` במצב `pending` ושורת `ParticipantPrivate` נפרדת, עם ולידציה בשרת ותגובה אחידה לכפילות. חלק השרת בלבד; אין מסכי לקוח.

## Boundaries & Constraints

**Always:** נקודת ההגשה `POST /invites/{token}/profiles` מסומנת `[AllowAnonymous]` ומוגנת באסימון anti-forgery ובבדיקת `Origin` הקיימים. שדות חובה: שם (עד 100), שם מוצר (עד 100), קישור למוצר (`http`/`https` מוחלט, עד 2048), מייל (פורמט תקין, עד 254, נשמר בצורה מנורמלת: trim ואותיות קטנות), נייד (9 עד 15 ספרות, מותרים `+`, רווח ומקף, נשמר מנורמל). `ParticipantPrivate` בטבלה נפרדת; המייל והנייד לא מופיעים בשום תגובה ולא ביומן. `Profile.CohortId` נקבע מהקישור ואינו משתנה. מזהים GUID v4. גרסה אחת `pending` לכל היותר לפרופיל (אינדקס ייחודי חלקי). `(CohortId, Email)` ייחודי. תגובה לכפילות זהה להצלחה. אסימון מבוטל, לא קיים או פגום: 404 זהה לזה שבנקודת הבדיקה. מיגרציית EF Core.

**Never:** אין שדה LinkedIn, אין שדות אופציונליים (1.8), קורות חיים (1.9), Turnstile והגבלת קצב (1.7), אישור ופרסום (1.11), שאילתה ציבורית של פרופילים (1.12). אין החזרת מזהה פרופיל או נתונים פרטיים בתגובה.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| הגשה תקינה | אסימון פעיל, כל שדות החובה | 202 `{ "status": "pending" }`; נוצרים Profile, גרסה `pending` ו-ParticipantPrivate | N/A |
| שדה חובה חסר | שדה ריק או רווחים בלבד | 400 `problem+json` עם שגיאה לכל שדה; לא נוצר דבר | ולידציה |
| קישור למוצר לא תקין | `ftp://x`, `javascript:...`, יחסי או ארוך מדי | 400 עם שגיאה בשדה הקישור | ולידציה |
| מייל או נייד לא תקינים | פורמט שגוי | 400 עם שגיאה בשדה | ולידציה |
| כפילות | אותו מייל (גם באותיות שונות) באותו מחזור | 202 זהה להצלחה, בלי פרופיל נוסף | תגובה אחידה |
| אותו מייל במחזור אחר | מחזור שונה | נוצר פרופיל נפרד | N/A |
| הגשות מקבילות | חמש הגשות זהות במקביל | פרופיל אחד בלבד, כולן 202 | אינדקס ייחודי |
| קישור מבוטל או פגום | אסימון מבוטל, לא קיים או ארוך מ-128 | 404 זהה, לא נוצר דבר | N/A |
| בלי אסימון anti-forgery | POST בלי כותרת או עם Origin זר | נדחה כבסיפור 1.3 | N/A |
| פרטיות | כל תגובה של הנקודה | אין בה מייל או נייד | N/A |

</intent-contract>

## Code Map

- `server/Api/Endpoints/CohortEndpoints.cs` -- `GET /invites/{token}` האנונימי: תבנית לאימות אסימון, 404 אחיד, כותרות `no-store`/`no-referrer`, ו-`Validation`. נקודת ההגשה החדשה יושבת באותו מקום או בקובץ `ProfileEndpoints.cs` חדש.
- `server/Features/Cohorts/CohortService.cs` -- `ResolveInvite`/`ICohortRepository`; לשימוש חוזר בפענוח האסימון לפני יצירת פרופיל.
- `server/Domain/` -- כאן `Profile`, `ProfileRevision`, `ParticipantPrivate`; `server/Features/Profiles/` -- שירות ההגשה ו-`IProfileRepository`.
- `server/Infrastructure/AppDbContext.cs`, `CohortRepository.cs` -- תבנית מיפוי snake_case, אינדקסים ומימוש מאגר; מיגרציה חדשה (README מסביר את `dotnet-ef`).
- `server/Api/Resources/Strings.cs` -- הודעות שגיאה בעברית.
- `server/Tests/CohortTests.cs`, `AuthTestSupport.cs` -- `AuthApiFactory`/`ApiClient` (כולל anti-forgery ו-Origin) להרחבה.
- אין לשנות: `client/`, התנהגות ההגנות מסיפורים 1.3-1.5.

## Tasks & Acceptance

**Execution:**
- `server/Domain/Profiles/` -- `Profile` (Id, CohortId, Visibility, ApprovedRevisionId, CreatedAt), `ProfileRevision` (Status, שדות החובה, SubmittedAt), `ParticipantPrivate` (Email, Phone) -- AR4, AR5
- `server/Features/Profiles/` -- שירות הגשה: ולידציה, נרמול, יצירה בטרנזקציה אחת, כפילות אחידה -- FR-2, FR-5
- `server/Infrastructure/` -- מיפוי, אינדקס `pending` ייחודי חלקי, `(cohort_id, email)` ייחודי, מימוש מאגר ומיגרציה -- AD-2, AD-18
- `server/Api/Endpoints/` -- `POST /invites/{token}/profiles` -- FR-2
- `server/Tests/` -- בדיקות אינטגרציה לשורות המטריצה, כולל בדיקת מצב מסד הנתונים, מקביליות ופרטיות -- AR21
- `README.md` -- הנקודה החדשה והשדות -- תפעול

**Acceptance Criteria:**
- Given הגשה תקינה, when נשלחת, then נוצר `Profile` משויך למחזור של הקישור, `ProfileRevision` במצב `pending` ושורה ב-`ParticipantPrivate`, והפרופיל אינו מוחזר באף נקודה ציבורית.
- Given כל תגובה של הנקודה, when נבדקת, then אין בה מייל, נייד או מזהה פרופיל.
- Given כפילות מייל באותו מחזור, when מוגשת, then התגובה זהה להצלחה ואין פרופיל נוסף.

## Spec Change Log

## Review Triage Log

### 2026-10-21 — Review pass
- verdicts: כ-35 ממצאים מארבע שכבות (רבים כפולים) — high 0, medium 4, low 6, false 1
- findings:
  - `[medium]` `[patch]` מייל עלול להגיע ליומן דרך חריגת האינדקס הייחודי בכפילות — הוסרה נתיב החריגה, נוספה בדיקת לוגים
  - `[medium]` `[patch]` גוף שגוי או content-type שגוי מחזירים 400/415 לפני בדיקת האסימון ולכן חושפים אסימון תקף — האסימון נבדק קודם, ונוספה הגבלת גודל גוף
  - `[medium]` `[patch]` תווי בקרה ב-name גורמים ל-500; קישור עם אישורים; מיפוי הודעות נופל ל"נייד" — תוקנו
  - `[medium]` `[patch]` בדיקות חסרות (מייל ו-URL לא תקינים, טקסט הודעות, כותרות 404, 404 מלא) ותיעוד כפילות ב-README — נוספו
  - `[low]` `[defer]` נוסחי הפרטיות והודעת ההצלחה בלקוח, חוסרי שלמות בסכמה, timing בכפילות — נרשמו ב-deferred
  - `[low]` `[reject]` מירוץ ביטול מחזור, נרמול Unicode וקונפיוזבלים, פורמט נייד עם מפרידים בקצוות, בדיקה שבירה של הודעת Postgres, מרחב שמות מיגרציה — לא סביר שייפגעו בשימוש רגיל
  - `[false]` `[reject]` הגנת anti-forgery לא חלה על הנקודה — הבדיקות מוכיחות שהיא חלה (בקשה בלי אסימון נדחית)
## Verification

**Commands:**
- `dotnet build server` -- expected: 0 שגיאות
- `dotnet test server` -- expected: כל הבדיקות עוברות (דורש Docker)

## Auto Run Result

- **שינוי:** `POST /invites/{token}/profiles` ציבורי: ולידציה ונרמול של שדות החובה, יצירת `Profile`, `ProfileRevision` במצב `pending` ו-`ParticipantPrivate` נפרד בטרנזקציה אחת, תגובת 202 אחידה גם לכפילות מייל במחזור, ו-404 אחיד לקישור מבוטל או פגום (נבדק לפני קריאת הגוף).
- **קבצים:** `server/Domain/Profiles/Profile.cs`, `server/Features/Profiles/ProfileService.cs`, `server/Infrastructure/ProfileRepository.cs`, מיגרציית `Profiles`, `server/Api/Endpoints/ProfileEndpoints.cs`, `appsettings.json`, `Strings.cs`, `server/Tests/ProfileSubmissionTests.cs`, `AuthTestSupport.cs`, `README.md`.
- **סקירה:** 4 תיקוני medium הוחלו (מייל ביומן דרך חריגת כפילות, אחידות 404 לפני קריאת הגוף, תווי בקרה וקישור עם אישורים, בדיקות חסרות). 3 פריטים נדחו ל-`deferred` (נוסחי פרטיות והודעת הצלחה בלקוח, חוסרי שלמות בסכמה, timing בכפילות). השאר נדחו כמתועד ביומן.
- **המלצה לסקירת המשך:** כן. הסיכון שלא אומת: בדיקת לכידת הלוגים נשענת על `Console.Out` ולא הוכח שהיא נכשלת מול מסלול החריגה הישן.
- **אימות:** `dotnet build server` ללא שגיאות, `dotnet test server`: 108 מתוך 108 עוברות מול Postgres אמיתי. הרצה מלאה חשפה מיצוי חיבורים (`too many clients`); תוקן ב-`AuthApiFactory.Dispose` בניקוי ה-pools.
- **סיכונים שנותרו:** הגשה חוזרת עם מייל קיים מתעלמת מהנתונים החדשים (מסלול התיקון באפיק 2); ללא Turnstile והגבלת קצב עד סיפור 1.7.
