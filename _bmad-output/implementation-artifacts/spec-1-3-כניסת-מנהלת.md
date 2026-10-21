---
title: 'סיפור 1.3: כניסת מנהלת'
type: 'feature'
created: '2026-10-21'
status: 'done'
baseline_revision: '04b6fd5c90a2e9a236c2c74e25e5507aa8b9cccd'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-resume_app-2026-10-21/ARCHITECTURE-SPINE.md'
warnings: []
deferred:
  - summary: >-
      ספייק העוגיות ב-Vercel (כניסה מקצה לקצה, העוגיה חוזרת ונשלחת, אין מטמון CDN) לא בוצע
    evidence: |-
      דורש חיבור Vercel ו-Render; כל הבדיקות רצות ברמת השרת בלבד. אם Vercel לא מעבירה את המארח המקורי, כל POST יחזיר 403 עד הגדרת ALLOWED_ORIGINS. הסיפור לא נסגר סופית עד שהבדיקה עוברת
    location: >-
      README.md (ספייק עוגיות)
    severity: high (unverified)
  - summary: >-
      מפתחות Data Protection נשמרים בבסיס הנתונים בלי הצפנה
    evidence: |-
      מי שקורא את הטבלה יכול לזייף עוגיות ואסימוני anti-forgery; לשקול ProtectKeysWith לפני עלייה
    location: >-
      server/Api/Security/IdentityServiceExtensions.cs
    severity: medium
  - summary: >-
      נעילת חשבון ללא הגבלת קצב לפי IP מאפשרת לנעול את האדמין היחיד, ואין אכיפה של שני אדמינים
    evidence: |-
      חמש כניסות כושלות נועלות 15 דקות; הגבלת קצב לפי IP בסיפור 1.7 ושני אדמינים בסיפור 1.4
    location: >-
      server/Api/Endpoints/AuthEndpoints.cs
    severity: medium
  - summary: >-
      רשימת הסיסמאות הנפוצות קצרה (כ-97 פריטים)
    evidence: |-
      להחליף ברשימה מלאה (למשל SecLists) לפני עלייה לאוויר
    location: >-
      server/Api/Security/common-passwords.txt
    severity: low---

<intent-contract>

## Intent

**Problem:** אין בשרת זהות, ולכן אי אפשר להגן על מסכי ופעולות הניהול, ושום נקודת קצה לא נדרשת לאימות כברירת מחדל.

**Approach:** להוסיף ל-`server/` ASP.NET Core Identity עם משתמש אדמין שנזרע ממשתני סביבה, כניסה בעוגיית סשן מוקשחת, `FallbackPolicy` סגורה, מדיניות סיסמה ונעילה, הגנת anti-forgery ובדיקת `Origin`, ומפתחות Data Protection בבסיס הנתונים. חלק השרת בלבד; אין מסכי לקוח.

## Boundaries & Constraints

**Always:** כל נקודת קצה דורשת אימות אלא אם מסומנת `[AllowAnonymous]` (`/healthz` ונקודות הכניסה). נתיבי השרת בלי קידומת `/api` (Vercel מסירה אותה). עוגיה `__Host-`, `HttpOnly`, `Secure` מפורש, `SameSite=Strict`, תוקף סשן, נתיב `/`. תגובות מאומתות `Cache-Control: no-store`. כל בקשת שינוי: אסימון anti-forgery ובדיקת `Origin`. כשל כניסה: תגובה זהה (401) למשתמש שאינו קיים, לסיסמה שגויה ולנעילה. אסור לרשום סיסמה או אסימון ביומן. טבלאות Identity בסכמה דרך מיגרציית EF Core.

