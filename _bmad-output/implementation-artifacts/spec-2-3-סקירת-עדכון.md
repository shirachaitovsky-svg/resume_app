---
title: 'סיפור 2.3: סקירת עדכון'
type: 'feature'
created: '2026-10-22'
status: 'done'
baseline_revision: '11de63b3c9ce8ff0c420293ec6d9fbe1844c202e'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-resume_app-2026-10-21/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-2-2-כניסה-ועריכה-כגרסה-חדשה.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-1-11-אישור-פרסום.md'
warnings: []
deferred:
  - summary: >-
      מסך סקירת העדכון בלקוח (הצגה זו לצד זו, הדגשת שינויים, סימון "עדכון", הצגת שינוי מייל או נייד בנפרד, והודעה שיש גרסה חדשה בעת 409) טרם נבנה
    evidence: |-
      השרת מחזיר `published`, `changes`, `contactChange` ו-`latestPendingRevisionId`; סיפור 5.6
    location: >-
      client/ (טרם קיים)
    severity: medium
  - summary: >-
      אין בדיקה שמכסה מרוץ בין שני פרופילים באותו מחזור לאותו מייל חדש (נתיב `UniqueViolation` כגיבוי לבדיקה המקדימה)
    evidence: |-
      המרוץ צר והאינדקס הייחודי הוא גיבוי; להוסיף בדיקת מקביליות
    location: >-
      server/Infrastructure/SubmissionReviewRepository.cs
    severity: low
  - summary: >-
      השוואת תמונה ב-SQL מול בייטים מלאים בכל קריאת פרטים, וקורות חיים שנדחו בשני הצדדים אינם מופיעים ב-`changes`
    evidence: |-
      מוגבל ל-512KB; לשקול עמודת hash ולסמן שינוי ב-cv שנדחה
    location: >-
      server/Infrastructure/SubmissionReviewRepository.cs
    severity: low---

<intent-contract>

## Intent

**Problem:** כשמשתתפת מגישה עדכון לפרופיל שפורסם, המנהלת רואה רק את הגרסה המוצעת ואינה יכולה לראות מה השתנה, ושינוי מייל או נייד ממתין נשאר תקוע בלי נתיב אישור, ובלי הודעה מתאימה כשהעדכון הוחלף בינתיים.

**Approach:** להרחיב את נקודות הניהול של ההגשות ב-`server/`: פרטי הגשה שמחזירים גם את הגרסה המפורסמת כעת ורשימת השדות ששונו, נקודות אישור ודחייה מפורשות של שינוי מייל או נייד ממתין (נפרדות מאישור הפרסום, ונרשמות ב-`AuditLog`), ותגובת 409 לאישור גרסה שהוחלפה שמפנה לגרסה הממתינה החדשה. חלק השרת בלבד.

## Boundaries & Constraints

