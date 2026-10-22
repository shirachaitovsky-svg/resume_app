---
title: 'סיפור 6.4: הסרת מנגנון הקישור האישי לעריכה'
type: 'refactor'
created: '2026-10-23'
status: 'done'
baseline_revision: '3905802c5c0ca8bb4cac1a9e3edc426e39fe15e8'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-6-context.md'
warnings: [oversized]
deferred: []
---

<intent-contract>

## Intent

**Problem:** אחרי כניסה עם גוגל (6.1, 6.5, 6.2, 6.3), נשארו בשרת שני מסלולי כניסה ויצירה סותרים: קישור אישי לעריכה בקליק במייל, והגשה אנונימית דרך קישור הזמנה. הם עוקפים את הזהות לפי גוגל, מחזיקים הגנות ומשתני סביבה ייעודיים, ובדיקות רבות תלויות בהם.

**Approach:** שרת בלבד (הלקוח כבר לא משתמש בהם). מסירת מנגנון `edit-profile` (אסימון, בקשת קישור, החלפה לסשן, מייל ותבנית) והגשת `POST /invites/{token}/profiles`, כך שיצירה ועריכה נעשות רק דרך סשן גוגל. נשארים אסימוני `contact-decision`, תשתית ה-Outbox, `GET /invites/{token}`, והחזרה לתיקון עם קישור לאתר בלבד. בדיקות שזרעו פרופיל דרך המנגנון עוברות לעוזר זריעה משותף אחד, בלי להחליש כיסוי.

## Boundaries & Constraints

**Always:**
- סדר העבודה: קודם עוזר זריעה משותף בתמיכת הבדיקות (יצירת מחזור, פרופיל, גרסה `pending` או מאושרת, ו-`ParticipantPrivate` ישירות ב-`AppDbContext`, ועוזר כניסה של משתתפת לפי הספק המדומה עם `google_sub` מקושר), והעברת כל הבדיקות הקיימות אליו. רק אחר כך מסירת הקוד, ומחיקת בדיקות שבודקות את המנגנון הישן עצמו.
- להסיר מהשרת: אסימון `edit-profile` ו-`EditProfilePurpose` (הנפקה, צריכה, הנפקה מחדש וביטול בהסרת פרופיל), בקשת הקישור האישי לעריכה (נקודת קצה, שירות `EditLinkService`, `EditLinkEndpoints`), `POST /profiles/session` ו-`POST /profiles/session/logout`, תבנית המייל `EditLinkEmail` והענף שלה ב-`EmailOutboxProcessor` ו-`TemplateKey` (שורות `edit-link` קיימות ב-Outbox נסגרות כ-`dead` עם קטגוריה `unknown-template`), סקופי הגבלת הקצב ומשתני הסביבה של הקישור (`RATE_LIMIT_EDIT_*` ודומיהם) והרשומות ב-`PublicFormGuard`, `RequestIntegrityMiddleware`, `AuthPipelineExtensions`, `DependencyInjection`.
- להסיר `POST /invites/{token}/profiles` (ההגשה האנונימית) יחד עם הגנת Turnstile והגבלות הקצב הייעודיות לה (`ProfileSubmitIp` אם אין לה עוד שימוש מחוץ ל-`POST /me/profile`: אם יש, להשאיר). `POST /me/profile` ו-`GET /invites/{token}` נשארים ללא שינוי.
- מדיניות `Participant`: רק `purpose = google-signin` ודורשת `profile_id`. `PersonalToken` נשאר ל-`contact-decision` (ול-`verify-contact-email` של המבקר).
- מייל החזרה לתיקון (`SubmissionReturnedEmail`): קישור לאתר (`PUBLIC_BASE_URL/me`) בלי אסימון, והנוסח מתאים לכניסה עם גוגל. אין בשורת ה-Outbox ובמייל אסימון.
- בדיקות: לא מחלישים. כל מה שנבדק דרך המנגנון הישן ועדיין רלוונטי (אישור, החזרה לתיקון, הסרה, מסירה, עריכה כגרסה חדשה, שינוי נייד) נבדק דרך העוזר החדש. בדיקות של המנגנון הישן עצמו נמחקות במפורש, וכל קובץ שנמחק או קוצץ נרשם ב-`## Implementation Notes` או ב-`Spec Change Log`.
- תיעוד: README (נקודות קצה, משתני סביבה) ו-ARCHITECTURE-SPINE (AD-5, AD-6, AD-12 ו-AR10 ב-`epics.md`) מעודכנים כך שאין אזכור של `edit-profile`, בקשת קישור אישי לעריכה או החלפה לסשן, מלבד מה שהוחלף במפורש.
- ריצת שרת מלאה אחת בלבד בכל פעם, ללא הרג תהליכי `testhost`, ו-`TestResults` לא נכנס לקומיט.

