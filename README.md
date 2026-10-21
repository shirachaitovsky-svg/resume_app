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
| `ALLOWED_ORIGINS` | שרת | אופציונלי: מקורות נוספים (מופרדים בפסיק, למשל `https://app.example.com`) המותרים בבקשות שינוי, מעבר לכתובת המארח שמגיעה מ-Vercel |

## הרצה מקומית

```powershell
# 1. Postgres
docker run -d --name resume-pg -e POSTGRES_PASSWORD=dev -e POSTGRES_DB=resume -p 5432:5432 postgres:17

# 2. שרת (המיגרציות רצות באתחול)
$env:ORIGIN_SECRET = "dev-secret"
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
| `GET /invites/{token}` | אנונימי | 200 עם `{ cohortName }` בלבד. קישור מבוטל, לא קיים או פגום: 404 `problem+json` זהה |

האסימון הוא 256 ביט אקראיים (base64url, 43 תווים), ייחודי באינדקס, ולא נרשם ביומן.
