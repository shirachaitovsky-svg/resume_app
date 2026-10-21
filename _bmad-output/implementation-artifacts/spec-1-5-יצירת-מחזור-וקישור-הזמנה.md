---
title: 'סיפור 1.5: יצירת מחזור וקישור הזמנה'
type: 'feature'
created: '2026-10-21'
status: 'done'
baseline_revision: '1405fc15375e5c260485a320092d0f86b542205d'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-resume_app-2026-10-21/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-1-4-אימות-דו-שלבי-למנהלת.md'
warnings: []
deferred:
  - summary: >-
      אין עדיין מסכי לקוח לניהול: כניסת מנהלת, הרשמת TOTP, מסך "מחזורים" עם כפתור העתקה, ועמוד `/join/{token}` למבקרת
    evidence: |-
      סיפורים 1.3 עד 1.5 מימשו רק את השרת; קריטריוני הקבלה המקוריים מתארים מסכים. אף סיפור ברשימה לא מכסה את מסכי הניהול ולכן יש להוסיף סיפור לקוח
    location: >-
      client/
    severity: high
  - summary: >-
      נקודת בדיקת קישור ההזמנה האנונימית ללא הגבלת קצב
    evidence: |-
      אסימון של 256 ביט מונע ניחוש, אך שאילתות בלי תקרה פתוחות לעומס; הגנת הטפסים בסיפור 1.7
    location: >-
      server/Api/Endpoints/CohortEndpoints.cs
    severity: low---

<intent-contract>

## Intent

**Problem:** המנהלת לא יכולה ליצור מחזור, ואין דרך להזמין משתתפות להגיש פרופיל לתוך מחזור מסוים.

**Approach:** להוסיף ב-`server/` ישות `Cohort` עם `InviteToken` אקראי חזק, נקודות ניהול (יצירה, רשימה, ביטול קישור) המוגנות באדמין, ונקודה ציבורית שמאמתת קישור הזמנה ומחזירה רק את שם המחזור. חלק השרת בלבד; אין מסכי לקוח בסיפור זה.

## Boundaries & Constraints

**Always:** נקודות הניהול תחת `/admin/cohorts` דורשות `AdminPolicy` (ובכך גם אימות דו-שלבי שהושלם). הנקודה הציבורית מסומנת `[AllowAnonymous]` מפורשות. `InviteToken`: 32 בתים אקראיים מקריפטוגרפיה בקידוד base64url, ייחודי באינדקס. מזהה המחזור GUID v4. תגובה ציבורית מחזירה אך ורק `PublicInvite` DTO ייעודי (שם מחזור), בלי מזהה פנימי, בלי רשימת פרופילים ובלי פרטי מחזורים אחרים. תגובות שגיאה ב-`problem+json`. קישור מבוטל וקישור לא קיים מחזירים תגובה זהה. שינוי מצב הביטול הוא עדכון מותנה (409 בהתנגשות). כתובת הקישור נבנית מ-`PUBLIC_BASE_URL` (ברירת מחדל `http://localhost:4200`) בצורה `{base}/join/{token}`. טבלאות ב-snake_case ומיגרציית EF Core.

**Never:** אין מסכי לקוח, אין יצירת פרופיל, אין מחיקת מחזור, אין הגבלת קצב או Turnstile (סיפור 1.7). אין רישום האסימון ביומן.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| יצירת מחזור | אדמין מחובר ומושלם TOTP, שם תקין | 201 עם `id`, `name`, `inviteToken`, `inviteUrl`, `createdAt` | N/A |
| שם לא תקין | שם ריק, רווחים בלבד או ארוך מ-100 | 400 `problem+json` עם שגיאת שדה | ולידציה |
| רשימת מחזורים | אדמין | 200 עם המחזורים, החדש ראשון, כולל סטטוס ביטול | N/A |
| קישור תקף | מבקר אנונימי, אסימון פעיל | 200 עם `{ cohortName }` בלבד | N/A |
| קישור מבוטל | אסימון שבוטל | 404 `problem+json`, בלי שם מחזור | תגובה זהה לאסימון לא קיים |
| קישור לא קיים או פגום | אסימון שגוי או ארוך מ-128 תווים | 404 זהה | N/A |
| ביטול | אדמין, מחזור פעיל | 204; ביטול חוזר 409 | עדכון מותנה |
| לא אדמין | אנונימי: 401; מחובר ללא תפקיד: 403 | נדחה בכל נקודות `/admin/cohorts` | N/A |
| בקשת שינוי | בלי אסימון anti-forgery או עם Origin זר | נדחית כבסיפור 1.3 | N/A |

</intent-contract>

## Code Map

- `server/Domain/` -- פרויקט ריק; כאן ישות `Cohort` (ללא תלות בכלום).
- `server/Features/` -- פרויקט ריק; כאן `Cohorts/` עם שירות ו-`ICohortRepository` (AD: vertical slices).
- `server/Infrastructure/AppDbContext.cs` -- IdentityDbContext מסיפור 1.3; להוסיף `DbSet<Cohort>`, מיפוי snake_case ואינדקס ייחודי על `InviteToken`; מיגרציה חדשה (`dotnet tool run dotnet-ef migrations add`, ראו README). מימוש `ICohortRepository` ב-`Infrastructure/`.
- `server/Api/Endpoints/AdminEndpoints.cs` -- מכיל את `/admin/ping` ו-`AdminPolicy`; לצרף `MapGroup("/admin/cohorts")`. `AuthEndpoints.cs` -- תבנית `problem+json`/`Validation` ו-`AllowAnonymous`.
- `server/Api/Program.cs` -- רישום שירותים ו-`Map*`; `RequestIntegrityMiddleware` כבר מגן על POST.
- `server/Api/Resources/Strings.cs` -- מחרוזות עברית להודעות שגיאה.
- `server/Tests/AuthTestSupport.cs`, `AdminTotpTests.cs` -- `AuthApiFactory`, `ApiClient`, והעזר שמשלים כניסה והרשמה ל-TOTP; להשתמש בהם.
- אין לשנות: `client/`, `vercel.json`, `middleware.js`, התנהגות ההגנות מסיפורים 1.3-1.4.

