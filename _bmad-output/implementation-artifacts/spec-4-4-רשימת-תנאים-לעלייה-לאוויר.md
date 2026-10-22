---
title: 'סיפור 4.4: רשימת תנאים לעלייה לאוויר'
type: 'feature'
created: '2026-10-23'
status: done
baseline_revision: '5399a38f796ae713c0dd3c68da467d86b62b6a84'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-4-context.md'
warnings: [oversized]
deferred: []
---

<intent-contract>

## Intent

**Problem:** תנאי העלייה פזורים בכמה מסמכים וסיפורים, ושלוש התכונות שהאתר מבטיח (נקודות קצה סגורות כברירת מחדל, אין פרטי קשר בתגובות ציבוריות, ושני אדמינים עם אימות דו-שלבי) לא נבדקות אוטומטית באופן ממצה, כך ששינוי עתידי יכול לשבור אותן בשקט.

**Approach:** שלוש בדיקות שרת חדשות על Postgres אמיתי שנועלות את התכונות האלה, ורשימת תנאי עלייה אחת מאוחדת ב-`docs/launch-checklist.md` עם סדר מומלץ, אחראי, הוכחה וסטטוס לכל סעיף, כך שברור מה אומת אוטומטית ומה דורש פעולה.

## Boundaries & Constraints

**Always:**
- בדיקת נקודות קצה (`server/Tests/EndpointSecurityTests.cs`): בונה את המארח (`AuthApiFactory`) ועוברת על כל `RouteEndpoint` ב-`EndpointDataSource`. לכל נקודה מחשבת אם היא אנונימית (יש `IAllowAnonymous` במטא-דאטה) או מוגנת (יש `IAuthorizeData` או מדיניות), ומוודאת: (1) כל נקודה אנונימית נמצאת ב-allowlist מפורש בבדיקה (שיטה ונתיב), (2) אין בבדיקה נקודה ב-allowlist שאינה קיימת בפועל, (3) כל נקודה שאינה ב-allowlist מוגנת במפורש בקבוצת הרשאה (מדיניות `Admin` או `Participant`) ולא רק בברירת המחדל. הודעת הכשל מפרטת מה נוסף ומה חסר, כך ששינוי עתידי מכשיל את הבדיקה עד שהרשימה מעודכנת.
- בדיקת פרטיות (`server/Tests/PublicPrivacyTests.cs`): זורעת משתתפת עם מייל וטלפון מזוהים (מייל ייחודי, טלפון עם ספרות ייחודיות), מחזור, פרופיל מאושר (כולל קורות חיים ותמונה) וגרסה ממתינה, ובקשת פרטי קשר. קוראת לכל נקודת `GET` ציבורית מה-allowlist (גלריה, מחזורים, פרופיל לפי מזהה, תמונה, מחזורים פעילים, `GET /invites/{token}`, `/healthz`) עם ערכים אמיתיים, ונתיבים עם מזהה לא קיים או אסימון לא תקין (שגיאות 404/401/400), ומוודאת שגוף התגובה וכותרות התגובה אינם מכילים את המייל (גם בקידוד URL), את הטלפון (נקי, ספרות בלבד, עם פורמט מעוצב), את שם הקובץ של האסימון, או כל אסימון שנזרע. שגיאות מחזירות `problem+json` בלי נתונים אישיים.
- אימות אדמינים (`server/Tests/AdminBootstrapTests.cs`, ולאתר או לקשר בדיקות קיימות מסיפורים 1.3 ו-1.4 במקום לשכפל): כשמוגדרים `ADMIN_USERNAME`/`ADMIN_INITIAL_PASSWORD` ו-`ADMIN_USERNAME_2`/`ADMIN_INITIAL_PASSWORD_2` נוצרים שני אדמינים; כל אחד מהם חייב להחליף סיסמה ראשונית ולהירשם ל-TOTP לפני שהוא ניגש לנקודת ניהול (403 `password-change-required` או `totp-setup-required` לפי הקיים); הכניסה אחרי ההרשמה דורשת קוד; ושניהם לא נוצרים שוב באתחול נוסף (אין כפילות). אם אין בקוד אכיפה שמונעת הפעלה בייצור עם אדמין אחד בלבד, זה מתועד כתנאי עלייה ידני בלבד (בלי להוסיף אכיפה).
- `docs/launch-checklist.md`: מאחד את הסעיפים הקיימים (הקשחה בייצור, ניטור מיילים, גיבוי ושחזור) בלי כפילויות לרשימה אחת, בסדר מומלץ לעלייה (ראשית מה שלוקח זמן ותלוי בגורמים חיצוניים, אחר כך תצורה, אחר כך אימות ועלייה). בכל סעיף: מה, אחראי (מנהלת הקורס, בונת האתר, הארגון), הוכחה נדרשת, וסטטוס: "אומת אוטומטית" עם קישור לקובץ הבדיקה או הסקריפט, או "דורש פעולה". כלל מוצהר שהאתר לא מפורסם עד שכל הסעיפים מתקיימים.
- הסעיפים: פרויקט OAuth בגוגל (מסך הסכמה, כתובת callback, `GOOGLE_CLIENT_ID`/`GOOGLE_CLIENT_SECRET`, `GOOGLE_PROVIDER=google`, ספייק כניסה דרך Vercel); דומיין מאומת SPF/DKIM וספק מיילים אמיתי (`EMAIL_PROVIDER`, מייל בדיקה מגיע לתיבה ולא לספאם); אישור הארגון לאחסון נתונים בחו"ל והסכמי עיבוד נתונים עם Vercel, Render וספק המיילים; מדיניות שמירה ומחיקה (כולל גיבויים); שרת ו-DB בתשלום ב-Render עם שחזור לנקודת זמן באזור אירופה; הרצת `server/db/roles.sql` ושני connection strings; מפתחות Turnstile אמיתיים וחיבור ווידג'ט בלקוח (היום `DevTurnstileService` שמחזיר טוקן קבוע); שני אדמינים עם TOTP והחלפת סיסמה ראשונית; פרופילים ישנים בלי `GoogleSub` אם קיימים; נקודת liveness ל-Render; אישור נוסח עמוד "אודות"; בדיקת `no-store` ו-`__Host-` בכתובת הייצור דרך Vercel; תרגיל שחזור בייצור; נגישות (קישור לבדיקות axe ובדיקה ידנית עם קורא מסך); ותרחיש מלא בייצור עם מיילים אמיתיים (קישור לבדיקת ה-e2e כבסיס).
- `README.md`: הפניה לרשימה. ריצה אחת בלבד של בדיקות שרת מלאות, ו-`TestResults` לא נכנס לקומיט.

