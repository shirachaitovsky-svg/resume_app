---
title: 'סיפור 1.13: עמוד פרופיל ציבורי'
type: 'feature'
created: '2026-10-21'
status: 'done'
baseline_revision: 'ed8d6ff59567d3d154b461cb5e0e8a554075d6bf'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-resume_app-2026-10-21/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-1-12-גלריה-ציבורית.md'
warnings: []
deferred:
  - summary: >-
      עמוד הפרופיל בלקוח (טבעת סביב תמונה, רקע כחול, כפתור "לפתיחת המוצר: {שם}", שאלות ממוספרות, קורות חיים ברנדרר בטוח, ומצב "הפרופיל אינו זמין") טרם נבנה
    evidence: |-
      השרת מחזיר נתונים בלבד; סיפור 5.7
    location: >-
      client/ (טרם קיים)
    severity: medium
  - summary: >-
      ניקוי קורות החיים רץ בכל קריאה אנונימית ללא מטמון לפי גרסה, ובאותו מכסה כמו הגלריה; ואין בדיקה שהפרופיל והגלריה חולקים אותה
    evidence: |-
      הגבלת הקצב לפי IP מצמצמת; לשקול שמירת התוצאה המנוקה לפי מזהה גרסה בסיפור 4.1
    location: >-
      server/Features/Profiles/PublicProfileQuery.cs
    severity: low
  - summary: >-
      כתובת התמונה אינה מגורסת ולכן תמונה שהוחלפה עלולה להישאר במטמון עד 5 דקות; GUID באותיות גדולות מתקבל כצורה קנונית
    evidence: |-
      מתועד ב-README; לשקול ETag או פרמטר גרסה
    location: >-
      server/Api/Endpoints/GalleryEndpoints.cs
    severity: low---

<intent-contract>

## Intent

**Problem:** מבקר שלוחץ "לפרופיל" בגלריה לא מקבל את הפרופיל המלא של משתתפת (תפקיד, היכרות, תשובות וקורות חיים), ואין ודאות שמה שמוצג בציבור זהה בכל נתיבי הקריאה.

**Approach:** להוסיף ב-`server/` נקודה ציבורית `GET /profiles/{profileId}` שעוברת דרך `PublicProfileQuery` ומחזירה את הפרופיל המלא מהגרסה המאושרת בלבד, עם 404 אחיד לפרופיל שאינו זמין, ולהעביר את נקודת התמונה הציבורית הקיימת לאותו כלל נראות משותף. חלק השרת בלבד.

## Boundaries & Constraints

**Always:** הנקודה מסומנת `[AllowAnonymous]` ועוברת דרך `PublicProfileQuery` (שאילתה יחידה, אותו עוזר `VisibleProfiles` כמו בגלריה). תנאים: `Visibility = Active`, `ApprovedRevisionId` לא ריק, `Status = Approved`, והנתונים מהגרסה המאושרת בלבד. נקודת התמונה הציבורית `GET /profiles/{profileId}/photo` משתמשת באותו עוזר נראות (כדי שלא יסטו), ללא שינוי בהתנהגותה החיצונית. מזהה הפרופיל מתקבל בצורת GUID `D` בלבד. כל מקרה של פרופיל שאינו זמין (לא קיים, מזהה פגום או לא קנוני, ממתין בלבד, הוחזר, הוחלף, הוסר) מחזיר אותה תגובת 404 `problem+json`, בלי לחשוף אם הוא קיים. `PublicProfileDetail`: `id`, `name`, `productName`, `productUrl`, `description`, `role`, `workplace`, `introText`, `avatarId`, `photoUrl`, `cohort` (`id`, `name`), `introAnswers` (מערך של `{ number, text }` רק לשאלות 1 עד 3 שנענו, בסדר עולה; שאלה ריקה או רווחים בלבד לא מופיעה; הנוסחים עצמם בלקוח), ו-`cv` (Markdown מנוקה). קורות החיים עוברים `SafeMarkdown.Clean` בכל קריאה; ערך שנדחה או ריק מחזיר `cv: null`. שדות טקסט אחרים מוחזרים כפי שנשמרו (טקסט פשוט). אין בתגובה: מייל, נייד, `InviteToken`, פרטי בקשות, מזהה גרסה, סטטוס או שדות ניהול. כותרות: `Cache-Control: public, max-age=30`, `X-Content-Type-Options: nosniff`; ל-404 `no-store`. הגבלת קצב לפי IP דרך `PublicFormGuard` (אותו scope כמו הגלריה). בדיקת הסריקה בהשתקפות על טיפוסי `Public*` מכסה גם את הטיפוס החדש ללא שינוי, ובדיקת JSON נוספת עבורו.