**Never:**
- לא נוגעים בלקוח, בכניסה עם גוגל, ב-`POST /me/profile` או ב-`GET/PUT /me/profile`.
- לא מסירים אסימוני `contact-decision` ולא את תשתית ה-Outbox והמייל הכללית.
- לא מוחקים בדיקה שאינה מכסה את המנגנון הישן.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| נתיבים שהוסרו | `POST /profiles/session`, בקשת קישור אישי לעריכה, `POST /invites/{token}/profiles` | אינם קיימים (404 או 401 לפי מדיניות ברירת המחדל, כמו כל נתיב לא ממופה) | N/A |
| סשן קישור ישן | עוגיית משתתפת עם `purpose=edit-profile` | נדחית: 401 ב-`/me/profile` | N/A |
| עריכה | סשן גוגל עם `profile_id` | `GET/PUT /me/profile` עובדים כמו לפני | N/A |
| יצירה | סשן גוגל בלי פרופיל | `POST /me/profile` עובד כמו לפני | N/A |
| החזרה לתיקון | מנהלת מחזירה הגשה | מייל עם קישור ל-`/me` בלי אסימון, ושורת Outbox בלי אסימון | N/A |
| Outbox ישן | שורה עם תבנית `edit-link` | נסגרת כ-`dead` עם `unknown-template`, בלי שליחה | N/A |
| הסרת פרופיל | מנהלת מסירה פרופיל | מבטל אסימוני `contact-decision` ובקשות פתוחות, בלי התייחסות ל-`edit-profile` | N/A |
| מסירת פרטי קשר | שרשרת בקשה, החלטה ואישור מסירה | ממשיכה לעבוד ללא שינוי | N/A |

</intent-contract>

## Code Map

- `server/Api/Endpoints/{EditLinkEndpoints.cs,ParticipantEndpoints.cs,ProfileEndpoints.cs,CohortEndpoints.cs}` -- הנתיבים להסרה (`/profiles/session`, בקשת קישור, `/invites/{token}/profiles`) ו-`GET /invites/{token}` שנשאר; `/me/profile` נשאר
- `server/Features/Email/{EditLinkService.cs,EditLinkEmail.cs,EmailOutboxProcessor.cs,PersonalTokens.cs,IEmailOutbox.cs,SubmissionReturnedEmail.cs}` -- שירות, תבנית וענף עיבוד להסרה; `PersonalTokens` ו-`IPersonalTokenIssuer` מצומצמים ל-`contact-decision` ו-`verify-contact-email`; מייל החזרה לתיקון מקבל קישור לאתר
- `server/Domain/Email/EmailOutboxEntry.cs`, `server/Infrastructure/{EmailOutboxRepository.cs,ProfileAdminRepository.cs,DependencyInjection.cs}` -- קבועים, מימוש והסרה בפרופיל; הסרת רישומי DI וסקופי קצב
- `server/Api/Security/{IdentityServiceExtensions.cs,PublicFormGuard.cs,RequestIntegrityMiddleware.cs,AuthPipelineExtensions.cs}`, `server/Features/PublicForms/IRateLimiter.cs`, `server/Api/Program.cs` -- מדיניות `Participant`, סקופים וניתובים אנונימיים
- `server/Tests/{AuthTestSupport.cs,EditLinkTests.cs,EditLinkRobustnessTests.cs,ParticipantEditTests.cs,ProfileSubmissionTests.cs,ProfileOptionalFieldsTests.cs,ProfileRemovalTests.cs,ReturnForCorrectionTests.cs,SubmissionReviewTests.cs,PublicFormProtectionTests.cs,Cohort*Tests.cs,ContactRequest*Tests.cs,ContactDecisionTests.cs,ContactDeliveryTests.cs,EmailSenderTests.cs,GoogleSignInTests.cs,MyProfileCreationTests.cs}` -- תלויות במנגנון הישן, או משתמשות בו לזריעה; להעביר לעוזר המשותף
- `README.md`, `_bmad-output/planning-artifacts/{epics.md,architecture/**/ARCHITECTURE-SPINE.md,prds/**/prd.md,ux-designs/**/EXPERIENCE.md}` -- הפניות להסרה

