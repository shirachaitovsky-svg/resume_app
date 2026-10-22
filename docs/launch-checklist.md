# רשימת תנאים לעלייה לאוויר

## הקשחה בייצור

התנאים האלה תלויים בפריסה בפועל (Vercel ו-Render), ולכן לא ממומשים ולא נבדקים בהרצה המקומית. אם תנאי אינו מתקיים, האתר לא מפורסם.

- [ ] בדיקת `Cache-Control: no-store` ועוגיות `__Host-` מקצה לקצה בכתובת Vercel בייצור (AR3, ספייק עוגיות): ל-`__Host-resume-participant`, `__Host-resume-admin`, `__Host-resume-oidc` ו-`__Host-resume-csrf` יש `Secure`, `Path=/` וללא `Domain`; ולכל תגובה של `/me`, `/admin` ו-`/auth` יש `no-store`, כולל תגובות שגיאה. מקומית הבדיקות האלה רצות מול השרת (`ProductionHardeningTests`).
- [ ] הפעלת תפקידי `migrator` ו-`app` ב-Render: הרצת `server/db/roles.sql` (ראו README, "תפקידי בסיס הנתונים") לפני ההפעלה הראשונה ושוב אחרי המיגרציה הראשונה, והגדרת שני connection strings: `DATABASE_URL_MIGRATOR` (migrator) ו-`DATABASE_URL` (app).
- [ ] שמירה שה-`app` לא מקבל הרשאות רחבות אחרי מיגרציות חדשות: אחרי כל מיגרציה שמוסיפה טבלה מריצים שוב את `roles.sql`, ובודקים ש-`app` אינו בעלים של אובייקטים, ושאין לו `UPDATE`, `DELETE` או `TRUNCATE` על `audit_log` וגם לא `CREATE` על הסכמה.
- [ ] ב-`render.yaml` הערך `DATABASE_URL` נקשר לבעל בסיס הנתונים, ולכן אחרי הפריסה הראשונה חובה להגדיר ידנית ב-Render (שרת האפליקציה): `DATABASE_URL` = כתובת התפקיד `app`, ו-`DATABASE_URL_MIGRATOR` = כתובת התפקיד `migrator`. בלי זה האפליקציה רצה עם הבעלים ובלי שום הגבלה. אחראי: מי שמפעילה את הפריסה. הוכחה: צילום הגדרות הסביבה (בלי הסיסמאות) ותוצאת השאילתות שלהלן.

  ```sql
  -- מריצים כמשתמש-על או כבעלים; כל השורות צריכות להחזיר false, והאחרונה true
  SELECT has_table_privilege('app', 'audit_log', 'UPDATE')   AS app_update_audit,   -- false
         has_table_privilege('app', 'audit_log', 'DELETE')   AS app_delete_audit,   -- false
         has_table_privilege('app', 'audit_log', 'TRUNCATE') AS app_truncate_audit, -- false
         has_schema_privilege('app', 'public', 'CREATE')     AS app_create_schema,  -- false
         has_table_privilege('app', 'audit_log', 'INSERT')   AS app_insert_audit;   -- true
  SELECT count(*) AS objects_owned_by_app FROM pg_class c JOIN pg_roles r ON r.oid = c.relowner WHERE r.rolname = 'app'; -- 0
  ```

  שחזור מהרשאה שגויה: מריצים שוב `server/db/roles.sql` (ראו README) ומריצים מחדש את השאילתות.

## ניטור מיילים

- [ ] נקודת liveness ללא סוד ל-Render לא נבנתה: `GET /healthz` דורש את `x-origin-secret`, ו-`GET /healthz/details` דורש אדמין מחובר, ולכן אין נקודה ש-Render יכולה לבדוק בלי סוד. תנאי עלייה: להחליט לפני הפריסה איך מנטרים את השירות (למשל ניטור חיצוני עם הסוד ב-`/healthz`, או נקודת liveness ייעודית ללא נתונים), ולרשום את ההחלטה כאן. אחראי: מי שמפעילה את הפריסה.
- [ ] הגדרת `ADMIN_NOTIFICATION_EMAIL` בשרת ה-worker ובדיקה שהתראת "תקלה בשליחת מיילים" מגיעה לתיבה (דורש דומיין מאומת, ראו הסעיף על SPF/DKIM). `OUTBOX_STUCK_AFTER_MINUTES` ברירת מחדל 15.
- [ ] מי שנכנסת ל-`/admin/email-outbox` ול-`/admin/audit-log` בייצור רואה את הרשימות, ו"ניסיון חוזר" על שורה `dead` מחזיר אותה לתור (נרשם ביומן הביקורת).


## גיבוי ושחזור

מקומית הגיבוי והשחזור נבדקים (`BackupRestoreTests` וסקריפט `scripts/backup-restore.ps1`, ראו `docs/backup-restore.md`). התנאים הבאים תלויים בפריסה בפועל. אם תנאי אינו מתקיים, האתר לא מפורסם.

- [ ] Postgres ושרת האפליקציה ב-Render בתוכנית בתשלום (תוכנית חינמית אינה מספקת גיבויים ושחזור).
- [ ] שחזור לנקודת זמן (point-in-time recovery) מופעל ב-Postgres של Render, והאזור הוא אירופה (למשל Frankfurt), בהתאם לאישור האחסון של הארגון.
- [ ] תרגיל שחזור אחד בסביבת הייצור: שחזור לנקודת זמן למופע חדש, עלייה של השרת מולו ובדיקה שהגלריה, תמונה וגרסאות זהות. מתעדים כאן תאריך, מי ביצעה, נקודת הזמן ששוחזרה, משך הפעולה (RTO בפועל) ותוצאה, ואז מוחקים את המופע הזמני.
- [ ] החלטה על מדיניות שמירה ומחיקה לגיבויים (כמה זמן נשמרים וכיצד נמחקים, כולל בקשת מחיקה של משתתפת), ורישום ההחלטה כאן. אחראי: מנהלת הקורס.
