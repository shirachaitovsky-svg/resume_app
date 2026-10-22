---
title: 'סיפור 4.2: ניטור מיילים ויומן ביקורת'
type: 'feature'
created: '2026-10-23'
status: 'done'
baseline_revision: '57018c809b858390ed4cdf950a1a1624cf68efaa'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-4-context.md'
warnings: [multiple-goals, oversized]
deferred: []
---

<intent-contract>

## Intent

**Problem:** אם מייל נכשל סופית או נתקע, המנהלת לא יודעת על כך אלא אם היא בודקת ידנית את הנתונים, ואין לה דרך לנסות שוב. בנוסף, יומן הביקורת נכתב אבל אין שום מסך שמאפשר לקרוא אותו.

**Approach:** שרת ולקוח. נקודות אדמין לרשימת מיילים תקועים ולניסיון חוזר ידני (בלי כפילות ועם רישום ביקורת), התראה אחת למנהלת על Outbox תקוע, `/healthz/details` מוגן שמדווח על מצב ה-Outbox, ונקודת קריאה בלבד ליומן הביקורת. בלקוח, שני מסכי ניהול חדשים.

## Boundaries & Constraints

**Always:**
- הרשימה (`GET /admin/email-outbox/stuck` או הרחבה של `GET /admin/email-outbox` הקיים, לפי מה שנוח לשמור תאימות): שורות `dead`, ושורות `pending` שגילן או זמן התפיסה שלהן עבר סף (`OUTBOX_STUCK_AFTER_MINUTES`, ברירת מחדל 15). כל שורה: מזהה, `TemplateKey`, הישות המקושרת (מזהה בקשה או פרופיל בלבד), מצב, זמן הכשל או הגיל, מספר ניסיונות וקטגוריית הכשל. **בלי** כתובת, תוכן מייל או אסימון.
- `POST /admin/email-outbox/{id}/retry`: מחזיר שורה `dead` ל-`pending` בעדכון מותנה (`WHERE status = 'Dead'`): מאפס ניסיונות, `claim_count`, שגיאה ופקיעת lease, `next_attempt_at = now`, ובלי לשנות את `DeduplicationKey`, כך שלא נוצרת שורה כפולה ושליחה כפולה בלתי אפשרית. 409 אם השורה אינה `dead`, 404 אם לא קיימת או מזהה לא תקין (GUID `D`). נרשם `AuditLog` (`retry-email`, ישות `email-outbox`, מבצעת ומזהה בלבד) באותה טרנזקציה. עם anti-forgery ובדיקת Origin כמו שאר הפעולות.
- התראה: תהליך הרקע של ה-Outbox (או sweep קיים) שולח מייל התראה פשוט אחד ל-`ADMIN_NOTIFICATION_EMAIL` כשיש שורות `dead` או תקועות, עם תבנית חדשה ללא פרטים אישיים (רק מספרים וקישור ל-`/admin`), ו-`DeduplicationKey` לפי חלון זמן (למשל תאריך ושעה) כדי שלא יישלחו התראות חוזרות על אותה תקלה בכל סבב. שורת ההתראה עצמה אינה נספרת כ"תקועה".
- `GET /healthz` הקיים ללא שינוי. נוסף `GET /healthz/details` לאדמין מחובר בלבד: `{status: "ok"|"degraded", outbox: {dead, stuckPending, oldestAgeSeconds}}`, `degraded` כשיש dead או תקועות, `no-store`, ללא נתונים אישיים. נקודת liveness ללא סוד ל-Render לא נבנית, ונרשמת ב-`docs/launch-checklist.md` כתנאי עלייה.
- `GET /admin/audit-log`: אדמין בלבד, קריאה בלבד (אין `POST`/`PUT`/`PATCH`/`DELETE` לנתיב), `limit` (ברירת מחדל 50, מקסימום 100, מעבר לתקרה נחתך), `offset`, 400 לערכים לא תקינים, מיון מהחדש לישן, תגובה `{items, total}`. שדה לכל פריט: זמן, מבצעת (מזהה ושם משתמש אם קיים), פעולה, סוג ישות ומזהה ישות. ללא נתונים אישיים.
- לקוח: שני מסכים בתוך `/admin` (קישורים בדף הבית של הניהול): "מיילים תקועים" (טבלה עם עימוד ל-100, מצב ריק, שגיאה עם ניסיון חוזר, כפתור "ניסיון חוזר" עם חלון אישור, הודעת הצלחה, וטיפול ב-409/404/401 כמו מסכי הניהול הקיימים) ו"יומן ביקורת" (עימוד, קריאה בלבד). כל המחרוזות ב-`strings.he.ts`, רכיבי מערכת העיצוב ודפוס מסכי הניהול הקיימים (`admin-review`, `admin-profiles`), בדיקות לקוח לכל מצב, ובדיקות axe (`expectNoSeriousViolations`, `expectSingleH1`).
- שרת: בדיקות על Postgres אמיתי לכל הנקודות (401 ו-403, עימוד, ניסיון חוזר מוצלח ו-409, אין כפילות ושמירת מפתח ה-dedup, `AuditLog` ללא נתונים אישיים, ותגובות שאינן חושפות כתובת או תוכן), ובדיקה שהתראה אחת בלבד נשלחת לחלון. README מתעדכן.
- לא מחלישים בדיקות קיימות, ריצת שרת מלאה אחת בלבד, ו-`TestResults` לא נכנס לקומיט.

