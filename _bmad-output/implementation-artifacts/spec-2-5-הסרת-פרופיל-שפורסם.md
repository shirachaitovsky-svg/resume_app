---
title: 'סיפור 2.5: הסרת פרופיל שפורסם'
type: 'feature'
created: '2026-10-22'
status: 'done'
baseline_revision: '16cc8ee7e06f6f919069ddf293ba319fd8728e1e'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-resume_app-2026-10-21/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-1-11-אישור-פרסום.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-2-2-כניסה-ועריכה-כגרסה-חדשה.md'
warnings: []
deferred:
  - summary: >-
      בקשות פרטי קשר פתוחות אינן מועברות ל-`expired` בהסרה, כי הטבלה והמכונה של אפיק 3 טרם קיימות
    evidence: |-
      AD-13 מחייב זאת; סיפור 3.1 חייב להוסיף את העברת הבקשות הפתוחות של הפרופיל ל-`expired` בתוך טרנזקציית ההסרה
    location: >-
      server/Infrastructure/ProfileAdminRepository.cs
    severity: medium
  - summary: >-
      אין בדיקת rollback לכשל באמצע טרנזקציית ההסרה (כתיבת `audit_log` שנכשלת), ואין מסך הסרה עם חלון אישור בלקוח
    evidence: |-
      הטרנזקציה נבדקת בהצלחה, 409 ומרוץ; חלון האישור "הפרופיל יוסר מהאתר הציבורי." הוא בלקוח (סיפור 5.6)
    location: >-
      server/Infrastructure/ProfileAdminRepository.cs
    severity: low
  - summary: >-
      אין נתיב שחזור ייעודי לפרופיל שהוסר, והסרה אינה מחליפה גרסאות ממתינות
    evidence: |-
      פרופיל חוזר ל-Active רק באישור גרסה (סיפור 1.11); הסימון `profileRemoved` ו-`reactivated` מבהיר זאת. להחליט אם נדרש שחזור ייעודי בסיפור 4.1
    location: >-
      server/Infrastructure/SubmissionReviewRepository.cs
    severity: low
---

<intent-contract>

## Intent

**Problem:** תוכן שפורסם ושאסור שיוצג עוד אי אפשר להסיר מהאתר הציבורי מיידית, ואין למנהלת רשימה של הפרופילים כדי לבחור ולבדוק מה הוסר.

**Approach:** להוסיף ב-`server/` נקודת ניהול `POST /admin/profiles/{profileId}/remove` שהופכת `Visibility` ל-`removed` בעדכון מותנה בטרנזקציה אחת, מבטלת את אסימוני העריכה של הפרופיל וכותבת `AuditLog`, בלי למחוק נתונים; וכן `GET /admin/profiles` שמאפשרת למנהלת לראות פרופילים פעילים ושהוסרו. חלק השרת בלבד.

## Boundaries & Constraints