## Tasks & Acceptance

**Execution:**
- `server/Domain/Cohorts/` -- ישות `Cohort` (Id, Name, InviteToken, CreatedAt, RevokedAt) ויצירת אסימון -- AR11
- `server/Features/Cohorts/` -- שירות יצירה, רשימה, ביטול ואימות הזמנה, ו-`ICohortRepository` -- AD-7
- `server/Infrastructure/` -- מיפוי EF, אינדקס ייחודי, מימוש המאגר ומיגרציה -- AR11
- `server/Api/Endpoints/` -- `POST/GET /admin/cohorts`, `POST /admin/cohorts/{id}/revoke`, `GET /invites/{token}` (אנונימי, `PublicInvite`) -- FR-1
- `server/Tests/` -- בדיקות אינטגרציה לשורות המטריצה, כולל בדיקה שה-DTO הציבורי אינו מכיל שדות פרטיים -- AR4, AR21
- `README.md` -- משתנה `PUBLIC_BASE_URL` והנקודות החדשות -- תפעול

**Acceptance Criteria:**
- Given אדמין שהשלים כניסה והרשמת TOTP, when יוצר מחזור עם שם, then הקישור שנוצר בנוי כ-`{PUBLIC_BASE_URL}/join/{token}` והאסימון מכיל 256 ביט.
- Given מבקר לא מחובר, when פותח קישור תקף, then הוא מקבל שם המחזור בלבד, ובקישור מבוטל או לא קיים מקבל תגובה זהה שאינה חושפת פרטים.
- Given משתמש שאינו אדמין, when פונה לנקודות `/admin/cohorts`, then 401 או 403.

## Spec Change Log

## Review Triage Log

### 2026-10-21 — Review pass
- verdicts: כ-30 ממצאים מארבע שכבות (רבים כפולים) — high 0, medium 3, low 4, false 1
- findings:
  - `[medium]` `[patch]` תגובות `/invites/{token}` ללא `Cache-Control` ו-`Referrer-Policy` — נוספו, עם בדיקה
  - `[medium]` `[patch]` `PUBLIC_BASE_URL` לא מאומת — נוסף אימות ובדיקה
  - `[medium]` `[patch]` בדיקות חסרות לרשימה (`inviteToken`, `inviteUrl`, `revokedAt`) ולאסימון פגום ריאלי — נוספו
  - `[low]` `[patch]` סדר רשימה לא דטרמיניסטי כשיש זמני יצירה שווים — נוסף tie-break
  - `[high]` `[defer]` אין מסכי לקוח לניהול ולעמוד ההזמנה — נרשם ב-deferred, יש להוסיף סיפור
  - `[low]` `[defer]` הגבלת קצב לבדיקת הקישור — סיפור 1.7
  - `[low]` `[reject]` תווי בקרה בשם, רשימה בלי עימוד, התנגשות אסימון, timing של אסימון פגום, ExistsAsync לא אטומי, שמות כפולים, ביטול בלתי הפיך, מרחב שמות של מיגרציה — לא סביר שייפגעו בשימוש רגיל
  - `[false]` `[reject]` האסימון נרשם ביומן — רישום הבקשות של ASP.NET מוגדר ל-Warning ולכן הנתיב אינו נרשם
## Verification

**Commands:**
- `dotnet build server` -- expected: 0 שגיאות
- `dotnet test server` -- expected: כל הבדיקות עוברות (דורש Docker)

## Auto Run Result

- **שינוי:** ישות `Cohort` עם `InviteToken` של 256 ביט, `POST/GET /admin/cohorts` ו-`POST /admin/cohorts/{id}/revoke` (אדמין בלבד, ביטול כעדכון מותנה), ו-`GET /invites/{token}` אנונימי שמחזיר שם מחזור בלבד (404 זהה לקישור מבוטל, לא קיים או פגום), עם `no-store` ו-`Referrer-Policy: no-referrer`. הקישור נבנה מ-`PUBLIC_BASE_URL`.
- **קבצים:** `server/Domain/Cohorts/Cohort.cs`, `server/Features/Cohorts/CohortService.cs`, `server/Infrastructure/CohortRepository.cs`, מיגרציית `Cohorts`, `server/Api/Endpoints/CohortEndpoints.cs`, `Program.cs`, `Strings.cs`, `server/Tests/CohortTests.cs`, `README.md`.
- **סקירה:** 3 תיקוני medium (כותרות מטמון, אימות `PUBLIC_BASE_URL`, בדיקות חסרות) ו-1 low (סדר רשימה) הוחלו. 2 פריטים נדחו ל-`deferred`, ובהם הפער הגבוה: אין מסכי לקוח לניהול ולעמוד ההזמנה. השאר נדחו כמתועד ביומן.
- **המלצה לסקירת המשך:** כן. הסיכון שלא אומת: קריטריוני הקבלה המקוריים מתארים מסכים (מסך מחזורים עם כפתור העתקה, עמוד הזמנה), והם לא קיימים; נבדק רק חוזה ה-API.
- **אימות:** `dotnet build server` ללא שגיאות, `dotnet test server`: 60 מתוך 60 עוברות מול Postgres אמיתי.
- **סיכונים שנותרו:** מסכי לקוח חסרים (דורש סיפור חדש); בדיקת הקישור האנונימית ללא הגבלת קצב עד סיפור 1.7.