## Tasks & Acceptance

**Execution:**
- `server/Tests/AuthTestSupport.cs` (או קובץ תמיכה חדש) -- עוזר זריעה ועוזר כניסה לפי הספק המדומה, והעברת כל הבדיקות הקיימות אליהם -- שלב 1
- `server/Api`, `server/Features`, `server/Domain`, `server/Infrastructure` -- הסרת המנגנון הישן והגשת ההזמנה, צמצום המדיניות, ותיקון מייל החזרה לתיקון -- שלב 2
- `server/Tests` -- מחיקת בדיקות של המנגנון עצמו, בדיקות חדשות לנתיבים שהוסרו (404/401), לסשן ישן, לסגירת שורת `edit-link` ב-Outbox ולמייל החזרה לתיקון -- כיסוי
- `README.md`, קבצי האפיון -- תיעוד והסרת הפניות -- שלב 3

**Acceptance Criteria:**
- Given חיפוש בקוד השרת, when מחפשים `EditProfilePurpose`, `edit-profile`, `EditLink`, `profiles/session` ו-`invites/{token}/profiles`, then אין התייחסות שנותרה, פרט להערות הסבר מפורשות.
- Given כל בדיקות השרת, when הן רצות (ריצה מלאה אחת), then כולן עוברות ואין בדיקה שנמחקה בלי ציון.
- Given `ng test --watch=false` ו-`ng build`, when הם רצים, then עוברים ללא שינוי.

## Spec Change Log

## Review Triage Log

