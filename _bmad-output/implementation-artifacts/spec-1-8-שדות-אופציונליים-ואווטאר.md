---
title: 'סיפור 1.8: שדות אופציונליים ואווטאר'
type: 'feature'
created: '2026-10-21'
status: 'done'
baseline_revision: '64c20f750d710789f410bcc614ebb81571fd42ab'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-resume_app-2026-10-21/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-1-7-הגנה-על-טפסים-ציבוריים.md'
warnings: []
deferred:
  - summary: >-
      מסכי הלקוח של הסיפור (בורר דמות עם תצוגה מקדימה, מונה 120 תווים, הקטנת תמונה בדפדפן ל-300KB, רינדור אווטאר ברירת מחדל) וציורי הדמויות לא קיימים
    evidence: |-
      השרת מאמת את השדות ואת מזהי הדמויות בלבד; יש לממש בסיפורי הלקוח ולהכין את ששת האיורים
    location: >-
      client/ (טרם קיים)
    severity: medium
  - summary: >-
      עמודת התמונה (bytea) יושבת על ישות הגרסה, ולכן שאילתה שטוענת גרסה בלי הקרנה תטען עד כ-300KB לשורה
    evidence: |-
      להוסיף הקרנה בשאילתות הניהול והציבור בסיפורים 1.10, 1.12 ו-1.13, או לפצל לטבלה נפרדת
    location: >-
      server/Domain/Profiles/Profile.cs
    severity: low
  - summary: >-
      תמונה של פרופיל שהוסר עשויה להישאר במטמון ציבורי עד 5 דקות, ואין ETag או בקשות מותנות
    evidence: |-
      `Cache-Control: public, max-age=300`; מתועד ב-README; לשקול ETag לפי מזהה גרסה
    location: >-
      server/Api/Endpoints/ProfileEndpoints.cs
    severity: low
  - summary: >-
      עיבוד התמונה רץ גם בהגשות כפולות שמתעלמים מהן, ומאפשר הבחנה בתזמון
    evidence: |-
      נוגע לחשיפת קיום כתובת; ההגבלות בסיפור 1.7 מצמצמות את הסיכון
    location: >-
      server/Features/Profiles/ProfileService.cs
    severity: low---

<intent-contract>

## Intent

**Problem:** הגשת הפרופיל (סיפור 1.6) מקבלת רק שדות חובה, ואין דרך להוסיף תיאור, פרטי היכרות, תמונה או דמות אווטאר.

**Approach:** להרחיב ב-`server/` את `POST /invites/{token}/profiles` בשדות אופציונליים (תיאור קצר, תפקיד, מקום עבודה, טקסט היכרות, שלוש תשובות היכרות, בחירת דמות) ובהעלאת תמונה אופציונלית שהשרת מאמת ומקודד מחדש ושומר כחלק מהגרסה, ולהוסיף נקודה ציבורית שמגישה רק תמונה של גרסה מאושרת. חלק השרת בלבד.

## Boundaries & Constraints

**Always:** כל השדות החדשים אופציונליים, והגשה בלעדיהם ממשיכה לעבוד כבסיפור 1.6. תיאור קצר עד 120 תווים (נדחה בשרת מעבר לכך), תפקיד ומקום עבודה עד 100, טקסט היכרות עד 1000, כל תשובת היכרות עד 1000 (שלוש תשובות לפי אינדקס 1 עד 3; נוסחי השאלות הם קבועים בלקוח). בחירת דמות: מזהה מתוך שש דמויות קבועות (`girl-1..3`, `boy-1..3`); מזהה אחר נדחה. בלי תמונה ובלי בחירה: ברירת מחדל `default`. הטקסטים החופשיים מנוקים כבסיפור 1.6 (בלי תווי בקרה) ונשמרים כטקסט פשוט. הבקשה מקבלת `application/json` (כבעבר) או `multipart/form-data` (שדות הטקסט כחלקים בשמותיהם ו-`photo` כקובץ). הגבלת גוף: 16KB ל-JSON, וכ-512KB ל-multipart. תמונה: עד כ-300KB אחרי הקטנה בדפדפן; השרת מאמת סוג לפי תוכן (JPEG, PNG או WebP בלבד, לא SVG ולא סוג אחר), מגביל מימדים ובייטים, מפענח ומקודד מחדש ל-JPEG או WebP ושומר כ-`bytea` בשורת הגרסה (`ProfileRevision`), לא בקובץ. ספרייה לעיבוד תמונה: רישיון מתירני (MIT או Apache) שרץ בקונטיינר Linux של הפרויקט. תמונה שהועלתה גוברת על אווטאר. הנקודה הציבורית `GET /profiles/{profileId}/photo` מחזירה אך ורק את תמונת הגרסה המאושרת של פרופיל `Active`, עם `X-Content-Type-Options: nosniff`, `Cache-Control` מתאים, ו-404 אחיד בכל מקרה אחר (אין גרסה מאושרת, אין תמונה, פרופיל שהוסר, מזהה לא קיים). ההגנות מסיפור 1.7 (Turnstile והגבלת קצב) ממשיכות לחול על ההגשה; לנקודת התמונה מגבלת IP בלבד. הפרטיות: אין מייל או נייד בשום תגובה.

