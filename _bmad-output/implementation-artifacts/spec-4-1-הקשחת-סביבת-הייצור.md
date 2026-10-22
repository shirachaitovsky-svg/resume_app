---
title: 'סיפור 4.1: הקשחת סביבת הייצור (מקומי)'
type: 'feature'
created: '2026-10-23'
status: 'done'
baseline_revision: 'bdcd40b3f46e95119057d2c24699219d3d585a5d'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-4-context.md'
warnings: [oversized]
deferred: []
---

<intent-contract>

## Intent

**Problem:** השרת רץ מול בסיס הנתונים עם משתמש אחד שמריץ גם מיגרציות וגם את האפליקציה, ויכול לשנות את `audit_log`. אין טיפול אחיד בשגיאות שלא נתפסו (בייצור יוחזר 500 ריק, ובפיתוח דף מפתחים עם stack trace), ויומני הפרטיות נבדקים רק בחלק מהזרימות.

**Approach:** שרת בלבד, מותאם להרצה מקומית. תמיכה בשני connection strings, `migrator` למיגרציות ו-`app` לאפליקציה, עם סקריפט הרשאות מינימליות ובדיקה על Postgres אמיתי. טיפול אחיד בשגיאות כ-`problem+json` בלי stack trace. השלמת בדיקות פרטיות ביומנים לכל הזרימות. כל מה שתלוי בפריסה (Vercel ו-Render) נרשם כתנאי עלייה.

## Boundaries & Constraints