**Never:** אין TOTP (סיפור 1.4), אין ממשק לקוח, אין הרשמה עצמית, אין ממשק ניהול משתמשים. אין סיסמה בקוד או בריפו.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| זריעה ראשונה | אין אדמין; `ADMIN_USERNAME`, `ADMIN_INITIAL_PASSWORD` מוגדרים | נוצר אדמין עם `MustChangePassword`, תפקיד `Admin` | חסרים משתנים: מדלגים ורושמים אזהרה |
| זריעה חוזרת | אדמין כבר קיים | לא נוצר נוסף | No error expected |
| אנונימי לנקודה מוגנת | בלי עוגיה | 401 | N/A |
| מחובר שאינו אדמין | משתמש ללא תפקיד `Admin` | 403 | N/A |
| סיסמה חלשה | קצרה מ-12 או ברשימת הסיסמאות הנפוצות | נדחית בהחלפה | שגיאת ולידציה (`problem+json`) |
| נעילה | חמישה ניסיונות כושלים | משתמש ננעל; התגובה זהה ל-"משתמש לא קיים" | 401 אחיד |
| כניסה מוצלחת | פרטים נכונים | העוגיה עם כל המאפיינים; `no-store`; סיסמה חד-פעמית דורשת החלפה לפני כל פעולה אחרת | N/A |
| בקשת שינוי | בלי אסימון anti-forgery או עם `Origin` זר | נדחית (400/403) | N/A |

</intent-contract>

## Code Map

- `server/Api/Program.cs` -- נוכחי: Serilog, `OriginSecretMiddleware`, forwarded headers, migrate, `MapHealth`. כאן רישום Identity, אימות והרשאה (`FallbackPolicy`), anti-forgery ו-middleware ל-`Origin` ו-`no-store`. הסדר: סוד מקור ואז forwarded headers ואז אימות.
- `server/Api/Endpoints/HealthEndpoints.cs` -- תבנית נקודות קצה עם `AllowAnonymous`; ליצור `AuthEndpoints.cs` במקביל.
- `server/Infrastructure/AppDbContext.cs` -- להפוך ל-`IdentityDbContext` וגם `IDataProtectionKeyContext`.
- `server/Infrastructure/DependencyInjection.cs` -- `AddInfrastructure` ומיגרציות תחת advisory lock; זריעת האדמין רצה אחרי המיגרציה.
- `server/Infrastructure/Migrations/` -- מיגרציה חדשה (`dotnet tool run dotnet-ef migrations add`, ראו README).
- `server/Tests/ApiFactory.cs`, `HealthzTests.cs` -- תבנית בדיקות; הבדיקות החדשות רצות על Postgres אמיתי דרך Testcontainers (Docker מותקן במחשב). משתני סביבה מגדירים את הסביבה, אוסף `[Collection("env")]`.
- אין לשנות: `client/`, `vercel.json`, `middleware.js`.

## Tasks & Acceptance

**Execution:**
- `server/Infrastructure/` -- חבילות Identity ו-Data Protection EF; `AppDbContext` כ-IdentityDbContext עם `DataProtectionKeys`; משתמש עם שדה `MustChangePassword`; מיגרציה -- AR18, AR9
- `server/Infrastructure/` -- זריעת אדמין ממשתני סביבה (פעם אחת) אחרי המיגרציה -- AR20
- `server/Api/Security/` -- הגדרות Identity: סיסמה של 12 תווים לפחות, ולידטור שדוחה סיסמאות מרשימה נפוצה מצורפת כמשאב, נעילה אחרי 5 ניסיונות, עוגיה `__Host-` Strict, תפקיד `Admin` -- NFR6
- `server/Api/Endpoints/AuthEndpoints.cs` -- `POST /auth/login`, `POST /auth/logout`, `POST /auth/change-password`, `GET /auth/me`, `GET /auth/csrf`; תגובות אחידות; חסימת כל השאר עד החלפת סיסמה -- FR-17
- `server/Api/Program.cs` -- `FallbackPolicy`, anti-forgery, בדיקת `Origin` לשיטות משנות, `no-store` -- AD-5, AD-20
- `server/Tests/` -- בדיקות אינטגרציה לשורות המטריצה (כולל נעילה והחלפת סיסמה) -- AR21
- `README.md` -- משתני `ADMIN_USERNAME`, `ADMIN_INITIAL_PASSWORD` והסבר על ספייק העוגיות ב-Vercel -- תפעול