**Always:** כל הנקודות תחת `/admin/submissions` דורשות `AdminPolicy`. **פרטי הגשה** (`GET /admin/submissions/{revisionId}`) ממשיכים כבעבר, ומוסיפים: `published` (השדות הנראים של הגרסה המאושרת הנוכחית של הפרופיל: שם, מוצר וקישור, תיאור, תפקיד, מקום עבודה, טקסט היכרות, שלוש התשובות, `avatarId`, `hasPhoto`, וקורות חיים אחרי `SafeMarkdown.Clean`; `null` כש-`kind` הוא `new`), `changes` (רשימת שמות השדות ששונו בין המפורסם למוצע, ריקה כש-`new`; השוואה לפי ערכי הטקסט המנוקים, ולתמונה לפי שוויון בייטים: `photo` מופיע כששונתה, הוסרה או הוחלפה) ו-`contactChange` (`{ pendingEmail, pendingPhone }` כשיש שינוי ממתין, אחרת `null`; מייל ונייד נוכחיים נשארים בשדות הקיימים). שינוי המייל והנייד אינו חלק מ-`changes` ואינו מוצג כחלק מהשוואת הפרסום. **אישור הפרסום** (`POST /admin/submissions/{revisionId}/approve` מסיפור 1.11) אינו נוגע ב-`ParticipantPrivate` ואינו מחיל שינוי מייל או נייד; כשהגרסה אינה `pending` (הוחלפה, הוחזרה או כבר אושרה): 409 עם `code: "revision-not-pending"` וכעת גם `latestPendingRevisionId` (מזהה הגרסה הממתינה הנוכחית של אותו פרופיל, או `null` אם אין), כך שהמסך יציג שיש גרסה חדשה; גרסה שלא קיימת: 404 אחיד כבעבר. **שינוי מייל או נייד ממתין:** `POST /admin/submissions/{revisionId}/approve-contact-change` ו-`POST /admin/submissions/{revisionId}/reject-contact-change`, חלים על גרסה שנושאת `pending_email` או `pending_phone` ואינה `superseded` או `returned` (כלומר `pending` או `approved`). אישור: בטרנזקציה אחת עם נעילת שורת הפרופיל, מעדכן את `ParticipantPrivate` (מייל ו/או נייד מנורמלים), מקדם את `contact_version` של `ParticipantPrivate` (עמודה חדשה, ברירת מחדל 1, לבדיקת העקביות של מסירת פרטים בסיפור 3.4), מנקה את `pending_email` ו-`pending_phone` של הגרסה (עדכון מותנה: רק אם עדיין זהים לערכים שהמנהלת ראתה, והגרסה בסטטוס המותר), וכותב `AuditLog` (`approve-contact-change`, סוג ישות `profile-revision`, מזהה הגרסה, בלי ערכים). אם המייל החדש כבר תפוס במחזור (`(cohort_id, email)` ייחודי) אצל פרופיל אחר: 409 עם `code: "email-taken"`, בלי שינוי כלשהו ובלי חשיפת הפרופיל הקיים. דחייה: מנקה את שני השדות ללא שינוי ב-`ParticipantPrivate`, ו-`AuditLog` (`reject-contact-change`). שתי הפעולות: 404 אחיד לגרסה שאינה קיימת או מזהה שאינו GUID בצורה `D`; 409 עם `code: "no-contact-change"` כשאין שינוי ממתין או שהוחלף בינתיים, ו-`code: "revision-not-eligible"` כשהגרסה `superseded` או `returned`. בקשות שינוי דורשות anti-forgery ו-`Origin`; בלי אסימון: נדחות. אישור וגם דחייה מקבלים בגוף את הערכים שהמנהלת ראתה (`{ "pendingEmail": "...", "pendingPhone": "..." }`, כל אחד אופציונלי אך לפחות אחד, והשוואה מנורמלת) כדי למנוע אישור של ערך שהוחלף בינתיים; אי התאמה: 409 `code: "contact-change-stale"`. מקביליות: אישור ודחייה מקבילים, או שני אישורים, מצליחים פעם אחת בלבד. תגובת הצלחה: 200 עם `revisionId`, `status` (`contact-change-approved` או `contact-change-rejected`). אין החזרת מייל או נייד בתגובות הצלחה (רק ב-`GET` של ההגשה). `AuditLog` ללא תוכן אישי. תור הסקירה (`GET /admin/submissions`) מוסיף לכל פריט `hasContactChange` (בוליאני). המיגרציה מוסיפה `contact_version`.

**Never:** אין החזרה לתיקון (2.4), אין הסרת פרופיל (2.5), אין שליחת מיילים או התראות, אין מסכי לקוח, אין החלה אוטומטית של שינוי מייל או נייד באישור הפרסום, אין חשיפת פרטי פרופיל אחר ב-`email-taken`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| פרטי עדכון | גרסה `pending` של פרופיל עם גרסה מאושרת | `published` מלא, `changes` מפרט את השדות ששונו, `kind: update` | N/A |
| שינוי בתמונה | הוחלפה, הוסרה או נשארה | `photo` ב-`changes` רק כששונתה | N/A |
| הגשה חדשה | פרופיל בלי גרסה מאושרת | `published: null`, `changes: []` | N/A |
| אין שינויים | עדכון זהה לחלוטין | `changes: []` | N/A |
| קורות חיים מסוכנים | `cv` ישיר בבסיס הנתונים, בצד המפורסם או המוצע | מנוקה או `null` כבפרטי ההגשה | לא נכשל |
| אישור עדכון | המנהלת מאשרת גרסה ממתינה | המאושרת הקודמת `superseded`, החדשה `approved`, הגלריה והפרופיל מציגים אותה, `AuditLog` | N/A |
| אישור גרסה שהוחלפה | הגרסה `superseded` | 409 `revision-not-pending` עם `latestPendingRevisionId` | N/A |
| אישור בלי גרסה ממתינה חדשה | הגרסה `approved` או `returned` | 409 עם `latestPendingRevisionId: null` | N/A |
| אישור פרסום עם שינוי מייל | גרסה עם `pending_email` | `ParticipantPrivate` ללא שינוי והשינוי נשאר ממתין | N/A |
| אישור שינוי קשר | מייל ונייד חדשים | `ParticipantPrivate` מעודכן, `contact_version` עלה, השדות הממתינים נוקו, `AuditLog` | N/A |
| מייל תפוס | המייל החדש קיים אצל פרופיל אחר במחזור | 409 `email-taken`, בלי שינוי, בלי חשיפה | אטומי |
| דחיית שינוי קשר | שינוי ממתין | השדות הממתינים נוקו, `ParticipantPrivate` ללא שינוי, `AuditLog` | N/A |
| אין שינוי ממתין | גרסה בלי `pending_*` | 409 `no-contact-change` | N/A |
| ערך שהוחלף | גוף בקשה לא תואם לערך הממתין הנוכחי | 409 `contact-change-stale` | N/A |
| גרסה לא כשירה | `superseded` או `returned` | 409 `revision-not-eligible` | N/A |
| מקביליות | חמש בקשות אישור מקבילות לאותו שינוי | בדיוק 200 אחד, השאר 409, `AuditLog` אחד | נעילה ועדכון מותנה |
| אישור מול דחייה | אישור ודחייה מקבילים | בדיוק אחת מצליחה | נעילה |
| הרשאה | אנונימי: 401; מחובר שאינו אדמין: 403 | נדחה בכל הנקודות | N/A |
| הגנת בקשה | בלי anti-forgery או Origin זר | נדחית | N/A |
| תור | פריט עם שינוי קשר | `hasContactChange: true` | N/A |
| פרטיות | תגובות הצלחה ויומן | אין בהן מייל או נייד | N/A |