**Never:**
- לא מציגים או שולחים ביומן, ברשימה או בהתראה כתובת מייל, נייד או תוכן הודעה. לא ניתן לערוך או למחוק שורות ביומן הביקורת דרך הממשק. לא משנים את `/healthz` הקיים.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| רשימת תקועים | שורות `dead` ו-`pending` ישנות | הרשימה בלי כתובות, עם סוג, ישות, ניסיונות וגיל | לא אדמין: 401 או 403 |
| ניסיון חוזר | שורה `dead` | `pending` עם מונים מאופסים, אותו dedup key, ושורת `AuditLog` | לא `dead`: 409. לא קיימת: 404 |
| ניסיון חוזר כפול | שתי בקשות מקבילות | אחת מצליחה והשנייה 409, בלי שורה כפולה | N/A |
| התראה | יש `dead` או תקועות | מייל התראה אחד לחלון, בלי פרטים אישיים | אין `ADMIN_NOTIFICATION_EMAIL`: לא נשלח |
| `/healthz/details` | אדמין, יש dead | `degraded` עם ספירות | לא אדמין: 401 או 403 |
| `/healthz` | כל קורא | כמו היום | N/A |
| יומן ביקורת | אדמין | פריטים מהחדש לישן, עימוד עד 100 | `limit` או `offset` לא תקינים: 400 |
| כתיבה ליומן | `POST`, `PUT`, `DELETE` לנתיב | לא ממופה | N/A |
| מסך לקוח | רשימה, ריק, שגיאה, ניסיון חוזר | כל מצב מוצג, והחלון מאשר | 409: רענון והודעה |

</intent-contract>

## Code Map