### 2026-10-23 — Review pass
- verdicts: 15 findings (מקובצים לפי סיבת שורש מארבע השכבות) — high 0, medium 2, low 10, false 3, maybe-false 0
- findings:
  - `[medium]` `[patch]` בדיקת הנתיבים שהוסרו (`RetiredPersonalLinkTests`) מקבלת 401, ולכן נתיב `/profiles/session` שיחזור ויחזיר `link-invalid` יעבור אותה — להסיר את 401 מהמותר או לאמת שהגוף אינו `link-invalid`
  - `[low]` `[patch]` `client/README.md` עדיין מתאר את `/join/:token` כטופס הגשה — עודכן
  - `[low]` `[patch]` `RATE_LIMIT_EDIT_SESSION_IP` נשאר ב-`HighLimits` של בדיקות — הוסר, ומחרוזות ושיטות שנשארו ללא שימוש (`Strings.LinkInvalid` ודומיהן) הוסרו אחרי grep
  - `[medium]` `[reject]` פרופילים ישנים (בלי `GoogleSub`) לא יכולים להיכנס, והחזרה לתיקון שולחת אותם ל-`/me` שם יקבלו 409 `email-taken` — נרשם כבר ב-`deferred` של 6.5, ואין פרופילי ייצור
  - `[low]` `[reject]` כל מי שיש לו חשבון גוגל יכול ליצור פרופיל בכל מחזור, ושמירה חלשה מספאם (אין הגבלה לפי חשבון) — החלטת מוצר מאושרת, פרסום דורש אישור המנהלת, ויש הגבלה לפי IP
  - `[low]` `[reject]` נתוני עבר שלא נוקו (שורות `edit-profile` ב-`personal_tokens`, שורות `edit-link` ב-Outbox, מוני הגבלה) — פוקעים או נסגרים בעצמם (`unknown-template`), ואין נתוני ייצור
  - `[low]` `[reject]` ה-`OnValidatePrincipal` חוזר על מדיניות `Participant`, ואינו מתועד או נספר — הגנה כפולה מכוונת, וגם נבדק בעוגייה ישנה
  - `[low]` `[reject]` מייל ההחזרה לתיקון אינו מציין את החשבון או הכתובת, ומחסרים אזהרות — המייל נשלח לכתובת החשבון עצמה
  - `[low]` `[reject]` שינוי בנאי `RateLimitOptions` הפוזיציוני — מהדר ובדיקות עוברים, ללא נזק
  - `[low]` `[reject]` אי אימות מחיקת העוגייה, ושימוש חוזר בקוד הערות XML ישנות — קוסמטי
  - `[low]` `[reject]` אין הודעה על משתני סביבה שהוסרו — נרשם ב-README
  - `[low]` `[reject]` `/me` למבקרים לא מחוברים עלול להיות מבוי סתום — הלקוח מציג כניסה ב-`/me` ומחזיר אליו אחרי הכניסה
  - `[false]` `[reject]` "עוגייה בעלת purpose ישן עדיין מאמתת בנתיבים שאינם משתמשים ב-Participant" — ה-hook ברמת הסכמה דוחה אותה בכל בקשה
  - `[false]` `[reject]` "מסמכי האפיון לא עודכנו" — AD-5, AD-6, AR10 ו-PRD כבר עודכנו בהצעת השינוי, ונבדק ב-grep
  - `[false]` `[reject]` "שורת submission-returned ישנה נשלחת מחדש עם הגוף החדש" — רצוי: הגוף ללא אסימון

## Implementation Notes

- Shared helper: `server/Tests/ParticipantSeeding.cs` (cohort and profile seeding straight into `AppDbContext`, `SignInAsync` through the fake Google provider, `ClientWithForgedSession`, and `/me/profile` creation helpers). All former seeding through the invite submission and the personal link moved to it.
- Renamed (git mv) and migrated to the `submission-returned` template, since they test the generic Outbox/worker: `EditLinkTests.cs` -> `EmailOutboxTests.cs`, `EditLinkRobustnessTests.cs` -> `EmailOutboxRobustnessTests.cs`.
- Deleted tests (they tested the removed mechanism itself): in EmailOutbox tests, the whole edit-link request endpoint section (registered/unregistered email, invalid email, malformed body, parallel requests, removed profiles, two cohorts, email and IP limits, turnstile/origin/csrf), the newer-token-revokes-earlier test, the two-rows-one-profile token test, the link-fragment unit test; in ParticipantEditTests: valid/reused/invalid/concurrent token exchange, removed-profile exchange, session exchange protection and rate limit, logout, raw-token-leak test; in ProfileRemovalTests: token issued after removal cannot start a session; in ProfileOptionalFieldsTests: multipart with bad invite token (404); in ProfileSubmissionTests: invalid email, duplicate email 202, same email in another cohort, five parallel identical submissions, revoked/missing/malformed invite tokens, bad body with bad token, missing anti-forgery (kept in MyProfileCreationTests); in PublicFormProtectionTests: Turnstile on submission, failed-turnstile counting, per-email limit. ProfileSubmissionTests, ProfileOptionalFieldsTests and the profile-IP limit test were retargeted to `POST /me/profile` (201 instead of 202, email from the Google session).
- Retired names in tests live only in `RetiredPersonalLinkTests.cs` (assembled from pieces) with new tests: removed routes are not mapped, a cookie with the retired purpose is refused with 401 (the Participant cookie now rejects any purpose other than `google-signin`), the invite check still works, remaining token purposes are contact-only. A leftover `edit-link` outbox row closes as `dead` / `unknown-template`.
- Server: removed `EditLinkEndpoints`, `EditLinkService`, `EditLinkEmail`, `/profiles/session` (+logout), `POST /invites/{token}/profiles`, `ProfileService.SubmitAsync`, `IProfileRepository.TryAddAsync`, edit token issue/redeem, `EditProfilePurpose`, `EditLinkTemplate`, rate-limit scopes/options/env vars (`RATE_LIMIT_PROFILE_EMAIL`, `RATE_LIMIT_EDIT_*`). `EditLinkRecipient` renamed `ReturnedMailRecipient`. The returned mail now links to `{PUBLIC_BASE_URL}/me` with no token. `GET /profiles/csrf` stays (the client uses it for `/me/*`).
- Docs: README updated. Planning docs (ARCHITECTURE-SPINE, epics AR10) already had the new wording.

