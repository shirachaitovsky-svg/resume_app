---
title: 'סיפור 2.2: כניסה ועריכה כגרסה חדשה'
type: 'feature'
created: '2026-10-21'
status: 'done'
baseline_revision: '13bf3444a16151767c2e8bf8d9d3d845b840c140'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-resume_app-2026-10-21/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-2-1-קישור-אישי-במייל.md'
warnings: ['oversized']
deferred:
  - summary: >-
      יציאה מוחקת את העוגיה בדפדפן בלבד; עוגיה שהועתקה תקפה עד 60 הדקות, ואין ביטול סשן בצד השרת
    evidence: |-
      העוגיה חסרת מצב; לשקול גרסת סשן לפרופיל בסיפור 4.1
    location: >-
      server/Api/Endpoints/ParticipantEndpoints.cs
    severity: medium
  - summary: >-
      תוקף מוחלט של 60 דקות ואי-חידוש העוגיה נבדקים רק בעת ההנפקה; אין שעון נשלט בבדיקות, וגם `UpdateOutcome.Busy` (503 בעיבוד תמונה) לא נבדק
    evidence: |-
      דורש `TimeProvider` נשלט במפעל הבדיקות
    location: >-
      server/Api/Security/IdentityServiceExtensions.cs
    severity: low
  - summary: >-
      טיוטה שנדחתה או הוחלפה נעלמת מהטופס כשיש גרסה מאושרת (המקור הוא הממתינה האחרונה או המאושרת)
    evidence: |-
      החזרה לתיקון עם הערה היא סיפור 2.4; התנהגות מקור העריכה שם
    location: >-
      server/Infrastructure/ProfileRepository.cs
    severity: low
  - summary: >-
      מסכי הכניסה והעריכה בלקוח (החלפת אסימון והסרתו מהכתובת, הטופס, באנר "השינויים ממתינים לאישור...", והודעת קישור לא תקף עם הסבר לבקשת קישור חדש) טרם נבנו
    evidence: |-
      סיפור 5.8; ה-Referrer-Policy על דף הקישור עצמו הוא אחריות הלקוח
    location: >-
      client/ (טרם קיים)
    severity: medium---

<intent-contract>

## Intent

**Problem:** משתתפת שקיבלה קישור אישי לא יכולה להיכנס ולערוך את הפרופיל שלה, ושינוי בפרופיל שפורסם חייב להגיע לאישור בלי לשנות את מה שמוצג בציבור.

**Approach:** להוסיף ב-`server/` זהות משתתפת: `POST /profiles/session` שמחליף אסימון קישור אישי (צריכה אטומית, חד-פעמית) בעוגיית סשן קצרה, ונקודות `GET /me/profile` ו-`PUT /me/profile` שמציגות ומעדכנות את הפרופיל של הסשן בלבד, כך שכל שינוי נשמר כגרסה `pending` חדשה (הקודמת הממתינה הופכת `superseded`) והגרסה המאושרת נשארת ללא שינוי. שינוי מייל או נייד נשמר כבקשה ממתינה ואינו חל מיד. חלק השרת בלבד.

## Boundaries & Constraints

