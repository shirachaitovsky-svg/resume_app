---
title: 'סיפור 1.4: אימות דו-שלבי למנהלת'
type: 'feature'
created: '2026-10-21'
status: 'done'
baseline_revision: '798e1d333d93be927bccbaff8e84b77681495477'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-resume_app-2026-10-21/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-1-3-כניסת-מנהלת.md'
warnings: []
deferred:
  - summary: >-
      אין הגנת replay לקוד TOTP: אותו קוד תקף כ-90 שניות
    evidence: |-
      ספק ה-authenticator של Identity לא שומר את צעד הזמן האחרון; דורש עמודה חדשה ומיגרציה
    location: >-
      server/Api/Endpoints/AuthEndpoints.cs
    severity: medium
  - summary: >-
      אין מנגנון איפוס TOTP לאדמין שאיבד את המכשיר וקודי השחזור, ואין רישום אירועי TOTP
    evidence: |-
      השחזור כיום הוא ידני בבסיס הנתונים (מתועד ב-README); ממשק ניהול משתמשים ויומן ביקורת מתוכננים בסיפור 4.2
    location: >-
      README.md
    severity: medium
  - summary: >-
      ניסיונות כניסה מקבילים עלולים לעבור את סף 5 הניסיונות לפני שהנעילה נשמרת, וההרשמה הראשונה ל-TOTP פתוחה למי שיודע את הסיסמה
    evidence: |-
      הגבלת קצב לפי IP בסיפור 1.7; חלון ההרשמה הראשונה הוא מובנה בתכנון (הרשמה אחרי החלפת סיסמה)
    location: >-
      server/Api/Endpoints/AuthEndpoints.cs
    severity: low---

<intent-contract>

## Intent

**Problem:** כניסת האדמין (סיפור 1.3) מסתמכת על סיסמה בלבד, ואם היא נחשפת פרטי הקשר של המשתתפות חשופים. יש גם אדמין אחד בלבד, ואם ננעלים בחוץ אין דרך חזרה.

**Approach:** להוסיף ל-`server/` אימות דו-שלבי TOTP חובה: הרשמה (מפתח ו-URI ל-QR, קודי שחזור), כניסה שדורשת קוד TOTP או קוד שחזור, וחסימת כל פעולה אחרת עד שההרשמה הושלמה. להוסיף אפשרות לזרוע אדמין שני. חלק השרת בלבד; אין מסכי לקוח.

## Boundaries & Constraints

**Always:** נבנה על ASP.NET Core Identity (ספק ה-authenticator וקודי השחזור שלו) ועל התשתית מסיפור 1.3. הכניסה היא בקשה אחת (`/auth/login`) שנושאת שם משתמש, סיסמה וקוד; כל כשל (משתמש לא קיים, סיסמה שגויה, קוד שגוי, נעילה) מחזיר 401 זהה, כדי שהקוד השגוי לא יחשוף שהסיסמה נכונה. קוד שגוי נספר כניסה כושלת לנעילה. קוד שחזור תקף פעם אחת. אסור לרשום סיסמה, מפתח, קוד או קוד שחזור ביומן. קודי שחזור מוצגים פעם אחת בלבד בעת ההפעלה.

