---
title: 'סיפור 4.3: גיבוי ושחזור (מקומי)'
type: 'feature'
created: '2026-10-23'
status: 'done'
baseline_revision: 'c7ba41bd32327117662ebf5950139352e94904f0'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-4-context.md'
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** נתוני המשתתפות (פרופילים, תמונות, גרסאות, פרטי קשר) שמורים רק במסד נתונים אחד, ואין גיבוי, אין דרך מתועדת לשחזר, ואין הוכחה שגיבוי באמת ניתן לשחזור ושהאפליקציה עולה מולו.

**Approach:** סקריפט גיבוי ושחזור מקומי (pg_dump ו-pg_restore בקונטיינר), בדיקה אוטומטית שמוכיחה שחזור מלא ועלייה של השרת מול המסד המשוחזר בלי לגעת במסד הפיתוח, מסמך תפעול עם RPO ו-RTO, והדרישות של סביבת הייצור כתנאי עלייה.

## Boundaries & Constraints

**Always:**
- `scripts/backup-restore.ps1` (Windows ו-docker; אם נוח גם `scripts/backup-restore.sh` עם אותה התנהגות): פעולות `backup` ו-`restore` ו-`verify` (גיבוי ושחזור יחד). פרמטרים עם ברירות מחדל: קונטיינר `resume-dev-pg`, מסד מקור `resume`, משתמש `postgres`, סיסמה `dev`, תיקיית גיבויים `backups/` (ב-`.gitignore`), שם קובץ עם חותמת זמן, ומסד היעד `resume_restore_check`. `pg_dump -Fc` בתוך הקונטיינר והעתקה החוצה (או הזרמה), ו-`pg_restore` למסד יעד שנוצר מחדש (`DROP DATABASE IF EXISTS` ואז `CREATE`) בכל ריצת `restore`.
- הגנות: סירוב לשחזר אל מסד בשם זהה למסד המקור, אל מסד ששמו `resume`, `postgres`, `template0`, `template1` או `resume_e2e`, או אל מסד שיש לו חיבורים פעילים מהאפליקציה (בדיקת `pg_stat_activity`), אלא עם דגל `-Force` מפורש; הודעות שגיאה ברורות בעברית, וקוד יציאה שאינו אפס בכל כשל. הסקריפט אינו מדפיס את הסיסמה, ומקבל אותה גם ממשתנה סביבה.
- בדיקת שרת אוטומטית (`server/Tests/BackupRestoreTests.cs`, Testcontainers דרך `PostgresFixture`, בלי לגעת במסד הפיתוח של המשתמשת): זורעת נתונים מייצגים (מחזור, פרופיל מאושר עם גרסה ותמונה, גרסה ממתינה, `ParticipantPrivate`, בקשת פרטי קשר ושורת `AuditLog`), מבצעת `pg_dump -Fc` בתוך הקונטיינר ו-`pg_restore` למסד חדש (באותו תהליך ובאותה פקודת פרמטרים של הסקריפט, או באמצעות הרצת הסקריפט עצמו אם אפשרי בקונטיינר הבדיקה, בעדיפות להריץ את אותה לוגיקה ולא עותק), מרימה את השרת (`AuthApiFactory`) מול המסד המשוחזר עם `MIGRATE_ON_STARTUP=true` ומוכיחה: המיגרציות מזהות מסד מעודכן ואינן משנות אותו (היסטוריית מיגרציות זהה), `GET /profiles` ו-`GET /profiles/{id}` מחזירים את אותם נתונים, תמונת הפרופיל המאושר חוזרת בייטים זהים, הגרסה הממתינה והנתונים הפרטיים והבקשה ושורת הביקורת קיימים (בבדיקות DB ישירות), ושאין כפילויות.
- מסמך `docs/backup-restore.md` בעברית: איך מגבים ומשחזרים (פקודות), מי מבצע (מנהלת הקורס ובונת האתר), RPO ו-RTO (בפיתוח לפי תדירות הגיבוי שנבחרת; ביעד ייצור מה נדרש מ-Render), אזהרות (אין לשחזר על מסד חי, הגיבוי כולל נתונים אישיים ולכן נשמר מוצפן ובגישה מצומצמת, ומחיקת גיבויים ישנים לפי מדיניות שמירה שטרם נקבעה), ואיך מריצים את בדיקת השחזור.
- `docs/launch-checklist.md`: סעיף "גיבוי ושחזור" עם: Postgres ושרת בתשלום ב-Render, שחזור לנקודת זמן מופעל, אזור אירופה, תרגיל שחזור אחד בסביבת הייצור והתיעוד שלו, והחלטה על מדיניות שמירה ומחיקה.
- `README.md`: הפניה קצרה למסמך ולסקריפט. ריצה אחת בלבד של בדיקות שרת מלאות, ו-`TestResults` ו-`backups/` לא נכנסים לקומיט.