**Always:** `POST /profiles/session` עם גוף `{ "token": "..." }`, `[AllowAnonymous]`, מוגן ב-anti-forgery, `Origin` והגבלת קצב לפי IP (scope `edit-session-ip`, ברירת מחדל 30 לשעה, `RATE_LIMIT_EDIT_SESSION_IP`); בלי Turnstile (האסימון עצמו הסוד). פענוח האסימון: hash (SHA-256) והחלפה בעדכון מותנה אטומי אחד `UPDATE personal_tokens SET used_at = now WHERE token_hash = @h AND purpose = 'edit-profile' AND used_at IS NULL AND revoked_at IS NULL AND expires_at > now RETURNING profile_id`, כך שהאסימון חד-פעמי וקריאות מקבילות מצליחות פעם אחת בלבד. אסימון פג, שנוצל, בוטל, לא קיים, פגום, באורך חריג או בעל מטרה אחרת: אותה תגובת 401 `problem+json` עם `code: "link-invalid"` בלי הבחנה. פרופיל שהוסר (`Visibility = removed`): אותו 401. הצלחה: 200 `{ "status": "ok" }` ו-`Set-Cookie` של סשן משתתפת: סכמת אימות נפרדת מהאדמין, שם `__Host-resume-participant`, `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`, תוקף 60 דקות מוחלט (בלי sliding), עם claim של `profileId` ו-`purpose=edit-profile` בלבד. התגובה כוללת `Referrer-Policy: no-referrer` ו-`Cache-Control: no-store`. סשן אדמין וסשן משתתפת אינם מתחלפים זה בזה: נקודות `/me/*` מקבלות רק סשן משתתפת, ונקודות האדמין רק אדמין. `POST /profiles/session/logout` מסיים את הסשן. `GET /me/profile` מחזיר את הנתונים של הפרופיל של הסשן בלבד (בלי מזהה בנתיב או בגוף): מקור העריכה (הגרסה הממתינה האחרונה אם קיימת, אחרת המאושרת), כל שדות ההגשה כולל מייל ונייד נוכחיים של המשתתפת (פרטיים לה בלבד), `hasPhoto`, מחזור, וסטטוסים: `hasPublished` (יש גרסה מאושרת), `pending` (`submittedAt` של הגרסה הממתינה או `null`), ו-`pendingContactChange` (בוליאני). `PUT /me/profile` מקבל את אותם שדות ובאותה צורה כמו הגשה ראשונה (JSON או multipart, כולל `cv`, `avatarId`, `photo`, אותם כללי ולידציה, ניקוי ומגבלות, ואותו קוד ולידציה משותף), ובנוסף `removePhoto` (בוליאני). שדות החובה חובה גם בעדכון. אם לא נשלחה תמונה חדשה ו-`removePhoto` שקר, התמונה של המקור מועתקת לגרסה החדשה. בטרנזקציה אחת: אם קיימת גרסה `pending` לפרופיל היא הופכת `superseded` (עדכון מותנה) ונוצרת גרסה `pending` חדשה (האינדקס הייחודי החלקי נשמר); הגרסה המאושרת, `ApprovedRevisionId` ו-`Visibility` אינם משתנים; `ParticipantPrivate` אינו משתנה. מייל או נייד שונים מהנוכחיים מנורמלים ונשמרים בגרסה החדשה כשדות `pending_email` ו-`pending_phone` (חדשים) בלבד, ואינם חלים (האישור הוא סיפור 2.3); מייל זהה לנוכחי אינו שינוי. אין בדיקת ייחודיות של המייל החדש בשלב זה (לא לחשוף קיום) והיא תיעשה באישור. תגובת הצלחה: 202 `{ "status": "pending" }`. כשל ולידציה: 400 `problem+json` כבהגשה ראשונה ולא נוצר דבר. אין שאילתה או החזרה של פרטי פרופיל אחר; סשן בלי פרופיל פעיל (הוסר מאז): 401. מגבלת גוף כבהגשה (JSON 160KB, multipart 512KB). בקשות שינוי דורשות anti-forgery ו-Origin. מיגרציית EF Core. בלי PII ביומנים.

