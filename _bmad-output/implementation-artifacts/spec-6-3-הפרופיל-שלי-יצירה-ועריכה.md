---
title: 'סיפור 6.3: הפרופיל שלי, יצירה ועריכה במסך אחד'
type: 'feature'
created: '2026-10-22'
status: 'done'
baseline_revision: 'c51c1e5ace19986871f332ad83446ced8d3409cb'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-6-context.md'
warnings: [oversized]
deferred: []
---

<intent-contract>

## Intent

**Problem:** אחרי כניסה עם גוגל, `/me` מציג רק מייל ויציאה. אין טופס ליצירת פרופיל בלי קישור הזמנה ואין טופס עריכה, והטופס היחיד (`/join/:token`) קשור לקישור הזמנה ולא נוגע בשרת החדש. יצירה ועריכה צריכות להיות מסך אחד, בלי כפילות.

**Approach:** לקוח, ותוספת שרת קטנה אחת. רכיב טופס משותף (`ProfileForm`) שחולץ מ-`join-form` עם מצבי יצירה ועריכה, שנטען ב-`/me`: משתתפת בלי פרופיל מקבלת יצירה (בחירת מחזור מ-`/cohorts/active`), ומשתתפת עם פרופיל מקבלת עריכה עם הנתונים הקיימים. `/join/:token` הופך להפניה ל-`/me` עם מחזור מסומן מראש. בשרת, `GET /invites/{token}` מחזיר גם `cohortId`.

## Boundaries & Constraints

**Always:**
- מצב יצירה (`hasProfile=false` ב-`GET /auth/google/me`): שדה חובה לבחירת מחזור (רשימה מ-`GET /cohorts/active`, מסומן מראש לפי `?cohort=<id>` אם קיים ופעיל), שם, מוצר, קישור, נייד, והאופציונליים (תיאור, תפקיד, מקום עבודה, היכרות ושלוש תשובות, אווטאר, תמונה, קורות חיים), התצוגה המקדימה והנוסחים והאזהרות כמו בטופס הקיים. המייל מוצג לקריאה בלבד ("המייל נלקח מחשבון הגוגל שלך") ואינו נשלח. שליחה: `POST /me/profile` (JSON, או multipart עם `photo`), ללא Turnstile.
- מצב עריכה (`hasProfile=true`): `GET /me/profile`, הטופס מלא בנתונים הקיימים (אין שדה מחזור), ובאנרים: "השינויים ממתינים לאישור. הגרסה הקודמת עדיין מוצגת באתר." כשיש `pending`, ו"הוחזר עם הערה" והערת המנהלת כש-`returned` קיים, עם פעולה ראשית "עריכה והגשה מחדש". שליחה: `PUT /me/profile` (JSON או multipart: תמונה חדשה מחליפה, `removePhoto` מסירה, אחרת נשארת). המייל לקריאה בלבד, והנייד ניתן לעריכה ושינוי שלו ממתין לאישור המנהלת (מוצג מהשרת: `pendingPhone`).
- כשיש תמונה בפרופיל ואין תמונה חדשה, מוצג "יש תמונה" עם אפשרות להחליף או להסיר. (אין כתובת תמונה לבעלת הפרופיל, כך שאין תצוגה של תמונה ממתינה.)
- אחרי יצירה מוצלחת (201): מסך "הפרופיל הוגש וממתין לאישור המנהלת" והסבר, והמצב עובר לעריכה (טעינה מחדש של `me` ו-`GET /me/profile`). 409 `profile-exists`: מעבר למצב עריכה. 409 `email-taken`: הודעה ברורה בראש הטופס. 401: חזרה למסך כניסה. 429 ו-503: הודעה והנתונים נשמרים. שגיאות שדה מהשרת ליד השדה (כולל `cohortId`), בסיכום בראש הטופס, בלי איבוד נתונים.
- קישור הזמנה: `/join/:token` קורא `GET /invites/{token}` (קלט `cohortId` ו-`cohortName`), ומפנה ל-`/me?cohort=<cohortId>` (החלפת היסטוריה). טוקן לא תקף או מבוטל: הודעה "הקישור אינו תקף" עם המשך ל-`/me` בלי מחזור. כשהמשתתפת לא מחוברת, כתובת החזרה של הכניסה (`returnUrl`) משמרת את `?cohort=`. אין שמירה ב-`localStorage` או `sessionStorage`.
- שימוש חוזר בקוד הקיים: האימות (`join-validation`), הקטנת התמונה (`photo-resize`), בורר האווטאר, תצוגת הקורות החיים ותצוגה מקדימה, וסגנונות, בלי שכפול. ה-`JoinFormPage` הישן מוסר, ובדיקותיו עוברות לרכיב המשותף.
- שרת: ב-`PublicInvite` של `GET /invites/{token}` נוסף `CohortId` (GUID) בלי להסיר שדה, עם בדיקה. הלקוח קורא `cohortName` (לא `name`).