**Never:** אין קורות חיים (1.9), אין אישור או פרסום (1.11), אין גלריה או עמוד פרופיל (1.12, 1.13), אין מסכי לקוח. אין כתיבה לדיסק. אין החזרת תמונה של גרסה ממתינה.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| בלי שדות אופציונליים | רק שדות החובה | 202 כבסיפור 1.6; אווטאר `default` | N/A |
| תיאור ארוך | 121 תווים | 400 עם שגיאה בשדה | ולידציה |
| תיאור בגבול | 120 תווים | נשמר | N/A |
| דמות תקפה | `avatarId=girl-2` | נשמר | N/A |
| דמות לא מוכרת | `avatarId=robot-9` | 400 עם שגיאה בשדה | ולידציה |
| תמונה תקינה | JPEG או PNG תקין עד הגבול | מקודדת מחדש ונשמרת בגרסה ו-`bytea`; האווטאר מוחלף | N/A |
| SVG או לא תמונה | קובץ SVG, HTML או קובץ עם סיומת מטעה | 400 ולא נוצר דבר | נבדק לפי תוכן |
| תמונה גדולה | מעל הגבול או במימדים חריגים | 400 או 413, לא נוצר דבר | N/A |
| תמונה פגומה | בייטים שאינם פענוח תקין | 400 | N/A |
| תמונה של גרסה ממתינה | `GET /profiles/{id}/photo` לפני אישור | 404 אחיד | N/A |
| תמונה של גרסה מאושרת | גרסה מאושרת עם תמונה (נקבע בבדיקה ישירות בבסיס הנתונים) | 200 עם `nosniff` וסוג התמונה | N/A |
| פרופיל שהוסר או לא קיים | `Visibility=removed` או מזהה אחר | 404 אחיד | N/A |

</intent-contract>

## Code Map

- `server/Api/Endpoints/ProfileEndpoints.cs` -- `POST /invites/{token}/profiles`: האסימון נפתר לפני קריאת הגוף, גבול 16KB, 413/415/400, `PublicFormGuard`; כאן מוסיפים multipart ושדות אופציונליים תוך שמירה על הסדר והגנות.
- `server/Features/Profiles/ProfileService.cs` -- ולידציה, `NormalizeEmail`, יצירה בטרנזקציה אחת; להוסיף ולידציית השדות החדשים ואת רשימת הדמויות.
- `server/Domain/Profiles/Profile.cs`, `server/Infrastructure/ProfileRepository.cs`, `AppDbContext.cs` -- `ProfileRevision` ומיפוי snake_case; להוסיף עמודות (תיאור, תפקיד, מקום עבודה, טקסט היכרות, תשובות, `avatar_id`, `photo` כ-`bytea` ו-content-type) ומיגרציה (README מסביר `dotnet-ef`).
- `server/Api/Security/PublicFormGuard.cs` -- מגבלות IP ו-Turnstile; להשתמש בו לנקודת התמונה (IP בלבד, בלי Turnstile).
- `server/Api/Endpoints/CohortEndpoints.cs` -- תבנית נקודה ציבורית עם 404 אחיד וכותרות `no-store`; נקודת התמונה לא `no-store` אלא `Cache-Control` ציבורי מתון.
- `server/Tests/ProfileSubmissionTests.cs`, `PublicFormProtectionTests.cs`, `AuthTestSupport.cs` -- `ApiClient` שמוסיף Turnstile ו-IP; להרחיב לשליחת multipart.
- אין לשנות: `client/`, התנהגות ההגנות מסיפורים 1.3 עד 1.7.

## Tasks & Acceptance

**Execution:**
- `server/Domain/Profiles/` ו-`server/Infrastructure/` -- הרחבת `ProfileRevision` בשדות האופציונליים ובתמונה, ומיגרציה -- AR12, AR19
- `server/Features/Profiles/` -- ולידציה של השדות, רשימת הדמויות, ועיבוד תמונה (סוג לפי תוכן, מימדים, קידוד מחדש) מאחורי ממשק -- AR12, FR-3
- `server/Api/Endpoints/` -- קבלת JSON או multipart בנקודת ההגשה, ו-`GET /profiles/{profileId}/photo` -- FR-3, AD-19
- `server/Tests/` -- בדיקות לשורות המטריצה, כולל SVG ותמונה פגומה, גבולות 120/300KB, ו-404 לגרסה ממתינה -- AR21
- `README.md` -- השדות החדשים, נוסחי הבקשה (JSON ו-multipart) והנקודה הציבורית -- תפעול