**Always:** כל הנקודות תחת `/admin/profiles` דורשות `AdminPolicy`; הסרה דורשת anti-forgery ו-`Origin`. מזהה הפרופיל GUID בצורה `D` בלבד; מזהה פגום, לא קנוני או לא קיים: 404 `problem+json` אחיד. **הסרה:** בטרנזקציה אחת עם נעילת שורת הפרופיל (כבאישור וההחזרה): עדכון מותנה `UPDATE profiles SET visibility = 'Removed' WHERE id = @id AND visibility = 'Active'`; אם לא עודכנה שורה והפרופיל קיים: 409 עם `code: "profile-not-active"` ושום דבר לא משתנה; ביטול כל אסימוני `edit-profile` שטרם נוצלו ושלא בוטלו של הפרופיל (`revoked_at`); כתיבת `AuditLog` (`remove-profile`, סוג ישות `profile`, מזהה הפרופיל, מזהה המנהלת, בלי תוכן אישי); ללא שינוי ב-`ApprovedRevisionId`, בגרסאות, ב-`ParticipantPrivate`, בתמונות או בנתונים אחרים (הסרה אינה מחיקה). מקביליות: חמש הסרות מקבילות לאותו פרופיל: בדיוק 200 אחד, השאר 409, `AuditLog` אחד. תגובת הצלחה: 200 עם `profileId` ו-`visibility: "removed"`, בלי מייל או נייד. שורות `EmailOutbox` שממתינות לפרופיל שהוסר ממשיכות להיות `dead` עם `no-recipient` כבסיפור 2.1 (בלי שינוי בקוד ה-worker). **השפעה על המשתתפת:** סשן משתתפת קיים של פרופיל שהוסר: `GET /me/profile`, `PUT /me/profile` ו-`POST /profiles/session` מחזירים 401 `session-invalid` או `link-invalid` ללא שימוש באסימון (כבסיפור 2.2) ומוחקים את העוגיה. **השפעה ציבורית:** הפרופיל נעלם מ-`GET /profiles`, מ-`GET /profiles/{profileId}`, מנקודת התמונה ומ-`GET /cohorts` (המחזור נעלם אם לא נשארו בו פרופילים ציבוריים); אין תגובה ציבורית שחושפת קיום פרופיל שהוסר; אין כל שינוי נדרש בשאילתה הציבורית שמסננת `Visibility = Active`. **אישור פרסום מחדש:** אישור גרסה של פרופיל שהוסר ממשיך להחזיר אותו ל-`Active` (התנהגות סיפור 1.11) ואינו משתנה. **בקשות פרטי קשר פתוחות:** הטבלה והמכונה של אפיק 3 טרם קיימות, ולכן אין מה לעדכן כאן; נרשם ב-`deferred` ובתיעוד שסיפור 3.1 חייב להעביר בקשות פתוחות ל-`expired` בהסרה. **רשימת הפרופילים:** `GET /admin/profiles` (`AdminPolicy`, `Cache-Control: no-store`): `{ items, total }`, פרמטרים `visibility` (`active`, `removed`, או `all`; ברירת מחדל `active`; אחר: 400), `cohort` (GUID `D`, אופציונלי), `limit` (1 עד 100, ברירת מחדל 50, מעל 100 נחתך), `offset` (0 ומעלה), מהחדש לישן לפי זמן יצירת הפרופיל ואז מזהה. פריט: `profileId`, `name`, `productName` (מהגרסה המאושרת האחרונה, ואם אין, מהגרסה האחרונה), `cohortName`, `visibility`, `hasApproved`, `createdAt`. בלי מייל, נייד, קורות חיים או תוכן אחר בפריט. הנתונים של פרופיל שהוסר נשארים זמינים למנהלת: גם דרך `GET /admin/profiles?visibility=removed`, ופרטי ההגשות שלו דרך נקודות הסקירה הקיימות לפי מזהה גרסה כמו קודם. ללא PII ביומנים. אין שינוי סכמה נדרש מלבד אם מתגלה צורך באינדקס.

**Never:** אין מחיקת נתונים, אין החזרת פרופיל שהוסר בנקודה ייעודית (אישור מחדש בלבד), אין מסכי לקוח או חלון אישור (בלקוח), אין שינוי בנקודות הציבוריות, אין מייל למשתתפת על ההסרה, אין עבודה על בקשות פרטי קשר (אפיק 3).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| הסרה | אדמין, פרופיל `Active` | 200; `Visibility = Removed`; אסימוני עריכה בוטלו; `AuditLog`; נתונים נשמרו | N/A |
| כבר הוסר | פרופיל `Removed` | 409 `profile-not-active`; ללא שינוי ובלי `AuditLog` נוסף | 409 |
| לא קיים | GUID אקראי, פורמט לא קנוני | 404 אחיד | N/A |
| מקביליות | חמש הסרות מקבילות | בדיוק 200 אחד; `AuditLog` אחד | נעילה ועדכון מותנה |
| גלריה ופרופיל | אחרי ההסרה | הפרופיל לא מופיע ב-`/profiles`, `/profiles/{id}` (404), `/photo` (404), והמחזור נעלם מ-`/cohorts` אם ריק | N/A |
| אסימון עריכה | אסימון תקף שטרם נוצל לפני ההסרה | לא ניתן להחלפה אחרי הסרה (401 `link-invalid`) | N/A |
| סשן קיים | משתתפת עם עוגיה אחרי ההסרה | 401 ב-`/me/*`, העוגיה נמחקת | N/A |
| נתונים | אחרי ההסרה | גרסאות, תמונות ו-`participant_private` קיימים; `ApprovedRevisionId` ללא שינוי | N/A |
| פרופיל ללא גרסה מאושרת | פרופיל `Active` עם הגשה ממתינה בלבד | ניתן להסרה כרגיל | N/A |
| אישור מחדש | אישור גרסה של פרופיל שהוסר | `Visibility = Active` (כבסיפור 1.11) | N/A |
| Outbox | שורה ממתינה לפרופיל שהוסר | `dead` עם `no-recipient` | N/A |
| רשימה | `visibility=removed` | רק פרופילים שהוסרו; בלי PII | N/A |
| רשימה: פרמטרים | `all`, `cohort`, `limit`, `offset` | סינון, עימוד ו-`total` נכונים | `visibility` לא מוכר או `offset` שלילי: 400 |
| הרשאה | אנונימי: 401; מחובר שאינו אדמין: 403 | נדחה בכל הנקודות | N/A |
| הגנת בקשה | הסרה בלי anti-forgery או Origin זר | נדחית | N/A |
| פרטיות | תגובות ויומן | אין מייל, נייד או תוכן אישי | N/A |