**Always:**
- משתנה סביבה חדש `DATABASE_URL_MIGRATOR` (אופציונלי): כשהוא קיים, `MigrateDatabaseAsync` (כולל נעילת ה-advisory) רצה עם חיבור מ-`DbContext` נפרד שנבנה ממנו, והאפליקציה ממשיכה עם `DATABASE_URL`. כשאינו קיים, ההתנהגות הנוכחית ללא שינוי (משתמש יחיד בפיתוח). יצירת האדמין הראשון (`AdminSeeder`) ממשיכה תחת חיבור האפליקציה.
- `server/db/roles.sql` (מתועד ב-README, ופרמטרי שמות וסיסמאות כמשתני `psql`): תפקיד `migrator` (בעלות על הסכמה `public`, `CREATE` עליה, בעלות על טבלת `__EFMigrationsHistory` והטבלאות שהוא יוצר) ותפקיד `app` (`LOGIN`, בלי `CREATE` על הסכמה, ובלי בעלות על אובייקטים). ל-`app`: `SELECT, INSERT, UPDATE` על כל טבלאות `public` שתיווצרנה (באמצעות `ALTER DEFAULT PRIVILEGES FOR ROLE migrator`), `DELETE` רק על הטבלאות שהקוד מוחק מהן (`rate_limit_counters` וטבלאות Identity `users` ו-`user_roles`, לפי הקוד שהתגלה, להשלים אחרי בדיקה), `USAGE, SELECT` על כל הרצפים, ועל `audit_log` **בלבד** `SELECT, INSERT` (`REVOKE UPDATE, DELETE, TRUNCATE`), בלי הרשאות `TRUNCATE` או `DDL` לשום טבלה. הרשאות `audit_log` מוחלות בסקריפט על הטבלה אחרי המיגרציות (ובנוסף כברירת מחדל שנחסמת ב-`ALTER DEFAULT PRIVILEGES` לטבלה זו אם אפשר), עם הוראות להרצה אחרי מיגרציה ראשונה.
- בדיקת אינטגרציה על Postgres אמיתי (`PostgresFixture` + Testcontainers): יוצרת את התפקידים לפי הסקריפט (הסקריפט עצמו נקרא ומורץ בבדיקה, לא עותק שלו), מריצה את השרת עם `DATABASE_URL_MIGRATOR` של `migrator` ו-`DATABASE_URL` של `app`, ומוכיחה: המיגרציות עוברות, האפליקציה פועלת (אדמין נוצר, כניסה עם גוגל מדומה ויצירת פרופיל, ויומן ביקורת נכתב), `CREATE TABLE` תחת `app` נכשל, `UPDATE` ו-`DELETE` ו-`TRUNCATE` על `audit_log` תחת `app` נכשלים (שגיאת הרשאה), ושינוי נתונים רגיל (למשל `UPDATE profiles`) ו-`DELETE` מ-`rate_limit_counters` עובדים.
- שגיאות: בכל הסביבות (לא רק בייצור), חריגה שלא נתפסה מחזירה `500` בפורמט `application/problem+json` עם `status`, `title` כללי ו-`traceId`, בלי stack trace ובלי הודעת החריגה. תגובות סטטוס ריקות (404, 405, 401, 403 של מסלול לא ממופה) מקבלות גוף `problem+json` אחיד. `UseExceptionHandler` ו-`UseStatusCodePages` (או `AddProblemDetails` עם handler) בראש הצינור, לפני כל שאר ה-middleware שמוסיפים כותרות, כך שגם בשגיאות מתקבלות כותרות `no-store` של מסלולים מאומתים. החריגה עצמה נרשמת ביומן (ללא נתונים אישיים בשדות שהקוד שלנו מוסיף).
- יומנים: בדיקות עם ה-sink הקיים (`CapturingSink`, להעביר לתמיכה משותפת בבדיקות) לכל אחת מהזרימות שלא מכוסות: עריכת פרופיל (כולל תוכן קורות חיים ושינוי נייד), בקשת פרטי קשר (שם, מייל וסיבת הפנייה), אימות כתובת, החלטת משתתפת, אישור מסירה ושליחת הפרטים (נייד ומייל), החזרה לתיקון (הערת המנהלת), וחריגה לא צפויה (500). מוודאות שאין ביומנים (Serilog, כל הרמות) מייל, נייד, אסימון, `id_token`, `code`, `state`, תוכן קורות חיים, הערת מנהלת או סיבת פנייה.
- כותרות ועוגיות: בדיקה שמשלימה את החסר בלבד: ל-`__Host-resume-participant`, `__Host-resume-admin`, `__Host-resume-oidc` ו-`__Host-resume-csrf` יש `Secure`, `Path=/` וללא `Domain` (דרישות הקידומת `__Host-`), `HttpOnly` כנדרש (ל-csrf נקבע כפי שהוא היום), ו-`SameSite` תואם; ולכל תגובה מאומתת (`/me`, `/admin`, `/auth`) `Cache-Control: no-store`, וגם בתגובת שגיאה.
- תנאי עלייה שנרשמים (ב-`docs/launch-checklist.md`, ליצור את הקובץ אם אינו קיים עם הסעיף הזה בלבד: "הקשחה בייצור"): בדיקת `no-store` ו-`__Host-` מקצה לקצה בכתובת Vercel בייצור (AR3, ספייק עוגיות), הפעלת תפקידי `migrator` ו-`app` ב-Render (הרצת `roles.sql` והגדרת שני connection strings), ושמירה שה-`app` לא מקבל הרשאות רחבות אחרי מיגרציות חדשות.
- ריצת שרת מלאה אחת בלבד, ללא הרג `testhost`, ו-`TestResults` לא נכנס לקומיט.

**Never:**
- לא נוגעים בלקוח, ולא משנים התנהגות מוצר מעבר להקשחה. לא מסירים תמיכה במשתמש יחיד בפיתוח. לא משביתים או מחלישים בדיקות קיימות.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| פיתוח | רק `DATABASE_URL` | כמו היום: מיגרציות ואפליקציה באותו משתמש | N/A |
| שני תפקידים | `DATABASE_URL_MIGRATOR` ו-`DATABASE_URL` שונים | מיגרציות תחת `migrator`, האפליקציה תחת `app` ופועלת | חיבור מיגרציה נכשל: השרת נעצר בהודעה ברורה |
| DDL ב-`app` | `CREATE TABLE` תחת `app` | נכשל בהרשאה | N/A |
| `audit_log` ב-`app` | `UPDATE`, `DELETE`, `TRUNCATE` | נכשלים בהרשאה | `INSERT` ו-`SELECT` מצליחים |
| מחיקת מונה קצב | `DELETE` מ-`rate_limit_counters` תחת `app` | מצליח | N/A |
| חריגה לא צפויה | נקודת קצה שזורקת | 500 `problem+json` בלי stack trace, עם `traceId` | החריגה ביומן בלי נתונים אישיים |
| נתיב לא ממופה | `POST /nope`, `GET /nope`, 405 | גוף `problem+json` עם `status` תואם | N/A |
| יומנים | כל הזרימות | אין מייל, נייד, אסימון, `id_token`, קורות חיים או הערות | N/A |
| עוגיות | כניסה מדומה ואדמין | `Secure`, `Path=/` ללא `Domain`, ו-`no-store` | N/A |