**Never:** אין כפתור או נקודה לבקשת פרטי קשר (סיפור 3.1), אין עריכה או כתיבה, אין מסכי לקוח, אין שינוי בנקודות הגלריה או הניהול מעבר לשימוש חוזר בעוזר. אין החזרת תמונה של גרסה ממתינה.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| פרופיל מאושר | `Active` עם גרסה מאושרת | 200 עם `PublicProfileDetail` מלא | N/A |
| שאלות | רק שאלה 2 נענתה | `introAnswers` מכיל `{ number: 2, ... }` בלבד | N/A |
| בלי שאלות | אין תשובות | `introAnswers: []` | N/A |
| קורות חיים | `cv` תקין | Markdown מנוקה | N/A |
| קורות חיים פגומים | `cv` שנכנס ישירות לבסיס הנתונים עם `<script>` או `javascript:` | מנוקה, או `null` כשנדחה | לא נכשל |
| ממתין בלבד | גרסה `pending` בלי מאושרת | 404 אחיד | N/A |
| עדכון ממתין | מאושר + `pending` חדשה | נתוני המאושרת בלבד | N/A |
| הוחזר או הוחלף | גרסה `returned` או `superseded` בלבד | 404 אחיד | N/A |
| הוסר | `Visibility = removed` | 404 אחיד | N/A |
| מזהה לא קיים או פגום | GUID אקראי, `abc`, צורה `N` או `B` | 404 אחיד | N/A |
| מצביע לא עקבי | `ApprovedRevisionId` לגרסה לא מאושרת או של פרופיל אחר | 404 אחיד | N/A |
| תמונה | פרופיל עם תמונה מאושרת | `photoUrl` מוגדר והנקודה `/photo` מחזירה אותה | בלי תמונה: `null` |
| עקביות | כל מקרה לא זמין | `/profiles/{id}` ו-`/profiles/{id}/photo` מסכימים (שניהם 404) | N/A |
| פרטיות | כל תגובה | אין מייל, נייד או שדה פרטי | N/A |
| קצב | חריגה מהסף | 429 עם `Retry-After` ו-`code` | N/A |

</intent-contract>

## Code Map

- `server/Infrastructure/PublicProfileReader.cs` -- עוזר `VisibleProfiles()` מסיפור 1.12 (הצירוף והסינון הראשיים); כאן מוסיפים `GetByIdAsync` ומעבירים אליו גם את שאילתת התמונה.
- `server/Features/Profiles/PublicProfileQuery.cs` -- `PublicProfileQuery` ו-`IPublicProfileReader`; להוסיף שיטת קריאה לפרופיל בודד.
- `server/Infrastructure/ProfileRepository.cs` -- `GetApprovedPhotoAsync` (כרגע שאילתת נראות נפרדת) שיש להעביר לעוזר המשותף.
- `server/Api/Endpoints/GalleryEndpoints.cs`, `ProfileEndpoints.cs` -- תבנית נקודה ציבורית, ניתוח GUID בצורה `D`, 404 אחיד, כותרות והגבלת קצב `CheckIpAsync`; נקודת התמונה ב-`ProfileEndpoints.cs`.
- `server/Features/Markdown/SafeMarkdown.cs` -- `Clean(text, maxLength)` לקורות חיים בקריאה.
- `server/Domain/Profiles/Profile.cs` -- שדות `ProfileRevision` (תפקיד, מקום עבודה, טקסט היכרות, `IntroAnswer1..3`, `Cv`).
- `server/Tests/PublicGalleryTests.cs`, `SubmissionReviewTests.cs`, `ProfileOptionalFieldsTests.cs` -- `SeedAsync` ובדיקות הסריקה של `Public*` (חייבת להמשיך לעבור ולכסות את הטיפוס החדש).
- אין לשנות: `client/`, נקודות הניהול, ההגנות מסיפורים 1.3 עד 1.12.

## Tasks & Acceptance

**Execution:**
- `server/Features/Profiles/` ו-`server/Infrastructure/` -- שיטת קריאה לפרופיל בודד דרך `PublicProfileQuery`, והעברת שאילתת התמונה לעוזר הנראות המשותף -- FR-8, AR5
- `server/Api/Endpoints/` -- `GET /profiles/{profileId}` עם `PublicProfileDetail`, ניקוי קורות חיים, שאלות שנענו בלבד, 404 אחיד וכותרות -- FR-8, FR-9
- `server/Tests/` -- בדיקות לכל שורות המטריצה, כולל עקביות בין פרופיל לתמונה וסריקת `Public*` -- AR4, AR13, AR21
- `README.md` -- הנקודה, השדות והעקביות -- תפעול