</intent-contract>

## Code Map

- `server/Api/Endpoints/SubmissionEndpoints.cs` -- קבוצת `/admin/submissions` (תור, פרטים, תמונה, `approve` עם 409 `revision-not-pending`, אכיפת `AdminPolicy`, ניתוח GUID בצורה `D`); כאן מוסיפים את שתי הנקודות החדשות ואת שדות 409.
- `server/Features/Profiles/SubmissionReviewService.cs`, `server/Infrastructure/SubmissionReviewRepository.cs` -- שירות ומאגר הסקירה: `GetAsync` (פרטים עם `SafeMarkdown.Clean`), `ApproveAsync` (טרנזקציה, נעילת שורת הפרופיל, `ApproveStatus`, `AuditLog`); להרחיב לפרטי `published`/`changes`/`contactChange` ולפעולות החדשות באותו דפוס.
- `server/Domain/Profiles/Profile.cs` -- `ProfileRevision` (`PendingEmail`, `PendingPhone`, `Status`), `ParticipantPrivate`; להוסיף `ContactVersion` ומיגרציה (README: `dotnet-ef`). `server/Domain/Audit/AuditLogEntry.cs` -- יומן (ללא תוכן אישי).
- `server/Features/Profiles/ProfileService.cs` -- `NormalizeEmail`, נרמול נייד וולידציה; לשימוש חוזר בנרמול הערכים הממתינים.
- `server/Infrastructure/ProfileRepository.cs` -- דוגמה לשינוי מייל ממתין (2.2) ולייחודיות `(cohort_id, email)`.
- `server/Features/Markdown/SafeMarkdown.cs` -- ניקוי קורות חיים.
- `server/Tests/SubmissionReviewTests.cs`, `ParticipantEditTests.cs`, `AuthTestSupport.cs` -- יצירת גרסאות במצבים שונים ישירות בבסיס הנתונים, `ApiClient` של אדמין עם TOTP ו-anti-forgery; להרחיב.
- אין לשנות: `client/`, הנקודות הציבוריות, נתיבי המשתתפת.

## Tasks & Acceptance

**Execution:**
- `server/Domain/Profiles/` ו-`server/Infrastructure/` -- `ParticipantPrivate.ContactVersion` ומיגרציה -- AD-17
- `server/Features/Profiles/` ו-`server/Infrastructure/` -- פרטי הגשה עם `published`, `changes` ו-`contactChange`; `hasContactChange` בתור; `latestPendingRevisionId` ב-409 של האישור -- FR-6, FR-7, FR-12, AR6
- `server/Features/Profiles/` ו-`server/Infrastructure/` -- אישור ודחייה של שינוי מייל או נייד בטרנזקציה עם נעילה, עדכונים מותנים, בדיקת ייחודיות ו-`AuditLog` -- AR17, AR19
- `server/Api/Endpoints/` -- `POST .../approve-contact-change` ו-`POST .../reject-contact-change` -- AR17
- `server/Tests/` -- בדיקות אינטגרציה לשורות המטריצה, כולל מרוצים, קורות חיים מסוכנים בשני הצדדים והוכחה שהגלריה והפרופיל הציבורי מציגים את הגרסה החדשה אחרי אישור -- AR6, AR21
- `README.md` -- הנקודות, קודי 409 והשדות החדשים -- תפעול

