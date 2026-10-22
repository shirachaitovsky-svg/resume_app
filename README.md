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
| `RATE_LIMIT_GALLERY_IP` | שרת | בקשות גלריה (`GET /profiles`, `GET /cohorts`) לשעה ל-IP (ברירת מחדל 600) |
| `RATE_LIMIT_WINDOW_MINUTES` | שרת | אורך החלון הקבוע בדקות (ברירת מחדל 60). חייב להיות מחלק של 1440 (1, 5, 15, 30, 60, 120, 1440 וכו'); ערך אחר נדחה באתחול. שאר ערכי `RATE_LIMIT_*` חייבים להיות מספרים שלמים חיוביים |
| `RATE_LIMIT_EDIT_LINK_IP` | שרת | בקשות קישור אישי (`POST /profiles/edit-link`) לשעה ל-IP (ברירת מחדל 20) |
| `RATE_LIMIT_EDIT_LINK_EMAIL` | שרת | בקשות קישור אישי לשעה לכתובת מייל מנורמלת (ברירת מחדל 5) |
| `RATE_LIMIT_EDIT_SESSION_IP` | שרת | החלפות אסימון קישור אישי בסשן (`POST /profiles/session`) לשעה ל-IP (ברירת מחדל 30) |
| `RATE_LIMIT_CONTACT_REQUEST_IP` | שרת | בקשות פרטי קשר (`POST /profiles/{id}/contact-requests`) לשעה ל-IP (ברירת מחדל 10) |
| `RATE_LIMIT_CONTACT_REQUEST_EMAIL` | שרת | בקשות פרטי קשר לשעה לכתובת המבקר המנורמלת (ברירת מחדל 3) |
| `RATE_LIMIT_CONTACT_REQUEST_PROFILE` | שרת | בקשות פרטי קשר לשעה לפרופיל, גם לפרופיל שאינו קיים (ברירת מחדל 10) |
| `RATE_LIMIT_CONTACT_VERIFY_IP` | שרת | אימות כתובת מבקר (`POST /contact-requests/verify`) לשעה ל-IP (ברירת מחדל 30) |
| `RATE_LIMIT_CONTACT_DECISION_IP` | שרת | תצוגה מקדימה והחלטת משתתפת (`POST /contact-decisions/preview` ו-`/decide`) לשעה ל-IP (ברירת מחדל 30) |
| `EMAIL_PROVIDER` | שרת | `smtp` או `resend`. ב-Development ברירת המחדל `smtp` אל `localhost:1025` (Mailpit); **מחוץ ל-Development חובה, ובלעדיו השרת לא עולה** |
| `EMAIL_FROM` | שרת | כתובת השולח. חובה מחוץ ל-Development |
| `ADMIN_NOTIFICATION_EMAIL` | שרת | כתובות המנהלת להתראת בקשת פרטי קשר: אחת או עד 5 מופרדות בפסיק. חובה מחוץ ל-Development; ב-Development ברירת מחדל `admin@example.test` |
| `SMTP_HOST`, `SMTP_PORT`, `SMTP_USER`, `SMTP_PASSWORD`, `SMTP_SECURE` | שרת | ל-`EMAIL_PROVIDER=smtp`. `SMTP_HOST` חובה מחוץ ל-Development; `SMTP_PORT` ברירת מחדל 587 (1025 ב-Development); `SMTP_SECURE=true` (ברירת מחדל מחוץ ל-Development) מצפין: SSL מלא בפורט 465, אחרת STARTTLS; `SMTP_USER` ו-`SMTP_PASSWORD` אופציונליים |
| `RESEND_API_KEY` | שרת | ל-`EMAIL_PROVIDER=resend`. חובה במקרה זה |
| `PUBLIC_BASE_URL` | שרת | כתובת האתר הציבורי, בסיס קישור ההזמנה והקישור האישי. ב-Development ברירת המחדל `http://localhost:4200`; **מחוץ ל-Development חובה** (קישור במייל אל localhost אינו תקין) |
| `EMAIL_WORKER` | שרת | `false` מכבה את תהליך הרקע ששולח מיילים מה-Outbox (ברירת מחדל: רץ; אינו רץ בלי בסיס נתונים) |
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

משתנה סביבה `PUBLIC_BASE_URL` (שרת; ברירת מחדל `http://localhost:4200` ב-Development בלבד, מחוץ ל-Development חובה, ראו סיפור 2.1) קובע את בסיס קישור ההזמנה: `{PUBLIC_BASE_URL}/join/{token}`.

| נקודה | הרשאה | תיאור |
| --- | --- | --- |
| `POST /admin/cohorts` | אדמין | `{ "name": "..." }` (עד 100 תווים). 201 עם `id`, `name`, `inviteToken`, `inviteUrl`, `createdAt` |
| `GET /admin/cohorts` | אדמין | `{ items, total }`, החדש ראשון, כולל סטטוס ביטול |
| `POST /admin/cohorts/{id}/revoke` | אדמין | 204; ביטול חוזר 409; מזהה לא קיים 404 |
| `GET /admin/submissions` | אדמין | תור הגרסאות הממתינות (`pending`), מהישנה לחדשה: `{ items, total }`. פריט: `revisionId`, `name`, `productName`, `cohortName`, `kind` (`new` או `update`), `submittedAt`, `hasContactChange`, `profileRemoved` (הפרופיל הוסר; אישור הגרסה יחזיר אותו לפעילות). `limit` בין 1 ל-100 (ברירת מחדל 50; מעל 100 נחתך) ו-`offset` 0 ומעלה; ערך מחוץ לטווח 400. מזהה גרסה בפורמט GUID סטנדרטי בלבד |
| `GET /admin/submissions/{revisionId}` | אדמין | כל פרטי ההגשה (כולל `profileRemoved`): מייל, נייד, `productUrl`, `avatarId`, `hasPhoto`, שדות הטקסט, ו-`cv`. רק `cv` מנוקה כ-Markdown (`SafeMarkdown.Clean`); שאר הטקסטים מוחזרים כפי שנשמרו, כטקסט פשוט, והלקוח חייב להציגם כטקסט ולא כ-HTML. `cv` שנדחה מוחזר כ-`null` עם `cvInvalid: true`. לא קיים או לא ממתינה: 404 |
| `GET /admin/submissions/{revisionId}/photo` | אדמין | תמונת הגרסה הממתינה, `nosniff` ו-`no-store`; בלי תמונה 404 |
| `POST /admin/submissions/{revisionId}/approve` | אדמין + anti-forgery + `Origin` | אישור פרסום של גרסה ממתינה, בטרנזקציה אחת: הגרסה `approved`, `Profile.ApprovedRevisionId` מצביע אליה, `Visibility = active`, וגרסה מאושרת קודמת הופכת `superseded`. 200 עם `revisionId`, `profileId`, `status: "approved"` ו-`reactivated` (`true` כשהאישור החזיר פרופיל שהוסר ל-`Active`, אחרת `false`; בלי מייל או נייד). 409 `problem+json` כשהגרסה קיימת ואינה `pending` (כולל אישור חוזר או מקביל; רק בקשה אחת מצליחה). 404 אחיד למזהה לא תקין (GUID בצורה `D` בלבד) או לא קיים. האישור אינו נוגע ב-`ParticipantPrivate` ואינו אישור מסירת פרטי קשר |
| `POST /admin/submissions/{revisionId}/approve-contact-change` | אדמין + anti-forgery + `Origin` | אישור מפורש של שינוי מייל או נייד ממתין (נפרד מאישור הפרסום). גוף: `{ "pendingEmail"?, "pendingPhone"? }` (לפחות אחד; הערכים שהמנהלת ראתה, מושווים אחרי נרמול ובהתאמה מלאה לערכים הממתינים). חל על גרסה `pending` או `approved` שנושאת שינוי. בטרנזקציה עם נעילת שורת הפרופיל: מעדכן את `participant_private`, מקדם `contact_version`, מנקה את `pending_email` ו-`pending_phone`, וכותב `audit_log` (`approve-contact-change`, בלי ערכים). 200 `{ revisionId, status: "contact-change-approved" }` בלי מייל או נייד |
| `POST /admin/submissions/{revisionId}/reject-contact-change` | אדמין + anti-forgery + `Origin` | אותו גוף; מנקה את השדות הממתינים בלי לשנות את `participant_private`, ו-`audit_log` (`reject-contact-change`). 200 `{ revisionId, status: "contact-change-rejected" }` |
| `POST /admin/submissions/{revisionId}/return` | אדמין + anti-forgery + `Origin` | החזרה לתיקון (סיפור 2.4). גוף: `{ "note": "..." }`; ההערה חובה, 1 עד 1000 יחידות UTF-16 אחרי trim, טקסט פשוט: תווי בקרה (חוץ מ-line feed ומ-tab) ותווים נסתרים נדחים ב-400 על `note`, וירידות שורה מנורמלות ל-line feed. סימנים כמו `<` ו-`>` מותרים. אימות ההערה (400) קודם ל-404 ול-409. בטרנזקציה עם נעילת שורת הפרופיל: עדכון מותנה `pending` ל-`returned` עם `return_note` ו-`returned_at`, `audit_log` (`return-revision`, בלי הערה) ושורת `email_outbox` (`submission-returned`, `profile_id`, `revision_id`, מפתח `submission-returned:{revisionId}`; בלי הערה, אסימון, מייל או נייד). גרסה שאינה `pending`: 409 `revision-not-pending` עם `latestPendingRevisionId`; לא קיימת או מזהה לא קנוני: 404. 200 `{ revisionId, status: "returned" }`. המייל נשלח רק על ידי ה-worker: טקסט פשוט, נושא קבוע, ההערה כפי שנכתבה (מייל טקסט אינו משטח HTML; רק תווים נסתרים מוסרים, ושורות מנורמלות), שם המחזור וקישור אישי חדש `/edit#token=...` (מבטל אסימונים קודמים; משך התוקף נגזר מהגדרת האסימון). הרשומה הופכת `dead` בלי שליחה ובלי הנפקת אסימון כש: הפרופיל הוסר או חסרה שורה פרטית (`no-recipient`), הגרסה לא קיימת (`revision-missing`), הגרסה כבר אינה `returned` (`revision-not-returned`, למשל כשהמשתתפת כבר הגישה מחדש), או ההערה ריקה (`empty-note`). `return_note` נשמר ללא הגבלת זמן (אין עדיין מדיניות שמירה), ואין נקודת אדמין שמציגה אותו אחרי השליחה |
| `GET /admin/contact-changes` | אדמין | שינויי מייל או נייד שלא נפתרו, מהישן לחדש: `{ items, total }` (`limit` 1..100, ברירת מחדל 50; `offset` ≥ 0). כולל גרסה `pending` וגם גרסה `approved` שאין לפרופיל גרסה ממתינה חדשה ממנה (כלומר גם אחרי אישור הפרסום, כשהיא כבר לא בתור). לא כולל `superseded`, `returned`, שינוי שנפתר או פרופיל שהוסר (אישור או דחייה של שינוי כזה: 409 `revision-not-eligible`). פריט: `revisionId`, `profileId`, `name`, `productName`, `cohortName`, `status` (`pending` או `approved`), `email` ו-`phone` נוכחיים, `pendingEmail`, `pendingPhone`, `submittedAt`. נתונים לאדמין בלבד, `Cache-Control: no-store` |
| `GET /admin/profiles` | אדמין | רשימת פרופילים לניהול (סיפור 2.5): `{ items, total }`, מהחדש לישן לפי `createdAt` ואז מזהה. פרמטרים: `visibility` (`active` ברירת מחדל, `removed` או `all`; אחר 400), `cohort` (GUID בצורה `D`, אופציונלי), `limit` 1..100 (ברירת מחדל 50; מעל 100 נחתך), `offset` 0 ומעלה. פריט: `profileId`, `name`, `productName` (מהגרסה המאושרת, ואם אין מהגרסה האחרונה), `cohortName`, `visibility`, `hasApproved`, `createdAt`; בלי מייל, נייד או קורות חיים. `Cache-Control: no-store`. פרופילים שהוסרו זמינים כאן עם `visibility=removed` |
| `POST /admin/profiles/{profileId}/remove` | אדמין + anti-forgery + `Origin` | הסרת פרופיל מהאתר הציבורי (סיפור 2.5), בלי מחיקת נתונים. בטרנזקציה אחת עם נעילת שורת הפרופיל: עדכון מותנה `visibility` מ-`Active` ל-`Removed`, ביטול אסימוני `edit-profile` שטרם נוצלו (`revoked_at`), ו-`audit_log` (`remove-profile`, ישות `profile`, מזהה הפרופיל). 200 `{ profileId, visibility: "removed" }`. פרופיל שכבר הוסר: 409 עם `code: "profile-not-active"` ובלי שינוי. מזהה לא קנוני או לא קיים: 404 אחיד. הפרופיל נעלם מ-`/profiles`, `/profiles/{id}`, התמונה ו-`/cohorts`; סשן משתתפת קיים נדחה ב-401 והעוגיה נמחקת; שורות outbox ממתינות נהפכות `dead` עם `no-recipient`. אישור גרסה חדשה של פרופיל שהוסר מחזיר אותו ל-`Active` (סיפור 1.11). **בקשות פרטי קשר (סיפור 3.1):** באותה טרנזקציה, בקשות `awaiting_email_verification` ו-`submitted` של הפרופיל עוברות ל-`expired` ואסימוני `verify-contact-email` הפתוחים שלהן מתבטלים (AD-13); מצבי ההמשך של אפיק 3 יתווספו לרשימה זו בסיפורים 3.3 ו-3.4 |
| `GET /invites/{token}` | אנונימי | 200 עם `{ cohortName }` בלבד. קישור מבוטל, לא קיים או פגום: 404 `problem+json` זהה |

**יומן ביקורת (`audit_log`):** כל אישור נרשם באותה טרנזקציה עם `actor_id` (מזהה המנהלת), `at` (UTC), `action` (`approve-revision`, `return-revision`, `approve-contact-change`, `reject-contact-change` או `remove-profile`), `entity_type` (`profile-revision`, או `profile` להסרה) ו-`entity_id` (מזהה הגרסה, או מזהה הפרופיל להסרה) בלבד, בלי שם, מייל, נייד או טקסט. לטבלה אין מפתחות זרים בכוונה (השורות נשמרות גם אם הישות או המשתמשת נמחקות); אכיפת append-only תתווסף בסיפור 4.2.

**הסרת פרופיל (סיפור 2.5), הבהרות:** אין נקודת שחזור. פרופיל שהוסר חוזר ל-`Active` רק כשאדמין מאשרת גרסה שלו (סיפור 1.11), ו-`profileRemoved` בתור ובפרטי ההגשה, ו-`reactivated` בתגובת האישור, מציגים זאת במפורש. `audit_log` הוא התיעוד היחיד של מי הסירה פרופיל ומתי (אין עמודת מסיר או זמן הסרה על הפרופיל). ההסרה אינה מחליפה (`superseded`) גרסאות ממתינות: הן נשארות בתור. מסך הניהול וחלון האישור להסרה הם עבודת לקוח (סיפור 5.6).

תגובת 409 של האישור כוללת בגוף ה-`problem+json` את השדה `code: "revision-not-pending"`, כדי שהלקוח יבדיל אותה מ-409 אחרים. אם מצביע `ApprovedRevisionId` של הפרופיל מפנה לגרסה שאינה `approved`, האישור נכשל ב-500 `problem+json` וכל השינויים מתבטלים.

**סקירת עדכון ושינוי קשר (סיפור 2.3):** `GET /admin/submissions/{revisionId}` מוסיף `published` (השדות הנראים של הגרסה המאושרת הנוכחית, קורות חיים מנוקים; `null` ל-`kind: new`), `changes` (שמות השדות ששונו: `name`, `productName`, `productUrl`, `description`, `role`, `workplace`, `introText`, `introAnswer1..3`, `avatarId`, `cv`, `photo`; ריק ל-`new`) ו-`contactChange` (`{ pendingEmail, pendingPhone }` או `null`; לא חלק מ-`changes`). התור מוסיף `hasContactChange` לכל פריט. אישור הפרסום אינו מחיל שינוי מייל או נייד. 409 של אישור הפרסום כולל גם `latestPendingRevisionId` (הגרסה הממתינה הנוכחית של הפרופיל או `null`). קודי 409 של נקודות שינוי הקשר (`code`): `no-contact-change` (אין שינוי ממתין), `contact-change-stale` (הגוף אינו תואם לערך הממתין), `revision-not-eligible` (הגרסה `superseded` או `returned`), `email-taken` (המייל כבר קיים במחזור אצל פרופיל אחר, בלי שינוי ובלי חשיפה). 404 אחיד לגרסה לא קיימת או מזהה שאינו GUID בצורה `D`; 400 לגוף ריק או ערך לא תקין. אישור ודחייה מקבילים מצליחים פעם אחת בלבד. `participant_private.contact_version` (ברירת מחדל 1) עולה בכל החלת שינוי, לבדיקת העקביות של מסירת פרטים (סיפור 3.4).

פירוט נוסף לנקודות שינוי הקשר: אין פתרון חלקי. הגוף חייב להתאים בדיוק לכל שדה ממתין (מייל ו/או נייד, כפי שהם ממתינים), אחרת 409 `contact-change-stale`. אישור ודחייה מותרים על גרסה `pending` או על גרסה `approved` שאין לפרופיל גרסה `pending` חדשה ממנה; אחרת 409 `revision-not-eligible`. בקשת שינוי שנישאה על גרסה שהוחלפה (`superseded`) נשלחת שוב על ידי מילוי הטופס מראש (סיפור 2.2), ואחרת נזנחת. בנקודות אלה 400 לגוף לא תקין קודם ל-404 למזהה לא קיים. אישור מייל שכבר זהה לנוכחי (בלי שינוי נייד) אינו נוגע ב-`participant_private` ואינו מקדם `contact_version`, אך מנקה את השדה הממתין ונרשם ב-`audit_log`. רשימת `changes` בסדר קבוע: `name`, `productName`, `productUrl`, `description`, `role`, `workplace`, `introText`, `introAnswer1`, `introAnswer2`, `introAnswer3`, `avatarId`, `cv`, `photo`.


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

## גלריה ציבורית (סיפור 1.12)

הגלריה ורשימת המחזורים קוראות דרך `PublicProfileQuery` יחידה: רק פרופילים `Active` עם גרסה מאושרת, והנתונים מהגרסה המאושרת בלבד (גרסה ממתינה, שהוחזרה או שהוחלפה לא מופיעה). נקודת התמונה `GET /profiles/{profileId}/photo` עוברת מסיפור 1.13 דרך אותו עוזר נראות.

**`GET /profiles`** (אנונימי): `{ items, total }`, ממוין לפי זמן ההגשה של הגרסה המאושרת מהחדש לישן (הגשה מאושרת אחרונה קודם), ובשוויון לפי מזהה בסדר יורד. פרמטרים:
- `cohort`: מזהה מחזור GUID בצורה `D`; פגום: 400, לא קיים: רשימה ריקה.
- `limit`: ברירת מחדל 100; מעל 200 נחתך ל-200 (לא שגיאה); מתחת ל-1 או לא מספר: 400.
- `offset`: מינימום 0; שלילי, לא מספר או גדול מדי: 400.
- מספרים ספרות בלבד (בלי רווח או `+`), ומפתח חוזר (`limit=1&limit=2`) הוא 400.

כרטיס: `id`, `name`, `productName`, `productUrl`, `description` (תיאור חסר מוחזר כמחרוזת ריקה `""`), `avatarId`, `photoUrl` (נתיב יחסי `/profiles/{id}/photo`, או `null` בלי תמונה), `cohort` (`id`, `name`). כל המזהים הם GUID אקראיים (v4) יציבים.

**`GET /cohorts`** (אנונימי): `{ items: [{ id, name }], total }` של מחזורים שיש בהם פרופיל ציבורי, לפי שם. המזהה הוא `Cohort.Id` היציב, לא קישור ההזמנה.

כותרות: `Cache-Control: public, max-age=30` (שגיאות: `no-store`), `X-Content-Type-Options: nosniff`. לכן פרופיל שהוסר או הוחזר עלול להישאר במטמונים משותפים עד 30 שניות. מוגבלות לפי IP בלבד (`RATE_LIMIT_GALLERY_IP`), בלי Turnstile; חריגה: 429 עם `Retry-After` ו-`code: rate-limited`. בדיקת CI סורקת את כל טיפוסי `Public*` ואת ה-JSON ומוודאת שאין בהם מייל, נייד, אסימון, בקשה או גרסה.

## עמוד פרופיל ציבורי (סיפור 1.13)

**`GET /profiles/{profileId}`** (אנונימי) עובר דרך `PublicProfileQuery` ואותו עוזר נראות (`VisibleProfiles`) כמו הגלריה, וגם נקודת התמונה `GET /profiles/{profileId}/photo` עברה אליו, כך שהשתיים תמיד מסכימות. תנאים: `Visibility = Active`, `ApprovedRevisionId` מצביע לגרסה `Approved` של אותו פרופיל, והנתונים מהגרסה המאושרת בלבד. מזהה בצורת GUID `D` בלבד.

כל מקרה לא זמין (לא קיים, מזהה פגום או לא קנוני, ממתין בלבד, הוחזר, הוחלף, הוסר, מצביע לא עקבי) מחזיר אותו 404 `problem+json` עם `no-store`, בלי לחשוף אם הפרופיל קיים.

שדות: `id`, `name`, `productName`, `productUrl`, `description` (`""` כשחסר), `role`, `workplace`, `introText`, `avatarId`, `photoUrl` (או `null`), `cohort` (`id`, `name`), `introAnswers` (מערך `{ number, text }` לשאלות 1 עד 3 שנענו בלבד, בסדר עולה; ריק או רווחים בלבד לא מופיע; `[]` כשאין) ו-`cv`. קורות החיים עוברים `SafeMarkdown.Clean` בכל קריאה, וערך שנדחה או ריק מחזיר `cv: null`. אין בתגובה מייל, נייד, אסימון הזמנה, פרטי בקשות, מזהה גרסה או סטטוס. כותרות: `Cache-Control: public, max-age=30`, `nosniff`; הגבלת קצב לפי IP עם אותו scope כמו הגלריה (`RATE_LIMIT_GALLERY_IP`).

פרטים נוספים על עמוד הפרופיל:
- `role`, `workplace` ו-`introText` מסורילים כ-`null` כשחסרים, ו-`description` כ-`""`.
- גוף ה-404: `application/problem+json` עם `title` (`לא נמצא`) ו-`status` (404), זהה בכל מקרה לא זמין, בשתי הנקודות (פרופיל ותמונה).
- `GET /profiles/{profileId}` חולקת את מגבלת הקצב של הגלריה (`RATE_LIMIT_GALLERY_IP`), ואילו נקודת התמונה שומרת על ה-scope שלה (`RATE_LIMIT_PHOTO_IP`).
- שינוי התנהגות: נקודת התמונה מקבלת עכשיו רק GUID קנוני עם מקפים (צורה `D`); צורות אחרות (`N`, `B`, `P`) שהתקבלו קודם מחזירות 404.
- `photoUrl` אינו מגורסא: אחרי אישור מחדש של תמונה חדשה, הישנה עלולה להישאר במטמון עד 5 דקות (`max-age=300`).
- תשובות היכרות עוברות חיתוך רווחים ונרמול שורות ל-`\n`; שאילתת הגלריה אינה שולפת את עמודות הפרופיל המלא (נבדק בבדיקה).

## גלריה ועמוד פרופיל בלקוח ונתוני דוגמה (סיפור 5.7)

דף הבית (`/`) הוא הגלריה הציבורית ו-`/profile/:id` הוא עמוד הפרופיל; שניהם קוראים את `GET /profiles`, `GET /cohorts` ו-`GET /profiles/{id}`. סינון המחזור נשמר ב-`?cohort={id}`.

לזריעת נתוני דוגמה למסד הפיתוח (פיתוח בלבד; שני מחזורים, שמונה פרופילים ציבוריים, פרופיל שהוסר ופרופיל ממתין שאינם מוצגים):

```powershell
# הקונטיינר resume-dev-pg חייב לרוץ והמיגרציות להיות מוחלות
powershell -File scripts/dev-seed.ps1
```

הסקריפט אידמפוטנטי (מזהי GUID קבועים, מחיקה ויצירה מחדש של שורות הזריעה בלבד) ומדפיס סיכום. אחריו: `curl http://localhost:4200/api/profiles` (דרך `npm start` בלקוח) מחזיר את הפרופילים. ה-SQL ב-`scripts/dev-seed.sql`.

## קישור אישי במייל (סיפור 2.1)

**`POST /profiles/edit-link`** (אנונימי) עם גוף `{ "email": "..." }`. מוגן כמו טפסים ציבוריים אחרים: `Origin`, anti-forgery, הגבלת IP (`RATE_LIMIT_EDIT_LINK_IP`), Turnstile (`X-Turnstile-Token`), והגבלה לפי כתובת מייל מנורמלת (`RATE_LIMIT_EDIT_LINK_EMAIL`). התגובה תמיד `202 {"status":"ok"}`, בין אם הכתובת רשומה ובין אם לא; מייל לא תקין מחזיר 400 ולידציה; חריגה מהמגבלה 429 עם `Retry-After` ו-`code: rate-limited`. הנתיב לא שולח מייל: לכל פרופיל `Active` עם אותה כתובת (גם בלי גרסה מאושרת) נכתבת שורת `email_outbox` (`TemplateKey = edit-link`, מזהה הפרופיל, `DeduplicationKey = edit-link:{profileId}:{yyyyMMddHH}` ייחודי, כך שבקשות חוזרות באותה שעה לא יוצרות כפילות).

**Outbox ותהליך רקע.** שורת Outbox מכילה מזהים, סטטוס, ניסיונות, `NextAttemptAt`, `LeaseUntil`, קטגוריית שגיאה וזמנים בלבד: אין אסימון, מייל, נייד או תוכן. `EmailOutboxWorker` תופס שורה אחת בכל פעם (`pending` שהגיע זמנה) בהצהרה אטומית אחת עם `FOR UPDATE SKIP LOCKED` וקובע lease של 2 דקות (קריסה: ה-lease פג והשורה נתפסת שוב). השליחה מחוץ לטרנזקציה: קריאת המייל מ-`participant_private` (רק לפרופיל `Active`), הנפקת אסימון, שליחה דרך `IEmailSender`, ורק אז `sent`. כישלון זמני (רשת, 5xx) מתזמן ניסיון נוסף עם backoff אקספוננציאלי (30 שנ' כפול 2 בחזקת הניסיון, תקרה של שעה); תגובת 429 מהספק דוחה לפי `Retry-After` בלי להיספר כניסיון כושל. אחרי 5 ניסיונות כושלים השורה `dead`. פרופיל שהוסר או בלי פרטי קשר: `dead` עם הקטגוריה `no-recipient`, בלי שליחה. מפתח האידמפוטנטיות לספק הוא מזהה השורה ומספר התפיסה (`{id}:{claimCount}`), כי ניסיון חוזר שולח גוף אחר (אסימון חדש).

**`GET /admin/email-outbox`** (`AdminPolicy`): `{ counts: { pending, sent, dead }, items, total }` כש-`items` רשימת שורות `dead` (`id`, `templateKey`, `attempts`, `lastErrorCategory`, `createdAt`), עם `limit` (ברירת מחדל 50, עד 200) ו-`offset`. בלי כתובת, טקסט או אסימון.

**אסימון קישור אישי** (`personal_tokens`): 32 בתים אקראיים (256 ביט) בקידוד base64url, נשמר רק כ-SHA-256 עם `Purpose = edit-profile`, תוקף 24 שעות, `UsedAt`, `RevokedAt`. הנפקה חדשה לאותו פרופיל מבטלת את הקודמים שטרם נוצלו באותה טרנזקציה (תחת נעילת שורת הפרופיל). האסימון אינו נרשם ביומן ואינו נשמר בשום מקום מלבד ה-hash ובגוף המייל. פתיחת הקישור והעריכה הם סיפור 2.2.

**המייל**: `text/plain; charset=utf-8` בלבד, עברית, נושא קבוע, והקישור `{PUBLIC_BASE_URL}/edit#token={token}` (האסימון בפרגמנט). ערכים שנכנסים לכותרות עוברים `EmailHeaderSanitizer`.

**ספקים.** `IEmailSender` (`Features/Email`) הוא נקודת ההחלפה היחידה. `SmtpEmailSender` (MailKit) ל-`EMAIL_PROVIDER=smtp`, ו-`ResendEmailSender` (HTTP, כותרת `Idempotency-Key`) ל-`EMAIL_PROVIDER=resend`. לפיתוח מקומי:

```powershell
docker compose -f docker-compose.dev.yml up -d mailpit
# ב-Development בלי EMAIL_PROVIDER: SMTP אל localhost:1025. המיילים מופיעים ב-http://localhost:8025
```

מיגרציה `EmailOutbox` מוסיפה את הטבלאות `email_outbox` ו-`personal_tokens` ואינדקס על `participant_private.email`.

### מגבלות והתנהגות תפעולית (סיפור 2.1)

- **תפיסה בודדת ובעלות על ה-lease.** כל תפיסה היא שורה אחת, כך שה-lease (2 דקות) מכסה שליחה אחת בלבד. סימון `sent` וכישלון נכתבים רק אם השורה עדיין `pending` ועדיין נושאת את ה-lease של התפיסה הנוכחית (`lease_until` ששמור בבסיס הנתונים); worker ישן שה-lease שלו פג לא יכול לדרוס תוצאה חדשה, ובמקרה כזה נרשמת אזהרה בלי PII. כל תפיסה מגדילה את `claim_count`; שורה שנתפסה 10 פעמים (למשל קריסה באמצע שליחה) הופכת `dead` עם `too-many-claims`.
- **שליחה לפחות פעם אחת (at-least-once).** אם השליחה הצליחה והסימון `sent` נכשל, הסימון נוסה שוב עד 3 פעמים בלי לשלוח מחדש; אם גם זה נכשל, ה-lease יפוג והמייל עלול להישלח שוב. כך גם בכישלון דו-משמעי (פסק זמן אחרי שהספק קיבל). ניסיון חוזר מנפיק אסימון חדש, והוא מבטל את האסימון הקודם שטרם נוצל; הקישור האחרון שהתקבל הוא התקף.
- **סוגי כישלון.** כישלון זמני נספר ומתוזמן עם backoff; 429 נדחה בלי להיספר, ושורה שנדחית כך יותר מ-24 שעות מיצירתה הופכת `dead` (`deferred-too-long`); כישלון קבוע (כתובת לא תקינה, דחיית SMTP 5xx, 4xx של Resend מלבד 408 ו-429) הופך `dead` מיד. בכיבוי השרת לא נרשם ניסיון כושל.
- **דה-דופליקציה לפי שעה.** מפתח הכפילות כולל את השעה (`yyyyMMddHH`): בקשה שנייה לאותו פרופיל באותה שעה מקבלת 202 אבל לא מייצרת מייל נוסף.
- **מגבלת קצב לפי מייל ניתנת לניצול.** צד שלישי שמכיר כתובת יכול למצות את מכסת השעה שלה (`RATE_LIMIT_EDIT_LINK_EMAIL`) ולמנוע מהמשתתפת לבקש קישור עד סוף החלון.
- **ה-worker רץ בתהליך השרת.** ב-Render החינמי, מופע ישן ("נרדם") אינו שולח מיילים עד שמתעורר. לייצור נדרש שרת בתשלום.
- **אין ניקוי.** שורות `sent` ו-`dead` ואסימונים שפג תוקפם אינם נמחקים עדיין.
- **תוכן המייל** כולל את שם המחזור, כדי להבדיל בין קישורים לאותה כתובת במחזורים שונים.
- **אימות תצורה בעלייה.** מחוץ ל-Development: `PUBLIC_BASE_URL` חייב להיות https בלי query או fragment, `EMAIL_FROM` חייב להיות כתובת תקינה, `SMTP_SECURE=false` אסור יחד עם `SMTP_USER`, ו-`EMAIL_WORKER` חייב להיות `true/false/1/0/yes/no/on/off` (בכל סביבה).

## כניסה ועריכה כגרסה חדשה (סיפור 2.2)

**`POST /profiles/session`** (אנונימי) עם גוף `{ "token": "..." }` מחליף אסימון קישור אישי (`edit-profile`) בסשן משתתפת. מוגן ב-`Origin`, anti-forgery והגבלת קצב לפי IP (`RATE_LIMIT_EDIT_SESSION_IP`, ברירת מחדל 30 לשעה); בלי Turnstile, כי האסימון עצמו הסוד. הצריכה היא `UPDATE` מותנה אטומי אחד (`used_at IS NULL AND revoked_at IS NULL AND expires_at > now`, מטרה `edit-profile`, ופרופיל `Active`), כך שהאסימון חד-פעמי וקריאות מקבילות מצליחות פעם אחת בלבד. כל אסימון לא תקף (פג, נוצל, בוטל, לא קיים, פגום, באורך חריג, מטרה אחרת, פרופיל שהוסר) מקבל את אותו `401` עם `code: link-invalid`; אסימון של פרופיל שהוסר אינו נצרך. הצלחה: `200 {"status":"ok"}` ועוגיית `__Host-resume-participant` (`HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`, תוקף מוחלט של 60 דקות בלי הארכה), עם claims של `profile_id` ו-`purpose=edit-profile` בלבד; התגובה כוללת `Referrer-Policy: no-referrer` ו-`Cache-Control: no-store`. **`POST /profiles/session/logout`** מוחק את העוגיה.

**שתי זהויות נפרדות.** סשן המשתתפת הוא סכמת אימות נפרדת (`Participant`) ומדיניות `Participant`. נקודות `/me/*` מקבלות רק אותה (עוגיית אדמין נדחית ב-401), ונקודות האדמין מקבלות רק סשן אדמין. **`GET /profiles/csrf`** (אנונימי) מנפיק אסימון anti-forgery הקשור לזהות האנונימית, בלי קשר לעוגיות שבדפדפן (למשל כשמוחזקת גם עוגיית אדמין). הלקוח משתמש בו לבקשות `POST /profiles/session`, `POST /profiles/session/logout` ו-`/me/*` (בכותרת `X-CSRF-TOKEN`), ואלה נבדקות מול הזהות האנונימית; `GET /auth/csrf` נשאר לנקודות האדמין והאימות. כל בקשת שינוי דורשת גם `Origin` תואם.

**`GET /me/profile`** מחזיר את הפרופיל של הסשן בלבד (בלי מזהה בנתיב): מקור העריכה (הגרסה הממתינה האחרונה אם יש, אחרת המאושרת), כל שדות ההגשה, `hasPhoto`, `cohortName`, `hasPublished`, `pending` (זמן ההגשה של הגרסה הממתינה או `null`) ו-`pendingContactChange`. `email` ו-`phone` הם הערכים למילוי הטופס מראש: הערך המבוקש כשממתין שינוי פרטי קשר, אחרת הנוכחי. בנוסף `pendingEmail` ו-`pendingPhone` (`null` כשאין בקשה) ו-`currentEmail` ו-`currentPhone` (פרטיים למשתתפת בלבד).

**`PUT /me/profile`** מקבל JSON או multipart כמו הגשה ראשונה (אותם שדות, `cv`, `avatarId`, `photo`, אותה ולידציה, ניקוי ומגבלות גוף: 160KB ל-JSON ו-512KB ל-multipart) ובנוסף `removePhoto`. שדות החובה חובה גם בעדכון. בטרנזקציה אחת (נעילת שורת הפרופיל): גרסה `pending` קיימת הופכת `superseded` ונוצרת גרסה `pending` חדשה; הגרסה המאושרת, `approved_revision_id`, `visibility` ו-`participant_private` אינם משתנים, כך שהגלריה והעמוד הציבורי ממשיכים להציג את הגרסה המאושרת עד אישור. בלי `photo` חדש ועם `removePhoto` שקר התמונה של המקור מועתקת. מייל או נייד שונים מהנוכחיים (אחרי נרמול) נשמרים בגרסה החדשה כ-`pending_email` ו-`pending_phone` ואינם חלים (האישור הוא סיפור 2.3); אין בדיקת ייחודיות של מייל חדש בשלב זה (היא נעשית באישור השינוי, סיפור 2.3). הצלחה: `202 {"status":"pending"}`; ולידציה: `400` ולא נוצר דבר; סשן בלי פרופיל פעיל: `401` (`code: session-invalid`).

**שינוי פרטי קשר בעדכונים חוזרים.** ב-`PUT` ערך שנשלח ושווה לערך הממתין שומר את הבקשה, ערך השווה לנוכחי מבטל במפורש בקשה ממתינה, וכל ערך אחר מחליף אותה. מכיוון ש-`GET` מחזיר את הערך הממתין למילוי מראש, עריכה לא קשורה שהטופס שולח כמו שהוא אינה מבטלת בקשה ממתינה.

**סשן.** העוגיה חסרת מצב, ולכן יציאה (`POST /profiles/session/logout`) מנקה אותה בדפדפן בלבד; עוגיה שהועתקה נשארת תקפה עד תום 60 הדקות. סשן שפג באמצע עריכה (או שהפרופיל שלו הוסר או נחסרו פרטיו) מקבל `401` עם `code: session-invalid` והשרת מוחק את העוגיה; על המשתתפת לבקש קישור אישי חדש. המיגרציה `ParticipantEdit` מוסיפה את `profile_revisions.pending_email` ו-`pending_phone`.

**החזרה לתיקון, צד המשתתפת (סיפור 2.4):** `GET /me/profile` מחזיר `returned: { note, returnedAt }` כשהגרסה האחרונה של הפרופיל הוחזרה ואין גרסה `pending` חדשה ממנה, אחרת `null`. מקור העריכה כשאין `pending`: הגרסה שהוחזרה האחרונה אם היא חדשה מהמאושרת, אחרת המאושרת; שינוי מייל או נייד ממתין שנישא על הגרסה שהוחזרה ממשיך למלא את הטופס. `PUT /me/profile` יוצר גרסה `pending` חדשה והגרסה שהוחזרה נשארת `returned` (היסטוריה). גרסה שהוחזרה אינה מוצגת בציבור, אינה בתור הסקירה, וגרסה מאושרת קודמת נשארת מוצגת.

**שינוי מייל או נייד ממתין על גרסה שהוחזרה:** נקודות האדמין לאישור ודחייה של שינוי קשר מקבלות רק גרסה `pending` או `approved`, ולכן המנהלת אינה יכולה לפתור שינוי שנישא על גרסה `returned`. השינוי נשמר בטופס של המשתתפת (prefill) ועובר לגרסה החדשה כשהיא מגישה מחדש, והוא נשאר בלתי פתור עד אז.

## בקשת פרטי קשר ואימות כתובת (סיפור 3.1)

חלק השרת בלבד, עד מצב `submitted`. אין עדיין התראה למנהלת, פנייה למשתתפת או מסירת פרטים (סיפורים 3.2 עד 3.4), ואין חשיפת פרטי קשר.

**`POST /profiles/{profileId}/contact-requests`** (אנונימי) עם גוף `{ "name", "email", "reason" }`. מוגן ב-`Origin`, anti-forgery, Turnstile (`X-Turnstile-Token`) והגבלת קצב לפי IP, לפי כתובת המבקר המנורמלת ולפי פרופיל (משתנים בטבלה למעלה). ולידציה רצה לפני כל בדיקה של הפרופיל: `name` 1 עד 100 אחרי trim, `email` כבהגשת פרופיל, `reason` 1 עד 1000, טקסט פשוט בלי קישורים (`http://`, `https://`, `www.`, `javascript:`, `data:` ועוד, בלי תלות באותיות), בלי תווי בקרה (חוץ מ-`\n` ו-`\t`) ובלי תווים נסתרים; כשל: 400 עם שגיאה לשדה. התגובה לפרופיל `Active`, לפרופיל שהוסר, לפרופיל שאינו קיים ולמזהה פגום זהה בגוף, בסטטוס ובכותרות: `202 {"status":"verification-sent"}`. רק פרופיל `Active` יוצר `contact_requests` חדשה במצב `awaiting_email_verification` (תקפה 24 שעות) ושורת `email_outbox` (`contact-verification`, `request_id`, `deduplication_key = contact-verification:{requestId}`; בלי אסימון, כתובת, שם או טקסט). בקשה פתוחה קיימת לאותו (פרופיל, כתובת) אינה יוצרת נוספת (אינדקס ייחודי חלקי `ux_contact_requests_open_profile_visitor`) והתגובה זהה.

**המייל:** ה-worker קורא את כתובת המבקר ושמו מהבקשה בזמן השליחה, מנפיק אסימון `verify-contact-email` (256 ביט, hash בלבד, 24 שעות, חד-פעמי, `personal_tokens.request_id`; אסימון קודם לאותה בקשה מתבטל) ושולח טקסט פשוט בעברית עם `{PUBLIC_BASE_URL}/contact/verify#token=...`. המייל אינו מזכיר את המשתתפת ואינו כולל פרט קשר או את תוכן הבקשה. בקשה שהוסרה, פגה או אומתה לפני השליחה: השורה `dead` עם קטגוריה `request-not-awaiting`.

**`POST /contact-requests/verify`** (אנונימי, `{ "token" }`, `Origin` ו-anti-forgery; הגבלת IP `RATE_LIMIT_CONTACT_VERIFY_IP`, בלי Turnstile כי האסימון הוא הסוד). הקישור במייל הוא GET לעמוד לקוח; הצריכה היא POST בלבד (סורקי מייל). האסימון נצרך בעדכון מותנה אטומי ובאותה טרנזקציה, תחת נעילת הבקשה, `UPDATE contact_requests SET status='submitted', verified_at=now WHERE status='awaiting_email_verification' AND expires_at > now`. הצלחה: `200 {"status":"submitted"}` (בלי מידע על הפרופיל), והכתובת ננעלת (אין נקודה שמשנה `visitor_email`). כל כשל (פג, נוצל, בוטל, לא קיים, פגום, מטרה אחרת, בקשה שכבר אומתה, פרופיל שאינו `Active`) מחזיר אותו `401` עם `code: "link-invalid"`; אסימון תקף של בקשה שפגה או של פרופיל שאינו `Active` גם מעביר את הבקשה ל-`expired`. תגובות כוללות `Cache-Control: no-store` ו-`Referrer-Policy: no-referrer`.

**`POST /admin/contact-requests/{id}/send-to-participant`** (אדמין, `Origin` ו-anti-forgery): מעביר בקשה `submitted` ל-`sent_to_participant` בעדכון מותנה, כותב שורת Outbox `contact-decision-request` (מזהים בלבד) ושורת ביקורת. `200`; בקשה שאינה `submitted` או פרופיל שאינו `Active`: `409 request-not-submitted`; לא קיימת: `404`. ה-worker מנפיק אסימון `contact-decision` (14 יום) ושולח למייל המשתתפת.

**`POST /contact-decisions/preview`** (אנונימי, `{ "token" }`, `Origin` ו-anti-forgery; הגבלת IP `RATE_LIMIT_CONTACT_DECISION_IP`): מחזיר שם המבקר והסיבה בלבד ואינו צורך את האסימון. `401 link-invalid`, `410 link-expired` (והבקשה עוברת ל-`expired`), `409 already-decided`.

**`POST /contact-decisions/decide`** (אנונימי, `{ "token", "decision": "approved" | "declined" }`, כמו preview): צורך את האסימון ומעביר ל-`participant_approved` (נשמרים זמן וגרסת פרטי הקשר) או `participant_declined`; רק החלטה אחת מצליחה, האחרות `409`. תגובות עם `no-store` ו-`Referrer-Policy: no-referrer`.

**סדר נעילות:** פרופיל, אחר כך בקשה, אחר כך אסימונים, בכל הנתיבים (הגשה, אימות, הנפקה, הסרה), כדי למנוע deadlock.

**תפוגה ברקע:** בכל מחזור `EmailOutboxWorker` מעביר בקשות `awaiting_email_verification` ש-`expires_at` שלהן עבר ל-`expired` (עדכון מותנה) ומבטל את אסימוני האימות הפתוחים שלהן. כשה-worker כבוי (`EMAIL_WORKER=false`) אין סוויפר, אך אימות של בקשה שפגה עדיין נדחה ומעביר אותה ל-`expired`.

**מכונת המצבים (AD-3):** `awaiting_email_verification` ל-`submitted` או `expired`; `submitted` ל-`sent_to_participant` (3.3) או `expired`; `sent_to_participant` ל-`participant_approved`, `participant_declined` או `expired`; `participant_approved` ל-`delivered` (3.4). מצבים פתוחים: כל מצב מלבד `expired`, `participant_declined` ו-`delivered`.

**פרטיות:** בקשה שלא אומתה אינה נחשפת בשום נקודה; אין קריאה מ-`participant_private`; אין בתגובות, ב-Outbox, ביומנים או בהודעות שגיאה כתובת מבקר, שם, סיבה או פרט קשר.

### הבהרות ומגבלות מוכרות (סיפור 3.1)

- **עמוד הלקוח טרם קיים:** הקישור במייל מוביל ל-`{PUBLIC_BASE_URL}/contact/verify#token=...`, עמוד לקוח שעדיין לא נבנה (סיפור לקוח). הוא אמור לשלוח את האסימון ב-POST ל-`/contact-requests/verify`.
- **קודי תגובה בשתי הנקודות:** אפשריים 403 (`Origin`, anti-forgery, Turnstile בהגשה), 429 (קצב, עם `Retry-After`), 415 (גוף שאינו JSON) ו-413 (גוף גדול מדי בהגשה). באימות, כל כשל אסימון, כולל גוף גדול מדי או JSON לא קריא, הוא 401 `link-invalid`; רק גוף שאינו JSON הוא 415. בהגשה, JSON פגום הוא 400.
- **מכסות שצד שלישי יכול לנצל:** המכסות לפי פרופיל ולפי כתובת סופרות כל ניסיון, גם של מי שאינו בעל הכתובת, ולכן אפשר למצות אותן בשם אחר. כתובות עם plus-addressing (`a+x@...`) אינן מקוננות לכתובת אחת.
- **תזמון:** התגובה אחידה בגוף, בסטטוס ובכותרות, אך הזמן עשוי לרמוז על מצב הפרופיל (סיכון שיורי ידוע).
- **שמירת נתונים:** שם המבקר, כתובתו וסיבת הפנייה נשמרים ללא הגבלת זמן; אין עדיין מדיניות שמירה ומחיקה.
- **מסנן הקישורים בסיבה (ובשם) הוא best-effort:** נבדק אחרי NFKC עבור `http(s)://`, `www.` וסכמות כמו `javascript:` ו-`data:` (כשהן צמודות לתו הבא); שמות דומיין חשופים (`example.com`) אינם נדחים, וגם לא טקסט רגיל כמו `data: 5`.
- **פרופיל שאינו פעיל בזמן השליחה:** שורת ה-Outbox נהפכת `dead` עם `profile-not-active` ללא מייל ואסימון. כל שורת `contact-verification` שנהפכת `dead` (כשל קבוע, `request-not-awaiting`, `profile-not-active`, `too-many-claims`) סוגרת את הבקשה ל-`expired`, כדי שהמבקר יוכל לנסות שוב; בקשה שלא אומתה ופגה אינה חוסמת הגשה חדשה גם לפני הסוויפר.
- **סוויפר התפוגה:** רץ ב-worker לכל היותר פעם בדקה לכל מופע (`CONTACT_SWEEP_INTERVAL_SECONDS`, ברירת מחדל 60), עד 500 בקשות בריצה, עם `FOR UPDATE SKIP LOCKED` (לעולם לא ממתין לנעילת בקשה ואינו נועל פרופיל). סדר הנעילות בנתיבי ההגשה, האימות, הנפקת האסימון וההסרה: פרופיל, בקשה, אסימונים; הסוויפר ו-dead של Outbox נוגעים רק בשורת בקשה אחת או יותר ובאסימוניה, בלי נעילת פרופיל.
- **סטטוס:** על `contact_requests.status` יש אילוץ CHECK (`ck_contact_requests_status`).

## התראה למנהלת ותור הבקשות (סיפור 3.2)

**התראה:** בעדכון המותנה של `POST /contact-requests/verify` שמעביר `awaiting_email_verification` ל-`submitted`, ובאותה טרנזקציה, נכתבת שורת `email_outbox` (`contact-request-notification`, `request_id`, `deduplication_key = contact-notification:{requestId}`, `ON CONFLICT DO NOTHING`, בלי שם, כתובת, סיבה או פרט קשר). אם הכתיבה נכשלת הטרנזקציה כולה מתבטלת: הבקשה נשארת `awaiting_email_verification` והאסימון לא נוצל. אימות חוזר או מרוץ אינם יוצרים שורה נוספת.

**המייל:** ה-worker שולח טקסט פשוט (`text/plain; charset=utf-8`) בעברית עם נושא קבוע, זמן האימות (UTC), שם המחזור, שם הפרופיל המבוקש (מהגרסה המאושרת, ואם אין, מהגרסה האחרונה) וקישור `{PUBLIC_BASE_URL}/admin/contact-requests`. אין בו שם המבקר, כתובתו, הסיבה או פרט קשר של משתתפת. מייל אחד לכל כתובת; השורה מסומנת `sent` רק כששלחו כולן. כשל בכתובת אחת מנסה שוב את השורה כולה (at-least-once: כתובת שכבר קיבלה עלולה לקבל שוב). ניסיונות, backoff, 429 ו-`dead` כבשאר המיילים. בקשה ששינתה מצב אחרי האימות (למשל `expired` בהסרת פרופיל) עדיין מקבלת התראה כל עוד `verified_at` קיים; בלי `verified_at`: `dead` עם `request-not-verified`; בלי כתובות מנהלת בתצורה: `dead` עם `no-recipient`. שורה `dead` מופיעה ב-`GET /admin/email-outbox` והבקשה נשארת בתור.

**`ADMIN_NOTIFICATION_EMAIL`** (שרת): כתובת אחת או עד 5 מופרדות בפסיק; כל אחת תקינה ומנורמלת (trim, אותיות קטנות, כפילויות מתאחדות). מנותחת באתחול בכל סביבה כשהמשתנה מוגדר (תצורה פגומה מכשילה את האתחול); חובה מחוץ ל-Development; ב-Development ברירת המחדל `admin@example.test`. אין כתובת בקוד או בריפו (ב-Render: `sync: false`).

**`GET /admin/contact-requests`** (`AdminPolicy`, `Cache-Control: no-store`): אך ורק בקשות שאומתו (`verified_at` לא ריק); לעולם לא `awaiting_email_verification` ולא בקשה שפגה בלי אימות. פרמטרים: `status` (`open` ברירת מחדל = `submitted`, `sent_to_participant`, `participant_approved`; או `submitted`, `sent_to_participant`, `participant_approved`, `participant_declined`, `delivered`, `expired`, `all`; ערך אחר: 400), `limit` (1 עד 100, ברירת מחדל 50, מעל 100 נחתך; קטן מ-1: 400), `offset` (0 ומעלה; שלילי: 400). סדר: הישנה ביותר שאומתה קודם (`verified_at`, אחר כך מזהה). תגובה `{ items, total }`.

פריט: `requestId`, `visitorName`, `visitorEmail` (הכתובת המאומתת), `reason`, `profileId`, `profileName`, `productName` (מהגרסה המאושרת, ואם אין, מהגרסה האחרונה), `cohortName`, `profileVisibility`, `createdAt`, `verifiedAt`, `status`, ושני אישורים נפרדים: `participantDecision` (`approved`, `declined` או `null`), `participantDecidedAt`, ו-`managerApprovedAt` (או `null`). אין פרט קשר של משתתפת (לא נקרא `participant_private`).

**`GET /admin/contact-requests/{requestId}`** (GUID בצורה `D`): אותו פריט; 404 אחיד לא קיים, פגום או שלא אומת. אנונימי: 401; מחובר שאינו אדמין: 403. אין עדיין פעולות כתיבה על בקשות.

**עמודות חדשות ב-`contact_requests`** (מיגרציה `ContactRequestQueue`): `participant_decision` (CHECK `approved`/`declined`, `ck_contact_requests_participant_decision`), `participant_decided_at`, `manager_approved_at`; כולן nullable ויתמלאו בסיפורים 3.3 ו-3.4. אינדקסים `ix_contact_requests_verified` ו-`ix_contact_requests_queue_open` (`verified_at`, `id`) לתור.

### עדכונים לסיפור 3.2 (אחרי סקירה)

- **`notificationStatus`:** כל פריט בתור כולל `notificationStatus`: `none` (אין שורת התראה), `pending`, `sent` או `dead`, לפי שורת ה-`email_outbox` של ההתראה (`contact-notification:{requestId}`). כך התראה שנכשלה סופית (`dead`) נראית ליד הבקשה שלה; פירוט הכשל ב-`GET /admin/email-outbox`.
- **קישור במייל:** המייל מפנה ל-`{PUBLIC_BASE_URL}/admin/contact-requests`, עמוד לקוח שעדיין לא קיים (סיפור לקוח); עד אז התור זמין רק ב-API.
- **ברירת המחדל `open`** אינה כוללת בכוונה את `participant_declined` (ולא `delivered` ו-`expired`): יש להשתמש ב-`status=participant_declined` או ב-`status=all`.
- **כמה כתובות מנהלת:** המשלוח הוא at-least-once. ניסיון חוזר של השורה עלול לשלוח שוב לכתובות שכבר קיבלו; כפילות נמנעת רק אצל ספקים שמכבדים מפתח אידמפוטנטיות (המפתח יציב לכל זוג שורה וכתובת: מזהה השורה ו-hash של הכתובת). איחוד תוצאות: 429 עוצר את הלולאה ודוחה את השורה לפי `Retry-After` בלי לספור ניסיון; חריגה או כשל זמני בכתובת אחת מנסים שוב את כל השורה; כשל קבוע בכל הכתובות: `dead`; כשל קבוע בחלק מהכתובות כשלפחות אחת נמסרה: השורה `sent` (ניסיון חוזר רק יחזור על מיילים לכתובות התקינות).
- **תצורה (Rollout ב-Render):** `ADMIN_NOTIFICATION_EMAIL` חובה מחוץ ל-Development בכל מופע שמריץ את ה-worker (`EMAIL_WORKER` אינו `false`); מופע web בלבד (`EMAIL_WORKER=false`) עולה גם בלעדיו. כשהמשתנה מוגדר הוא תמיד מנותח. מקטעים ריקים (פסיק בסוף) מתעלמים; כפילויות מתאחדות לפני בדיקת המקסימום (5); שגיאה מציינת את מיקום הרשומה ולא את ערכה.
- **שמות ריקים:** כששם המחזור או הפרופיל חסר, המייל והתור מציגים `(ללא שם)`.
- **שאילתות שגויות בתור:** `limit`/`offset` שאינם מספרים שלמים, ו-`status` חוזר, מחזירים אותה תגובת 400 בצורת `problem+json` עם `errors` לשדה.
- **אילוצי DB נוספים:** `ck_contact_requests_decision_pair` (`participant_decided_at` מוגדר בדיוק כש-`participant_decision` מוגדר) ו-`ck_contact_requests_manager_approval` (`manager_approved_at` דורש `participant_decision = 'approved'`), במיגרציה `ContactRequestDecisionChecks`.

## החלטת משתתפת (סיפור 3.3)

מסלול ההחלטה מתועד בסעיף 3.1 (`send-to-participant`, `/contact-decisions/preview`, `/contact-decisions/decide`): הבקשה עוברת `submitted` ל-`sent_to_participant` ל-`participant_approved` או `participant_declined`; ההסכמה שומרת `participant_decided_at` ו-`participant_contact_version`. סירוב או היעדר מענה אינם מודיעים למבקר.

## אישור ושליחת פרטי הקשר (סיפור 3.4)

**`POST /admin/contact-requests/{id}/approve-delivery`** (אדמין, `Origin` ו-anti-forgery, `no-store`): בטרנזקציה אחת ועם נעילת שורת הבקשה, עדכון מותנה `WHERE status = participant_approved AND manager_approved_at IS NULL` קובע `manager_approved_at`, נכתבת שורת `email_outbox` (`contact-delivery`, `deduplication_key = contact-delivery:{requestId}`, מזהים בלבד) ושורת `AuditLog` (`approve-delivery`). `200`; בקשה שאינה `participant_approved` (סירוב, ללא תשובה, `delivered`, כבר אושרה ושורתה לא `dead`): `409 request-not-approved`; לא קיימת או מזהה לא תקין: `404`; אנונימי: `401`, לא אדמין: `403`.

**ניסיון ידני:** אישור חוזר על בקשה ששורת המסירה שלה `dead` מחזיר את אותה שורה ל-`pending` (מונים מאופסים, אותו מפתח, בלי שורה כפולה, `manager_approved_at` נשמר) ורושם שורת ביקורת. בכל מצב אחר: `409`.

**השליחה (worker):** בודק מחדש ברגע השליחה, ובכשל השורה `dead` (סופי) בלי שליחה, עם קטגוריה גלויה בתור (`deliveryErrorCategory`): `profile-not-active` (הפרופיל לא `Active`), `delivery-not-approved` (הבקשה לא `participant_approved` או בלי `manager_approved_at`), `consent-expired` (`participant_decided_at` ישן מ-14 יום), `contact-changed` (`participant_contact_version` שונה מ-`contact_version` הנוכחי), `no-recipient` (חסר מייל או נייד של המשתתפת או כתובת המבקר). אם עברו כל הבדיקות, המייל והנייד נקראים מ-`participant_private` ונשלחים בטקסט פשוט רק ל-`visitor_email` המאומת, עם שם המשתתפת. מפתח האידמפוטנטיות הוא מזהה השורה (יציב בין ניסיונות). `delivered` נקבע באותה טרנזקציה שבה השורה מסומנת `sent` ורק אחרי שליחה מוצלחת; כשל זמני של הספק מנסה שוב (backoff) בלי `delivered`. שורה שנתפסה שוב אחרי שהבקשה כבר `delivered` מסומנת `sent` בלי מייל נוסף. בכל השרשרת (שורת Outbox, יומני ביקורת ותגובות API) אין מייל או נייד של המשתתפת.

**התור:** כל פריט ב-`GET /admin/contact-requests` כולל בסוף `deliveryStatus` (`none`, `pending`, `sent`, `dead`) ו-`deliveryErrorCategory` (או `null`).

**מגבלות:** המשלוח at-least-once: קריסה בין השליחה לסימון עלולה לשלוח שוב אחרי פקיעת ה-lease (כפילות נמנעת רק אצל ספק שמכבד את מפתח האידמפוטנטיות). אין מיגרציה חדשה.