**Never:** אין ממשק לקוח ואין רינדור QR בשרת (מוחזר URI בפורמט `otpauth://`). אין SMS או מייל כגורם שני. אין ממשק ניהול משתמשים או איפוס אדמין.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| הרשמה ראשונה | אדמין אחרי החלפת סיסמה, בלי TOTP | רק נקודות ההרשמה (`/auth/totp/setup`, `/auth/totp/enable`) והחלפת סיסמה, יציאה ובדיקת מצב פתוחות; השאר 403 | N/A |
| הפעלה | קוד TOTP תקף ל-`/auth/totp/enable` | ההרשמה מושלמת, מוחזרים 10 קודי שחזור פעם אחת, והחסימה מוסרת | קוד שגוי: שגיאת ולידציה ונספר לנעילה |
| כניסה עם TOTP | סיסמה וקוד תקפים | 200 והעוגיה כבסיפור 1.3 | N/A |
| קוד שגוי | סיסמה נכונה, קוד שגוי | 401 זהה לסיסמה שגויה, ונספר לנעילה | 401 אחיד |
| קוד חסר | סיסמה נכונה, בלי קוד, ל-TOTP פעיל | 401 זהה | 401 אחיד |
| קוד שחזור | קוד שחזור תקף | כניסה מוצלחת; שימוש חוזר באותו קוד: 401 | 401 אחיד |
| נעילה | חמישה כשלונות (סיסמה או קוד) | נעילה, ותגובה זהה | 401 אחיד |
| אדמין שני | `ADMIN_USERNAME_2` ו-`ADMIN_INITIAL_PASSWORD_2` מוגדרים | נוצר אדמין שני עם סיסמה חד-פעמית; לא נוצר שוב אם קיים | חסרים: מדלגים; כשיש פחות משני אדמינים נרשמת אזהרה באתחול |

</intent-contract>

## Code Map

- `server/Api/Endpoints/AuthEndpoints.cs` -- `/auth/login` (מקבל היום `Username`, `Password`), `/auth/change-password`, `/auth/me`; כאן להוסיף `Code`/`RecoveryCode` לכניסה ואת נקודות ההרשמה. לשמור על `InvalidCredentials()` האחידה ועל ה-dummy hash.
- `server/Api/Security/AuthPipelineExtensions.cs` -- `UseMustChangePasswordGate` ורשימת `AllowedWhileMustChange`; להרחיב לחסימה גם בזמן הרשמת TOTP חסרה.
- `server/Api/Security/AppUserClaimsPrincipalFactory.cs` -- claim `MustChangePassword`; להוסיף claim למצב "TOTP טרם הופעל".
- `server/Api/Security/IdentityServiceExtensions.cs` -- הגדרות Identity; להפעיל את ספק ה-authenticator ואת מנפיק ה-URI (`AddDefaultTokenProviders`).
- `server/Infrastructure/Identity/AdminSeeder.cs` -- זריעה מסיפור 1.3 (נעילת advisory, בדיקת תוצאות); להוסיף זריעת אדמין שני ואזהרה כשיש פחות משניים.
- `server/Infrastructure/Identity/AppUser.cs` -- הטבלאות כבר כוללות `TwoFactorEnabled` ושדות אסימוני Identity; מיגרציה רק אם משתנה מודל.
- `server/Tests/AuthTestSupport.cs`, `AdminAuthTests.cs` -- `AuthApiFactory`, `ApiClient`, Testcontainers; להשתמש ולהרחיב. לחישוב קוד TOTP בבדיקות להשתמש בספרייה פנימית או במימוש RFC 6238 קצר בקוד הבדיקות.
- אין לשנות: `client/`, `vercel.json`, `middleware.js`, התנהגות `__Host-` והעוגיה.

## Tasks & Acceptance

**Execution:**
- `server/Api/Security/` -- ספק authenticator, claim וגבול הרשמה: עד הרשמה חסומות כל הנקודות מלבד ההרשמה, החלפת סיסמה, יציאה ובדיקת מצב -- FR-17
- `server/Api/Endpoints/AuthEndpoints.cs` -- `POST /auth/totp/setup`, `POST /auth/totp/enable` (קודי שחזור פעם אחת), הרחבת `/auth/login` לקוד TOTP או קוד שחזור בתגובה אחידה, ועדכון `/auth/me` -- AD-20
- `server/Infrastructure/Identity/AdminSeeder.cs` -- אדמין שני ממשתני סביבה ואזהרת "פחות משני אדמינים" -- AD-20
- `server/Tests/` -- בדיקות אינטגרציה לשורות המטריצה, כולל שימוש חוזר בקוד שחזור וקוד שגוי שזהה לסיסמה שגויה -- AR21
- `README.md` -- משתני האדמין השני וזרימת ההרשמה והכניסה -- תפעול

