---
title: 'סיפור 6.5: יצירת פרופיל בשרת לפי כניסה עם גוגל'
type: 'feature'
created: '2026-10-22'
status: 'done'
baseline_revision: '494680ea63418ac556619f7d64d2008927c918a0'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-6-context.md'
warnings: []
deferred:
  - summary: >-
      משתתפת שכבר הגישה פרופיל דרך קישור הזמנה (בלי GoogleSub) ונכנסת עם גוגל באותו מייל מקבלת 409 email-taken ואין דרך לקשר.
    evidence: |-
      POST /me/profile יוצר רק פרופיל חדש, ו-(CohortId, Email) ייחודי. אין נתוני ייצור, ולכן אין הגירה, אבל נדרש מסלול קישור לפי מייל מאומת לפני עלייה לאוויר אם קיימים פרופילים ישנים.
    location: >-
      server/Infrastructure/ProfileRepository.cs (TryAddForGoogleAsync)
    severity: medium (unverified)
  - summary: >-
      מצב Busy של עיבוד תמונה וענפי ה-unique-violation ב-TryAddForGoogleAsync אינם נבדקים ישירות.
    evidence: |-
      בדיקת המרוץ מכסה חלקית את ux_profiles_google_sub, ואין בדיקה שמכריחה Busy.
    location: >-
      server/Tests/MyProfileCreationTests.cs
    severity: low
---

<intent-contract>

## Intent

**Problem:** משתתפת מחוברת עם גוגל (סיפור 6.1) עדיין לא יכולה ליצור פרופיל: הגשה קיימת רק דרך קישור הזמנה, ו-`GoogleSub` לא נקבע בשום מקום. בלי זה אי אפשר לממש את "הפרופיל שלי" (6.3), וגם שינוי 1.6 (יצירה בלי הזמנה) לא מתממש.

**Approach:** שרת בלבד. `POST /me/profile` יוצר פרופיל חדש לפי סשן גוגל בלי profile_id: מחזור נבחר מרשימה פעילה, אותם שדות וולידציה כמו ההגשה הקיימת, מייל מהסשן, `GoogleSub` נקבע, גרסה `pending`. בנוסף רשימת מחזורים פעילים ציבורית, והנפקה מחדש של עוגיית הסשן עם `profile_id`.

## Boundaries & Constraints

**Always:**
- `POST /me/profile` ממופה `AllowAnonymous` ומאמת ידנית את סכמת `Participant` כמו `GET /auth/google/me`: דורש `purpose = google-signin`, `google_sub` ו-`email`, ו**בלי** `profile_id`. אחרת 401. מוגן ב-anti-forgery ובדיקת Origin הקיימים לפי `UsesAnonymousToken`, ובהגבלת קצב לפי IP (`ProfileSubmitIp`) ובלי Turnstile.
- הגוף כמו `SubmitProfileRequest` ועוד `cohortId`. המייל מהגוף, אם נשלח, מתעלמים ממנו: המייל תמיד מהסשן. כל הוולידציה, ההקטנה של תמונה, הניקוי של קורות חיים ואווטאר, ומגבלות הגודל משותפים עם ההגשה הקיימת (אותו `ValidateAsync` ואותם עוזרים).
- המחזור חייב להתקיים ולא להיות מבוטל (`RevokedAt` ריק), אחרת 400 עם שגיאה על `cohortId`.
- יצירה אחת לחשבון: אם כבר קיים פרופיל עם אותו `GoogleSub`, או שיצירה מקבילה מפרה את הייחודיות, 409 עם `code: profile-exists`. התנגשות `(CohortId, Email)` עם פרופיל אחר: 409 `code: email-taken`, ובשני המקרים בלי פרופיל חלקי.
- נוצרים `Profile` עם `GoogleSub`, `ProfileRevision` במצב `pending` ו-`ParticipantPrivate` עם המייל מהסשן והנייד מהגוף, באותה טרנזקציה. הפרופיל אינו מוצג בציבור.
- בהצלחה: 201 עם `{profileId, status: "pending"}`, והעוגייה מונפקת מחדש עם `profile_id` (אותו עוזר שנבנה מחדש מהכניסה ב-6.1), כך ש-`/me/*` ו-`GET /auth/google/me` עובדים מיד.
- `GET /cohorts/active` (אנונימי, `no-store`): מחזורים לא מבוטלים בלבד, `{items:[{id,name}], total}`, בלי אסימון הזמנה.
- אין מייל, נייד, `sub` או טוקן ביומנים או בשורות ביקורת.