**Acceptance Criteria:**
- Given אדמין מחובר, when ניגש לנקודת בדיקה מוגנת `GET /admin/ping`, then 200; אנונימי 401; משתמש בלי תפקיד 403.
- Given כניסה מוצלחת, when נבדק `Set-Cookie`, then שם העוגיה מתחיל ב-`__Host-` ויש בה `HttpOnly`, `Secure`, `SameSite=Strict`, בלי `Expires`; והתגובה `Cache-Control: no-store`.
- Given הפעלה מחדש של השרת, when נשלחת בקשה עם עוגיה קיימת, then היא תקפה (מפתחות Data Protection בבסיס הנתונים).
- Given פריסת preview ב-Vercel, when כניסה דרך כתובת Vercel בלבד, then העוגיה חוזרת ונשלחת; אימות ידני בלבד אחרי חיבור הפריסה, ואם נכשל הסיפור לא נסגר עד בחירת חלופה (README מתעד).

## Spec Change Log

## Review Triage Log

### 2026-10-21 — Review pass
- verdicts: כ-50 ממצאים מארבע שכבות (רבים כפולים) — high 0, medium 6, low 6, false 2
- findings:
  - `[medium]` `[patch]` ניחוש סיסמה נוכחית בהחלפת סיסמה עוקף את הנעילה — נספרת כניסה כושלת
  - `[medium]` `[patch]` זריעת האדמין: תוצאות לא נבדקות, בלי נעילה בין מופעים, ותלויה ב-MIGRATE_ON_STARTUP — תוקן
  - `[medium]` `[patch]` בדיקת הזריעה לא מזהה זריעה כפולה; ALLOWED_ORIGINS וקבצי סביבה ללא בדיקה — נוספו בדיקות
  - `[medium]` `[patch]` חריגת anti-forgery מחזירה 500 במקום 403 — נתפסת
  - `[low]` `[patch]` timing בנעילה, אורך סיסמה בלי הגבלה, תוצאת UpdateAsync לא נבדקת — תוקנו
  - `[medium]` `[defer]` ספייק Vercel, מפתחות DP לא מוצפנים, נעילת אדמין יחיד, רשימת סיסמאות קצרה — נרשמו ב-deferred
  - `[low]` `[reject]` סדר gate מול UseAuthorization, מקף/הבדלי נתיב ב-gate, ולידציית SecurityStamp, DbContext בלי חיבור (500 ברור מספיק), מיגרציה בלי ניקוי BOM, audit log, רכיבי דוקומנטציה — לא סביר שייפגעו בשימוש רגיל
  - `[false]` `[reject]` Designer ו-snapshot חסרים — קיימים והוחרגו מה-diff; פער Intent (Vercel) — מתועד כספייק ידני במפרט
## Verification

**Commands:**
- `dotnet build server` -- expected: 0 שגיאות
- `dotnet test server` -- expected: כל הבדיקות עוברות (דורש Docker)

## Auto Run Result

- **שינוי:** כניסת אדמין בשרת: Identity עם זריעה ממשתני סביבה והחלפת סיסמה חובה, `FallbackPolicy` סגורה (401/403), סיסמה של 12+ תווים עם רשימה נפוצה, נעילה, עוגיה `__Host-` Strict, anti-forgery ובדיקת `Origin`, `no-store`, ומפתחות Data Protection בבסיס הנתונים.
- **קבצים:** `server/Api/Security/*`, `server/Api/Endpoints/AuthEndpoints.cs` ו-`AdminEndpoints.cs`, `server/Infrastructure/Identity/*`, מיגרציית `AdminIdentity`, `Program.cs`, `README.md`, בדיקות אינטגרציה על Postgres אמיתי (Testcontainers).
- **סקירה:** 5 תיקוני medium ו-3 תיקוני low הוחלו (נעילה בהחלפת סיסמה, זריעה אטומית, 403 על חריגות anti-forgery, בדיקות חסרות ועוד). 4 פריטים נדחו ל-`deferred`, והשאר נדחו כמתועד ביומן.
- **המלצה לסקירת המשך:** כן. הסיכון שלא אומת: ספייק העוגיות והבדיקת `Origin` דרך Vercel (שורת AC אחרונה), שדורש חיבור הפריסה; אם Vercel לא מעבירה את המארח המקורי, כל POST יחזיר 403 עד הגדרת `ALLOWED_ORIGINS`.
- **אימות:** `dotnet build server` ללא שגיאות, `dotnet test server`: 31 מתוך 31 עוברות מול Postgres 16 אמיתי.
- **סיכונים שנותרו:** הספייק של Vercel; מפתחות DP לא מוצפנים; רשימת סיסמאות קצרה; נעילת אדמין יחיד (ראו `deferred`).
