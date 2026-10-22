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