**Never:**
- לא נוגעים בשום דבר אחר בשרת. לא מציגים נייד או מייל של אחרות. לא משנים את ה-`POST /invites/{token}/profiles` בשרת.
- לא מוסיפים כתובת תמונה לבעלת הפרופיל.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| יצירה | מחוברת בלי פרופיל, מחזור נבחר, שדות חובה תקינים | `POST /me/profile`, מסך "ממתין לאישור", מעבר לעריכה | שדה חסר: שגיאה ליד השדה, הנתונים נשמרים |
| מחזור מסומן מראש | `/me?cohort=<id>` של מחזור פעיל | המחזור נבחר מראש ברשימה | מזהה לא ברשימה: לא נבחר דבר |
| קישור הזמנה | `/join/:token` תקף | הפניה ל-`/me?cohort=<cohortId>` | 404: "הקישור אינו תקף" והמשך ל-`/me` |
| לא מחוברת מקישור | `/join/:token` ואין סשן | מסך כניסה, וכפתור הכניסה שומר `returnUrl=/me?cohort=<id>` | N/A |
| עריכה | יש פרופיל | הטופס מלא מ-`GET /me/profile`, מייל לקריאה בלבד | 401: מסך כניסה |
| ממתין | `pending` קיים | באנר "השינויים ממתינים לאישור..." | N/A |
| הוחזר | `returned` קיים | באנר "הוחזר עם הערה" עם ההערה, ופעולה "עריכה והגשה מחדש" | N/A |
| שליחת עריכה | שינוי שדות | `PUT /me/profile`, באנר "ממתין", הנתונים נשארים | שגיאות שדה ליד השדה |
| שינוי נייד | נייד חדש | נשלח, ומוצג "שינוי הנייד ממתין לאישור" | N/A |
| כבר קיים | `POST` מחזיר 409 `profile-exists` | מעבר למצב עריכה | N/A |
| מייל תפוס | 409 `email-taken` | הודעה בראש הטופס, הנתונים נשמרים | N/A |
| תמונה | בחירה, החלפה והסרה | כמו בטופס הקיים, ובעריכה `removePhoto` | תמונה לא נתמכת או גדולה: הודעה |

</intent-contract>

## Code Map

- `client/src/app/pages/join/{join-form.ts,.html,.css,join-validation.ts,avatar-picker.ts,photo-resize.ts,profile-preview.ts}` -- הטופס הקיים (מצבים loading/ready/invalid/done/error, `values`, `optional`, `avatarId`, `photoBlob`, `submit()`, `onSubmitError`); לחלץ את הטופס ל-`ProfileForm` משותף, ולהשאיר ב-`/join/:token` רק הפניה
- `client/src/app/pages/me/{me.ts,me-session.ts,me.spec.ts}` -- מצב `signedIn` עם `.profile-slot`; לטעון לתוכו יצירה או עריכה, ולקרוא `?cohort=`
- `client/src/app/app.routes.ts` (~25-40) -- `join/:token` מצביע ל-`JoinFormPage`; להחליף ברכיב הפניה
- `client/src/app/shared/api/{api-client.ts,profile.models.ts}` -- `post`, `put`, `postMultipart`; להוסיף מודלים `OwnProfile` ו-`CohortRef` לרשימה הפעילה
- `client/src/app/shared/strings.he.ts` (~99 `myProfile`, ~111 `joinForm`) -- להוסיף כותרות, כפתורים, באנרים ("ממתין", "הוחזר עם הערה"), הודעות לעריכה ויצירה
- `client/src/app/shared/ui/avatar/avatar.ts`, `shared/url.ts` -- בורר ותצוגת אווטאר; אין כתובת תמונה לבעלת הפרופיל
- `client/src/app/pages/join/{join-form.spec.ts,join-optional.spec.ts}` -- בדיקות הטופס הקיים (215 ו-411 שורות) שיש להעביר לרכיב המשותף
- שרת: `server/Api/Endpoints/CohortEndpoints.cs` (~62, `PublicInvite(CohortName)`) -- להוסיף `CohortId`; `ParticipantEndpoints.cs` (~25-30 `OwnProfileResponse`, ~101-115 GET/PUT `/me/profile`) ו-`ProfileEndpoints.cs` (~70 `POST /me/profile`) לקריאה בלבד
- `client/src/app/pages/contact/contact-pages.spec.ts`, `pages/me/me.spec.ts` -- דפוסי בדיקה (`RouterTestingHarness`, `HttpTestingController`)