- `server/Api/Endpoints/EmailOutboxEndpoints.cs (~19)` -- `GET /admin/email-outbox` הקיים (מונים ושורות dead, בלי כתובת) ושמות המסלול; להרחיב או להוסיף נקודות
- `server/Features/Email/{EmailOutboxProcessor.cs,IEmailOutbox.cs}`, `server/Infrastructure/EmailOutboxRepository.cs` -- מצבי השורה (`Pending`, `Sent`, `Dead`), מונים, `claim`, `lease`, `next_attempt_at`, `LastErrorCategory`, תהליך הרקע (`EmailOutboxWorker`) והתבניות; מקום לתבנית ההתראה ולסריקה
- `server/Domain/Email/EmailOutboxEntry.cs`, `server/Domain/Audit/AuditLogEntry.cs` -- שדות השורה, קבועי תבנית ופעולות ביקורת; להוסיף `retry-email` ו-`email-outbox`
- `server/Api/Program.cs`, `server/Infrastructure/DependencyInjection.cs` -- מיפוי `/healthz`, אפשרויות סביבה (`ADMIN_NOTIFICATION_EMAIL` קיים בהתראות בקשות פרטי קשר)
- `server/Api/Endpoints/ContactRequestAdminEndpoints.cs`, `ProfileAdminEndpoints.cs` -- דפוס נקודת אדמין עם עימוד, 404 אחיד, `no-store` ו-anti-forgery
- `server/Tests/{ContactRequestQueueTests.cs,ContactDeliveryTests.cs,ParticipantSeeding.cs,AuthTestSupport.cs}` -- עוזרי בדיקה (אדמין עם TOTP, זריעה)
- `client/src/app/pages/admin/{admin.routes.ts,home/admin-home.ts,review/*,profiles/*}`, `shared/strings.he.ts (~admin)`, `testing/a11y.ts` -- דפוס מסך ניהול עם שירות, טבלה, חלון אישור ובדיקות
- `README.md`, `docs/launch-checklist.md`, `server/Tests`

## Tasks & Acceptance

**Execution:**
- `server/Domain`, `server/Infrastructure`, `server/Features/Email`, `server/Api/Endpoints` -- רשימת תקועים, ניסיון חוזר, התראה, `/healthz/details`, ויומן ביקורת -- NFR7, NFR9
- `server/Tests/*` -- בדיקות לכל שורה במטריצה
- `client/src/app/pages/admin/{email-outbox,audit-log}/*`, `admin.routes.ts`, `admin-home.ts`, `strings.he.ts` -- מסכי הניהול ובדיקות לקוח ו-axe
- `README.md`, `docs/launch-checklist.md` -- תיעוד, ונקודת liveness ללא סוד כתנאי עלייה

**Acceptance Criteria:**
- Given כל נקודות הניהול החדשות, when קוראת אליהן משתמשת שאינה אדמין, then 401 או 403.
- Given ניסיון חוזר על שורה `dead`, when הוא מתבצע פעמיים, then אחד מצליח והשני 409, ושורת Outbox אחת בלבד עם אותו מפתח dedup.
- Given יומן ההתראות, הרשימה, `/healthz/details` ויומן הביקורת, when נבדקים, then אין בהם כתובת מייל, נייד או תוכן.
- Given `dotnet test server` (ריצה אחת) ו-`ng test --watch=false` ו-`ng build` ו-`npm run e2e`, when הם רצים, then הכול עובר.

## Spec Change Log

## Review Triage Log