**Never:** אין החזרה לתיקון (2.4), אין אישור שינוי מייל או נייד (2.3), אין הסרה (2.5), אין מסכי לקוח, אין שינוי של הגרסה המאושרת או של מה שמוצג בציבור, אין שימוש חוזר באסימון, אין שמירת האסימון הגולמי.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| החלפת אסימון | אסימון תקף | 200; עוגיית סשן לפי המפרט; האסימון נוצל | N/A |
| שימוש חוזר | אותו אסימון פעם שנייה | 401 `link-invalid` | אחיד |
| אסימון לא תקף | פג, בוטל, לא קיים, פגום, מטרה אחרת | אותו 401 בדיוק | אחיד |
| מקביליות | חמש בקשות החלפה לאותו אסימון | בדיוק 200 אחד, השאר 401 | עדכון אטומי |
| פרופיל שהוסר | אסימון תקף לפרופיל `removed` | 401 | אחיד |
| קריאת פרופיל | סשן תקף | הנתונים של הפרופיל של הסשן, בצורה מלאה | N/A |
| בלי סשן | אנונימי | 401 | N/A |
| סשן אדמין | עוגיית אדמין בלבד | 401 או 403 ב-`/me/*` | N/A |
| עדכון פרופיל מפורסם | שינוי תיאור | נוצרת גרסה `pending`; המאושרת ללא שינוי; הגלריה והפרופיל הציבורי מציגים את הישנה | N/A |
| עדכון שנייה | כבר יש `pending` | הישנה `superseded`, החדשה `pending`, אחת בלבד | אינדקס חלקי |
| תמונה | בלי `photo` חדש | התמונה מועתקת; עם `removePhoto` היא מוסרת; עם `photo` חדש היא מוחלפת | N/A |
| שינוי מייל או נייד | ערך חדש | נשמר כ-`pending_email` או `pending_phone`; `ParticipantPrivate` ללא שינוי | N/A |
| מייל זהה | אותו מייל כמו הנוכחי | אינו נחשב שינוי | N/A |
| ולידציה | שדה חובה חסר, קישור לא תקין, CV מסוכן | 400 ולא נוצר דבר | ולידציה |
| בקשה בלי anti-forgery | PUT או POST ללא אסימון או Origin זר | נדחית | N/A |
| בידוד | סשן של משתתפת א' | אין גישה לנתוני ב', אין מזהה בנתיב | N/A |
| קצב | חריגה מהסף | 429 עם `Retry-After` | N/A |
| פרטיות | תגובות | אין בהן אסימון גולמי; מייל ונייד רק למשתתפת עצמה | N/A |

</intent-contract>

## Code Map

- `server/Features/Email/PersonalTokens.cs`, `server/Infrastructure/EmailOutboxRepository.cs` (`PersonalTokenIssuer`) -- חישוב hash של אסימון והנפקה בסיפור 2.1; כאן מוסיפים `TryRedeemAsync` אטומי.
- `server/Api/Endpoints/EditLinkEndpoints.cs`, `ProfileEndpoints.cs` -- תבנית נקודה ציבורית מוגנת (anti-forgery, Origin, `PublicFormGuard.CheckIpAsync`), גוף JSON או multipart וקוד הוולידציה של ההגשה; לחלץ ולשמור על קוד ולידציה ועיבוד תמונה משותפים ללא שכפול (`ProfileService`, `IPhotoProcessor`, `SafeMarkdown`, `UnsafeCharacters`).
- `server/Api/Security/IdentityServiceExtensions.cs`, `AuthPipelineExtensions.cs`, `RequestIntegrityMiddleware.cs` -- סכמת העוגיה של האדמין (`__Host-resume-admin`), `FallbackPolicy`, `AdminPolicy`, בדיקת anti-forgery ו-Origin; להוסיף סכמה ומדיניות `Participant` נפרדות מבלי לשנות את האדמין, ולוודא שהסדר ב-`Program.cs` תקין.
- `server/Features/Profiles/ProfileService.cs`, `server/Infrastructure/ProfileRepository.cs` -- יצירה בטרנזקציה, `NormalizeEmail`, ושאילתות; להוסיף שירות עדכון ומאגר.
- `server/Domain/Profiles/Profile.cs` -- `ProfileRevision` (סטטוסים `pending`/`approved`/`superseded`/`returned`), אינדקס ייחודי חלקי על `pending` אחת לפרופיל; להוסיף `PendingEmail` ו-`PendingPhone` ומיגרציה (README: `dotnet-ef`).
- `server/Infrastructure/SubmissionReviewRepository.cs` -- דוגמה לטרנזקציה עם נעילת שורת הפרופיל ועדכונים מותנים (`ApproveAsync`); לשימוש כדפוס.
- `server/Infrastructure/PublicProfileReader.cs` -- הקריאה הציבורית; מוכיחה בבדיקה שעדכון ממתין אינו משנה את הגלריה והפרופיל.
- `server/Tests/EditLinkTests.cs`, `AuthTestSupport.cs`, `PublicProfileDetailTests.cs`, `ProfileSubmissionTests.cs` -- יצירת אסימונים (אפשר ישירות דרך `PersonalTokenIssuer`), `ApiClient` עם anti-forgery ו-Turnstile, `SeedAsync`; להרחיב לעוגיית משתתפת ו-`ApiClient` לסשן.
- אין לשנות: `client/`, התנהגות האדמין והאישור, הנקודות הציבוריות הקיימות.

