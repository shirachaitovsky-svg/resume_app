---
title: 'סיפור 1.9: קורות חיים'
type: 'feature'
created: '2026-10-21'
status: 'done'
baseline_revision: '66320864fc3a05f5deb0e25fe7031208bd7e8aab'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-resume_app-2026-10-21/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-1-8-שדות-אופציונליים-ואווטאר.md'
warnings: []
deferred:
  - summary: >-
      אין נקודת קריאה לקורות החיים: תצוגת מנהלת (סיפור 1.10) ותצוגה ציבורית (סיפור 1.13) טרם קיימות, ולכן האכיפה היא בכתיבה בלבד
    evidence: |-
      הפונקציה `SafeMarkdown.Clean` משותפת ומיועדת לשימוש חוזר; אין כרגע קורא. יש להפעיל אותה גם בקריאה בסיפורים אלה
    location: >-
      server/Features/Markdown/SafeMarkdown.cs
    severity: medium
  - summary: >-
      עורך Markdown, תצוגה מקדימה, אזהרת הפרטיות ורנדרר בטוח ללא innerHTML חסרים בלקוח
    evidence: |-
      השרת שומר Markdown נקי בלבד; כל הרינדור והאזהרה בלקוח (סיפורי לקוח עתידיים)
    location: >-
      client/ (טרם קיים)
    severity: medium
  - summary: >-
      טקסטים חופשיים אחרים (תיאור, תשובות היכרות) אינם עוברים ניקוי HTML דומה
    evidence: |-
      נבדקים כטקסט פשוט בלי תווי בקרה בלבד; יש לשקול החלת אותה פונקציה לפני הצגה ציבורית (סיפור 1.13)
    location: >-
      server/Features/Profiles/ProfileService.cs
    severity: low---

<intent-contract>

## Intent

**Problem:** משתתפת לא יכולה להגיש קורות חיים, ותוכן חופשי שמוצג בציבור עלול לכלול HTML או קישורים מסוכנים.

**Approach:** להוסיף ב-`server/` שדה אופציונלי `cv` בהגשת הפרופיל: Markdown מוגבל (כותרות, רשימות, הדגשה, קישורים) שהשרת מאמת, מנקה מ-HTML, ודוחה בו קישורים שאינם `http` או `https`, ושומר כחלק מהגרסה. חלק השרת בלבד; עורך, תצוגה מקדימה ואזהרת הפרטיות הם בלקוח.

## Boundaries & Constraints

**Always:** `cv` אופציונלי ומתקבל ב-JSON או ב-multipart לצד שאר השדות. עד 20,000 תווים (נדחה בשרת מעבר לכך). נתמכים רק: פסקאות, כותרות, רשימות (ממוספרות ולא), הדגשה, נטוי, שבירת שורה, קישורים. אל HTML: כל תג HTML (בלוק או שורה) מוסר מהטקסט השמור, כולל `<script>`, `<img>`, `<iframe>` ותגי מרקדאון-HTML מעורבים. קישור (גם בצורת autolink) שכתובתו אינה `http` או `https` מוחלט (למשל `javascript:`, `data:`, `vbscript:`, `mailto:`, יחסי או ללא סכמה) נדחה: 400 עם שגיאה בשדה `cv`, ולא נשמר כלום. תמונות Markdown (`![...]`) נדחות. תווי בקרה (חוץ מ-`\t`, `\n`, `\r` שמנורמל ל-`\n`) ותווים נסתרים מסוכנים (אותה קבוצה כמו `UnsafeCharacters` מסיפור 1.8) נדחים. הטקסט השמור הוא Markdown נקי (ולא HTML מרונדר). הניקוי נעשה בפונקציה אחת משותפת שניתנת לשימוש חוזר (ניהול בסיפור 1.10 וציבור בסיפור 1.13 משתמשים באותה פונקציה). ספריית Markdown: רישיון מתירני (MIT, BSD או Apache). הגבלת גוף JSON עולה כדי להכיל `cv` (עד 64KB). תגובת ההגשה אינה משתנה ואינה מחזירה את הטקסט.