</intent-contract>

## Code Map

- `server/Api/Endpoints/SubmissionEndpoints.cs` -- תבנית נקודות אדמין עם `AdminPolicy`, ניתוח GUID `D`, 404 אחיד, 409 עם `code`, וקבלת מזהה המנהלת מ-`NameIdentifier`; נקודות הפרופילים החדשות בקובץ חדש לפי אותו דפוס.
- `server/Infrastructure/SubmissionReviewRepository.cs` -- `ApproveAsync` ו-`ReturnAsync`: טרנזקציה, נעילת שורת הפרופיל (`FOR UPDATE`), עדכון מותנה, `AuditLog`; להשתמש באותו דפוס ל-`RemoveAsync` (במאגר ובשירות מתאימים, או במאגר/שירות חדשים לפרופילים).
- `server/Domain/Profiles/Profile.cs` -- `Profile` (`Visibility`: `Active`/`Removed`, `ApprovedRevisionId`), `ProfileRevision`; `server/Domain/Email/EmailOutboxEntry.cs` -- `PersonalToken` ישות עם `RevokedAt`; `server/Domain/Audit/AuditLogEntry.cs` -- קבועי פעולות; להוסיף `remove-profile`.
- `server/Infrastructure/EmailOutboxRepository.cs` -- `PersonalTokenIssuer` ביטול אסימונים (דפוס ביטול קודמים); `TryRedeemEditTokenAsync` כבר דוחה פרופיל שאינו `Active`.
- `server/Infrastructure/PublicProfileReader.cs` -- `VisibleProfiles()` כבר מסנן `Visibility = Active`; לא לשנות, רק להוכיח בבדיקה.
- `server/Api/Endpoints/ParticipantEndpoints.cs`, `server/Infrastructure/ProfileRepository.cs` -- `GET`/`PUT /me/profile` ו-`session-invalid` ו-`SignOutAsync` כבר קיימים; להוכיח בבדיקה שסשן של פרופיל שהוסר נדחה ושהעוגיה נמחקת.
- `server/Tests/SubmissionReviewTests.cs`, `PublicGalleryTests.cs`, `ParticipantEditTests.cs`, `EditLinkTests.cs`, `AuthTestSupport.cs` -- אדמין עם TOTP ו-anti-forgery, יצירת פרופילים, גרסאות ואסימונים במצבים שונים; להרחיב.
- אין לשנות: `client/`, הנקודות והשאילתות הציבוריות, מנגנון המיילים.

## Tasks & Acceptance

**Execution:**
- `server/Domain/Audit/` -- קבוע `remove-profile` -- AR19
- `server/Features/Profiles/` ו-`server/Infrastructure/` -- `RemoveAsync` בטרנזקציה עם נעילת שורת הפרופיל, עדכון מותנה, ביטול אסימוני עריכה ו-`AuditLog`; ושאילתת רשימת פרופילים עם הקרנה, סינון ועימוד -- FR-19, AR5, AD-13, AD-14
- `server/Api/Endpoints/` -- `POST /admin/profiles/{profileId}/remove` ו-`GET /admin/profiles` -- FR-19
- `server/Tests/` -- בדיקות אינטגרציה לשורות המטריצה: מרוץ, אסימון וסשן אחרי הסרה, שינוי בציבור, נתונים שנשמרו, פרטיות והרשאות -- AR6, AR21
- `README.md` -- הנקודות, `profile-not-active`, והחוב של בקשות פרטי קשר לסיפור 3.1 -- תפעול

**Acceptance Criteria:**
- Given פרופיל מפורסם, when המנהלת מסירה אותו, then `Visibility` הופך `removed` בעדכון מותנה בטרנזקציה אחת ונרשם ב-`AuditLog`.
- Given פרופיל שהוסר, when מבקר מבקש אותו או את הגלריה, then הוא לא מופיע ולא נחשף בשום תגובה ציבורית.
- Given הסרה, when בוצעה, then אסימוני העריכה של הפרופיל מתבטלים והנתונים נשמרים למנהלת ואינם נמחקים.
- Given משתמש שאינו אדמין, when מנסה להסיר פרופיל, then 401 או 403.