## Tasks & Acceptance

**Execution:**
- `server/Features/Email/` ו-`server/Infrastructure/` -- `TryRedeemAsync` אטומי לאסימון `edit-profile` -- AR10, AD-6
- `server/Api/Security/` ו-`server/Api/Program.cs` -- סכמת אימות `Participant` (עוגיית `__Host-resume-participant` ל-60 דקות מוחלטות), מדיניות `ParticipantPolicy`, והפרדה מלאה מהאדמין -- AD-5
- `server/Domain/Profiles/` ו-`server/Infrastructure/` -- `PendingEmail` ו-`PendingPhone` על הגרסה, ומיגרציה -- AR17
- `server/Features/Profiles/` -- שירות עדכון בטרנזקציה אחת (supersede, יצירת `pending`, העתקת תמונה, שינוי מייל או נייד ממתין) עם ולידציה משותפת להגשה -- FR-11, FR-12, AR5, AR6
- `server/Api/Endpoints/` -- `POST /profiles/session`, `POST /profiles/session/logout`, `GET /me/profile`, `PUT /me/profile` -- FR-11, FR-12
- `server/Tests/` -- בדיקות אינטגרציה לשורות המטריצה, כולל מרוץ החלפה, מרוץ עדכונים והוכחה שהגלריה והפרופיל הציבורי אינם משתנים לפני אישור -- AR6, AR21
- `README.md` -- הנקודות, הסשן, `link-invalid`, והתנהגות העדכון -- תפעול

**Acceptance Criteria:**
- Given קישור אישי תקף, when מחליפים את האסימון, then נוצרת עוגיית סשן קצרה, האסימון נצרך אטומית פעם אחת, והתשובה כוללת `Referrer-Policy: no-referrer`.
- Given אסימון שפג, בוטל, נוצל או בעל מטרה אחרת, when משתמשים בו, then 401 אחיד והבקשה נדחית.
- Given פרופיל מפורסם, when המשתתפת מגישה שינוי, then נוצרת גרסה `pending` חדשה (גרסה ממתינה קודמת הופכת `superseded` בטרנזקציה אחת) והגרסה המאושרת נשארת ללא שינוי באתר הציבורי עד אישור.
- Given שינוי מייל או נייד, when הוגש, then הוא נשמר כבקשה ממתינה ואינו חל מיד.
- Given קורות חיים ותמונה בעריכה, when נשלחים, then חלים אותם ניקוי ואימות כמו בהגשה הראשונה.