**Never:** אין קובץ קורות חיים, אין רינדור HTML בשרת, אין עורך או תצוגה מקדימה, אין אישור או פרסום (1.11), אין נקודת קריאה ציבורית (1.13). אין שינוי של שדות הטקסט האחרים.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| בלי קורות חיים | `cv` חסר או ריק | נשמר ללא `cv`, 202 כבעבר | N/A |
| Markdown תקין | כותרות, רשימות, הדגשה, קישור `https` | נשמר כפי שנכתב (אחרי נרמול שורות) | N/A |
| HTML בטקסט | `<script>alert(1)</script>` ופסקה, או `<b>x</b>` | התגים מוסרים והטקסט הנותר נשמר | N/A |
| קישור מסוכן | `[x](javascript:alert(1))`, `<data:...>`, `[x](//evil)` | 400 על `cv`, לא נוצר דבר | ולידציה |
| קישור `http` ו-`https` | `[x](http://a.co)`, `[x](https://a.co)` | נשמר | N/A |
| תמונה | `![x](https://a.co/i.png)` | 400 על `cv` | ולידציה |
| ארוך מדי | 20,001 תווים | 400 על `cv` | ולידציה |
| בגבול | 20,000 תווים | נשמר | N/A |
| תווים מסוכנים | NUL, bidi, zero-width | 400 על `cv` | ולידציה |
| פרטיות | כל תגובה | אין בה `cv` או מייל או נייד | N/A |

</intent-contract>

## Code Map

- `server/Features/Profiles/ProfileService.cs` -- ולידציה ויצירה בטרנזקציה; קיימים שם `UnsafeCharacters` (מ-1.8), הסדר של בדיקות שדות אופציונליים והמיפוי להודעות שגיאה. כאן מוסיפים את `cv`.
- `server/Features/PublicForms/UnsafeCharacters.cs` -- קבוצת התווים המסוכנים (לשימוש חוזר).
- `server/Api/Endpoints/ProfileEndpoints.cs` -- גוף JSON (16KB) ו-multipart (512KB); האסימון נפתר לפני קריאת הגוף; להעלות את גבול ה-JSON ל-64KB ולהוסיף את השדה `cv`.
- `server/Domain/Profiles/Profile.cs`, `server/Infrastructure/AppDbContext.cs`, `ProfileRepository.cs` -- `ProfileRevision` ומיפוי snake_case; עמודה חדשה `cv` (text, nullable) ומיגרציה (README מסביר `dotnet-ef`).
- `server/Features/` -- מקום לפונקציית הניקוי המשותפת (`Markdown`/`SafeMarkdown`) ללא תלות ב-Web או Infrastructure.
- `server/Api/Resources/Strings.cs` -- הודעות שגיאה בעברית.
- `server/Tests/ProfileOptionalFieldsTests.cs`, `ProfileSubmissionTests.cs` -- תבנית בדיקות ו-`ApiClient`.
- אין לשנות: `client/`, ההגנות מסיפורים 1.3 עד 1.8.

## Tasks & Acceptance

**Execution:**
- `server/Features/` -- פונקציית ניקוי ואימות Markdown מבוססת מנתח Markdown (AST): הסרת HTML, דחיית קישורים ותמונות לא תקינים, הגבלת אורך ותווים -- AR13, AD-10
- `server/Domain/Profiles/` ו-`server/Infrastructure/` -- `ProfileRevision.Cv`, מיפוי ומיגרציה -- AR13
- `server/Features/Profiles/` ו-`server/Api/Endpoints/` -- קבלת `cv` ב-JSON וב-multipart, ולידציה, שמירה, והעלאת גבול ה-JSON -- FR-3
- `server/Tests/` -- בדיקות יחידה לפונקציית הניקוי ובדיקות אינטגרציה לשורות המטריצה, כולל תוקפים מוכרים של XSS -- AR21
- `README.md` -- השדה, הכללים והפונקציה המשותפת -- תפעול

**Acceptance Criteria:**
- Given קורות חיים עם HTML או קישור שאינו `http`/`https`, when נשמרים, then ה-HTML מוסר והקישור נדחה (400), ולא נשמר טקסט מסוכן.
- Given קורות חיים תקינים, when נשמרים, then הם נשמרים כ-Markdown כחלק מהגרסה, ואינם מופיעים באף תגובה ציבורית.
- Given הפונקציה המשותפת, when קוראים לה פעמיים על אותו קלט, then התוצאה זהה (אידמפוטנטית).

## Spec Change Log

## Review Triage Log