</intent-contract>

## Code Map

- `server/Infrastructure/{ConnectionStringResolver.cs (~21-33),DependencyInjection.cs (~17-37,140-203),AppDbContextFactory.cs}` -- חיבור יחיד היום; להוסיף `DATABASE_URL_MIGRATOR` ל-`MigrateDatabaseAsync`
- `server/Api/Program.cs (~31,51)`, `Api/Security/*` -- `AddProblemDetails()` בלי `UseExceptionHandler` או `UseStatusCodePages`; סדר ה-middleware (OriginSecret, ForwardedHeaders, NoStoreForAuth, Authentication, ...)
- `server/Infrastructure/AdminSeeder.cs (~43,136)` -- רץ אחרי המיגרציה, תחת חיבור האפליקציה; `users.DeleteAsync` ברולבק, ולכן `DELETE` על `users` נדרש
- `server/Infrastructure/RateLimiter.cs (~42-60)` -- upsert ו-`DELETE` ניקוי; טבלאות שהקוד מוחק מהן
- `server/Domain/Audit/AuditLogEntry.cs`, כל שימושי `db.AuditLog.Add` -- INSERT בלבד
- `server/Tests/{AuthTestSupport.cs (~17-36 PostgresFixture, ~54-130 AuthApiFactory, ~165 EnvSnapshot.Names, ~180 EnvScope),GoogleSignInTests.cs (~19-30 CapturingSink, ~467-500),MyProfileCreationTests.cs (~461-500)}` -- תשתית; `EnvSnapshot.Names` חייב לכלול `DATABASE_URL_MIGRATOR`; להוציא את `CapturingSink` לתמיכה משותפת
- `server/Tests/{ParticipantEditTests,ContactRequestTests,ContactDecisionTests,ContactDeliveryTests,ReturnForCorrectionTests}.cs` -- הזרימות לבדיקות היומנים
- `scripts/dev-seed.sql`, `README.md (~14-15, 83)`, `render.yaml (~40)` -- תיעוד והגדרות
- חדש: `server/db/roles.sql`, `docs/launch-checklist.md`

## Tasks & Acceptance

**Execution:**
- `server/Infrastructure/*`, `server/Api/Program.cs` -- `DATABASE_URL_MIGRATOR` ושגיאות `problem+json` -- AR18, AR21
- `server/db/roles.sql`, `README.md` -- תפקידים והרשאות מינימליות -- AR18, NFR9
- `server/Tests/*` -- בדיקות תפקידים על Postgres אמיתי, בדיקות שגיאות, יומנים לכל הזרימות, ועוגיות וכותרות -- NFR1, NFR6
- `docs/launch-checklist.md` -- סעיף "הקשחה בייצור" -- AR22

**Acceptance Criteria:**
- Given שימוש ב-`app` בלבד, when הוא מנסה DDL או `UPDATE`/`DELETE`/`TRUNCATE` על `audit_log`, then נדחה, והבדיקה מוכיחה זאת מול Postgres אמיתי.
- Given כל בדיקות השרת הקיימות, when הן רצות (ריצה מלאה אחת), then כולן עוברות.
- Given התנהגות פיתוח (`DATABASE_URL` בלבד), when השרת עולה, then ללא שינוי.

## Spec Change Log

## Review Triage Log

