---
title: 'סיפור 1.12: גלריה ציבורית'
type: 'feature'
created: '2026-10-21'
status: 'done'
baseline_revision: '4b14783c6284df0dc36e6bddb0b61d846e38dcf2'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-resume_app-2026-10-21/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-1-11-אישור-פרסום.md'
warnings: []
deferred:
  - summary: >-
      מסך הגלריה בלקוח (כרטיסים, צ'יפי מחזור, שלדי טעינה, מצב ריק "עדיין אין פרופילים במחזור הזה.", עמודות 3/2/1, סינון שנשמר בכתובת) וכפתור "לפרופיל" טרם נבנו
    evidence: |-
      השרת מחזיר נתונים בלבד; סיפור 5.7
    location: >-
      client/ (טרם קיים)
    severity: medium
  - summary: >-
      הסדר לפי זמן ההגשה של הגרסה המאושרת ולא לפי זמן האישור; אין עמודת `approved_at`
    evidence: |-
      פרופיל שהוגש מוקדם ואושר מאוחר ימוקם נמוך ברשימה; אם יידרש סדר לפי פרסום, להוסיף עמודה במיגרציה
    location: >-
      server/Infrastructure/PublicProfileReader.cs
    severity: low
  - summary: >-
      תשובות שגיאה 5xx בנקודות הציבוריות אינן מסומנות `no-store` במפורש, ואין אינדקסים ייעודיים לשאילתת הגלריה
    evidence: |-
      מתאים להקשחת הייצור בסיפור 4.1; בהיקף הנוכחי אין צורך באינדקס
    location: >-
      server/Api/Endpoints/GalleryEndpoints.cs
    severity: low---

<intent-contract>

## Intent

**Problem:** אחרי אישור, אין נקודה ציבורית שמחזירה את הפרופילים המאושרים, ולכן מבקר לא יכול לראות כרטיסים או לסנן לפי מחזור.

**Approach:** להוסיף ב-`server/` שאילתה ציבורית יחידה `PublicProfileQuery` שמחזירה אך ורק פרופילים `Active` עם גרסה מאושרת, ונקודות `GET /profiles` (כרטיסי גלריה עם סינון מחזור) ו-`GET /cohorts` (רשימת מחזורים לצ'יפים) שמשתמשות בה, עם DTO ציבוריים ייעודיים (`Public*`) ובדיקת CI שאין בהם שדה פרטי. חלק השרת בלבד.

## Boundaries & Constraints

**Always:** כל נקודה ציבורית עוברת דרך `PublicProfileQuery` (מחלקה אחת ב-`Features/Profiles`); אסור שאילתת קריאה ציבורית אחרת של פרופילים. תנאים: `Visibility = Active`, `ApprovedRevisionId` לא ריק, והנתונים נלקחים אך ורק מהגרסה המאושרת (גרסה ממתינה, שהוחזרה או שהוחלפה לא מופיעה ולא משפיעה). `GET /profiles` מחזיר `{ items, total }`; פרמטרים: `cohort` (מזהה מחזור GUID בצורה `D`, אופציונלי; ערך לא תקין: 400, מחזור לא קיים: רשימה ריקה), `limit` (ברירת מחדל 100, מקסימום 200, מינימום 1), `offset` (מינימום 0). סדר: האחרונים שפורסמו קודם (לפי זמן ההגשה של הגרסה המאושרת, ואז מזהה). כרטיס `PublicProfileCard`: `id` (מזהה הפרופיל, GUID v4), `name`, `productName`, `productUrl`, `description` (יכול להיות ריק), `avatarId`, `photoUrl` (הנתיב היחסי `/profiles/{id}/photo` רק כשיש תמונה בגרסה המאושרת, אחרת null), ו-`cohort` (`id`, `name`). `GET /cohorts` מחזיר `{ items: [{ id, name }], total }` של מחזורים שיש בהם לפחות פרופיל ציבורי אחד, לפי שם. מזהי מחזור בנקודות הציבוריות הם `Cohort.Id` (GUID v4), לעולם לא `InviteToken`. אין בשום תגובה: מייל, נייד, `InviteToken`, פרטי בקשות, קורות חיים, תשובות היכרות, או שדות ניהול (סטטוס גרסה, `ApprovedRevisionId`). כותרות: `Cache-Control: public, max-age=30`, `X-Content-Type-Options: nosniff`. הגבלת קצב לפי IP בלבד (בלי Turnstile) דרך `PublicFormGuard` הקיים (ברירת מחדל 600 לשעה, ניתנת להגדרה במשתנה סביבה). בדיקת CI (בדיקה אוטומטית) שסורקת בהשתקפות את כל הטיפוסים ששמם מתחיל ב-`Public` בהרכבת ה-API ובודקת שאין בהם מאפיין ששמו (ללא רגישות לאותיות) מכיל `email`, `phone`, `mobile`, `token`, `contact`, `request`, `private`, `revision`, ובנוסף בודקת את ה-JSON שהנקודות מחזירות.

**Never:** אין עמוד פרופיל מלא (סיפור 1.13), אין שדות קורות חיים או תשובות היכרות בכרטיס, אין מסכי לקוח, אין כתיבה, אין שינוי בנקודת התמונה הקיימת `GET /profiles/{profileId}/photo`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| גלריה | מבקר אנונימי, פרופילים מאושרים ופעילים | 200 עם כרטיסים, האחרון שפורסם קודם | N/A |
| גלריה ריקה | אין פרופילים ציבוריים | 200 עם `items: []` ו-`total: 0` | N/A |
| ממתין | פרופיל עם גרסה `pending` בלבד | לא מופיע | N/A |
| עדכון ממתין | פרופיל מאושר עם גרסה חדשה `pending` | מוצגים נתוני הגרסה המאושרת בלבד | N/A |
| הוחזר או הוחלף | גרסה `returned` או `superseded` | לא משפיעה | N/A |
| הוסר | `Visibility = removed` | לא מופיע | N/A |
| סינון מחזור | `cohort={id}` | רק פרופילים של אותו מחזור | מזהה פגום: 400; מחזור לא קיים: ריק |
| תמונה | גרסה מאושרת עם תמונה | `photoUrl` מוגדר; בלי תמונה: `null` ו-`avatarId` | N/A |
| עימוד | `limit=2&offset=1` | חלק נכון, `total` כולל | `limit` מעל 200 נחתך ל-200, `limit` קטן מ-1 או `offset` שלילי: 400 |
| מחזורים | `GET /cohorts` | רק מחזורים עם פרופיל ציבורי, בלי `InviteToken` | N/A |
| פרטיות | כל תגובה ציבורית | אין בה מייל, נייד או שדה פרטי | N/A |
| מזהים | כל מזהה | GUID v4 אקראי, לא מספר רץ | N/A |
| קצב | חריגה מהסף | 429 עם `Retry-After` ו-`code` | N/A |

</intent-contract>

## Code Map

- `server/Infrastructure/ProfileRepository.cs` -- `GetApprovedPhotoAsync`: דוגמה לשאילתת קריאה ציבורית שמצמידת `Profile.ApprovedRevisionId` לגרסה ובודקת `Visibility == Active`; להשתמש באותו דפוס ל-`PublicProfileQuery` (הקרנה בלי טעינת `Photo`).
- `server/Features/Profiles/` -- שירות וממשק שאילתה; כאן `PublicProfileQuery` ו-`IPublicProfileReader` (ממשק שימומש ב-Infrastructure כדי לשמור על תלות פנימה).
- `server/Domain/Profiles/Profile.cs`, `server/Domain/Cohorts/Cohort.cs` -- ישויות (`Cohort.Id`, `Name`, `InviteToken` שאסור להחזיר).
- `server/Api/Endpoints/ProfileEndpoints.cs`, `CohortEndpoints.cs` -- נקודות ציבוריות קיימות ותבנית `PublicFormGuard.CheckIpAsync` (נקודת התמונה משתמשת בה), כותרות ו-`problem+json`.
- `server/Api/Security/PublicFormGuard.cs`, `server/Features/PublicForms/` -- הגבלת קצב לפי IP; להוסיף מדיניות `gallery-ip` עם משתנה `RATE_LIMIT_GALLERY_IP`.
- `server/Api/Resources/Strings.cs` -- הודעות עברית.
- `server/Tests/ProfileOptionalFieldsTests.cs`, `SubmissionReviewTests.cs`, `AuthTestSupport.cs` -- יצירת פרופילים וגרסאות במצבים שונים ישירות בבסיס הנתונים; `ApiClient`.
- אין לשנות: `client/`, נקודות הניהול, `GET /profiles/{profileId}/photo`.

## Tasks & Acceptance

**Execution:**
- `server/Features/Profiles/` ו-`server/Infrastructure/` -- `PublicProfileQuery` ו-`IPublicProfileReader` עם הקרנה, סינון מחזור, עימוד ורשימת מחזורים -- FR-8, FR-10, AR5
- `server/Api/Endpoints/` -- `GET /profiles` ו-`GET /cohorts` עם DTO `Public*`, כותרות והגבלת קצב -- FR-8, FR-9, FR-10, AR14
- `server/Tests/` -- בדיקות אינטגרציה לשורות המטריצה ובדיקת CI בהשתקפות על כל טיפוסי `Public*` ועל ה-JSON -- AR4, NFR1, AR21
- `README.md` -- הנקודות, הפרמטרים והשדות -- תפעול

**Acceptance Criteria:**
- Given מבקר לא מחובר, when קורא ל-`GET /profiles`, then הוא מקבל כרטיסים רק לפרופילים `Active` עם גרסה מאושרת, דרך `PublicProfileQuery` יחידה, ובכל כרטיס אווטאר או תמונה, שם, תיאור קצר ותג מחזור.
- Given כל תגובה ציבורית, when נבדקת בבדיקה אוטומטית, then אין בה מייל, נייד או פרט בקשה, וכל טיפוסי `Public*` נסרקים.
- Given מזהה פרופיל או מחזור, when הוא יוצא מהשרת, then הוא GUID אקראי (v4).

## Spec Change Log

## Review Triage Log

### 2026-10-21 — Review pass
- verdicts: כ-30 ממצאים מארבע שכבות (רבים כפולים) — high 0, medium 2, low 6, false 0
- findings:
  - `[medium]` `[patch]` אין בדיקה שפרופיל עם `ApprovedRevisionId` שמצביע לגרסה שאינה מאושרת או לגרסה של פרופיל אחר נעדר מהגלריה ומרשימת המחזורים — נוספו
  - `[medium]` `[patch]` הצירוף והסינון כתובים פעמיים ולכן ה"נתיב היחיד" עלול להיסטות — חולצו לעוזר משותף
  - `[low]` `[patch]` ניתוח limit ו-offset מקבל רווח ו-`+`, מפתחות חוזרים עמומים, תמונה באורך אפס, בדיקות קצה ו-README (נתיב התמונה, סדר, מטמון 30 שניות, clamp) — תוקנו
  - `[low]` `[defer]` מסך הגלריה בלקוח, סדר לפי זמן אישור, no-store ב-5xx ואינדקסים — נרשמו ב-deferred
  - `[low]` `[reject]` עקביות count מול page, מכסת הקצב מאחורי NAT, שימוש בקצב לפני ולידציה, תלות ב-`Guid.NewGuid` בבדיקה, מיון עברית לפי collation, 410 שמירות בבדיקת הגבלה, HEAD ו-OPTIONS, העתק clamp ב-PublicProfileQuery — לא סביר שייפגעו בשימוש רגיל
## Verification

**Commands:**
- `dotnet build server` -- expected: 0 שגיאות
- `dotnet test server` -- expected: כל הבדיקות עוברות (דורש Docker)

## Auto Run Result

- **שינוי:** `PublicProfileQuery` יחידה (עם `IPublicProfileReader`) שמחזירה רק פרופילים `Active` עם גרסה מאושרת, בנתונים מהגרסה המאושרת בלבד; `GET /profiles` (כרטיסים עם `id`, שם, מוצר וקישור, תיאור, `avatarId`, `photoUrl`, מחזור; סינון מחזור, עימוד, האחרונים קודם) ו-`GET /cohorts` (מחזורים עם פרופיל ציבורי, בלי `InviteToken`), עם DTO ציבוריים, `Cache-Control: public, max-age=30`, `nosniff`, הגבלת קצב לפי IP ובדיקת CI בהשתקפות על כל טיפוסי `Public*` ועל ה-JSON.
- **קבצים:** `server/Features/Profiles/PublicProfileQuery.cs`, `server/Infrastructure/PublicProfileReader.cs`, `server/Api/Endpoints/GalleryEndpoints.cs`, `Program.cs`, `DependencyInjection.cs`, `Strings.cs`, `server/Tests/PublicGalleryTests.cs`, `AuthTestSupport.cs`, `README.md`.
- **סקירה:** 2 תיקוני medium (בדיקות למצביע לא עקבי, עוזר שאילתה משותף) ו-1 low קבוצתי הוחלו. 3 פריטים נדחו ל-`deferred` (מסך הגלריה בלקוח, סדר לפי זמן אישור, no-store ב-5xx ואינדקסים). השאר נדחו כמתועד ביומן.
- **המלצה לסקירת המשך:** כן. הסיכון שלא אומת: נתיב התמונה הציבורי מיישם את תנאי הנראות בנפרד מהשאילתה, ולכן הם עלולים להיסטות; נדרשת בדיקה משותפת בסיפור 1.13.
- **אימות:** `dotnet build server` ללא שגיאות, `dotnet test server`: 368 מתוך 368 עוברות מול Postgres אמיתי.
- **סיכונים שנותרו:** אין עדיין מסך גלריה בלקוח (סיפור 5.7).