**Never:**
- לא נוגעים בלקוח, ולא מסירים או משנים את `POST /invites/{token}/profiles`.
- לא משנים את `GET /cohorts` של הגלריה.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| יצירה | סשן גוגל בלי פרופיל, מחזור פעיל, שדות חובה | 201, פרופיל `pending` עם `GoogleSub` ומייל הסשן, עוגייה עם `profile_id` | N/A |
| בלי סשן | אין עוגייה או סשן קישור אישי | 401 | N/A |
| כבר קיים | אותו `sub` עם פרופיל | 409 `profile-exists`, בלי שינוי | N/A |
| מרוץ | שתי יצירות מקבילות לאותו `sub` | אחת 201 והשנייה 409 `profile-exists`, פרופיל אחד בלבד | N/A |
| מחזור לא תקין | מזהה לא קיים, ריק או מבוטל | 400 עם שגיאה ב-`cohortId` | N/A |
| שדה חובה | שם, מוצר, קישור או נייד חסרים או לא תקינים | 400 עם שגיאות לפי שדה, כמו ההגשה הקיימת | N/A |
| מייל בגוף | הגוף כולל `email` אחר | המייל השמור הוא של הסשן | N/A |
| התנגשות מייל | פרופיל אחר באותו מחזור עם אותו מייל | 409 `email-taken` | N/A |
| רשימת מחזורים | `GET /cohorts/active` | רק מחזורים לא מבוטלים, בלי `inviteToken` | N/A |
| אחרי יצירה | `GET /auth/google/me` ו-`GET /me/profile` | `hasProfile: true` ופרטי הפרופיל הממתין | N/A |

</intent-contract>

## Code Map

- `server/Api/Endpoints/ProfileEndpoints.cs` (~17-21, 29-45, 94-142) -- `SubmitProfileRequest`, `ReadProfileBodyAsync`, `CvOf`, `ToCommand`, `Invalid`, `Busy` לשימוש חוזר; נקודת ההגשה הקיימת נשארת
- `server/Features/Profiles/ProfileService.cs` (~67-130, 203-228) -- `SubmitAsync` ו-`ValidateAsync` (פרטי), `ValidateEmail`/`NormalizePhone`; להוסיף שיטת יצירה לפי סשן גוגל שחולקת את `ValidateAsync`
- `server/Infrastructure/ProfileRepository.cs` (~10) -- `TryAddAsync` (INSERT עם `ON CONFLICT (cohort_id,email) DO NOTHING`); להוסיף גרסה שמקבלת `GoogleSub` ומבחינה בין `profile-exists` ל-`email-taken`
- `server/Domain/Profiles/Profile.cs` (~18) -- `GoogleSub` עם setter פרטי ו-`Profile.Create(cohortId, now)`; להוסיף overload עם sub
- `server/Domain/Cohorts/Cohort.cs`, `server/Features/Cohorts/CohortService.cs` (~3-13, 278) -- `RevokedAt`, `ICohortRepository`; להוסיף שליפת מחזור פעיל לפי מזהה ורשימת מחזורים פעילים
- `server/Api/Endpoints/GoogleAuthEndpoints.cs` (~111-133) -- בניית ה-principal ו-`SignInAsync` מוטמעים בקולבק, ו-`/auth/google/me` כדוגמה לאימות ידני; לחלץ עוזר להנפקה מחדש עם `profile_id`
- `server/Api/Endpoints/ParticipantEndpoints.cs` (~99-115) -- קבוצת `/me` עם `ParticipantPolicy` (דורשת `profile_id`), לא להכניס אליה את הנקודה החדשה
- `server/Api/Security/PublicFormGuard.cs`, `server/Features/PublicForms/IRateLimiter.cs` -- `CheckIpAsync(ProfileSubmitIp)`
- `server/Tests/{GoogleSignInTests.cs,ProfileSubmissionTests.cs}` -- כניסה דרך הספק המדומה (`SignInAsync` פרטי ב-`GoogleSignInTests`, להעתיק או להעביר לתמיכה משותפת), `Valid(...)`, `PostWithCsrfAsync`, זריעת מחזור
- אין לשנות: `client/`, `GET /cohorts` של הגלריה, `/invites/{token}/profiles`