**Acceptance Criteria:**
- Given הגשה בלי שדות אופציונליים, when נשלחת, then היא מצליחה כבסיפור 1.6 והדמות `default`.
- Given העלאת תמונה, when הקובץ מגיע, then השרת מאמת סוג וגודל, מקודד מחדש ושומר ב-`bytea` כחלק מהגרסה; SVG או קובץ שאינו תמונה נדחה.
- Given גרסה ממתינה, when מבקר מבקש את התמונה, then 404, ורק תמונת הגרסה המאושרת מוגשת, עם `nosniff`.

## Spec Change Log

## Review Triage Log

### 2026-10-21 — Review pass
- verdicts: כ-45 ממצאים מארבע שכבות (רבים כפולים) — high 0, medium 7, low 6, false 1
- findings:
  - `[medium]` `[patch]` כיוון EXIF מתעלמים ולכן תמונות טלפון נשמרות הפוכות; catch רחב מסתיר תקלות תשתית כשגיאת משתמש; פענוח 12MP בלי תקרת מקביליות — תוקנו
  - `[medium]` `[patch]` חלק `photo` ריק (שדה קובץ ריק בדפדפן) נדחה; גוף multipart פגום עלול להחזיר 500; שדות כפולים; `avatarId=default` — תוקנו
  - `[medium]` `[patch]` תווי בקרה: טאב בטקסט ארוך נדחה, בעוד U+2028/2029, bidi ו-zero-width עוברים — אוחד עם סניטייזר המייל
  - `[medium]` `[patch]` בדיקות חסרות: שאילתת התמונה המאושרת עם כמה גרסאות ופרופילים, שטיחות alpha והסרת EXIF, שגיאות multipart, 12MP, no-store ב-404 — נוספו
  - `[low]` `[patch]` README חסר תגובות 403/429/503/415, מטמון של פרופיל שהוסר ונכסי Linux — עודכן
  - `[low]` `[defer]` מסכי לקוח ואיורי דמויות, bytea על ישות הגרסה, מטמון ציבורי, תזמון בהגשה כפולה — נרשמו ב-deferred
  - `[low]` `[reject]` דחיית תמונות מונפשות, הודעת שגיאה נפרדת למימדים, Down של המיגרציה, מגבלת קצב לתמונות מאחורי NAT, מרחב שמות של מיגרציה, אלט טקסט והסכמה לתמונות פנים — לא סביר שייפגעו בשימוש רגיל או מחוץ להיקף הסיפור
  - `[false]` `[reject]` Designer ו-snapshot חסרים — הקבצים קיימים והוחרגו מה-diff
## Verification

**Commands:**
- `dotnet build server` -- expected: 0 שגיאות
- `dotnet test server` -- expected: כל הבדיקות עוברות (דורש Docker)

## Auto Run Result

- **שינוי:** הרחבת `POST /invites/{token}/profiles` בשדות אופציונליים (תיאור עד 120, תפקיד, מקום עבודה, טקסט היכרות, שלוש תשובות, בחירת דמות מתוך שש) ובהעלאת תמונה ב-`multipart/form-data`: אימות לפי תוכן (JPEG, PNG, WebP), תיקון כיוון EXIF, קידוד מחדש ל-JPEG על רקע לבן בלי metadata ושמירה כ-`bytea` בגרסה. נוספה `GET /profiles/{profileId}/photo` שמגישה רק תמונה של גרסה מאושרת של פרופיל `Active` עם `nosniff`.
- **קבצים:** `server/Domain/Profiles/Profile.cs`, `server/Features/Profiles/ProfileService.cs` ו-`IPhotoProcessor.cs`, `server/Features/PublicForms/UnsafeCharacters.cs`, `server/Infrastructure/SkiaPhotoProcessor.cs`, מיגרציית `ProfileOptionalFields`, `server/Api/Endpoints/ProfileEndpoints.cs`, `PublicFormGuard.cs`, `server/Tests/ProfileOptionalFieldsTests.cs`, `README.md`.
- **סקירה:** 7 תיקוני medium ו-3 low הוחלו (כיוון EXIF, שגיאות תשתית, תקרת מקביליות, חלק תמונה ריק, multipart פגום, תווים נסתרים, בדיקות חסרות). 4 פריטים נדחו ל-`deferred` (מסכי לקוח ואיורי דמויות, `bytea` על הישות, מטמון ציבורי, תזמון בהגשה כפולה). השאר נדחו כמתועד ביומן.
- **המלצה לסקירת המשך:** כן. סיכונים שלא אומתו: SkiaSharp בקונטיינר Linux של Render לא הורץ בפועל; תמונה מאושרת נבדקה רק באישור שנקבע ישירות בבסיס הנתונים (זרימת האישור בסיפור 1.11).
- **אימות:** `dotnet build server` ללא שגיאות, `dotnet test server`: 221 מתוך 221 עוברות מול Postgres אמיתי.
- **סיכונים שנותרו:** מסכי לקוח חסרים; Turnstile מול השירות החי לא נבדק.