## Spec Change Log

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: כ-30 ממצאים מארבע שכבות (רבים כפולים) — high 0, medium 3, low 6, false 0
- findings:
  - `[medium]` `[patch]` אישור גרסה ממתינה של פרופיל שהוסר מפרסם אותו מחדש בשקט — נוספו `profileRemoved` ב-API ו-`reactivated` בתגובת האישור
  - `[medium]` `[patch]` שינוי קשר ממתין של פרופיל שהוסר מופיע ב-`/admin/contact-changes` וניתן לאישור — הוחרג ונחסם כ-`revision-not-eligible`
  - `[medium]` `[patch]` בדיקות חסרות: תקרת עימוד, ביטול אסימונים (נוצל ומטרות אחרות), 404 אחיד מלא, אסימון שהונפק אחרי הסרה, tie-break — נוספו
  - `[low]` `[patch]` מחרוזות visibility מפוזרות, שורת `Forbidden` בפורמט שבור ו-README (אין שחזור ייעודי, הסרה לא מחליפה גרסאות) — תוקנו
  - `[low]` `[defer]` בדיקת rollback באמצע טרנזקציה, נתיב שחזור ייעודי, מסך לקוח — נרשמו ב-deferred (בקשות פרטי קשר פתוחות נרשמו כבר בפרונטמטר)
  - `[low]` `[reject]` שאילתות קורלטיביות וקאונט נפרד, נפילה ל-`string.Empty`, פרמטרי limit לא מספריים (נבדק: 400 מהמבנה), הערה ישנה במאגר, סיבת הסרה ושדה `removedAt` — לא סביר שייפגעו בשימוש רגיל
## Verification

**Commands:**
- `dotnet build server` -- expected: 0 שגיאות
- `dotnet test server` -- expected: כל הבדיקות עוברות (דורש Docker; ייתכן שיידרשו כ-5 דקות)

## Auto Run Result

- **שינוי:** `POST /admin/profiles/{profileId}/remove` (אדמין בלבד, anti-forgery ו-Origin): בטרנזקציה אחת עם נעילת שורת הפרופיל, עדכון מותנה `Active` ל-`Removed`, ביטול אסימוני עריכה שטרם נוצלו וכתיבת `AuditLog` (`remove-profile`); 409 `profile-not-active` להסרה חוזרת ו-404 אחיד למזהה לא תקין. כלום אינו נמחק, `ApprovedRevisionId` לא משתנה, והפרופיל נעלם מכל הנקודות הציבוריות. `GET /admin/profiles` (סינון `visibility`, מחזור, עימוד; בלי PII) לבחירה ולבדיקה של מה שהוסר. סשן משתתפת של פרופיל שהוסר נדחה ב-`/me/*`, ואסימון שהונפק אחרי הסרה אינו מתחלף. נוספו `profileRemoved` בתור ובפרטי ההגשה ו-`reactivated` בתגובת האישור, ושינויי מייל או נייד ממתינים של פרופיל שהוסר לא מופיעים ולא ניתנים לאישור (`revision-not-eligible`).
- **קבצים:** `server/Features/Profiles/ProfileAdminService.cs`, `server/Infrastructure/ProfileAdminRepository.cs`, `server/Api/Endpoints/ProfileAdminEndpoints.cs`, `SubmissionEndpoints.cs`, `SubmissionReviewRepository.cs`, `server/Domain/Audit/AuditLogEntry.cs`, `server/Tests/ProfileRemovalTests.cs`, `README.md`.
- **סקירה:** 3 תיקוני medium (פרסום מחדש בשקט באישור, שינוי קשר של פרופיל שהוסר, בדיקות חסרות) ו-1 low קבוצתי הוחלו. 2 פריטים נוספו ל-`deferred` (בדיקת rollback באמצע טרנזקציה, נתיב שחזור ייעודי), בנוסף לבקשות פרטי קשר פתוחות. השאר נדחו כמתועד ביומן.
- **המלצה לסקירת המשך:** כן. סיכון שלא אומת: בקשות פרטי קשר פתוחות אינן מועברות ל-`expired` בהסרה כי אפיק 3 טרם קיים; סיפור 3.1 חייב להוסיף את זה בתוך טרנזקציית ההסרה.
- **אימות:** `dotnet build server` ללא שגיאות, `dotnet test server`: 603 מתוך 603 עוברות מול Postgres אמיתי.
- **סיכונים שנותרו:** אין עדיין מסך הסרה וחלון אישור בלקוח (סיפור 5.6).