## Spec Change Log

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: כ-45 ממצאים מארבע שכבות (רבים כפולים) — high 0, medium 5, low 6, false 0
- findings:
  - `[medium]` `[patch]` אימות CSRF מול משתמש אנונימי בנתיבי `/me` נשבר כשבדפדפן גם עוגיית אדמין, וההערה לא מדויקת — נוסף `GET /profiles/csrf` אנונימי ובדיקה
  - `[medium]` `[patch]` בקשת שינוי מייל או נייד ממתינה מתבטלת בשקט בכל עריכה לא קשורה — הטופס ממולא בערך המבוקש, ושלושה נתיבים מפורשים עם בדיקות
  - `[medium]` `[patch]` `SingleAsync` על שורה חסרה גורם ל-500; אי עקביות בהגדרת תמונה ריקה — תוקנו
  - `[medium]` `[patch]` בדיקות חסרות: בחירת מקור התמונה בעריכה פשוטה, `removePhoto` ב-multipart, סטטוסים מדויקים להפרדת זהויות, גבולות גוף, בידוד בין פרופילים, פרופיל שלא אושר — נוספו
  - `[low]` `[patch]` עוגיה מתה אחרי session-invalid וקבוע מטרה כפול — תוקנו
  - `[medium]` `[defer]` ביטול סשן בשרת, בדיקת תוקף מוחלט עם שעון נשלט, טיוטה שנדחתה, ומסכי הלקוח — נרשמו ב-deferred
  - `[low]` `[reject]` אסימון שנצרך כש-SignIn נכשל, 415 ו-429 לעומת 401 אחיד, IsPersistent, איחוד NoStore, Referrer-Policy בדחיות מהשכבה הקודמת — לא סביר שייפגעו בשימוש רגיל
## Verification

**Commands:**
- `dotnet build server` -- expected: 0 שגיאות
- `dotnet test server` -- expected: כל הבדיקות עוברות (דורש Docker)

## Auto Run Result

- **שינוי:** זהות משתתפת: `POST /profiles/session` מחליף אסימון קישור אישי (צריכה אטומית חד-פעמית, 401 `link-invalid` אחיד לכל אסימון לא תקין) בעוגיית `__Host-resume-participant` (60 דקות מוחלטות, `Strict`, נפרדת מהאדמין); `GET /me/profile` ו-`PUT /me/profile` (JSON או multipart, כולל `removePhoto`) שמציגים ומעדכנים את הפרופיל של הסשן בלבד. כל עדכון יוצר גרסה `pending` חדשה (הקודמת הופכת `superseded` בטרנזקציה עם נעילת שורת הפרופיל), הגרסה המאושרת והגלריה אינן משתנות, ושינוי מייל או נייד נשמר כבקשה ממתינה (`pending_email`, `pending_phone`). נוסף `GET /profiles/csrf` אנונימי לטוקן anti-forgery.
- **קבצים:** `server/Api/Endpoints/ParticipantEndpoints.cs`, `ProfileEndpoints.cs`, `server/Api/Security/*`, `server/Features/Profiles/ProfileService.cs`, `server/Features/Email/PersonalTokens.cs`, `server/Infrastructure/{ProfileRepository,EmailOutboxRepository}.cs`, מיגרציית `ParticipantEdit`, `server/Tests/ParticipantEditTests.cs`, `README.md`.
- **סקירה:** 5 תיקוני medium (CSRF עם עוגיות אדמין ומשתתפת, ביטול שקט של בקשת שינוי מייל או נייד, `SingleAsync` על שורה חסרה, בדיקות חסרות) ו-2 low הוחלו. 4 פריטים נדחו ל-`deferred` (ביטול סשן בשרת, תוקף מוחלט בבדיקה עם שעון נשלט, טיוטה שנדחתה, מסכי לקוח). השאר נדחו כמתועד ביומן.
- **המלצה לסקירת המשך:** כן. סיכונים שלא אומתו: בדיקת ה-CSRF נעשית מול משתמש אנונימי בנתיבי `/me` (טוקן לא קשור לזהות); יציאה מוחקת רק את העוגיה בדפדפן; התוקף המוחלט לא נבדק עם שעון נשלט.
- **אימות:** `dotnet build server` ללא שגיאות, `dotnet test server`: 506 מתוך 506 עוברות מול Postgres אמיתי.
- **סיכונים שנותרו:** מסכי הכניסה והעריכה בלקוח (סיפור 5.8); אישור שינוי מייל או נייד ממתין בסיפור 2.3.
