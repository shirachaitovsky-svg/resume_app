# ביקורת גרסאות ומציאות: ARCHITECTURE-SPINE (תאריך בדיקה: 2026-10-21)

שיטה: חיפוש רשת וקריאת דפי תיעוד רשמיים. לכל ממצא מסומן אם אומת ממקור רשמי (מאומת), ממקור משני (חלקי), או שלא נמצא מקור (לא אומת). כל מה שמסומן "לא אומת" הוא ידע כללי שלי ולא הוכחה.

## פסק דין

המחסנית (Angular 22, .NET 10, EF Core/Npgsql 10, Render, Vercel, Resend, Turnstile) קיימת, עדכנית ומתאימה. אין טעות גרסה חוסמת. אבל יש **ארבע הנחות התנהגות שאינן מאומתות או שיש להן חור**, ובעיקר AD-9 (עוגיות דרך rewrite של Vercel), שהיא הנחת-יסוד של כל אימות האדמין ולא מתועדת בשום מקום שמצאתי. המלצה: לפני נעילת AD-9, לבצע ספייק של שעה (ראו ממצא 1).

## ממצאים עיקריים (לפי חומרה)

### 1. AD-9: שימור עוגיות ו-headers דרך Vercel rewrite לדומיין חיצוני. לא אומת
- מאומת: Vercel מתעדת rewrite לאתר חיצוני ב-`vercel.json` כ"reverse proxy". הדוגמה הרשמית: `{ "source": "/api/:path*", "destination": "https://api.example.com/:path*" }`. הכתובת בדפדפן לא משתנה (https://vercel.com/docs/routing/rewrites, עודכן 2026-08-11).
- מאומת: Vercel מעבירה לשרת המקור `x-forwarded-for`, `x-real-ip`, `x-forwarded-host`, `x-forwarded-proto`.
- **לא נמצא בתיעוד אף משפט** על העברת `Set-Cookie` בתגובה או `Cookie` בבקשה. ההנחה סבירה (proxy רגיל), אך לא מובטחת.
- חור חדש שמצאתי: מאז 2026-04-06 פרויקטים חדשים ב-Vercel **מכבדים כברירת מחדל `Cache-Control` של שרת המקור** ב-rewrite חיצוני ומטמינים ב-CDN. תגובה עם `Set-Cookie` או תגובת אדמין שחוזרת בלי `no-store` עלולה להיות מוגשת מהמטמון. לעומת זאת, תמונות פרופיל "עם כותרות מטמון" (AD-8) יזכו להטמנה ב-CDN, וזה רצוי.
- המלצות: (א) להוסיף ל-AD-9 כלל: כל תגובה מאומתת וכל תגובה עם `Set-Cookie` נשלחת עם `Cache-Control: no-store`; ניתן גם להגדיר `x-vercel-enable-rewrite-caching: 0` על נתיבי auth. (ב) לבדוק בפועל login מקצה לקצה דרך Vercel (preview) לפני נעילה. (ג) חלופה אם נכשל: דומיין משנה של אותו דומיין אתר (`api.example.org`) עם עוגיה על הדומיין האב; עדיין same-site.
- Vercel מתעדת גם דפוס לנעילת המקור: header סודי `x-origin-secret` שמוגדר ב-vercel.json ונבדק בשרת. **מומלץ לאמץ**, אחרת אפשר לפנות ישירות ל-Render ולעקוף את הגבלת הקצב לפי IP ואת כל מה שמניחים ש-Vercel הוא הכניסה היחידה (AD-12).

### 2. AD-9/AD-5: דגל Secure ו-IP אמיתי מאחורי שני proxy. חור בהגדרה
- ASP.NET Core Identity/Cookie: ב-.NET 10 נקודות קצה של API מחזירות 401/403 ולא מפנות לדף login (מאומת, Microsoft Learn). טוב ל-SPA. ברירות המחדל של Identity (HttpOnly, SameSite=Lax, ExpireTimeSpan ארוך עם sliding) מוכרות לי מהידע הכללי ולא אומתו מחדש בסבב הזה.
- Render מסיימת TLS והאפליקציה רואה HTTP. עם ברירת המחדל (`SecurePolicy = SameAsRequest`) העוגיה **לא תסומן Secure**. נדרש `Cookie.SecurePolicy = Always` מפורש, וגם `UseForwardedHeaders` (עם `ForwardedHeaders.XForwardedFor | XForwardedProto`, ובטיחות ב-`KnownNetworks`/`ForwardLimit`) כדי שהגבלת קצב לפי IP וגם הנתון ש-Turnstile מקבל (`remoteip`) יהיו כתובת המבקר ולא כתובת Vercel/Render. מומלץ להוסיף שני משפטים אלה ל-AD-9 או ל-AD-12.
- SameSite=Lax מגן חלקית מ-CSRF אך לא לגמרי (למשל GET עם תופעות לוואי). מומלץ לציין שאין שינוי מצב ב-GET ושקיים אימות Origin/antiforgery לנקודות אדמין.

### 3. Render: DB וגיבויים. מאומת, ויש עדכון לטקסט
- מאומת (docs.render.com): Postgres חינמי = 1GB, **פג 30 יום אחרי היצירה, ואחרי חסד של 14 יום נמחק**. אין גיבויים ואין PITR. הטקסט בספין "נרדם ומתמחק" לא מדויק: ה-**web service** החינמי נרדם אחרי 15 דקות (התעוררות כדקה), וה-**DB** פג ונמחק.
- מאומת: בתשלום יש PITR אוטומטי, **3 ימים ב-Hobby ו-7 ימים ב-Professional+**, וכן גיבוי לוגי להורדה ל-7 ימים. לכן הסעיף Deferred "גיבוי: מחכה להחלטה, הצעה גיבוי בתשלום" ניתן להחלפה בנתון ממשי: PITR כלול בכל תכנית בתשלום, ו-3 ימים הם כנראה מינימום סביר לקורס. מחיר תכניות DB ספציפיות לא נטען (עמוד התמחור לא החזיר מספרים) ולא אומת.
- Render תומכת ב-.NET רק דרך Docker (אין runtime מקורי) וזה תואם את הספין. דרישות: האזנה ל-`0.0.0.0` ול-`PORT` (ברירת מחדל 10000). ב-.NET יש להגדיר `ASPNETCORE_URLS=http://0.0.0.0:$PORT` או להשתמש ב-`ASPNETCORE_HTTP_PORTS`. מומלץ לרשום בספין.
- גרסת Postgres ב-Render: הספין עצמו מציין "לאשר". לא אומת. ראו ממצא 5.
- מיגרציות אוטומטיות בפריסה: סיכון ריצה כפולה אם יותר ממופע אחד. מומלץ lock (advisory lock) או שלב נפרד.

### 4. AD-4/AD-8: SKIP LOCKED. הרעיון נכון, ההרחבה ב-AD-8 חלשה
- מאומת: `FOR UPDATE SKIP LOCKED` קיים מאז PostgreSQL 9.5 ומיועד בדיוק לטבלאות דמויות-תור (תיעוד PostgreSQL אומר שהוא "לא מתאים לשימוש כללי" אלא לתורים). EF Core אינו מספק API מובנה; צריך SQL גולמי (`FromSql`/`ExecuteSql`).
- הנעילה חיה רק כל עוד הטרנזקציה. לכן ב-AD-4: אם שולחים מייל בתוך הטרנזקציה, קריסה אחרי השליחה וטרם ה-commit תגרום לשליחה כפולה (at-least-once). מומלץ לציין: claim קצר (עדכון `locked_until`/`status=sending`), שליחה מחוץ לטרנזקציה, `Idempotency-Key` של Resend, וטיפול ב-claim שנתקע.
- AD-8 אומר "כל עבודת רקע ננעלת ב-SKIP LOCKED". זה לא מתאים לעבודה ארוכה או שאינה שורה בטבלה (למשל פקיעת בקשות אחרי 14 יום). להשתמש ב-advisory lock או ב-claim של שורה.

### 5. GUID v7 (AD-11): נכון, עם הבהרה
- מאומת: `Guid.CreateVersion7()` קיים מ-.NET 9 ונשאר ב-.NET 10 (Microsoft Learn). EF Core Npgsql 9 ו-10 מייצרים UUIDv7 בצד הלקוח כברירת מחדל למפתחות Guid (מאומת, npgsql.org release notes 10.0).
- מאומת: ייצור **בצד ה-DB** (`uuidv7()`) דורש PostgreSQL 18 ו-`SetPostgresVersion(18,0)`. אם Render מספקת PG 16/17, צריך להישאר בייצור בצד השרת. זה מחזק את הצורך לסגור את "גרסת Postgres לאשר" בספין.
- חלקי: מקורות משניים טוענים שדוגמת ה-byte order ב-.NET שונה מ-RFC ושזה פוגע באינדקסים. הטענה מקורה ב-SQL Server ובבלוג יחיד; לא הוכחה ל-Npgsql. מדובר בעומס נמוך (קורס, עשרות פרופילים), לכן לא קריטי. אין צורך בפעולה פרט להערה.
- הערה: GUID v7 מכיל חותמת זמן ולכן אינו "אטום" לחלוטין (חושף זמן יצירה). זה בסדר לשימוש ב-AD-11 (אנטי-ניחוש), אך לא להשתמש בו כאסימון (האסימון ב-AD-6 הוא 256 ביט נפרד, נכון).

### 6. Angular
- Angular 22 יצא ב-2026-06-03 והוא הגרסה הפעילה (מקורות משניים: versionlog, herodevs, endoflife). 22.0.6 ו-22.1.0 מוזכרות; תאריכי patch לא עקביים בין המקורות. הערה: ל-Angular 22 מתוכננת Active support עד 2027-06 ו-LTS עד 2028-06, אך מקור יחיד טוען שהמדיניות השתנתה ל-12+12 בניגוד לדף הרשמי. הספין לא תלוי בכך.
- נכון להשאיר "לאמת עם `ng version`". הגרסה הבאה (v23) צפויה כ-2026-11 לפי מקור משני (לא מאומת), כלומר בתוך חודש מהיום. לבחור 22 הגיוני: LTS ארוך.
- "Node LTS הנוכחי" כללי מדי. כדאי לרשום את טווח Node שנתמך ב-Angular 22 (לא אומת בסבב זה).

## אימות מחסנית ללא ממצא מהותי

| נושא | תוצאה | מקור |
| --- | --- | --- |
| .NET 10 LTS, תמיכה עד נובמבר 2028 | מאומת. תאריך מדויק 2028-11-14 (מקורות משניים אומרים 11-10). הספין "2028-11" תקין | dotnet.microsoft.com support policy |
| EF Core 10 + Npgsql 10 על .NET 10 | מאומת: `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.x ל-net10.0. 10.0.0 יצא 2026-03-18, נראה 10.0.3 ביוני 2026. "10.x" נכון; שורת ה-1 כ"10.1" לא אומתה | nuget.org |
| Cloudflare Turnstile בצד שרת | מאומת: POST ל-`https://challenges.cloudflare.com/turnstile/v0/siteverify` עם `secret` ו-`response` (ו-`remoteip` אופציונלי). אסימון חד-פעמי, תקף 300 שניות (`timeout-or-duplicate`). מומלץ גם לבדוק `hostname` ו-`action` בתגובה | developers.cloudflare.com/turnstile |
| Resend מחיר ודומיין | מאומת מול `resend.com/pricing.md`: Free 0$, 3,000 מיילים/חודש, **100/יום**, 3 דומיינים; Pro 20$ ל-50,000. בלי דומיין מאומת אפשר לשלוח (`onboarding@resend.dev`) רק לכתובת של בעל החשבון (מקורות משניים ותיעוד אינטגרציה של Resend). לכן עלייה לאוויר חסומה בלי דומיין, כפי שהספין כותב. 100 ביום מספיק לנפח צפוי, אך סיכון בהצפה: ה-Outbox צריך להתחשב במגבלה (מצב `429` וניסיון חוזר) | resend.com/pricing |
| Render ו-Docker | מאומת, ראו ממצא 3 | docs.render.com |

## פעולות מומלצות (לפי סדר)

1. ספייק של שעה: login של אדמין דרך Vercel preview אל Render, אימות `Set-Cookie` (HttpOnly, Secure, SameSite=Lax) ושליחת `Cookie` חזרה, בדיקת `x-forwarded-*` ובדיקת שאין הטמנת CDN. עד אז לסמן AD-9 כ"מאומת חלקית".
2. להוסיף ל-AD-9: `Cache-Control: no-store` לתגובות מאומתות, `x-origin-secret`, `SecurePolicy=Always`, `UseForwardedHeaders`.
3. לסגור גרסת Postgres ב-Render ולהחליט אם ייצור GUID v7 נשאר בשרת (ברירת מחדל, בטוח).
4. לדייק AD-4: claim קצר, שליחה מחוץ לטרנזקציה, idempotency, התחשבות במגבלת 100/יום.
5. לעדכן Deferred (גיבוי) ו"נרדם ומתמחק" לפי הנתונים של Render לעיל.

## מה לא נבדק
- מחירי תכניות Render ו-Vercel הספציפיים (Hobby מול Pro; תנאי שימוש מסחרי ב-Hobby של Vercel), גרסאות Postgres הזמינות ב-Render, תאימות Node ל-Angular 22, ברירות מחדל מדויקות של Identity cookie (ExpireTimeSpan וכד'). אלה דורשים בדיקה ידנית.