**Acceptance Criteria:**
- Given פרופיל `Active` עם גרסה מאושרת, when נפתח, then מוחזרים שם, תיאור, תפקיד, מקום עבודה, מחזור, קישור המוצר, אווטאר או תמונה, שאלות ההיכרות שנענו (ממוספרות) וקורות חיים מנוקים.
- Given פרופיל שלא קיים, ממתין או הוסר, when מבקשים אותו, then 404 אחיד שאינו חושף אם הוא קיים.
- Given העמוד, when נבדק, then אין בו מייל או נייד, וקורות החיים מוחזרים מנוקים.

## Spec Change Log

## Review Triage Log

### 2026-10-21 — Review pass
- verdicts: כ-30 ממצאים מארבע שכבות (רבים כפולים) — high 0, medium 3, low 5, false 0
- findings:
  - `[medium]` `[patch]` בדיקת קורות החיים העוינים לא קובעת תוצאה מדויקת לכל קלט — נוספה תוצאה צפויה לכל שורה
  - `[medium]` `[patch]` ה-SQL של רשימת הגלריה עלול לשלוף שדות כבדים (cv, תשובות) — נבדק ונוספה בדיקה שמצמידה את ההקרנה
  - `[low]` `[patch]` תיאור ריק, שדות null, 404 אחיד מלא בכל המקרים, תמונה באורך אפס, ניקוי רווחים בתשובות, README — תוקנו
  - `[low]` `[defer]` מסך הלקוח, מטמון קורות חיים מנוקים, גרסאות לתמונה — נרשמו ב-deferred
  - `[low]` `[reject]` מחזור חסר בצירוף, בדיקת אי-הסדר בין מצבי GUID באותיות גדולות, העתקות מיפוי בין שכבות, בדיקת אישור חוזר — לא סביר שייפגעו בשימוש רגיל
  - `[false]` `[reject]` אפשרות NullReferenceException ב-`Clean(null)` — הפונקציה מטפלת בקלט ריק כ"ללא" והבדיקות מכסות
## Verification

**Commands:**
- `dotnet build server` -- expected: 0 שגיאות
- `dotnet test server` -- expected: כל הבדיקות עוברות (דורש Docker)

## Auto Run Result

- **שינוי:** `GET /profiles/{profileId}` ציבורי דרך `PublicProfileQuery`: פרופיל מלא מהגרסה המאושרת בלבד (שם, מוצר וקישור, תיאור, תפקיד, מקום עבודה, טקסט היכרות, אווטאר או `photoUrl`, מחזור, תשובות היכרות שנענו בלבד ממוספרות 1 עד 3, וקורות חיים מנוקים דרך `SafeMarkdown.Clean` או `null`), עם 404 אחיד לכל מקרה לא זמין. נקודת התמונה הציבורית הועברה לאותו עוזר נראות משותף (`VisibleProfiles`), כך ששתי הנקודות מסכימות.
- **קבצים:** `server/Features/Profiles/PublicProfileQuery.cs`, `server/Infrastructure/PublicProfileReader.cs`, `server/Api/Endpoints/GalleryEndpoints.cs`, `ProfileEndpoints.cs`, `ProfileService.cs`, `server/Tests/PublicProfileDetailTests.cs`, `PublicGalleryTests.cs`, `ProfileSubmissionTests.cs` (תיקון בדיקת לוג לא יציבה), `README.md`.
- **סקירה:** 2 תיקוני medium (בדיקת קורות חיים עוינים מדויקת, הצמדת ההקרנה של הגלריה שלא תשלוף שדות כבדים) ו-4 low הוחלו. 3 פריטים נדחו ל-`deferred` (מסך הפרופיל בלקוח, מטמון קורות חיים מנוקים, גרסאות לתמונה). השאר נדחו כמתועד ביומן.
- **המלצה לסקירת המשך:** כן. הסיכון שלא אומת: ניקוי הקורות חיים רץ בכל קריאה ציבורית ובאותה מכסה כמו הגלריה, ולא נבדק עומס.
- **אימות:** `dotnet build server` ללא שגיאות, `dotnet test server`: 387 מתוך 387 עוברות מול Postgres אמיתי. נמצא ותוקן גם מקור הכשל הלא יציב מסיפורים 1.11 ו-1.13: קריאה של `StringWriter` של הלוג בלי נעילה בזמן ש-Serilog כותב אליו.
- **סיכונים שנותרו:** אין עדיין מסך פרופיל בלקוח (סיפור 5.7); כפתור "בקשת פרטי קשר" יתווסף בסיפור 3.1.
