# משתתפות ופרויקטים

שלד הפרויקט (סיפור 1.1): `client/` (Angular 22, עברית ו-RTL), `server/` (.NET 10), `Dockerfile`, `vercel.json`, `render.yaml`.

## דרישות

Node LTS, .NET SDK 10, ו-Postgres מקומי (למשל Docker Desktop).

## משתני סביבה

| משתנה | איפה | תיאור |
| --- | --- | --- |
| `ORIGIN_SECRET` | שרת, Vercel, הרצה מקומית של הלקוח | הסוד המשותף. השרת דוחה (403) כל בקשה בלי הכותרת `x-origin-secret` עם ערך זהה. חובה. |
| `DATABASE_URL` | שרת | כתובת `postgres://user:pass@host:5432/db` (כך Render מספקת). חלופה: `ConnectionStrings__Default` |
| `MIGRATE_ON_STARTUP` | שרת | `false` מדלג על מיגרציות (ברירת מחדל: רצות באתחול תחת advisory lock) |
| `RENDER_API_URL` | Vercel | כתובת השרת ב-Render, למשל `https://resume-app-api.onrender.com` |
| `ADMIN_USERNAME` | שרת | שם האדמין הראשון. נזרע פעם אחת בהפעלה, רק אם אין אדמין קיים. חסר: מדלגים ורושמים אזהרה |
| `ADMIN_INITIAL_PASSWORD` | שרת | סיסמה חד-פעמית לאדמין הראשון (12 תווים ומעלה, לא ברשימת הנפוצות). חובה להחליפה בכניסה הראשונה. אין לשמור בריפו |
| `ADMIN_USERNAME_2` | שרת | שם האדמין השני. נזרע פעם אחת (לפי שם משתמש) גם אם כבר קיים אדמין ראשון. חסר: מדלגים; כשיש פחות משני אדמינים נרשמת אזהרה באתחול |
| `ADMIN_INITIAL_PASSWORD_2` | שרת | סיסמה חד-פעמית לאדמין השני, באותן דרישות. חובה להחליפה בכניסה הראשונה. אין לשמור בריפו |
| `TURNSTILE_SECRET` | שרת | הסוד של Cloudflare Turnstile לאימות אנטי-בוטים. **חובה מחוץ לסביבת Development** (בלעדיו השרת לא עולה). ב-Development ברירת המחדל היא מפתח הבדיקה `1x0000000000000000000000000000000AA` שתמיד מצליח (מפתח הבדיקה שתמיד נכשל: `2x0000000000000000000000000000000AA`). אין מצב עקיפה בייצור |
| `RATE_LIMIT_PROFILE_IP` | שרת | הגשות פרופיל לשעה ל-IP (ברירת מחדל 20) |
| `RATE_LIMIT_PROFILE_EMAIL` | שרת | הגשות פרופיל לשעה ל-(מחזור, מייל) (ברירת מחדל 5) |
| `RATE_LIMIT_INVITE_IP` | שרת | בדיקות קישור (`GET /invites/{token}`) לשעה ל-IP (ברירת מחדל 120) |
| `RATE_LIMIT_PHOTO_IP` | שרת | בקשות תמונה (`GET /profiles/{id}/photo`) לשעה ל-IP (ברירת מחדל 1200) |
| `RATE_LIMIT_WINDOW_MINUTES` | שרת | אורך החלון הקבוע בדקות (ברירת מחדל 60). חייב להיות מחלק של 1440 (1, 5, 15, 30, 60, 120, 1440 וכו'); ערך אחר נדחה באתחול. שאר ערכי `RATE_LIMIT_*` חייבים להיות מספרים שלמים חיוביים |
| `ALLOWED_ORIGINS` | שרת | אופציונלי: מקורות נוספים (מופרדים בפסיק, למשל `https://app.example.com`) המותרים בבקשות שינוי, מעבר לכתובת המארח שמגיעה מ-Vercel |

## הרצה מקומית

```powershell
# 1. Postgres
docker run -d --name resume-pg -e POSTGRES_PASSWORD=dev -e POSTGRES_DB=resume -p 5432:5432 postgres:17

# 2. שרת (המיגרציות רצות באתחול)
$env:ORIGIN_SECRET = "dev-secret"
$env:ASPNETCORE_ENVIRONMENT = "Development"   # בלי זה נדרש TURNSTILE_SECRET; ב-Development משתמשים במפתח הבדיקה של Cloudflare
$env:DATABASE_URL = "postgres://postgres:dev@localhost:5432/resume"
dotnet run --project server/Api --urls http://localhost:5000

# 3. בדיקה
curl.exe -i http://localhost:5000/healthz                                   # 403
curl.exe -i -H "x-origin-secret: dev-secret" http://localhost:5000/healthz  # 200

# 4. לקוח (מעביר /api/* לשרת ומוסיף את הסוד)
cd client; npm install
$env:ORIGIN_SECRET = "dev-secret"; npm start
```

בדיקות ובנייה: `dotnet build server`, `dotnet test server`, ובתיקיית `client`: `npm run build`.

מיגרציה חדשה: `dotnet tool restore` ואז `dotnet tool run dotnet-ef migrations add <Name> --project server/Infrastructure --output-dir Migrations` (מתיקיית `server`).

## פריסה (חיבור ידני)

1. **Render:** ליצור Blueprint מ-`render.yaml` מהריפו ב-GitHub, ולהגדיר את `ORIGIN_SECRET` בלוח הבקרה. בנייה אוטומטית בכל דחיפה ל-`main`. אין `healthCheckPath` כי `/healthz` דורש את הסוד.
2. **Vercel:** לחבר את אותו ריפו (שורש הריפו). `vercel.json` מגדיר בנייה של `client/`. להגדיר `ORIGIN_SECRET` (אותו ערך) ו-`RENDER_API_URL`. `middleware.js` מעביר `/api/*` ל-Render ומוסיף את הכותרת; ב-`vercel.json` יש rewrite מקביל, יש להחליף בו את כתובת ה-placeholder.
3. **אימות:** `GET https://<vercel-domain>/api/healthz` מחזיר 200, ופנייה ישירה ל-Render בלי הסוד מחזירה 403.

## כניסת מנהלת (סיפור 1.3)

נקודות קצה (בלי `/api`, Vercel מסירה אותו): `GET /auth/csrf`, `POST /auth/login`, `POST /auth/logout`, `POST /auth/change-password`, `GET /auth/me`, `GET /admin/ping`. כל נקודת קצה דורשת אימות אלא אם סומנה `AllowAnonymous` (`/healthz`, `/auth/csrf`, `/auth/login`).

- כל בקשת שינוי (POST) דורשת כותרת `Origin` תואמת למארח וכותרת `X-CSRF-TOKEN` מ-`GET /auth/csrf`. האסימון קשור למשתמש, לכן יש להביא אסימון חדש אחרי כניסה.
- אדמין ראשון נוצר מ-`ADMIN_USERNAME` ו-`ADMIN_INITIAL_PASSWORD`, וכל פעולה פרט להחלפת סיסמה חסומה (403) עד ההחלפה. נדרשים לפחות שני אדמינים לפני עלייה לאוויר; ניהול משתמשים נוסף ידנית (אין ממשק).
- סיסמה: 12 תווים לפחות, לא ברשימה `server/Api/Security/common-passwords.txt` (רשימה קצרה, מומלץ להחליף ברשימה מלאה כמו SecLists). נעילה של 15 דקות אחרי 5 ניסיונות כושלים.
- העוגיה `__Host-resume-admin`: `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`, עוגיית סשן (בלי `Expires`), תוקף 8 שעות בהחלקה. מפתחות Data Protection בטבלה `data_protection_keys`, כך שההפעלות שורדות הפעלה מחדש.

## אימות דו-שלבי למנהלת (סיפור 1.4)

אימות TOTP הוא חובה. עד שההרשמה הושלמה כל נקודת קצה מלבד אלה חסומה (403, `code: totp-setup-required`): `/auth/totp/setup`, `/auth/totp/enable`, `/auth/change-password`, `/auth/logout`, `/auth/me`, `/auth/csrf`.

1. כניסה ראשונה עם הסיסמה החד-פעמית, והחלפתה ב-`POST /auth/change-password`.
2. `POST /auth/totp/setup` מחזיר `key` ו-`uri` (`otpauth://`) להוספה לאפליקציית authenticator (אין רינדור QR בשרת).
3. `POST /auth/totp/enable` עם `{ "code": "123456" }` מפעיל את האימות ומחזיר 10 `recoveryCodes`. מוצגים פעם אחת בלבד, יש לשמור אותם. קוד שגוי נספר לנעילה.
4. כניסה: `POST /auth/login` עם `{ username, password, code }` או `{ username, password, recoveryCode }`. קוד שחזור תקף פעם אחת. כל כשל (סיסמה, קוד, קוד חסר, נעילה) מחזיר 401 זהה, וקוד שגוי נספר לנעילה (5 כשלונות, 15 דקות).

אדמין שני נזרע מ-`ADMIN_USERNAME_2` ו-`ADMIN_INITIAL_PASSWORD_2` ומבצע את אותה הרשמה.

- בזמן שההרשמה תלויה ועומדת נפתחות גם `/auth/login` ו-`/healthz`, בנוסף לרשימה למעלה.
- אדמינים קיימים בלי TOTP מופעל נדרשים להרשמה כבר בבקשה הבאה שלהם.
- מונה הכשלונות לנעילה משותף לסיסמה ולגורם השני (קוד TOTP, קוד שחזור וקוד שגוי בהפעלה).
- **אין איפוס TOTP באפליקציה בגרסה זו.** מי שאיבדה גם את המכשיר וגם את קודי השחזור זקוקה לאופרטור שיאפס ישירות בבסיס הנתונים: בטבלה `users` להגדיר `"TwoFactorEnabled" = false` לשורת המשתמש, ובטבלה `user_tokens` למחוק את שורות אותו `"UserId"` עם `"LoginProvider" = '[AspNetUserStore]'` ו-`"Name"` ששווה `AuthenticatorKey` או `RecoveryCodes`. בכניסה הבאה (סיסמה בלבד) היא תידרש להירשם מחדש.

### ספייק עוגיות ב-Vercel (ידני, אחרי חיבור הפריסה)

1. לכנוס דרך כתובת Vercel בלבד: `GET /api/auth/csrf`, ואז `POST /api/auth/login` עם הכותרת `X-CSRF-TOKEN`.
2. לוודא ב-DevTools שהעוגיה `__Host-resume-admin` נשמרה ונשלחת ב-`GET /api/admin/ping` (אמור להחזיר 200 אחרי החלפת סיסמה).
3. אם `Origin` נדחה (403) כי Vercel אינה מעבירה את המארח המקורי ב-`X-Forwarded-Host`, להגדיר `ALLOWED_ORIGINS` לכתובת Vercel. אם העוגיה או `SameSite=Strict` נכשלים דרך ה-rewrite, הסיפור אינו נסגר עד בחירת חלופה (למשל `SameSite=Lax` בהחלטה מתועדת).

## מחזורים וקישורי הזמנה (סיפור 1.5)

משתנה סביבה `PUBLIC_BASE_URL` (שרת, אופציונלי, ברירת מחדל `http://localhost:4200`) קובע את בסיס קישור ההזמנה: `{PUBLIC_BASE_URL}/join/{token}`.

| נקודה | הרשאה | תיאור |
| --- | --- | --- |
| `POST /admin/cohorts` | אדמין | `{ "name": "..." }` (עד 100 תווים). 201 עם `id`, `name`, `inviteToken`, `inviteUrl`, `createdAt` |
| `GET /admin/cohorts` | אדמין | `{ items, total }`, החדש ראשון, כולל סטטוס ביטול |
| `POST /admin/cohorts/{id}/revoke` | אדמין | 204; ביטול חוזר 409; מזהה לא קיים 404 |
| `GET /admin/submissions` | אדמין | תור הגרסאות הממתינות (`pending`), מהישנה לחדשה: `{ items, total }`. פריט: `revisionId`, `name`, `productName`, `cohortName`, `kind` (`new` או `update`), `submittedAt`. `limit` בין 1 ל-100 (ברירת מחדל 50; מעל 100 נחתך) ו-`offset` 0 ומעלה; ערך מחוץ לטווח 400. מזהה גרסה בפורמט GUID סטנדרטי בלבד |
| `GET /admin/submissions/{revisionId}` | אדמין | כל פרטי ההגשה: מייל, נייד, `productUrl`, `avatarId`, `hasPhoto`, שדות הטקסט, ו-`cv`. רק `cv` מנוקה כ-Markdown (`SafeMarkdown.Clean`); שאר הטקסטים מוחזרים כפי שנשמרו, כטקסט פשוט, והלקוח חייב להציגם כטקסט ולא כ-HTML. `cv` שנדחה מוחזר כ-`null` עם `cvInvalid: true`. לא קיים או לא ממתינה: 404 |
| `GET /admin/submissions/{revisionId}/photo` | אדמין | תמונת הגרסה הממתינה, `nosniff` ו-`no-store`; בלי תמונה 404 |
| `GET /invites/{token}` | אנונימי | 200 עם `{ cohortName }` בלבד. קישור מבוטל, לא קיים או פגום: 404 `problem+json` זהה |

האסימון הוא 256 ביט אקראיים (base64url, 43 תווים), ייחודי באינדקס, ולא נרשם ביומן.

## הגשת פרופיל (סיפור 1.6)

| נקודה | הרשאה | תיאור |
| --- | --- | --- |
| `POST /invites/{token}/profiles` | אנונימי (anti-forgery ו-Origin כבכל POST) | גוף: `name`, `productName` (עד 100), `productUrl` (http/https מוחלט, עד 2048), `email` (עד 254), `phone` (9 עד 15 ספרות, מותרים `+` בהתחלה, רווח ומקף). 202 עם `{ "status": "pending" }`; ולידציה 400 `problem+json` עם שגיאה לכל שדה; אסימון מבוטל, לא קיים או פגום: 404 זהה לנקודת הבדיקה |

המייל נשמר באותיות קטנות והנייד כספרות בלבד (עם `+` מוביל), בטבלה נפרדת `participant_private`, ואינם מופיעים בשום תגובה או יומן. כפילות מייל באותו מחזור מחזירה 202 זהה להצלחה בלי פרופיל נוסף (אינדקס ייחודי `(cohort_id, email)`). גרסה `pending` אחת לכל היותר לפרופיל נאכפת באינדקס ייחודי חלקי. התגובה אינה כוללת מזהה פרופיל. הגנות הטפסים הציבוריים מתוארות בסעיף הבא (סיפור 1.7).

פרטי הנקודה:
- גוף הבקשה JSON (`Content-Type: application/json`) עד 160KB (מאז סיפור 1.9, כדי להכיל `cv` גם כשהלקוח שולח תווים כרצף `\uXXXX`). הקישור נבדק לפני קריאת הגוף, ולכן קישור לא תקף מחזיר תמיד את אותו 404 גם כשהגוף פגום. גוף פגום בקישור תקף: 400 `problem+json`; סוג תוכן שגוי: 415; גוף גדול מדי: 413.
- שגיאת ולידציה: 400 `application/problem+json` עם `errors` שהוא אובייקט `{ "<שדה>": ["<הודעה>"] }` (שדות: `name`, `productName`, `productUrl`, `email`, `phone`). תווי בקרה בשדות טקסט, וקישור עם שם משתמש וסיסמה, נדחים.
- 404 (`problem+json` זהה לנקודת `GET /invites/{token}`) לאסימון מבוטל, לא קיים או פגום.
- הנייד נשמר ללא רווחים ומקפים (ספרות בלבד, עם `+` מוביל אם הוזן).
- הגשה חוזרת עם מייל שכבר רשום באותו מחזור מתקבלת באותה תגובה (202), אך הנתונים שלה מתעלמים; נתיב התיקון יגיע עם זרימת העדכון (אפיק 2).
- זיהוי הכפילות נעשה ב-`INSERT ... ON CONFLICT DO NOTHING` בלי חריגה, וביומן מוגדרת רמת `Fatal` ל-`Microsoft.EntityFrameworkCore.Update` כדי שמייל או נייד לא יגיעו אליו.

## הגנה על טפסים ציבוריים (סיפור 1.7)

חלה על `POST /invites/{token}/profiles` ועל `GET /invites/{token}` (ובהמשך על בקשת קישור אישי ובקשת פרטי קשר). הסדר:

1. **הגבלת קצב לפי IP.** כל ניסיון נספר, גם אם ייכשל אחר כך. חריגה: 429 `problem+json` עם `Retry-After` (שניות עד סוף החלון), בלי פרטים נוספים.
2. **Turnstile.** האסימון בכותרת `X-Turnstile-Token`, ומאומת מול `https://challenges.cloudflare.com/turnstile/v0/siteverify` עם `TURNSTILE_SECRET` ו-timeout של 3 שניות. נכשל סגור: אסימון חסר או ארוך מ-2048 תווים, תגובה שאינה `success:true`, שגיאת רשת, timeout או תגובה לא צפויה, כולם 403 `problem+json`.
3. (הגשת פרופיל בלבד) אחרי בדיקת הקישור וקריאת הגוף ולפני יצירה: **הגבלת קצב לפי (מחזור, מייל)** (המייל מנורמל כמו באחסון). אם המייל חסר, אין מה למנות והוולידציה דוחה.

שיטת ההגבלה: חלונות קבועים שמעוגנים לחצות UTC (לכן אורך החלון מחלק את 1440 הדקות של יום, וכל חלון מתחיל בכפולה של אורכו מחצות), והמונים בטבלה `rate_limit_counters (scope, key_hash, window_start, count)` בבסיס הנתונים, כך שהם שורדים הפעלה מחדש ומשותפים בין מופעים. העדכון אטומי (`INSERT ... ON CONFLICT DO UPDATE ... RETURNING`). המפתח הוא HMAC-SHA256 של ה-IP או של (מחזור, מייל) עם מפתח שנגזר מ-`ORIGIN_SECRET`; ה-IP והמייל הגולמיים אינם נשמרים ואינם מופיעים ביומן. כשנפתח חלון חדש נמחקים חלונות ישנים (יותר מחלון אחד אחורה) של אותו `scope`. ה-IP נלקח מ-`HttpContext.Connection.RemoteIpAddress` אחרי `UseForwardedHeaders`, כלומר רק אחרי שהסוד המשותף אומת; בקשה עם `X-Forwarded-For` בלי הסוד נדחית ב-403 ואינה נספרת.

שימו לב: מפתח ה-HMAC נגזר מ-`ORIGIN_SECRET`, ולכן החלפת הסוד מאפסת בפועל את כל המונים (המפתחות הישנים כבר לא יתאימו).

הלקוח (סיפור נפרד) צריך לשלוח אסימון Turnstile טרי בכל בקשה ציבורית, כי אסימון תקף לשימוש אחד.

`EmailHeaderSanitizer.Sanitize` (ב-`server/Features/PublicForms`) מחליף `\r`, `\n`, תווי בקרה, מפרידי שורה/פסקה של Unicode, תווי כיווניות (bidi) ותווי רוחב אפס ברווח; יש להפעיל אותו על כל שדה חופשי שנכנס לכותרת מייל (נושא, כתובת, שם תצוגה) כשסיפור 2.1 ישלח מיילים.

## שדות אופציונליים ואווטאר (סיפור 1.8)

`POST /invites/{token}/profiles` מקבל, בנוסף לשדות החובה, שדות אופציונליים (הגשה בלעדיהם עובדת כבסיפור 1.6):

| שדה | הגבלה |
| --- | --- |
| `description` | תיאור קצר, עד 120 תווים |
| `role`, `workplace` | תפקיד ומקום עבודה, עד 100 תווים |
| `introText` | טקסט היכרות, עד 1000 תווים |
| `introAnswer1`, `introAnswer2`, `introAnswer3` | תשובות להיכרות (נוסחי השאלות בלקוח), עד 1000 תווים כל אחת |
| `avatarId` | אחד מ-`girl-1`, `girl-2`, `girl-3`, `boy-1`, `boy-2`, `boy-3`; אחר נדחה ב-400. ברירת מחדל `default` |
| `photo` | קובץ תמונה (multipart בלבד) |

הטקסטים נשמרים כטקסט פשוט, ללא תווי בקרה (בשדות הטקסט הארוכים `introText` ו-`introAnswerN` מותרות שורות חדשות). שדה ריק נחשב חסר. שגיאה: 400 `problem+json` עם `errors` לפי שדה.

**נוסחי הבקשה.** `Content-Type: application/json` (גוף עד 160KB, ללא `photo`), או `multipart/form-data` (עד 512KB): שדות הטקסט כחלקים בשמותיהם, והתמונה כחלק קובץ בשם `photo`. אסימון הקישור נבדק לפני קריאת הגוף כבעבר, וההגנות מסיפור 1.7 (Turnstile והגבלת קצב) חלות כרגיל.

**התמונה.** מוקטנת בדפדפן עד כ-300KB; השרת (SkiaSharp, רישיון MIT, עם נכסי Linux מקוריים בלי תלויות מערכת) מאמת: גודל עד 300KB, סוג לפי תוכן בלבד (JPEG, PNG או WebP; SVG, GIF או כל סוג אחר נדחים, הסיומת וה-`Content-Type` של הקובץ אינם נבדקים), מימדים עד 4096 לצלע ו-12 מגה-פיקסל, פענוח מלא (קובץ חתוך או פגום נדחה), ואז מקטין לעד 1024 פיקסלים בצלע הארוכה, מקודד מחדש ל-JPEG (בלי מטא-דאטה, ברקע לבן) ושומר כ-`bytea` בעמודה `photo` של `profile_revisions` (עם `photo_content_type`), כלומר כחלק מהגרסה ולא בדיסק. תמונה שהועלתה גוברת על אווטאר: במקרה זה `avatar_id` נשמר `default`. קובץ שגוי, פגום או גדול: 400 בשדה `photo` ולא נוצר דבר; גוף multipart מעל 512KB: 413.

**`GET /profiles/{profileId}/photo`** (אנונימי): מחזיר אך ורק את תמונת הגרסה המאושרת (`ApprovedRevisionId`) של פרופיל `Active`, עם `X-Content-Type-Options: nosniff` ו-`Cache-Control: public, max-age=300`. בכל מקרה אחר (אין גרסה מאושרת, אין תמונה, גרסה ממתינה, פרופיל שהוסר, מזהה לא קיים או פגום) מוחזר אותו 404 `problem+json` עם `no-store`. מוגבלת לפי IP בלבד (`RATE_LIMIT_PHOTO_IP`), בלי Turnstile. אין מייל או נייד בשום תגובה.

תוספות והבהרות:
- `avatarId=default` מתקבל ושקול לשדה חסר. `avatarId` לא תקין נדחה ב-400 גם כשהועלתה תמונה. חלק `photo` ריק (אפס בתים, כפי שדפדפן שולח כשלא נבחר קובץ) נחשב כאין תמונה. שם שדה כפול או חלק `photo` כפול, multipart פגום או חתוך, ויותר מ-30 חלקים: 400. סוג תוכן שאינו JSON או multipart: 415.
- כיוון התמונה לפי EXIF מיושם לפני הקידוד מחדש, כך שתמונות מטלפון אינן מסתובבות. העיבוד רץ מחוץ לחוט הבקשה, ולכל היותר שניים במקביל; אם אין מקום בתוך כשתי שניות: 503 `problem+json` עם `Retry-After` וקוד `photo-processing-busy`. תקלת תשתית בעיבוד (למשל נכס מקורי חסר) מחזירה 500 ונרשמת ביומן בלי תוכן התמונה.
- תווים נסתרים (מפרידי שורה ופסקה, bidi, רוחב אפס, BOM, כמו ב-`EmailHeaderSanitizer`) נדחים בכל שדות הטקסט האופציונליים; טאב ושורה חדשה מותרים רק ב-`introText` וב-`introAnswerN`.
- תגובות נקודת התמונה מלבד 200 ו-404: 403 כשכתובת הלקוח אינה ידועה (`client-address-unknown`), 429 עם `Retry-After` בחריגה מהמגבלה, ו-503 כשמאגר המונים אינו זמין (`rate-limit-unavailable`).
- `Cache-Control: public, max-age=300`: תמונה של פרופיל שהוסר, או שהוחלפה בגרסה חדשה, עשויה להישאר במטמונים ציבוריים עד 5 דקות.
- הנכסים המקוריים של SkiaSharp ללינוקס (`SkiaSharp.NativeAssets.Linux.NoDependencies`) מוזכרים לקונטיינר; בפיתוח מקומי ב-Windows או macOS נעשה שימוש בנכסים של סביבת ההרצה עצמה.

מיגרציה: `ProfileOptionalFields`.

## קורות חיים (סיפור 1.9)

שדה אופציונלי `cv` בהגשת הפרופיל, ב-JSON או כאחד משדות הטקסט של multipart (חלק בשם `cv`). נשמר בעמודה `cv` (text) של `profile_revisions`, כלומר כחלק מהגרסה, ואינו מוחזר באף תגובה (תגובת ההגשה לא משתנה). אין קובץ קורות חיים ואין רינדור HTML בשרת. גוף JSON עד 160KB: מספיק ל-20,000 תווים גם כשכולם נשלחים כרצף `\uXXXX`.

**מותר (רשימה סגורה):** פסקאות, כותרות, רשימות (ממוספרות ולא), הדגשה, נטוי, שבירת שורה וקישורים. **נדחה ב-400 בשדה `cv`:** כל מבנה אחר: קוד (מוטבע, גדר או הזחה), ציטוט (`>`), קו מפריד, תמונות (`![...]`), וכל קישור (גם `<autolink>` והגדרת קישור) שאינו `http://` או `https://` מוחלט, או שיש בו שם משתמש וסיסמה (`https://user:pass@host`). נדחים גם תווי בקרה ותווים נסתרים (כמו `UnsafeCharacters`; הבדיקה על הקלט הגולמי, לפני חיתוך רווחים), ותוכן שהניקוי לא הצליח לסיים תוך 250ms או 5 סבבים. טקסט עם ישויות (`&lt;script&gt;`) נשמר כפי שהוא, כטקסט.

**שגיאה:** `cv` הוא אחד משדות `errors` בחוזה ה-400 (`{ "cv": ["..."] }`): ארוך מ-20,000 תווים (נמדד אחרי נרמול שורות וחיתוך), תוכן לא נתמך, או `cv` שאינו מחרוזת ב-JSON (מספר, אובייקט, מערך, בוליאני). `null` או מחרוזת ריקה נחשבים חסר.

**מה נשמר ומה נזרק:** `\r\n` מנורמל ל-`\n`, ורק רווח, טאב ושורה חדשה נחתכים מהקצוות. כל תג HTML (כולל `<script>`, `<img>`, `<iframe>` והערות) מוסר, והטקסט שבין התגים נשמר (`<script>alert(1)</script>` נשמר כ-`alert(1)` טקסט רגיל). חריג: בלוק HTML שלא נסגר (למשל `<script` בלי `>`) מוסר יחד עם הטקסט שהוא בולע עד סוף הבלוק, וההגשה עדיין מתקבלת (202). קורות חיים שנשארו בלי תוכן אחרי הניקוי נשמרים כחסרים.

**הפונקציה המשותפת:** `ResumeApp.Features.Markdown.SafeMarkdown.Clean(text, maxLength)`, מבוססת Markdig (BSD-2) ו-AST, ועם ביטויים רגולריים בזמן ליניארי (`NonBacktracking`). אידמפוטנטית, ותשמש גם את מסך הניהול (1.10) ואת הקריאה הציבורית (1.13). אורך מקסימלי: `ProfileRevision.CvMaxLength` בלבד.

**חובה בלקוח:** להציג את הטקסט השמור דרך רנדרר Markdown בטוח (בלי HTML גולמי ובלי `innerHTML`), ולהציג קישורים עם `rel="noopener noreferrer"`.

מיגרציה: `ProfileCv`.
