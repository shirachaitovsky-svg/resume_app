---
title: 'סיפור 5.4: טופס הגשת פרופיל'
type: 'feature'
created: '2026-10-22'
status: 'done'
baseline_revision: '2bef10bf793fe5f7cac466ae9246ee9a019d04a1'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-resume_app-2026-10-21/DESIGN.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-resume_app-2026-10-21/EXPERIENCE.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-5-1-תשתית-הלקוח.md'
warnings: []
deferred:
  - summary: >-
      הבחנה בין כשל Turnstile לשגיאת שליחה כללית.
    evidence: |-
      כל שגיאה שאינה 404/429/400 מציגה אותה הודעה כללית; כשל אימות בוט עם טוקן אמיתי (5.9 או פריסה) עלול להטעות. יוכרע כשיוחלף שירות Turnstile האמיתי.
    location: >-
      client/src/app/pages/join/join-form.ts
    severity: low (unverified)
  - summary: >-
      קישור בלוק הפרטיות לשדות המייל והנייד דרך aria-describedby.
    evidence: |-
      הבלוק גלוי ומסומן role=note אך אינו מוקרא בעת מיקוד בשדה; בדיקה עם קורא מסך תכריע.
    location: >-
      client/src/app/pages/join/join-form.html
    severity: medium (unverified)
---

<intent-contract>

## Intent

**Problem:** משתתפת שקיבלה קישור הזמנה (`/join/{token}`) לא יכולה להגיש פרופיל: הנתיב `/join` הוא עמוד סטטי בלי טופס ובלי אסימון, ואין בלקוח שום קריאה לשרת ההגשה.

**Approach:** לבנות ב-`client/` טופס הגשה (שדות חובה בלבד: שם, שם מוצר, קישור למוצר, מייל, נייד) בנתיב `/join/:token`, שמאמת את הקישור מול `GET /invites/{token}`, מציג את שם המחזור ואת שני נוסחי הפרטיות בבלוק notice, שולח `POST /invites/{token}/profiles` ומציג מסך אישור אחיד. שדות אופציונליים, אווטאר וקורות חיים הם סיפור 5.5 ואינם כאן.

## Boundaries & Constraints