## Tasks & Acceptance

**Execution:**
- `server/Api/Endpoints/CohortEndpoints.cs` ובדיקה -- `CohortId` ב-`PublicInvite` -- מילוי מחזור מראש
- `client/src/app/pages/me/profile-form/*` -- רכיב `ProfileForm` משותף עם מצבי יצירה ועריכה, שחולץ מ-`join-form`, ושירות/מודלים (`OwnProfile`, רשימת מחזורים) -- FR-2, FR-12
- `client/src/app/pages/me/me.ts` וטעינת הטופס, `app.routes.ts` והפניית `/join/:token`, `strings.he.ts` -- שילוב, באנרים והפניה -- FR-11, UX-DR19
- הסרת `JoinFormPage` ובדיקות -- העברת בדיקות הטופס, בדיקות חדשות למצבי יצירה ועריכה, הפניה, באנרים ו-409

**Acceptance Criteria:**
- Given כל מסכי הטופס, when נבדק התוכן, then אין בהם נייד או מייל של משתתפת אחרת, והמייל שמוצג הוא של החשבון בלבד.
- Given `dotnet test server` (מחלקת הבדיקות של ההזמנות) ו-`ng test --watch=false` ו-`ng build`, when הם רצים, then הכול עובר בלי אזהרות חדשות.
- Given `JoinFormPage` הוסר, when מחפשים הפניות אליו, then אין כאלה בקוד.

## Spec Change Log

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: 18 findings (מקובצים לפי סיבת שורש מארבע השכבות) — high 0, medium 4, low 11, false 3, maybe-false 0
- findings:
  - `[medium]` `[patch]` אחרי יצירה מוצלחת הכותרת של מסך האישור אינה מקבלת מיקוד (נגישות, רגרסיה מול הדף הישן) — נוסף מיקוד ובדיקה
  - `[medium]` `[patch]` אחרי שמירת עריכה, כשל בטעינה מחדש מחליף את כל הטופס בשגיאה למרות שהשמירה הצליחה, וטעינה מחדש מאפסת ערכים בלי אישור גלוי — הטופס נשאר, הודעה "נשמר, ללא רענון" ואישור שמירה
  - `[medium]` `[patch]` אין בדיקה לעריכה עם תמונה חדשה (`putMultipart`), ואין בדיקה של 401 בשליחת יצירה או עריכה דרך `MePage` — נוספו
  - `[medium]` `[reject]` שליחת האימייל הנוכחי ב-PUT עלולה לעוות שינוי ממתין — השרת משווה למייל השמור, והערך שנשלח הוא הערך שהוצג, ואין שינוי מייל באתר (נבדק ב-`ProfileService.UpdateAsync`)
  - `[low]` `[reject]` מחיקת שדה אופציונלי או חזרה לאווטאר ברירת מחדל לא נתמכות — נבדק: `UpdateAsync` מחליף את כל הגרסה, ושדה חסר מתנקה והאווטאר חוזר לברירת מחדל
  - `[low]` `[reject]` `returnUrl=/me?cohort=...` עלול להידחות בשרת — נבדק ב-`SafeReturnUrl`: נתיב יחסי עם שאילתה תקין
  - `[low]` `[reject]` הסרת Turnstile ביצירה והעדר בדיקת חברות במחזור — החלטת מוצר מאושרת (כניסה עם גוגל ואישור מנהלת), עם הגבלת קצב לפי IP בשרת
  - `[low]` `[reject]` הזמנה למחזור לא פעיל אינה מוצגת למשתתפת בהודעה — נדיר, התיקון מסבך את הזרימה
  - `[low]` `[reject]` ניסיון חוזר אחרי כשל טעינה לאחר יצירה מציג טופס יצירה ל-409 — 409 `profile-exists` מעביר לעריכה
  - `[low]` `[reject]` מצב `hasProfile` בהורה לא מתעדכן אחרי יצירה, ופרמטר `cohort` נקרא פעם אחת — הרכיב נטען מחדש בכל כניסה, ומעבר פנימי לא קיים
  - `[low]` `[reject]` שמות `join-validation`/`JOIN_FIELDS`/`STRINGS.joinForm` וייבוא של `contact-request.ts` ממנה — שיוך שמות בלבד, ללא נזק
  - `[low]` `[reject]` מחרוזות לא בשימוש (`loadFailed`, `heading`, `cohortLabel`, `submit`), CSS ואריכות — קוסמטי
  - `[low]` `[reject]` נגישות קטנה: `aria-labelledby` קבוע, `aria-required` ב-select, הכרזה כפולה של שגיאות — ללא נזק ממשי
  - `[low]` `[reject]` `afterEach` ב-`me.spec` מרוקן בקשות, regex חלש ובדיקת סדר מאפיינים בשרת — בדיקות משלימות
  - `[low]` `[reject]` דף ההפניה: כותרת הדף, מיקוד בשגיאה, וכשל `navigate` — נדיר
  - `[false]` `[reject]` "לא נבדק ש-`/me/profile` וה-`cohorts/active` תואמים למודלים" — המודלים נכתבו לפי ה-DTO בשרת ונבדקו בקריאת הקוד
  - `[false]` `[reject]` "ה-`cohortId` בהזמנה אינו מחזור פעיל" — מחזור לא מבוטל הוא גם פעיל ב-`/cohorts/active`
  - `[false]` `[reject]` "אין שמירה בדפדפן" — אין שימוש ב-localStorage או sessionStorage