**Acceptance Criteria:**
- Given עדכון ממתין, when המנהלת פותחת אותו מהתור, then היא רואה את "מפורסם כעת" ואת "מוצע" ואת רשימת השדות ששונו, והתור מציין `update`.
- Given שינוי מייל או נייד ממתין, when המנהלת מסתכלת עליו, then הוא מוצג בנפרד, ואישור הפרסום אינו מחיל אותו אלא רק אישור מפורש שלה.
- Given אישור עדכון, when המנהלת מאשרת (עם `RevisionId`), then `ApprovedRevisionId` מוחלף בגרסה החדשה, הקודמת הופכת `superseded`, והגלריה והפרופיל מציגים את החדשה, והאישור נרשם ב-`AuditLog`.
- Given עדכון שהוחלף בינתיים בגרסה חדשה יותר, when המנהלת מנסה לאשר את הישנה, then 409 ומוחזר מזהה הגרסה הממתינה החדשה.

## Spec Change Log

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: כ-35 ממצאים מארבע שכבות (רבים כפולים) — high 0, medium 3, low 5, false 0
- findings:
  - `[medium]` `[patch]` שינוי מייל או נייד ממתין לא ניתן לגילוי אחרי אישור הפרסום (הגרסה יוצאת מהתור) — נוסף `GET /admin/contact-changes`
  - `[medium]` `[patch]` גרסה מאושרת עם גרסה ממתינה חדשה יותר עדיין ניתנת לטיפול לפי מזהה ומחילה בקשה מיושנת — נחסמה כ-`revision-not-eligible`
  - `[medium]` `[patch]` חסרות בדיקות: דחייה על גרסה מאושרת, `hasContactChange` לנייד בלבד, ושלא נכתבת שורת audit בכשל; אישור שאינו משנה ערך מקדם `contact_version` — תוקנו
  - `[low]` `[patch]` README: פעולות audit, היעדר אישור חלקי, 400 לפני 404, סדר `changes` — עודכן
  - `[low]` `[defer]` מסך הלקוח, מרוץ UniqueViolation, השוואת תמונה ב-SQL, cv שנדחה בשני הצדדים — נרשמו ב-deferred
  - `[low]` `[reject]` 400 לפני 404 בגוף שגוי (מתועד), `owner` null ב-`ApproveAsync` (נבדק: מחזיר 404), SingleAsync אחרי נעילה, אישור חלקי — לא סביר שייפגעו בשימוש רגיל או שהוחלט שלא לתמוך
## Verification

**Commands:**
- `dotnet build server` -- expected: 0 שגיאות
- `dotnet test server` -- expected: כל הבדיקות עוברות (דורש Docker)

## Auto Run Result

- **שינוי:** הרחבת נקודות הסקירה לעדכונים: פרטי הגשה מחזירים `published` (הגרסה המאושרת הנוכחית, קורות חיים מנוקים), `changes` (שדות ששונו, כולל תמונה) ו-`contactChange` (שינוי מייל או נייד ממתין, נפרד מהשוואת הפרסום); התור מוסיף `hasContactChange`; 409 של אישור גרסה שהוחלפה כולל `latestPendingRevisionId`. נוספו `POST /admin/submissions/{id}/approve-contact-change` ו-`reject-contact-change` (טרנזקציה עם נעילת הפרופיל, ערכים שהמנהלת ראתה חייבים להתאים, `email-taken` בלי חשיפה, קידום `contact_version`, `AuditLog`), ו-`GET /admin/contact-changes` לגילוי שינויים ממתינים גם אחרי אישור הפרסום. אישור הפרסום אינו מחיל שינוי מייל או נייד.
- **קבצים:** `server/Features/Profiles/SubmissionReviewService.cs`, `server/Infrastructure/SubmissionReviewRepository.cs`, `server/Api/Endpoints/SubmissionEndpoints.cs`, `server/Domain/Profiles/Profile.cs` ו-`AuditLogEntry.cs`, מיגרציית `ContactVersion`, `server/Tests/ContactChangeReviewTests.cs`, `README.md`.
- **סקירה:** 3 תיקוני medium (שינוי קשר ממתין שלא ניתן לגילוי אחרי אישור פרסום, בקשה מיושנת על גרסה מאושרת, בדיקות חסרות) ו-1 low הוחלו. 3 פריטים נדחו ל-`deferred` (מסך לקוח, בדיקת מרוץ UniqueViolation, השוואת תמונה ב-SQL). השאר נדחו כמתועד ביומן.
- **המלצה לסקירת המשך:** כן. סיכונים שלא אומתו: מרוץ בין שני פרופילים לאותו מייל חדש (נתיב `UniqueViolation` כגיבוי) לא נבדק; פרטי הגשה ממשיכים להיות זמינים רק לגרסאות `pending`.
- **אימות:** `dotnet build server` ללא שגיאות, `dotnet test server`: 538 מתוך 538 עוברות מול Postgres אמיתי.
- **סיכונים שנותרו:** מסך סקירת עדכון בלקוח (סיפור 5.6).