**Acceptance Criteria:**
- Given אדמין שהחליף סיסמה ולא הופעל לו TOTP, when ניגש ל-`/admin/ping`, then 403 עד שההרשמה הושלמה, ואז 200.
- Given אדמין עם TOTP פעיל, when נכנס עם סיסמה נכונה וקוד שגוי, then התגובה זהה (גוף, סטטוס וכותרות) לסיסמה שגויה, והכשל נספר לנעילה.
- Given קוד שחזור, when משתמשים בו פעם ופעם נוספת, then הראשונה מצליחה והשנייה 401.
- Given שני משתני האדמין השני, when השרת עולה על בסיס נתונים ריק, then נוצרים שני אדמינים, והאתחול החוזר אינו יוצר נוספים.

## Spec Change Log

## Review Triage Log

### 2026-10-21 — Review pass
- verdicts: כ-45 ממצאים מארבע שכבות (רבים כפולים) — high 0, medium 4, low 6, false 0
- findings:
  - `[medium]` `[patch]` הפעלת TOTP לפני יצירת קודי שחזור, ורשימה ריקה מוחזרת כהצלחה — הקודים נוצרים קודם והכשל מחזיר 500
  - `[medium]` `[patch]` אין בדיקות: אדמין שני שנוסף לפריסה קיימת, קוד שחזור שגוי שנספר לנעילה, `totpSetupRequired`, enable לפני setup, setup כפול — נוספו
  - `[low]` `[patch]` setup בחשבון נעול, והטענה ב-README על שחזור דרך האדמין השני ללא מנגנון — תוקנו
  - `[medium]` `[defer]` replay של קוד TOTP, איפוס TOTP ורישום אירועים, ניסיונות מקבילים והרשמה ראשונה — נרשמו ב-deferred
  - `[low]` `[reject]` claim של TOTP לכל משתמש לא-אדמין, CanSignInAsync, אותו שם בשני האדמינים, נרמול קודי שחזור, תנאי TOTP בבקשה כפולה, 401 בנעילה ב-enable, הבדלי timing, סלאש בנתיב ב-gate, claim ישן — לא סביר שייפגעו בשימוש רגיל והתיקון מוסיף מורכבות
## Verification

**Commands:**
- `dotnet build server` -- expected: 0 שגיאות
- `dotnet test server` -- expected: כל הבדיקות עוברות (דורש Docker)

## Auto Run Result

- **שינוי:** אימות דו-שלבי TOTP חובה לאדמין: הרשמה (`/auth/totp/setup`, `/auth/totp/enable` עם 10 קודי שחזור פעם אחת), כניסה בבקשה אחת עם קוד TOTP או קוד שחזור ותגובת 401 אחידה, חסימת כל הפעולות עד השלמת ההרשמה, וזריעת אדמין שני.
- **קבצים:** `server/Api/Endpoints/AuthEndpoints.cs`, `server/Api/Security/*` (gate, claim, ספק authenticator), `server/Infrastructure/Identity/AdminSeeder.cs`, `server/Tests/AdminTotpTests.cs` ו-`AuthTestSupport.cs`, `README.md`. ללא מיגרציה.
- **סקירה:** 2 תיקוני medium (יצירת קודי שחזור לפני הפעלה, ובדיקות חסרות) ו-2 low הוחלו. 3 פריטים נדחו ל-`deferred` (replay של קוד TOTP, איפוס TOTP ורישום אירועים, ניסיונות מקבילים). השאר נדחו כמתועד ביומן.
- **המלצה לסקירת המשך:** כן. הסיכון שלא אומת: ענף הכשל של `/auth/totp/enable` (החזרת 500 ושמירת TOTP כבוי כשיצירת הקודים נכשלת) לא נבדק, כי אין seam שמאלץ את המחולל להחזיר null.
- **אימות:** `dotnet build server` ללא שגיאות, `dotnet test server`: 46 מתוך 46 עוברות מול Postgres אמיתי.
- **סיכונים שנותרו:** אין הגנת replay לקוד TOTP; איפוס TOTP ידני בבסיס הנתונים (מתועד ב-README); אין רינדור QR (מוחזר URI בלבד, ללקוח); פריטי 1.3 שדחויים לפריסה.