**Never:**
- לא נוגעים במסד הפיתוח `resume` בבדיקות אוטומטיות. לא מוחקים או משנים נתוני משתמשת. לא משנים קוד מוצר אלא אם נדרש לבדיקה (ואז בצורה מינימלית ומתועדת).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| גיבוי | `backup` | קובץ `-Fc` בתיקיית הגיבויים, וקוד 0 | קונטיינר לא רץ: הודעה וקוד שאינו 0 |
| שחזור | `restore <קובץ>` | מסד `resume_restore_check` נוצר מחדש ומשוחזר | קובץ חסר או פגום: הודעה וקוד שאינו 0 |
| הגנה | יעד שווה למקור, `resume`, או עם חיבורים פעילים | סירוב ללא `-Force` | הודעה ברורה |
| אימות אוטומטי | בדיקת שרת | נתונים, תמונה, גרסאות ו-`AuditLog` זהים אחרי השחזור, והשרת עולה | N/A |
| מיגרציות | שרת מול מסד משוחזר | אין מיגרציות חדשות, היסטוריה זהה | N/A |
| הגנה בבדיקה | הקובץ והמסד הזמני | נמחקים בסוף הבדיקה | N/A |

</intent-contract>

## Code Map

- `scripts/` -- `dev-seed.ps1` ו-`dev-seed.sql` כדוגמה לדפוס סקריפט מקומי מול `resume-dev-pg` (פורט 5433, מסד `resume`, סיסמה `dev`)
- `server/Tests/{AuthTestSupport.cs (PostgresFixture ~17-36, AuthApiFactory),ParticipantSeeding.cs,DatabaseRolesTests.cs}` -- תשתית Testcontainers, `CREATE DATABASE`, הרצת פקודות בתוך הקונטיינר (`ExecAsync`/psql ב-`DatabaseRolesTests`), זריעה
- `server/Api/Endpoints/{GalleryEndpoints.cs,ProfileEndpoints.cs}` -- `GET /profiles`, `GET /profiles/{id}`, `/profiles/{id}/photo`
- `server/Infrastructure/DependencyInjection.cs (~140-203)` -- הרצת מיגרציות באתחול ונעילה
- `.gitignore`, `README.md`, `docs/launch-checklist.md`
- חדש: `scripts/backup-restore.ps1` (ו-`.sh`), `docs/backup-restore.md`, `server/Tests/BackupRestoreTests.cs`

## Tasks & Acceptance

**Execution:**
- `scripts/backup-restore.ps1` (+`.sh`), `.gitignore` -- הסקריפט עם הגנות -- AR20
- `server/Tests/BackupRestoreTests.cs` -- בדיקת שחזור ועלייה על Testcontainers -- NFR8
- `docs/backup-restore.md`, `docs/launch-checklist.md`, `README.md` -- תיעוד ותנאי עלייה -- AR22
- הרצה ידנית אחת של הסקריפט מול `resume-dev-pg` (רק `backup` ו-`restore` למסד `resume_restore_check`, ללא נגיעה ב-`resume`), והתוצאה ב-`## Implementation Notes`

**Acceptance Criteria:**
- Given נתונים מייצגים, when מגבים ומשחזרים למסד חדש, then השרת עולה מולו והנתונים והתמונה והגרסאות זהים.
- Given יעד שווה למקור או חי, when מריצים `restore`, then הסקריפט מסרב עם הודעה ברורה וקוד שגיאה.
- Given כל בדיקות השרת (ריצה אחת), when הן רצות, then כולן עוברות.

## Spec Change Log