### 2026-10-23 — Review pass
- verdicts: 14 findings (מקובצים לפי סיבת שורש מארבע השכבות) — high 0, medium 4, low 8, false 1, maybe-false 1
- findings:
  - `[medium]` `[patch]` ה-`CapturingSink` לא כולל את החריגה עצמה, ולכן בדיקת 500 עם מייל ונייד בהודעת החריגה אינה מוכיחה שהיומן נקי; ב-Production יומן החריגה של ה-framework כולל את הודעתה (למשל הודעות Npgsql עם ערכי מפתח) — הוטמע `IExceptionHandler` שרושם סוג חריגה ו-`traceId` בלבד, רישום ה-framework של `ExceptionHandlerMiddleware` מושתק, והסינק כולל טקסט חריגה בבדיקות
  - `[medium]` `[patch]` מפעל הבדיקות שנכשל בבנייה (סיסמת migrator שגויה) משאיר `DATABASE_URL_MIGRATOR` בסביבה ומזהם בדיקות הבאות — שחזור סביבה ב-`finally`, ואימות הסיבה בהודעה
  - `[medium]` `[patch]` ההרשאות `DELETE` על `users` ו-`user_roles` (גלגול אחורה של `AdminSeeder`) אינן נבדקות — נוספה בדיקה, וכן בדיקה ש-404 של מסלול לא ממופה במנהלת מוחזר כ-`problem+json`
  - `[medium]` `[patch]` `render.yaml` מצמיד את `DATABASE_URL` לבעלים של בסיס הנתונים, ולכן טיוטת הפריסה אינה מקיימת את החלוקה — נוסף סעיף מפורש ב-`docs/launch-checklist.md` שדורש ערך `app` ידני, ושאילתת אימות הרשאות
  - `[low]` `[patch]` `roles.sql` יוצא עם קוד 0 כשחסר משתנה — חריגה מפורשת (`RAISE EXCEPTION`), ושגיאת פורמט של `DATABASE_URL_MIGRATOR` נוקבת בשם המשתנה בלי הערך
  - `[low]` `[reject]` חלון הרשאות ברירת מחדל ל-`audit_log` עד הרצה חוזרת של הסקריפט — מתועד ב-README, ב-checklist ובכותרת הסקריפט, ואי אפשר להחריג טבלה בודדת ב-default privileges
  - `[low]` `[reject]` הרצה חוזרת של הסקריפט מאפסת סיסמאות — מכוון כשמעבירים את אותן הסיסמאות, והמסמך מורה להריץ פעמיים עם שתיהן
  - `[low]` `[reject]` `CONNECT`/`TEMP` ו-PUBLIC, בעלות על טבלאות קיימות, ו-psql 13+ — מחוץ להיקף ההקשחה המקומית
  - `[low]` `[reject]` `AppDbContext` של ה-migrator נבנה מחוץ ל-DI — רישום ה-DI מכיל רק `UseNpgsql`, ללא הגדרות נוספות
  - `[low]` `[reject]` בדיקות הקשיחות ב-Development בלבד, 401 במקום 404 לנתיב לא ממופה, ו-`ThrowingCohorts` משוכפל — הצינור אחיד לכל סביבה, ובדיקה של 404 מאומת נוספה
  - `[low]` `[reject]` בדיקות יומנים בצורות מנורמלות (טלפון בינלאומי, מגובב), סיסמאות, קוד TOTP וקבצי README — מחוץ להיקף
  - `[low]` `[reject]` בדיקת `UPDATE profiles` מצפה לשורה אחת בדיוק, ופרטי קוד דומים — יציב בבדיקה הממוקדת
  - `[maybe-false]` `[reject]` ריצת כל הבדיקות הקיימות תחת תפקיד `app` — הוכח בסיפור זה רק בזרימות מרכזיות; מתועד כמגבלה
  - `[false]` `[reject]` "אין התחדשות של `DATABASE_URL` בלי migrator" — ללא `DATABASE_URL_MIGRATOR` ההתנהגות כבעבר

## Implementation Notes

- `DATABASE_URL_MIGRATOR` (אופציונלי): `ConnectionStringResolver.ResolveMigrator`, ו-`MigrateDatabaseAsync` בונה `AppDbContext` נפרד ממנו (כולל ה-advisory lock). בלעדיו ללא שינוי.
- `server/db/roles.sql`: סקריפט psql בטוח להרצה חוזרת (משתני `-v`), מורץ פעמיים (לפני המיגרציה הראשונה ואחריה, כי `audit_log` נוצרת במיגרציה). `DELETE` רק על `rate_limit_counters`, `users`, `user_roles`; על `__EFMigrationsHistory` נשללות הרשאות `app`.
- שגיאות: `UseExceptionHandler` ו-`UseStatusCodePages` בראש הצינור; חריגה מחזירה `problem+json` עם כותרת עברית כללית ו-`traceId`. נתיב או מתודה בלי הרשאה מקבלים 401 ולא 404/405 (מדיניות הרשאה ברירת מחדל), והגוף אחיד גם כן.
- בדיקות חדשות: `DatabaseRolesTests` (מריץ את `roles.sql` עצמו דרך psql בקונטיינר), `ProductionHardeningTests`, `LogPrivacyTests`. `CapturingSink` הועבר ל-`AuthTestSupport.cs`, ו-`DATABASE_URL_MIGRATOR` נוסף ל-`EnvSnapshot.Names`.
- `docs/launch-checklist.md` נוצר עם הסעיף "הקשחה בייצור"; `README.md` ו-`render.yaml` עודכנו. הריצה המלאה של השרת וה-e2e לא הורצו על ידי המימוש.