### 2026-10-21 — Review pass
- verdicts: כ-30 ממצאים מארבע שכבות (רבים כפולים) — high 0, medium 5, low 5, false 1
- findings:
  - `[medium]` `[patch]` תווי בקרה בקצוות (U+2028, \v, \f ועוד) נחתכים ב-Trim לפני הבדיקה ולא נדחים — הבדיקה לפני החיתוך
  - `[medium]` `[patch]` רשימת המותר לא נאכפת: קוד, ציטוט וקווי הפרדה עוברים ותגי HTML בתוך קוד נשמרים — נאכפת הרשימה ונדחים שאר הצמתים
  - `[medium]` `[patch]` עומס CPU על קלט אנונימי: 20 מעברים ו-regex ריבועי — הוגבל ל-5 מעברים, regex ליניארי ותקרת זמן כוללת, עם בדיקות עוינות
  - `[medium]` `[patch]` קישור עם אישורים (`user:pass@host`) מתקבל; גבול JSON של 64KB נכשל לטקסט ב-\uXXXX; cv שאינו מחרוזת עלול לתת 500 — תוקנו
  - `[low]` `[patch]` מקור יחיד לאורך המרבי, הודעות שגיאה לא נבדקות, README לא מעודכן — תוקנו
  - `[medium]` `[defer]` אין קורא ל-cv (ניהול וציבור), ועורך ורנדרר בלקוח, וניקוי של שאר הטקסטים החופשיים — נרשמו ב-deferred
  - `[low]` `[reject]` ממד שם של namespace מוצל ב-Markdig, בדיקת אורך לפני נרמול CRLF, יחד עם photo, אישור הגרסה — לא סביר שייפגעו בשימוש רגיל או שייכים לסיפורים אחרים
  - `[false]` `[reject]` Designer ו-snapshot חסרים — הקבצים קיימים (נבדק ב-grep על העמודה)
## Verification

**Commands:**
- `dotnet build server` -- expected: 0 שגיאות
- `dotnet test server` -- expected: כל הבדיקות עוברות (דורש Docker)

## Auto Run Result

- **שינוי:** שדה אופציונלי `cv` בהגשת הפרופיל (JSON ו-multipart) כ-Markdown מוגבל. הפונקציה המשותפת `SafeMarkdown.Clean` (Markdig) מנרמלת שורות, דוחה תווים נסתרים, מסירה תגי HTML, אוכפת רשימת מותר (פסקאות, כותרות, רשימות, הדגשה, קישורים) ודוחה (400 על `cv`) תמונות, קוד, ציטוטים, קישורים שאינם `http`/`https` מוחלט וקישורים עם אישורים. נשמר כ-Markdown נקי כחלק מהגרסה ואינו מוחזר באף תגובה. מגבלות: 20,000 תווים, 5 מעברי הסרה, תקרת זמן של 250ms.
- **קבצים:** `server/Features/Markdown/SafeMarkdown.cs`, `server/Features/Profiles/ProfileService.cs`, `server/Api/Endpoints/ProfileEndpoints.cs`, `Strings.cs`, `server/Domain/Profiles/Profile.cs`, `AppDbContext.cs`, מיגרציית `ProfileCv`, `server/Tests/SafeMarkdownTests.cs` ו-`ProfileOptionalFieldsTests.cs`, `README.md`.
- **סקירה:** 5 תיקוני medium ו-2 low הוחלו (בדיקת תווים נסתרים לפני חיתוך, אכיפת רשימת המותר, הגנת CPU, קישורים עם אישורים, גבול JSON ו-`cv` שאינו מחרוזת). 3 פריטים נדחו ל-`deferred` (אין קורא ל-`cv` בניהול ובציבור, עורך ורנדרר בלקוח, ניקוי טקסטים חופשיים אחרים). השאר נדחו כמתועד ביומן.
- **המלצה לסקירת המשך:** כן. הסיכון שלא אומת: האכיפה היא בכתיבה בלבד, ו-Markdig עלול להתנהג אחרת בגרסאות עתידיות; מומלץ לחזור על הניקוי גם בקריאה בסיפורים 1.10 ו-1.13.
- **אימות:** `dotnet build server` ללא שגיאות, `dotnet test server`: 318 מתוך 318 עוברות מול Postgres אמיתי.
- **סיכונים שנותרו:** מסכי הלקוח (עורך, תצוגה מקדימה, אזהרת פרטיות, רנדרר בטוח) לא קיימים.