**Always:** Angular standalone, `OnPush`, signals, מחרוזות רק ב-`shared/strings.he.ts`, רכיבי מערכת העיצוב הקיימים (`Field`, `Button`, `GlassCard`, `LoadingSkeleton`, `ErrorState`, `InfoPage`), RTL, WCAG 2.2 AA. קריאות דרך `ApiClient` בלבד; ההגשה עם `{ turnstile: true }`. הנתיב `join/:token` מחליף את `join` (הנתיב `/join` ללא אסימון נשאר מציג את `InfoPage` הקיים "כדי ליצור פרופיל צריך קישור הזמנה מהמנהלת"). **טעינה:** `GET /invites/{token}` מחזיר `{ name }` (שם המחזור); בזמן הטעינה שלד; 404 או אסימון פגום או ריק: מסך "הקישור אינו תקף" בלי טופס ובלי חשיפת סיבה, עם הסבר לבקש קישור חדש מהמנהלת וקישור לגלריה; 429/5XX/רשת: `ErrorState` עם ניסיון חוזר. **טופס:** מעל השדות שם המחזור; שדות חובה בסדר: שם מלא, שם המוצר, קישור למוצר (`type=url`), מייל (`type=email`), נייד (`type=tel`), כל אחד עם "חובה" בטקסט (קומפוננטת `Field`, תווית מעל, גובה 52). אין שדה LinkedIn. **בלוק הבהרה (notice, UX-DR8):** אזור `role="note"` על רקע אפור מתחת לשדות המייל והנייד (או סביבם), תמיד גלוי, עם שני הנוסחים המדויקים: "כתובת המייל ומספר הנייד שלך גלויים רק למנהלת הקורס ואינם מוצגים באתר הציבורי" ו"פרטי התקשרות יימסרו למבקש רק לאחר אישור שלך לבקשה המסוימת." (מוגדרים כקבועים במשאבים, לא נחתכים ולא מוסתרים בטולטיפ). **אימות בלקוח לפני שליחה** (תואם שרת, אך השרת הוא המקור הסמכותי): חובה (אחרי trim) לכל חמשת השדות; קישור חייב להיות `http` או `https` מוחלט עם host, בלי רווחים ובלי פרטי משתמש; מייל עם `@` יחיד ודומיין עם נקודה; נייד 9 עד 15 ספרות עם `+` בהתחלה, רווחים ומקפים בלבד; אורכים: שם ושם מוצר עד 100, קישור עד 2048, מייל עד 254, נייד עד 40 (קבועים בקוד הלקוח). **שגיאות (UX-DR5):** שגיאה ליד כל שדה (`Field.error`, `aria-invalid`) וגם סיכום בראש הטופס (`role="alert"`, רשימת קישורים שמעבירים מיקוד לשדה), והמיקוד עובר לסיכום; כל הערכים נשמרים. שגיאות שרת 400 עם `fieldErrors` ממופות לשדות לפי המפתחות `name`, `productName`, `productUrl`, `email`, `phone` (מפתח לא מוכר מוצג בסיכום בלבד). **שליחה:** גוף JSON עם `name`, `productName`, `productUrl`, `email`, `phone` (אחרי trim; שדות אופציונליים לא נשלחים); הכפתור "שליחת הפרופיל" מנוטרל בזמן שליחה (אין שליחה כפולה) ומציג מצב עבודה. **הצלחה:** תשובת 202 (`{ status: 'pending' }`) מציגה מסך אישור: "הפרופיל הוגש וממתין לאישור המנהלת" עם הסבר קצר (המנהלת תבדוק, הפרופיל יופיע בגלריה רק אחרי אישור, ויישלח מייל כשיתקבל) וקישור לגלריה; כפילות (אותו מייל באותו מחזור) מחזירה אותה תשובה אחידה מהשרת ולכן אותו מסך בדיוק. מסך האישור לא מציג את המייל או הנייד שהוזנו, ורענון הדף חוזר לטופס ריק (אין שמירה ב-`localStorage`). **שגיאות אחרות בשליחה:** 404 (אסימון בוטל בינתיים): מסך "הקישור אינו תקף"; 429: הודעה עם `Retry-After` (`withRetryHint`); 503/5XX/רשת: הודעה בראש הטופס והנתונים נשמרים. כותרת הדף מתעדכנת (title). קישור "יצירת פרופיל" בניווט ממשיך לנתיב `/join` (העמוד הסטטי).