**Never:**
- לא משנים התנהגות מוצר, ולא מוסיפים אכיפה חדשה בקוד. אם הבדיקה מגלה נקודה שאינה אנונימית במפורש או חושפת פרטים, מתקנים את הנקודה (וזה נרשם), ולא מרחיבים את ה-allowlist כדי לעקוף.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| נקודה אנונימית חדשה | מוסיפים נקודה עם `[AllowAnonymous]` שאינה ב-allowlist | הבדיקה נכשלת עם שמה | מעדכנים את הרשימה במודע |
| נקודה שהוסרה | ב-allowlist ואינה קיימת | הבדיקה נכשלת | מסירים מהרשימה |
| נקודה מוגנת | כל נקודה אחרת | כוללת מדיניות `Admin` או `Participant` | נקודה בלי מדיניות מפורשת נכשלת |
| פרטיות | GET ציבורי עם נתונים זרועים | אין מייל או נייד בגוף ובכותרות | נחשף: הבדיקה נכשלת, תיקון הנקודה |
| שגיאה | מזהה לא קיים או אסימון לא תקין | `problem+json` בלי פרטים אישיים | N/A |
| שני אדמינים | שני זוגות משתני אדמין | שני משתמשים, כל אחד נחסם עד החלפת סיסמה והרשמת TOTP | N/A |
| אתחול חוזר | הפעלה שנייה | אין אדמין כפול | N/A |
| רשימת עלייה | פתיחת המסמך | סדר מומלץ, אחראי, הוכחה וסטטוס לכל סעיף | N/A |

</intent-contract>

## Code Map