## Tasks & Acceptance

**Execution:**
- `server/Domain`, `server/Features/Cohorts`, `server/Infrastructure` -- `Profile.Create` עם `GoogleSub`, שליפת מחזורים פעילים, והוספת פרופיל אטומית עם הבחנה בין כפילות sub לכפילות מייל -- AD-18
- `server/Features/Profiles/ProfileService.cs` -- יצירה לפי סשן גוגל עם אותה וולידציה -- FR-2
- `server/Api/Endpoints` -- `POST /me/profile`, `GET /cohorts/active`, ועוזר הנפקה מחדש של עוגיית הסשן -- AD-5
- `server/Tests/MyProfileCreationTests.cs`, `README.md` -- בדיקה לכל שורה במטריצה, כולל מרוץ והנפקה מחדש, ותיעוד

**Acceptance Criteria:**
- Given יצירה מוצלחת, when נבדקים הפרופיל, ה-Outbox, היומנים והתגובות, then אין בהם נייד או `sub`, והמייל השמור הוא של הסשן.
- Given הגשה קיימת דרך קישור הזמנה, when הבדיקות הקיימות רצות, then כולן עוברות ללא שינוי.
- Given כל הבדיקות, when הן רצות, then כולן עוברות.

## Spec Change Log

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: 18 findings (מקובצים לפי סיבת שורש מארבע השכבות) — high 0, medium 3, low 14, false 1, maybe-false 0
- findings:
  - `[medium]` `[patch]` הגבלת הקצב רצה לפני בדיקת הסשן וצורכת את התקציב המשותף עם הגשת ההזמנה — אימות הסשן קודם, ואחריו ההגבלה, ונוספה בדיקה
  - `[medium]` `[patch]` אין בדיקה שהשדות האופציונליים, האווטאר, התמונה וקורות החיים נשמרים ביצירה, ואין בדיקת multipart עם `cohortId` ותמונה — נוספו
  - `[low]` `[patch]` בדיקת רשימת מחזורים ריקה, ותיעוד קודי 429/413/415 ו-Busy ב-README — נוספו
  - `[medium]` `[defer]` משתתפת שכבר הגישה פרופיל דרך קישור הזמנה (בלי `GoogleSub`) ונכנסת עם גוגל באותו מייל מקבלת 409 `email-taken` ואין דרך לקשר — אין נתוני ייצור, ונדרש סיפור קישור לפי מייל מאומת
  - `[low]` `[defer]` מצב Busy וענפי unique-violation לא נבדקים ישירות — ענפים נדירים
  - `[low]` `[reject]` עוגייה ישנה עם `profile_id` של פרופיל שהוסר גורמת ל-401 קבוע — פרופילים לא נמחקים ו-`GoogleSub` לא מנותק
  - `[low]` `[reject]` מייל סשן שנכשל בוולידציית המייל מחזיר 400 על שדה `email` — כתובות גוגל תקינות, נדיר
  - `[low]` `[reject]` מחזור שבוטל בין הבדיקה להכנסה, וחריגת FK למחזור שנעלם — מחזורים לא נמחקים, מרוץ נדיר
  - `[low]` `[reject]` כשל בהנפקת העוגייה אחרי יצירה מחזיר 500 — הנפקה מקומית ללא קלט חיצוני
  - `[low]` `[reject]` `GET /cohorts/active` ללא הגבלת קצב והגבלת שורות — שמות מחזורים אינם רגישים, והגלריה כבר מציגה אותם
  - `[low]` `[reject]` כל מי שיש לו חשבון גוגל יכול להצטרף לכל מחזור — החלטת מוצר מאושרת בשיחה: פרסום דורש אישור המנהלת
  - `[low]` `[reject]` נוסח `SessionInvalid` מזכיר קישור אישי, ו-401 משמש גם ל"כבר יש פרופיל" — הלקוח קורא `me` קודם, והנוסח ניתן לשינוי ב-6.2
  - `[low]` `[reject]` ענף `default:` מחזיר `email-taken`, `CohortId` ב-`SubmitProfileRequest` המשותף, והערת `GoogleSession` — קוסמטי
  - `[low]` `[reject]` אין ביקורת או התראה למנהלת על יצירה — זהה להגשה דרך קישור הזמנה
  - `[low]` `[reject]` שם אינדקס `ix_participant_private_cohort_email` לא אומת — קיים ב-`AppDbContext`, ומכוסה בבדיקת `email-taken`
  - `[low]` `[reject]` בדיקת הגבלת הקצב אינה מוכיחה "לפי IP" — נבדק עם לקוח יחיד, והסקופ per-IP בהגדרה
  - `[low]` `[reject]` שינוי `SubmitProfileRequest` ו-refactor של `GoogleAuthEndpoints` ללא בדיקות רגרסיה — הבדיקות הקיימות עוברות ללא שינוי
  - `[false]` `[reject]` "אין בדיקת בקשת multipart אחרת מול נקודת הקצה" — אותו פרשן גוף משותף, ונבדק ב-`ProfileOptionalFieldsTests`