**Never:** אין שינוי בשרת או בפרוקסי; אין שדות אופציונליים, בורר אווטאר, העלאת תמונה, קורות חיים או תצוגה מקדימה של כרטיס (סיפור 5.5); אין שדה LinkedIn; אין `localStorage`/`sessionStorage`/רישום של מייל או נייד ביומן או ב-URL; אין חשיפה של מזהה פרופיל; אין ספריות חיצוניות; אין שינוי של רכיבי `Field` שוברי תאימות (אם נדרש `autocomplete`/`inputmode`/`maxlength`, מוסיפים קלטים אופציונליים עם ברירת מחדל שאינה משנה התנהגות קיימת); אין הצגת מייל או נייד אחרי ההגשה.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| קישור תקף | `/join/{token}` ו-`GET /invites/{token}` 200 | טופס עם שם המחזור, חמשת השדות ובלוק ההבהרה | N/A |
| טעינת קישור | הבקשה בדרך | שלד טעינה עם `aria-busy` | N/A |
| קישור לא תקף | 404 | "הקישור אינו תקף" בלי טופס | אחיד לכל סיבה |
| שגיאת טעינה | 5XX, רשת או 429 | `ErrorState` עם ניסיון חוזר | `AppError` |
| שדה חובה חסר | שליחה עם שדה ריק או רווחים | שגיאה ליד השדה ובסיכום, מיקוד לסיכום, אין בקשה לשרת | הערכים נשמרים |
| קישור לא תקין | `ftp://x`, `javascript:alert(1)`, `example.com` | שגיאת קישור ליד השדה ובסיכום | הערכים נשמרים |
| מייל או נייד לא תקינים | `a@b`, `12` | שגיאה ליד השדה המתאים | הערכים נשמרים |
| שגיאת שרת בשדה | 400 עם `errors.email` | השגיאה מוצגת ליד המייל ובסיכום | הערכים נשמרים |
| שליחה תקינה | חמשת השדות תקינים | `POST` עם `X-Turnstile-Token` ו-anti-forgery, הכפתור מנוטרל בזמן שליחה, אחר כך מסך אישור | N/A |
| כפילות | אותו מייל שוב | 202 אחיד, אותו מסך אישור | N/A |
| לחיצה כפולה | שתי לחיצות מהירות | בקשה אחת בלבד | N/A |
| אסימון בוטל | 404 בשליחה | "הקישור אינו תקף" | N/A |
| 429 בשליחה | חריגת קצב | הודעה עם זמן המתנה, הנתונים נשמרים | `AppError` |
| 5XX או רשת בשליחה | כשל | הודעת שגיאה בראש הטופס, ניסיון חוזר אפשרי | הנתונים נשמרים |
| פרטיות | כל מצב | אין מייל או נייד במסך האישור, ב-URL או בהודעות | N/A |
| נגישות | מקלדת | סדר טאב הגיוני, תוויות לכל שדה, סיכום שגיאות מוכרז, יעדי מגע 44px | N/A |

</intent-contract>

## Code Map

- `client/src/app/app.routes.ts` -- `join` מצביע על `JoinPage` (עמוד סטטי); להוסיף `join/:token` עצל לדף הטופס ולהשאיר `join` כעמוד ההסבר.
- `client/src/app/pages/join/join.ts` -- העמוד הסטטי הקיים (`InfoPage`); נשאר ללא אסימון. הטופס בתיקייה חדשה `client/src/app/pages/join/` כקומפוננטה נפרדת (למשל `join-form.ts`, `.html`, `.css`).
- `client/src/app/shared/api/api-client.ts` -- `post<T>(path, body, { turnstile: true })` מוסיף CSRF ו-`X-Turnstile-Token` (טוקן פיתוח `dev-turnstile-token` ב-`turnstile.service.ts`); `AppError` ב-`app-error.ts` נושא `kind`, `status`, `fieldErrors`, `retryAfterSeconds`; `withRetryHint` ל-429.
- `client/src/app/shared/ui/field/` -- `Field` (קלטים `label`, `type`, `required`, `error`, `value` כ-model); חסרים `autocomplete`, `inputmode`, `maxlength`, `hint` אם נדרשים: להוסיף כקלטים אופציונליים.
- `client/src/app/shared/ui/{button,glass-card,loading-skeleton,error-state,info-page}` -- רכיבי מצבים ועיצוב. `client/src/app/pages/gallery/gallery.ts` ו-`profile/profile.ts` -- דוגמה לדפוס טעינה, מצבים, `AppError` ו-signals.
- `client/src/app/shared/strings.he.ts` -- להוסיף קבוצת `joinForm` (תוויות, שגיאות, notice, אישור, "הקישור אינו תקף"); `join` הקיים נשאר.
- `client/src/styles.css` -- טוקנים; בלוק ה-notice על רקע אפור מהטוקנים הקיימים (`--color-surface-muted` או שווה ערך, לא hex חדש).
- שרת (קריאה בלבד): `server/Api/Endpoints/ProfileEndpoints.cs` (`POST /invites/{token}/profiles`: 202 `{status:'pending'}`, 404 אחיד, 400 `errors` לפי `name|productName|productUrl|email|phone`, 413/415, 429, 503), `CohortEndpoints.cs` (`GET /invites/{token}` מחזיר `{name}`), `server/Features/Profiles/ProfileService.cs` (כללי אימות), `server/Domain/Profiles/Profile.cs` (אורכים: שם 100, מוצר 100, קישור 2048). אין לשנות.