### 2026-10-23 — Review pass
- verdicts: 16 findings (מקובצים לפי סיבת שורש מארבע השכבות) — high 0, medium 5, low 9, false 1, maybe-false 1
- findings:
  - `[medium]` `[patch]` ניסיון חוזר מאפס את `claim_count`, ומפתח האידמפוטנטיות לספק הוא `{row.Id}:{ClaimCount}`, ולכן השליחה החוזרת עשויה להשתמש שוב במפתח של התפיסה הראשונה, וספק שמכבד מפתחות עלול להשליך את המייל בשקט — `claim_count` לא מאופס (פרט למקרה `too-many-claims`), ונוספה בדיקה שמפתח הניסיון החוזר שונה
  - `[medium]` `[patch]` מייל התראה נשלח גם אם התקלה נפתרה בינתיים ("0 נכשלו, 0 תקועים") — אם שני המונים אפס בזמן העיבוד, השורה מסומנת כנשלחה בלי שליחה, עם בדיקה
  - `[medium]` `[patch]` התראה בכל שעה UTC כל עוד נשארה שורת `dead` ישנה, גם כזו שאין דרך לסגור — חלון ההתראה הוא יום UTC, ונוספה בדיקה
  - `[medium]` `[patch]` סריקת ה-worker שמכניסה את ההתראה (כולל "אין נמען, אין שורה") אינה נבדקת — הוצאה לשיטה שניתנת להרצה מבדיקה, עם בדיקה לשני המצבים ובדיקת עיבוד שורת התראה בלי נמענים
  - `[medium]` `[patch]` מסך הלקוח: הודעת הצלחה ושגיאת רענון מוצגות יחד, ותא גיל עלול להציג `NaN` — תוקן
  - `[low]` `[reject]` שורות `outbox-alert` מופיעות ברשימת התקועים אך לא נספרות בבריאות — מכוון ומתועד: כך תקלה בשליחת ההתראה גלויה למנהלת בלי לגרום ללולאת התראות
  - `[low]` `[reject]` גיל מחושב מ-`CreatedAt` וההגדרה "תקוע" לפי `NextAttemptAt` — מבלבל מעט, אך אינו שגוי ומתועד
  - `[low]` `[reject]` אין אינדקסים חדשים ל-`email_outbox` ו-`audit_log` — טבלאות קטנות והסריקה אינה על נתיב חם
  - `[low]` `[reject]` אין תקרה לניסיונות חוזרים ידניים, ולא נשמר מצב קודם ביומן — הפעולה ידנית של אדמין ונרשמת
  - `[low]` `[reject]` שיטת `IOutboxMonitor?` אופציונלית ב-`EmailOutboxProcessor` משתמשת בקטגוריה `unknown-template` כשחסרה — נדיר ורק בבדיקות
  - `[low]` `[reject]` `limit` מעל המקסימום ב-`audit-log` מוחזר 400 בעודף מספרי ענק, ושימוש בקבוע בשם `StuckMaxLimit` — קוסמטי
  - `[low]` `[reject]` נגישות וכפילות בלקוח: מיקוד אחרי מעבר עמוד, קוד עימוד מכופל ו-CSS משותף, תוויות קטגוריית כשל, "מוצגות" — מחוץ להיקף
  - `[low]` `[reject]` נקודת liveness ללא סוד ל-Render ומעקב — תנאי עלייה שנרשם ב-checklist
  - `[low]` `[reject]` בדיקות שעון קיר סביב גבול שעה — החלון נקבע לפי יום, ובדיקות כבר מתייחסות לטווחים
  - `[maybe-false]` `[defer]` אין בדיקה ל-`OUTBOX_STUCK_AFTER_MINUTES` מותאם אישית, ולניסיון חוזר של `contact-verification` שבקשתו פקעה — בדיקות הרחבה שאינן חוסמות
  - `[false]` `[reject]` "ההתראה תיפול על הפרטים האישיים" — נבדק בבדיקת PII, אין כתובת או תוכן

## Implementation Notes

- שרת: `IOutboxMonitor` ו-`IAuditLogReader` (ממשק נפרד מ-`IEmailOutbox`, כדי לא לשבור מימושי בדיקה קיימים) במימוש `OutboxMonitorRepository`. "תקוע" = `Dead`, או `Pending` שה-`next_attempt_at` שלו עבר לפני יותר מ-`OUTBOX_STUCK_AFTER_MINUTES`. הרשימה מוסיפה `GET /admin/email-outbox/stuck` (הנקודה הקיימת נשארה). `limit` ו-`offset` בנקודות החדשות מנותחים כמחרוזות כדי שערך לא מספרי יחזיר 400 ולא 500; ברשימת התקועים `limit` מעל 100 נדחה ב-400, וביומן הביקורת נחתך ל-100.
- התראה: תבנית `outbox-alert` (ללא ישות), מפתח `outbox-alert:{yyyyMMddHH}`; נוספת מסוויפר ה-worker רק כשיש כתובת מנהלת. ה-processor קיבל פרמטר אופציונלי אחרון `IOutboxMonitor? monitor` כדי שבנאים קיימים בבדיקות ימשיכו לעבוד. ספירת `/healthz/details` וההתראה אינן כוללות שורות `outbox-alert`; הרשימה כוללת אותן אם הן `dead`.
- לקוח: `admin-email-outbox` ו-`admin-audit-log` עם עימוד קודם/הבא (100 ו-50), מחרוזות תחת `STRINGS.admin.emailOutbox`, `auditLog`, `paging`.
- תיקוני סקירה: ניסיון חוזר משאיר את `claim_count` (מפתח הספק הוא `{id}:{claimCount}`) ומאפס אותו רק לשורה שמתה ב-`too-many-claims`; התראה שנכנסה לתור אחרי שהתקלה נוקתה מסומנת `sent` בלי מייל; חלון ההתראה הוא יום UTC (`outbox-alert:{yyyyMMdd}`); גוף הסוויפר הוצא ל-`EmailOutboxWorker.SweepOutboxAlertAsync` ונבדק; בלקוח, כשל ברענון שקט מנקה את הודעת ההצלחה, ו-`ageText` מוגן מפני `NaN`.
- אימות: `OutboxMonitoringTests` (19), `EmailOutboxTests` וקבוצת Health (29) עברו; לקוח: 405 בדיקות ו-`ng build` עברו. ריצת שרת מלאה ו-e2e לא הורצו.