## Review Triage Log

### 2026-10-23 — Review pass
- verdicts: 11 findings (מקובצים לפי סיבת שורש מארבע השכבות) — high 0, medium 5, low 6, false 0, maybe-false 0
- findings:
  - `[medium]` `[patch]` הסקריפט עצמו (הגנות, יעדים מוגנים, `verify`) אינו מורץ בשום בדיקה, והבדיקה מעתיקה את הפקודות — נוספו בדיקות xUnit שמריצות את הסקריפט ב-PowerShell למקרי ההגנה שאינם דורשים docker
  - `[medium]` `[patch]` הבדיקה מאמתת "היסטוריית המיגרציות לא השתנתה" לפני שהשרת עלה (ריקה) — הלקוח נוצר ובקשות נשלחות לפני ההשוואה
  - `[medium]` `[patch]` כשל באמצע שחזור משאיר מסד חצי-משוחזר או `resume_restore_check` עם נתונים אישיים; `verify` לא משווה לספירות המקור — ניקוי בכשל, `try/finally`, והשוואת ספירות
  - `[medium]` `[patch]` `DROP ... WITH (FORCE)` ללא תנאי הופך את בדיקת החיבורים הפעילים למרוץ, ויעד שאינו `resume_restore*` נמחק בלי אישור — `DROP` רגיל בלי `-Force`, ו-`-Force` נדרש ליעד אחר
  - `[medium]` `[patch]` נתיב זמני קבוע בקונטיינר ושם קובץ גיבוי זהה באותה שנייה, ותיעוד לא מדויק על `-Force` ו-`verify` — שמות ייחודיים, ניקוי, קידוד UTF-8, ותיקון המסמכים
  - `[low]` `[reject]` סדר הגלריה אינו דטרמיניסטי — הנקודה ממיינת, ובדיקות קיימות מסתמכות על כך
  - `[low]` `[reject]` ניקוי קובץ הדמפ אחרי כשל dump בבדיקה — הקונטיינר נמחק בסוף
  - `[low]` `[reject]` הבדיקה מתחברת כמשתמש-על ולא כתפקיד `app` — מתועד: הרצה מחדש של `roles.sql` אחרי שחזור
  - `[low]` `[reject]` חוסר בדיקות לדמפ פגום, `-Force` עם חיבורים פעילים והעברת תמיכה ב-`.sh` — הגנת קובץ פגום נבדקה ידנית, ו-`.sh` אינו נדרש
  - `[low]` `[reject]` סיסמת ברירת מחדל `dev`, ושיטת `exit` בתוך פונקציות — סביבת פיתוח מקומית בלבד
  - `[low]` `[reject]` פרק פתרון תקלות, יעדי RPO ו-RTO ריקים ב-checklist וניהול `backups/` — המספרים יידרשו מהמנהלת לפני עלייה והוזכרו ב-checklist

## Implementation Notes

- נוספו `scripts/backup-restore.ps1` (ללא גרסת `.sh`, הוחלט לדלג), `server/Tests/BackupRestoreTests.cs`, `docs/backup-restore.md`, סעיף בצ'קליסט, הפניה ב-README ו-`backups/` ב-`.gitignore`. ב-`PostgresFixture` נוספו `ExecInContainerAsync` ו-`SuperUser` (תשתית בדיקות בלבד, בלי שינוי בקוד מוצר).
- הבדיקה רצה בקונטיינר Testcontainers באותן פקודות של הסקריפט (`pg_dump -Fc`, `pg_restore --no-owner --exit-on-error`), ולא מריצה את הסקריפט עצמו; עברה.
- ריצה ידנית מול `resume-dev-pg`: `restore` עם קובץ חסר, יעד `resume`, יעד `resume_e2e` וקובץ שאינו dump נדחו בקוד 1 עם הודעה בעברית. `verify` הצליח: גיבוי, שחזור ל-`resume_restore_check` (18 טבלאות, 15 מיגרציות, 10 פרופילים) ומחיקת מסד היעד. המסד `resume` נקרא בלבד (pg_dump). קובץ הגיבוי נשאר ב-`backups/` (מתעלמים בגיט). הגנת החיבורים הפעילים לא נבדקה ידנית.
- אחרי סקירה: הסקריפט תוקן (שמות זמניים ייחודיים וניקוי, מחיקת מסד יעד חצי-משוחזר, `verify` משווה ספירות מול המקור ותמיד מוחק את היעד, `DROP` רגיל בלי `-Force`, יעד שאינו `resume_restore*` דורש `-Force`, קידוד UTF-8). נוספו `BackupRestoreScriptGuardTests` (6 בדיקות שמריצות את הסקריפט), וההשוואה של היסטוריית המיגרציות ב-`BackupRestoreTests` עברה לאחר בקשות לשרת. נבדקה ידנית גם הגנת החיבורים הפעילים (סירוב, קוד 1) מול `resume_restore_check`, וקבצי הגיבוי שנוצרו נמחקו.