## Tasks & Acceptance

**Execution:**
- `client/src/app/shared/strings.he.ts` -- מחרוזות הטופס, שגיאות לכל שדה, שני נוסחי הפרטיות המדויקים, מסך האישור ו"הקישור אינו תקף" -- NFR4, UX-DR8, UX-DR16
- `client/src/app/shared/ui/field/` -- קלטים אופציונליים `autocomplete`, `inputmode`, `maxlength`, `describedBy`/`hint` ללא שבירת תאימות -- נגישות וניידות
- `client/src/app/pages/join/join-form.*` -- טופס, אימות לקוח, סיכום שגיאות עם מיקוד, מיפוי `fieldErrors`, שליחה עם נטרול כפול, מצבי טעינה, אסימון לא תקף, שגיאה ואישור -- FR-2, FR-4, FR-5, UX-DR5, UX-DR8
- `client/src/app/app.routes.ts` -- `join/:token` עצל -- UX-DR14
- `client/src/app/pages/join/*.spec.ts` -- בדיקות `vitest` לכל שורות המטריצה (כולל אימות קישורים `javascript:`/`ftp:`, מיפוי שגיאות שרת, שליחה כפולה, אחידות הכפילות, היעדר מייל/נייד במסך האישור, קיום שני הנוסחים ושאין שדה LinkedIn) באמצעות `HttpTestingController` -- NFR3
- `client/README.md` -- הערה קצרה על הטופס וכיצד לנסות אותו מול שרת הפיתוח (יצירת מחזור וקישור דרך מסך הניהול או ה-API) -- תפעול

**Acceptance Criteria:**
- Given קישור הזמנה תקף, when הוא נפתח, then מוצג טופס עם שם המחזור, חמשת שדות החובה, שני נוסחי הפרטיות המדויקים בבלוק גלוי ליד המייל והנייד, ובלי שדה LinkedIn.
- Given שדה חובה חסר או קישור שאינו http/https, when שולחים, then מוצגת שגיאה ליד השדה ובסיכום בראש הטופס, המיקוד עובר לסיכום וכל הערכים נשמרים.
- Given הגשה מוצלחת או כפילות, when התגובה (202) חוזרת, then מוצג אותו מסך "הפרופיל הוגש וממתין לאישור המנהלת" עם הסבר, בלי מייל או נייד.
- Given קישור מבוטל, שגוי או חסר אסימון תקף, when הוא נפתח, then מוצגת הודעה שהקישור אינו תקף בלי טופס.