- `server/Api/Program.cs`, `server/Api/Endpoints/*.cs` -- מיפוי כל הנקודות (`MapGroup`, `RequireAuthorization`, `AllowAnonymous`); `server/Api/Security/IdentityServiceExtensions.cs (AdminPolicy, ParticipantPolicy, FallbackPolicy)`
- `server/Tests/{AuthTestSupport.cs (AuthApiFactory, EnvScope, ApiClient),ParticipantSeeding.cs,PublicFormProtectionTests.cs,AdminAuthTests.cs (1.3, 1.4),ProductionHardeningTests.cs}` -- תשתית ועוזרי זריעה וכניסת אדמין
- `server/Infrastructure/Identity/AdminSeeder.cs` -- יצירת אדמין ראשון ושני (`ADMIN_USERNAME_2`, `ADMIN_INITIAL_PASSWORD_2`)
- `docs/launch-checklist.md` (38 שורות, סעיפים מ-4.1, 4.2, 4.3), `docs/backup-restore.md`, `README.md`
- חדש: `server/Tests/{EndpointSecurityTests.cs,PublicPrivacyTests.cs,AdminBootstrapTests.cs}`

## Tasks & Acceptance

**Execution:**
- `server/Tests/EndpointSecurityTests.cs` -- allowlist מפורש ובדיקת מדיניות לכל נקודה -- AR9
- `server/Tests/PublicPrivacyTests.cs` -- פרטיות כל נקודות ה-GET הציבוריות והשגיאות -- NFR1, AR4
- `server/Tests/AdminBootstrapTests.cs` (+ קישור לבדיקות 1.3 ו-1.4) -- שני אדמינים, החלפת סיסמה ו-TOTP -- NFR6
- `docs/launch-checklist.md`, `README.md` -- רשימה מאוחדת ומסודרת -- AR22

**Acceptance Criteria:**
- Given נקודה אנונימית חדשה או שהוסרה, when רצה `EndpointSecurityTests`, then היא נכשלת עד שה-allowlist מעודכן.
- Given נתונים זרועים עם מייל וטלפון, when נקראות כל נקודות ה-GET הציבוריות, then אין בהן את הנתונים האלה.
- Given כל הבדיקות (ריצת שרת מלאה אחת), when הן רצות, then כולן עוברות.
- Given `docs/launch-checklist.md`, when נפתח, then כל סעיף כולל אחראי, הוכחה וסטטוס, ויש סדר מומלץ וכלל "לא מפרסמים עד שהכול מתקיים".

## Spec Change Log

## Review Triage Log

### 2026-10-23 — Review pass
- verdicts: 31 findings (מקובצים לפי סיבת שורש מארבע השכבות) — high 0, medium 7, low 18, false 3, maybe-false 3
- findings:
  - `[medium]` `[patch]` בדיקת הפרטיות עוברת גם אם הנתונים הזרועים לעולם לא מוגשים (404 לכול) — נוספו בקרות חיוביות: 200 ותוכן לפרופיל המאושר, לתמונה, למחזורים הפעילים ול-`/invites/{token}`
  - `[medium]` `[patch]` רשימת ה-GET הציבוריים בבדיקת הפרטיות ידנית ואינה מחוברת ל-allowlist, כך שנקודה ציבורית חדשה לא תיבדק — נוספה בדיקה שכל נקודת GET אנונימית ב-allowlist מכוסה ברשימת הפרטיות או מוחרגת במפורש
  - `[medium]` `[patch]` נקודות POST ציבוריות (בקשת פרטי קשר, אימות, תצוגה מקדימה והחלטה) אינן נבדקות להד של מייל או אסימון בתשובה, והקידוד JSON (`@`) לא נבדק — נוספו בדיקות
  - `[medium]` `[patch]` אין בדיקה חוצת תפקידים (משתתפת מול נקודות ניהול ו-`/auth`, אדמין מול `/me/profile`), ואין בדיקה ש-`/dev-idp/authorize` אינו ממופה בתצורת `google` — נוספו
  - `[medium]` `[patch]` בדיקות `AdminBootstrapTests` חלשות (`NotEqual(OK)`), ואין בדיקה לקוד שגוי ולכך שסיסמת הפתיחה אינה תקפה אחרי ההחלפה — הוקשחו
  - `[medium]` `[patch]` רשימת העלייה: סטטוס מקומי וייצור מעורבים, אין סעיף `ORIGIN_SECRET` ו-`/dev-idp` בייצור, אין שאילתה לפרופילים בלי `GoogleSub`, ואין אישור סופי שמתעד גרסה (commit) וריצת שרת ולקוח ו-e2e — תוקן
  - `[low]` `[patch]` התאמת קידומות בלי גבול מקטע (`/me` מול `/metrics`), והסבר חסר למה `POST /me/profile` אנונימי ב-allowlist — תוקן
  - `[low]` `[reject]` הנחות בנוגע לחלוקת הנתונים הזרועים בבדיקת הפרטיות (פורמטי טלפון נוספים, base64) — הוגדרו שמונה פורמטים ושני קידודי מייל, מעבר לכך ספקולטיבי
  - `[low]` `[reject]` `Messages.Last` בלי הודעה ברורה ובדיקת תנאי אדמין עם שם משתמש זהה — קוסמטי או נדיר
  - `[low]` `[reject]` ה-theory של 401 לא מזהה את שינוי קבוצת `/auth`, נקודות נוספות וכללי CSRF — השינוי מכוסה בבדיקת המטא-דאטה, והתיאור הוסר מהתיאור המטעה
  - `[low]` `[reject]` אכיפה בקוד של שני אדמינים וחסימת `DevTurnstileService` בייצור — מחוץ להיקף (אין אכיפה חדשה בקוד), ומתועדים כסעיפי עלייה
  - `[low]` `[reject]` נושאים נוספים בצ'קליסט (הודעת פרטיות למשתתפת, סבב סודות, התראות כשל גיבוי) — לא נדרשים כתנאי עלייה בספק
  - `[maybe-false]` `[reject]` ביצוע ריצה אחת בלבד של `/dev-idp` תחת `fake` — מכוסה בבדיקה החדשה
  - `[false]` `[reject]` "הבדיקה מחמיצה נקודות מותנות" — נקודות `dev-idp` הוכללו במפורש בנפרד