## Design Notes

הבדיקה מריצה את אותה סדרת פקודות pg_dump ו-pg_restore שהסקריפט מריץ, בקונטיינר הבדיקה, כדי להוכיח שהפורמט והפרמטרים שהסקריפט משתמש בהם באמת ניתנים לשחזור, ושהשרת עולה מול המסד המשוחזר. עצם ההרצה מול מסד הפיתוח נשארת בדיקה ידנית אחת ומתועדת כדי לא להתנגש בעבודה של המשתמשת.

## Verification

**Commands:**
- `dotnet build server` -- expected: בנייה מצליחה בלי אזהרות חדשות
- `dotnet test server --filter "FullyQualifiedName~BackupRestore"` -- expected: עובר
- `dotnet test server` -- expected: כל הבדיקות עוברות (ריצה אחת, ברקע)
- `powershell -File scripts/backup-restore.ps1 verify` -- expected: ידני, מסד `resume_restore_check` משוחזר ונוקה

## Auto Run Result

Status: done

- **שינוי שמומש:** `scripts/backup-restore.ps1` (`backup`, `restore`, `verify`; גיבוי `pg_dump -Fc` מהקונטיינר, שחזור למסד `resume_restore_check` שנוצר מחדש, הגנות: מקור שווה ליעד, שמות מוגנים, יעד שאינו `resume_restore*`, חיבורים פעילים, קובץ חסר או פגום; ניקוי בכשל; `verify` משווה ספירות מול המקור ותמיד מוחק את המסד הזמני); בדיקת שרת `BackupRestoreTests` (זריעה, dump, restore, שרת עולה מול המסד המשוחזר, היסטוריית מיגרציות זהה אחרי הרצת הנקודות, נתונים ותמונה זהים); שש בדיקות הגנה שמריצות את הסקריפט ב-PowerShell; `docs/backup-restore.md`; סעיף "גיבוי ושחזור" ב-`docs/launch-checklist.md`; README ו-`.gitignore`.
- **ממצאי סקירה:** 11 ממצאים מקובצים. תוקנו 5 בדרגת medium: בדיקות שמריצות את הסקריפט עצמו, בדיקת היסטוריית מיגרציות שהייתה ריקה, ניקוי מסד חצי-משוחזר וספירות ב-`verify`, `DROP` רגיל בלי `-Force` ודרישת `-Force` ליעד זר, ושמות זמניים ייחודיים עם תיעוד מדויק. נדחו 6 low עם נימוק בלוג.
- **המלצה לסקירת המשך:** כן. סיכון לא מאומת: נתיב `-Force` (ניתוק חיבורים ומחיקה) אינו נבדק אוטומטית; ספירות המקור ב-`verify` נקראות רגע לפני ה-dump ולכן כתיבה במקביל עלולה לגרום להתאמה כוזבת של כשל; הבדיקה מתחברת כמשתמש-על ולא כתפקיד `app`.
- **אימות:** `dotnet test server`: 864 עברו (ריצה מלאה אחת על הקוד הסופי), הרצה ידנית של `verify` מול `resume-dev-pg` עברה (15 מיגרציות, ספירות תואמות, המסד הזמני נמחק), וה-`resume` לא שונה.
- **סיכונים נותרים:** אין `.sh`; דרישות Render (תשלום, שחזור נקודת זמן, אזור אירופה, תרגיל שחזור בייצור, ומדיניות שמירה) נרשמו כתנאי עלייה.