## Design Notes

הנקודה החדשה אינה בקבוצת `/me` כי מדיניות `Participant` דורשת `profile_id`, וזו בדיוק התכונה שחסרה למשתתפת לפני יצירה. אימות ידני כמו ב-`/auth/google/me` שומר על ברירת המחדל הסגורה. ההבחנה בין `profile-exists` ל-`email-taken` נשענת על אינדקס `ux_profiles_google_sub` ועל `ON CONFLICT` של `(cohort_id, email)`.

## Verification

**Commands:**
- `dotnet build server` -- expected: בנייה מצליחה בלי אזהרות חדשות
- `dotnet test server` -- expected: כל הבדיקות עוברות (נדרש Docker, כ-11 דקות; ריצה אחת בלבד בכל פעם)

## Auto Run Result

Status: done

- **שינוי שמומש (שרת בלבד):** `POST /me/profile` (סשן גוגל בלי `profile_id`, מחזור פעיל נבחר, אותה וולידציה כמו ההגשה הקיימת, המייל מהסשן, `GoogleSub` נקבע, גרסה `pending`, 201 והנפקה מחדש של העוגייה עם `profile_id`, 409 `profile-exists` ו-`email-taken`), `GET /cohorts/active` (בלי אסימון הזמנה), `Profile.Create` עם `GoogleSub`, ועוזרים משותפים (`GoogleSessionAsync`, `SignInParticipantAsync`) שחולצו מכניסת גוגל מסיפור 6.1.
- **ממצאי סקירה:** 18 ממצאים מקובצים. תוקנו 2 בדרגת medium ועוד 1 low: אימות הסשן לפני הגבלת הקצב המשותפת עם הגשת ההזמנה, בדיקות לשדות אופציונליים ולתמונה ב-multipart, ובדיקת רשימת מחזורים ריקה ותיעוד. נדחו לרשימת `deferred`: קישור פרופיל קיים (שנוצר דרך הזמנה) לחשבון גוגל לפי מייל מאומת, ובדיקות Busy ו-unique-violation. נדחו 12 ממצאי low ו-false אחד, עם נימוק בלוג.
- **המלצה לסקירת המשך:** כן. סיכון לא מאומת: משתתפת שהגישה בעבר דרך הזמנה ונכנסת עם גוגל באותו מייל תקבל 409 ואין מסלול קישור (אין נתוני ייצור כרגע).
- **אימות:** `dotnet build server` הצליח. `dotnet test server`: 859 עברו, ונכשלה בדיקה אחת קיימת מסיפור 3.2 (`A_notification_row_without_a_request_id_is_dead_as_unknown_template`). הסיבה: הבדיקה זורעת שורת Outbox עם `next_attempt_at = now()` של Postgres, והשעון של התהליך לפני שעון הקונטיינר ולכן השורה עדיין לא מגיעה. תוקן בבדיקה (`now() - interval '1 minute'`) ומחלקת הבדיקה רצה כולה (16/16).
- **סיכונים נותרים:** לא נבדק מול גוגל אמיתי. מרוץ בין ביטול מחזור ליצירה לא מכוסה. ללקוח עוד אין מסך יצירה (סיפור 6.3).