## Implementation Notes

- EndpointSecurityTests found five `/auth` endpoints (`GET /auth/me`, `POST /auth/logout`, `/auth/change-password`, `/auth/totp/setup`, `/auth/totp/enable`) protected only by the fallback policy. Fixed in `server/Api/Endpoints/AuthEndpoints.cs` by `RequireAuthorization(AdminPolicy)` on the `/auth` group (`/auth/csrf` and `/auth/login` keep `AllowAnonymous`). Regression: `Protected_endpoints_reject_an_anonymous_caller` and the explicit-policy check. The allowlist was not widened.
- PublicPrivacyTests: the photo upload file name was not covered (the seed writes photo bytes directly); everything else in the spec is checked, plus a contact-request verification token and the cohort invite token.
- Nothing in the code refuses production start with one admin (warning only), so it is a manual item (checklist 10).
- Ran only the three new classes: 12 passed. Full suite left to the caller.
- Review round: added positive controls and an allowlist-to-privacy-list coverage test, public POST no-echo test, segment-boundary area matching, cross-role tests, a `/dev-idp` absence test for GOOGLE_PROVIDER=google, specific status assertions plus wrong-TOTP and old-password tests in AdminBootstrapTests, and a restructured checklist (local vs production columns, ORIGIN_SECRET, /dev-idp, GoogleSub query, sign-off section). Targeted run: 20 passed. Unknown paths answer 401 in this host (not 404), so the /dev-idp test asserts the route is unmapped and equals the unknown-path answer.

## Design Notes

ה-allowlist בבדיקה הוא ההגנה מפני רגרסיה שקטה: ברירת המחדל של השרת היא `FallbackPolicy` שדורש אימות, אבל נקודה אנונימית חדשה תדרוש `[AllowAnonymous]` מפורש, והבדיקה מוודאת שהיא נוספת לרשימה במודע. בדיקת הפרטיות משתמשת בנתונים עם ערכים ייחודיים כדי שחיפוש מחרוזת בגוף התגובה יהיה חד-משמעי.

## Verification

**Commands:**
- `dotnet build server` -- expected: בנייה מצליחה בלי אזהרות חדשות
- `dotnet test server --filter "FullyQualifiedName~EndpointSecurity|FullyQualifiedName~PublicPrivacy|FullyQualifiedName~AdminBootstrap"` -- expected: עוברות
- `dotnet test server` -- expected: כל הבדיקות עוברות (ריצה אחת, ברקע)

## Auto Run Result

- סטטוס: done. סקירה ארבע שכבות, תיקונים הוחלו.
- שרת: 884 בדיקות עברו (ריצה מלאה אחת נקייה). e2e: 7/7 עברו. לא שונה קוד לקוד.
- נוצרו: EndpointSecurityTests, PublicPrivacyTests, AdminBootstrapTests; docs/launch-checklist.md אוחד לרשימה אחת.
- שינוי מוצר: קבוצת `/auth` דורשת מדיניות Admin, חוץ מ-`/auth/csrf` ו-`/auth/login`.