## Design Notes

ניסיון חוזר משמר את `DeduplicationKey` כי הוא כבר ייחודי ושייך לאותה שורה: איפוס המונים והחזרה ל-`pending` גורמים לתהליך הרקע לתפוס אותה שוב, והמפתח היציב ל-provider מונע שליחה כפולה. ההתראה נשלחת כמייל רגיל דרך אותה תשתית (Outbox) כך שגם היא כפופה לאותן הגנות, וחלון ה-dedup מונע הצפה כשהתקלה נמשכת.

## Verification

**Commands:**
- `dotnet build server` -- expected: בנייה מצליחה בלי אזהרות חדשות
- `dotnet test server` -- expected: כל הבדיקות עוברות (ריצה אחת, כ-10 דקות, ברקע)
- `cd client && npx ng test --watch=false && npx ng build` -- expected: עוברים בלי אזהרות חדשות
- `cd e2e && npm run e2e` -- expected: התרחיש עובר

## Auto Run Result

Status: done

- **שינוי שמומש:** שרת: `GET /admin/email-outbox/stuck` (שורות `dead` ו-`pending` תקועות, בלי כתובת או תוכן), `POST /admin/email-outbox/{id}/retry` (עדכון מותנה, שמירת מפתח ה-dedup, `AuditLog`), התראת Outbox אחת ליום UTC למנהלת, `GET /healthz/details` לאדמין בלבד, ו-`GET /admin/audit-log` לקריאה בלבד עם עימוד. לקוח: מסכי "מיילים תקועים" (עם חלון אישור לניסיון חוזר) ו"יומן ביקורת" בתוך `/admin`, עם בדיקות ו-axe. תיעוד: README, ו-`docs/launch-checklist.md` ("ניטור מיילים", כולל נקודת liveness ללא סוד כתנאי עלייה).
- **ממצאי סקירה:** 16 ממצאים מקובצים. תוקנו 5 בדרגת medium: איפוס `claim_count` בניסיון חוזר שיכול היה להשליך מייל אצל ספק שמכבד מפתחות (עכשיו נשמר, חוץ מ-`too-many-claims`), התראה מיושנת ("0 ו-0"), חלון התראה יומי, בדיקת סריקת ה-worker והתראה בלי נמען, ושגיאות במסך הלקוח. נדחו 9 low, 1 false ו-1 maybe-false עם נימוק בלוג.
- **המלצה לסקירת המשך:** כן. סיכון לא מאומת: שורות `dead` ישנות ללא דרך להסיר אותן יגרמו להתראה יומית; אין בדיקות ל-`OUTBOX_STUCK_AFTER_MINUTES` מותאם אישית ולניסיון חוזר של `contact-verification` שבקשתו פקעה.
- **אימות:** `dotnet test server`: 857 עברו (ריצה מלאה אחת על הקוד הסופי). `ng test --watch=false`: 407 עברו, `ng build` נקי. `npm run e2e`: 7 מתוך 7 עברו.
- **סיכונים נותרים:** `/healthz/details` מחזיר 200 גם ב-`degraded`, ולכן ניטור חיצוני לא יכול לפעול לפי קוד הסטטוס; אין נקודת liveness ללא סוד (תנאי עלייה).