## Design Notes

הטופס המשותף הוא רכיב מציג: הוא מקבל `mode`, ערכים התחלתיים ופונקציית שליחה, והורה (`/me`) אחראי לטעינה (`GET /me/profile` או רשימת מחזורים) ולתגובות (מסך הצלחה, מעבר מצב). כך אין שני טפסים. `/join/:token` נשאר רק כדי שקישורי הזמנה שכבר נשלחו ימשיכו לעבוד. הערכים ב-`OwnProfileResponse`: שדות התשובות הם `introAnswer1-3`, הסטטוס נגזר (`returned != null`, אחרת `pending != null`), ו-`email`/`phone` מציגים ערך ממתין כשיש.

## Verification

**Commands:**
- `cd client && npx ng test --watch=false` -- expected: כל הבדיקות עוברות
- `cd client && npx ng build` -- expected: בנייה מצליחה בלי אזהרות חדשות
- `dotnet test server/Tests/Tests.csproj --filter "FullyQualifiedName~Invite"` -- expected: עובר (שינוי `PublicInvite`)

**Manual checks (if no CLI):**
- ב-`ng serve` מול השרת במצב `GOOGLE_PROVIDER=fake`: כניסה, יצירת פרופיל, מעבר לעריכה, שינוי והגשה מחדש, ופתיחת `/join/<token>` של מחזור.

## Auto Run Result

Status: done

- **שינוי שמומש:** רכיב `ProfileForm` משותף (חולץ מהטופס הישן) עם מצבי יצירה ועריכה, נטען ב-`/me`: יצירה עם בחירת מחזור מ-`/cohorts/active` (מסומן מראש מ-`?cohort=`), עריכה מ-`GET /me/profile` עם באנרים "ממתין" ו"הוחזר עם הערה", מייל לקריאה בלבד, שמירה ב-`PUT`, ומסך "הפרופיל הוגש". `/join/:token` הוא עכשיו הפניה ל-`/me?cohort=<id>`. `JoinFormPage` הוסר ובדיקותיו הועברו. בשרת נוסף `cohortId` ל-`GET /invites/{token}` (שינוי יחיד).
- **ממצאי סקירה:** 18 ממצאים מקובצים. תוקנו 3 בדרגת medium: מיקוד בכותרת האישור אחרי יצירה, שמירת הטופס ואישור גלוי בעריכה גם כשהטעינה מחדש נכשלת, ובדיקות חסרות (עריכה עם תמונה חדשה ו-401 בשליחה). נדחו 1 medium, 11 low ו-3 false, עם נימוק בלוג.
- **המלצה לסקירת המשך:** כן. סיכון לא מאומת: הזרימה המלאה בדפדפן מול השרת במצב `fake` (כניסה, יצירה, מעבר לעריכה, פתיחת `/join/<token>`) לא נבדקה, וחוזה הנתונים בין הלקוח לשרת נבדק מול תגובות מדומות.
- **אימות:** `ng test --watch=false`: 371 בדיקות ב-29 קבצים עברו. `ng build` הצליח בלי אזהרות. `dotnet test` על בדיקות מחזורים והזמנות וגלריה: 64 עברו (שינוי השרת קטן ונוגע רק ב-`PublicInvite`).
- **סיכונים נותרים:** `PUT /me/profile` עדיין דורש `email` ומקבל את הנוכחי (לא ניתן לעריכה). אין כתובת תמונה לבעלת הפרופיל, ולכן תמונה ממתינה אינה מוצגת. הזמנה למחזור לא פעיל אינה מוצגת למשתתפת בהודעה.