## Spec Change Log

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: 24 findings — high 0, medium 7, low 9, false 3, maybe-false 5
- findings:
  - `[medium]` `patch` שגיאה ישנה נשארת אחרי תיקון שדה — נוקתה בעריכה ונוסף מבחן
  - `[medium]` `patch` מצב דולף כשהאסימון משתנה באמצע שליחה — `load()` מבטל מנוי ומאפס ערכים
  - `[medium]` `patch` מיקוד, כותרת ופרטי PII אחרי מסך אישור או קישור לא תקף — מיקוד לכותרת, כותרת עמוד, ניקוי הערכים
  - `[medium]` `patch` מבחן אסימון פגום לא וידא שלא נשלחה בקשה — נוסף `expectNone`
  - `[medium]` `patch` הנתיב `join/:token` האמיתי לא נבדק — נוסף מבחן עם `routes`
  - `[medium]` `patch` קלטי `Field` החדשים ו-`aria-describedby` המשולב ללא מבחנים — נוספו
  - `[medium]` `patch` אימות `aria-busy` וסגנון הקישור — נבדק, ללא שינוי
  - `[low]` `reject` הדגשת ניווט ל-`/join/:token` — קוסמטי
  - `[low]` `reject` הודעות שרת לשדות מוחלפות בנוסח כללי — הנוסחים מוגדרים במשאבים
  - `[low]` `reject` `maxlength` חותך הדבקה ארוכה — התנהגות מכוונת
  - `[low]` `reject` אימות מייל וקישור מקל יותר מהשרת — השרת סמכותי
  - `[low]` `reject` ניסוח נקבה — עקבי בכל האתר
  - `[low]` `reject` CSS מיותר, כפל הכרזה, `href="#"` בסיכום — קוסמטי
  - `[low]` `reject` הערות על תזמון במבחנים ו-README — לא פוגעות
  - `[maybe-false]` `defer` הבחנה בין כשל Turnstile לשגיאה כללית — ראו deferred
  - `[maybe-false]` `defer` קישור הערת הפרטיות לשדות דרך `aria-describedby` — ראו deferred
  - `[false]` `reject` תבנית האסימון בלקוח צרה מהשרת — התבנית `[A-Za-z0-9_-]` תואמת את `IsWellFormedToken`
  - `[false]` `reject` מפתח `errors` מול `fieldErrors` — `AppError` ממפה `errors` ל-`fieldErrors`
  - `[false]` `reject` הבטחת מייל באישור הגשה — הנוסח לפי UX-DR16
  - `[maybe-false]` `reject` שאר ממצאי Blind Hunter על אימות חלקי — השרת סמכותי

## Design Notes

סיכום השגיאות והשגיאות לכל שדה נגזרים מ-signal אחד (`errors: Record<field, string>`) כך שהסיכום והשדות תמיד מסונכרנים; מיקוד לסיכום אחרי רינדור (`afterNextRender` או `queueMicrotask`). הכפילות אינה נפרדת בלקוח: השרת מחזיר 202 זהה, והלקוח לא מבחין.

## Verification

**Commands:**
- `npm run build` ב-`client/` -- expected: בנייה מצליחה בלי שגיאות
- `npm test -- --watch=false` ב-`client/` -- expected: כל הבדיקות עוברות

**Manual checks (if no CLI):**
- יצירת מחזור דרך ה-API או מסד הפיתוח, פתיחת `/join/{token}` בדפדפן, הגשה תקינה ואחריה פרופיל `pending` בטבלאות; הגשה עם נתונים שגויים מציגה שגיאות ושומרת ערכים; טוקן שגוי מציג "הקישור אינו תקף".

## Auto Run Result

Status: done

- **שינוי:** טופס הגשת פרופיל ב-`/join/:token`: אימות קישור מול השרת, חמשת שדות החובה, בלוק הבהרה עם שני נוסחי הפרטיות, אימות לקוח, סיכום שגיאות עם מיקוד, שליחה עם Turnstile ו-CSRF ונטרול שליחה כפולה, מסך אישור אחיד ומסך "הקישור אינו תקף".
- **קבצים:** `pages/join/join-form.*` (טופס), `join-validation.ts` (כללי אימות), `app.routes.ts` (נתיב), `shared/ui/field/*` (קלטים אופציונליים), `strings.he.ts`, `client/README.md`, בדיקות ב-`join-form.spec.ts`, `app.spec.ts`, `field.spec.ts`.
- **סקירה:** 7 תיקונים הוחלו, 2 נדחו למעקב (`deferred`), השאר נדחו עם נימוק ביומן.
- **המלצת סקירה חוזרת:** false (לא תוקן ממצא high ופחות משני medium מהותיים).
- **אימות:** `npm run build` הצליח; `npm test -- --watch=false`: 14 קבצים, 124 בדיקות עברו. לא נבדק בדפדפן מול שרת הפיתוח.
- **סיכונים:** לא נבדק ידנית מול שרת חי; Turnstile עדיין טוקן פיתוח.