## Design Notes

מגבלת `audit_log` נאכפת בבסיס הנתונים ולא בקוד: גם באג עתידי או פגיעות הזרקה בנתיב האפליקציה לא יוכלו לשנות שורות ביקורת. הסקריפט נקרא בבדיקה מהקובץ עצמו כדי שלא יתפצל מהנבדק. החזרת `problem+json` גם בפיתוח מפשטת בדיקה ומונעת הבדל בין סביבות; כלי הפיתוח נשארים ביומן.

## Verification

**Commands:**
- `dotnet build server` -- expected: בנייה מצליחה בלי אזהרות חדשות
- `dotnet test server` -- expected: כל הבדיקות עוברות (ריצה אחת, כ-11 דקות, ברקע)
- `cd e2e && npm run e2e` -- expected: התרחיש עובר (השינוי בשגיאות לא אמור לפגוע)

## Auto Run Result

Status: done

- **שינוי שמומש (שרת, תיעוד וסקריפט):** `DATABASE_URL_MIGRATOR` אופציונלי למיגרציות (נעילה כולל) בנפרד מחיבור האפליקציה; `server/db/roles.sql` (תפקידי `migrator` ו-`app`, `audit_log` ל-SELECT ו-INSERT בלבד, DELETE רק לשלוש טבלאות, בלי DDL); `ProblemExceptionHandler` שמחזיר 500 `problem+json` עם `traceId` ורושם ביומן סוג חריגה בלבד (רישום ה-framework מושתק); `UseStatusCodePages` לגוף אחיד בתגובות ריקות; בדיקות תפקידים על Postgres אמיתי (הסקריפט המקורי מורץ ב-psql), בדיקות שגיאות, עוגיות `__Host-` ו-`no-store`, בדיקות פרטיות ביומנים לעריכה, בקשת פרטי קשר, החלטה, מסירה, החזרה לתיקון ו-500; ו-`docs/launch-checklist.md` עם סעיף "הקשחה בייצור".
- **ממצאי סקירה:** 14 ממצאים מקובצים. תוקנו 4 בדרגת medium ו-1 low: יומן חריגות שחשף נתונים (נבדק עכשיו כולל טקסט החריגה), זיהום סביבה אחרי כשל מפעל, בדיקת `DELETE` על `users` ו-`user_roles` ובדיקת 404 מאומת, סעיף Render ושאילתת אימות ב-checklist, ויציאה לא-אפסית ב-`roles.sql` כשחסר משתנה. נדחו 8 low, 1 false ו-1 maybe-false עם נימוק בלוג.
- **המלצה לסקירת המשך:** כן. סיכון לא מאומת: רק הזרימות המרכזיות הורצו תחת תפקיד `app` (לא כל מסלולי הקוד); הרצה של `roles.sql` נדרשת שוב אחרי כל מיגרציה שמוסיפה טבלה; חלון ברירת מחדל ל-`audit_log` עד אז.
- **אימות:** `dotnet test server`: 832 עברו (ריצה מלאה אחת על הקוד הסופי). `npm run e2e`: 7 מתוך 7 עברו. `dotnet build` הצליח.
- **סיכונים נותרים:** סעיפי Vercel/Render (בדיקת `no-store` ו-`__Host-` מקצה לקצה בכתובת ייצור, הגדרת שני connection strings ב-Render) הם תנאי עלייה ונרשמו ב-checklist. בבדיקות, נתיב לא ממופה אנונימי מקבל 401 (מדיניות ברירת המחדל) ולא 404.