## Design Notes

סדר העבודה בשני שלבים מונע את הסיכון המרכזי: עשרות בדיקות שזורעות פרופיל דרך `POST /invites/{token}/profiles` או סשן קישור. קודם מעבירים אותן לעוזר ישיר, ורק אז מסירים. שורות Outbox ישנות מתבנית `edit-link` נסגרות על ידי המנגנון הקיים של `unknown-template`, ולכן אין צורך במיגרציה.

## Verification

**Commands:**
- `dotnet build server` -- expected: בנייה מצליחה בלי אזהרות חדשות
- `dotnet test server` -- expected: כל הבדיקות עוברות (ריצה אחת בכל פעם, כ-11 דקות, ברקע)
- `cd client && npx ng test --watch=false` -- expected: כל הבדיקות עוברות
- `grep -rn "EditProfilePurpose\|EditLink\|profiles/session\|invites/{token}/profiles" server --include=*.cs` -- expected: אין התאמות, פרט להערות מפורשות

## Auto Run Result

Status: done

- **שינוי שמומש (שרת בלבד):** מסירת אסימון `edit-profile` והמנגנון שסביבו (בקשת קישור אישי לעריכה, `POST /profiles/session` והיציאה הישנה, `EditLinkEmail` והענף שלו ב-Outbox, הגשת `POST /invites/{token}/profiles`, סקופי קצב ומשתני סביבה ייעודיים); מדיניות `Participant` וסכמת העוגייה מקבלות רק `google-signin`; מייל החזרה לתיקון מקשר ל-`/me` בלי אסימון; שורות `edit-link` ישנות ב-Outbox נסגרות `dead`. נוסף עוזר זריעה משותף (`ParticipantSeeding.cs`) ו-`RetiredPersonalLinkTests`. בדיקות המנגנון הישן נמחקו או הועברו (`EditLinkTests` הפך ל-`EmailOutboxTests`), והפירוט ב-`## Implementation Notes`.
- **ממצאי סקירה:** 15 ממצאים מקובצים. תוקנו: בדיקת הנתיבים שהוסרו לא זיהתה handler שחוזר (נוספה בדיקת גוף `link-invalid`), `client/README.md` ו-`AuthTestSupport` ישנים. נדחו 10 ממצאי low ו-3 false עם נימוק בלוג, ו-2 medium/low עיקריים: פרופילים ישנים בלי `GoogleSub` (נרשם ב-6.5), והחלטת מוצר ליצירה בכל מחזור.
- **המלצה לסקירת המשך:** כן. סיכון לא מאומת: רגרסיה בנתיב שנעלם ולא נבדק בדפדפן; אין ממשק מחובר לפרופילים ישנים.
- **אימות:** `dotnet test server`: 816 עברו (ריצה מלאה אחת על קוד הייצור הסופי); אחרי התיקונים רצו רק בדיקות הקבצים שהשתנו (14 עברו), כי הקוד לא השתנה. `ng test`: 371 עברו, `ng build` נקי.
- **סיכונים נותרים:** `PUT /me/profile` עדיין דורש `email` שנשלח מהלקוח. נתיבים שהוסרו מחזירים 401/404/405 לפי המדיניות. אזהרת CS8604 ב-`EmailSenderTests` לא קשורה.
