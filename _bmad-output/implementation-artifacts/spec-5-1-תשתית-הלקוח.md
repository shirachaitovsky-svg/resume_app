---
title: 'סיפור 5.1: תשתית הלקוח'
type: 'feature'
created: '2026-10-22'
status: 'in-review'
baseline_revision: 'b14f6b13eed0485d5af0d0e3c370e0eae14d25e5'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-resume_app-2026-10-21/DESIGN.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-resume_app-2026-10-21/EXPERIENCE.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-1-2-מערכת-העיצוב-הבסיסית.md'
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** ללקוח אין כרגע שירות API, ניתוב, מעטפת או רכיבי מצב משותפים, ולכן כל מסך שייבנה יצטרך להמציא אותם מחדש ויראה אחרת. דף הבית עדיין מציג רק "האתר בהקמה".

**Approach:** לבנות ב-`client/` (Angular 22, standalone, signals) תשתית משותפת: שירות `ApiClient` עם נרמול שגיאות והודעות עברית אחידות, שירותי anti-forgery ו-Turnstile להגשות, ניתוב עצל עם עמוד "הדף אינו נמצא", מעטפת עם ניווט צף, וקומפוננטות מצב (שלד טעינה, מצב ריק, מצב שגיאה עם ניסיון חוזר). החלפת דף "האתר בהקמה" בעמוד בית זמני שיוחלף בגלריה בסיפור 5.7.

## Boundaries & Constraints

**Always:** הכול ב-`client/src/app/`. Angular standalone, `ChangeDetectionStrategy.OnPush`, signals, מאפייני CSS לוגיים, מחרוזות רק ב-`shared/strings.he.ts`, צבעים רק דרך הטוקנים הקיימים, והרכיבים הקיימים ב-`shared/ui/` (כפתור, שדה, צ'יפ, כרטיס זכוכית, ניווט צף). נגישות WCAG 2.2 AA: `lang="he" dir="rtl"`, קישור "דלג לתוכן" ראשון, `main` יחיד, יעדי מגע 44px, מיקוד גלוי. כל הקריאות לשרת דרך נתיב יחסי `/api/...` (הפרוקסי ב-`proxy.conf.js` מוסיף את הסוד ומסיר את הקידומת; לא לשנות אותו). **`ApiClient`** (`shared/api/`): עוטף `HttpClient` עם `get`, `post`, `put`, `delete`, `postMultipart`; כל בקשת שינוי (`POST`/`PUT`/`DELETE`) מקבלת אוטומטית אסימון anti-forgery בכותרת `X-CSRF-TOKEN` שנטען פעם אחת מ-`GET /api/profiles/csrf` ונשמר בזיכרון (ובכשל 400/403 של אסימון: טעינה מחדש וניסיון חוזר אחד), ו-`withCredentials` כך שהעוגיות נשלחות. אסימון Turnstile נשלח בכותרת `X-Turnstile-Token` רק כשהקריאה מבקשת זאת (`{ turnstile: true }`) דרך `TurnstileService`: ממשק עם `getToken(): Promise<string>`; המימוש כרגע מחזיר את הערך `dev-turnstile-token` (מפתח הבדיקה של Cloudflare בצד השרת מקבל אותו בפיתוח) והוא מוחלף בווידג'ט אמיתי בסיפורים 5.4 ו-5.9; יש להגדיר את המימוש כספק ניתן להחלפה (injection token). נרמול שגיאות: כל כשל HTTP הופך ל-`AppError` מסוג מוגדר (`validation` ל-400 עם שגיאות לפי שדה מתוך `errors` של `problem+json`, `unauthorized` ל-401, `forbidden` ל-403, `notFound` ל-404, `conflict` ל-409, `tooMany` ל-429 עם `retryAfterSeconds` מהכותרת `Retry-After`, `server` ל-5XX, `network` לכשל רשת או timeout) עם `status`, `code` (מ-`extensions.code` של השרת אם קיים) והודעה בעברית אחידה מתוך `strings.he.ts`, ובלי חשיפת פרטים פנימיים או גוף שגיאה גולמי. קריאות מוצלחות מחזירות את הגוף כפי שהוא. **ניתוב:** `app.routes.ts` עם `loadComponent` עצל: `''` (עמוד בית זמני), `design-system` (קיים), `about` (עמוד "אודות הקורס" פשוט עם כותרת וטקסט עזר קצר), ו-`**` עם `NotFoundPage`: "הדף אינו נמצא" וקישור חזרה לגלריה (`/`). `withComponentInputBinding` ו-`withInMemoryScrolling` (גלילה לראש בניווט). כותרת הדף (`Title`) מתעדכנת לפי הנתיב באמצעות `title` בהגדרות הנתיב ו-`TitleStrategy` שמוסיף את שם האתר. **מעטפת** (`app.html`): קישור "דלג לתוכן", ניווט צף (`nav-pill`) עם ארבעת הפריטים הקבועים (גלריה `/`, אודות הקורס `/about`, יצירת פרופיל, עריכת הפרופיל שלי) כאשר "יצירת פרופיל" ו"עריכת הפרופיל שלי" מובילים בשלב זה לנתיבי המשך שיוצגו כעמודי הסבר פשוטים: `/join` ("כדי ליצור פרופיל צריך קישור הזמנה מהמנהלת") ו-`/edit` ("כדי לערוך את הפרופיל, בקשי קישור אישי למייל"), כל אחד עמוד סטטי עם כותרת, משפט הסבר וכפתור חזרה לגלריה; הפריט הפעיל מסומן לפי הנתיב (`aria-current="page"`); `main` עם `router-outlet`; המעטפת אינה מציגה את "האתר בהקמה". **קומפוננטות מצב** (`shared/ui/`): `LoadingSkeleton` (שלד כרטיסים עם `aria-busy`, כיבוד `prefers-reduced-motion`, מספר שורות ניתן להגדרה), `EmptyState` (כותרת, טקסט, פעולה אופציונלית), `ErrorState` (הודעה מ-`AppError`, כפתור "ניסיון חוזר" שפולט אירוע, `role="alert"`); כולן עם מחרוזות מהמשאבים ועמוד ההדגמה `design-system` מציג אותן. בדיקות `vitest` (`ng test`) לכל ההתנהגויות שלמעלה, כולל נרמול כל סוגי השגיאה, ניסיון חוזר בעת כשל אסימון, כותרות Turnstile ו-CSRF, ומעטפת. `README.md` בתיקיית הלקוח או בשורש: איך מריצים ומה מבנה התיקיות החדש.

**Never:** אין מסכי מוצר (גלריה, פרופיל, טפסים, ניהול), אין ווידג'ט Turnstile אמיתי, אין שמירת אסימונים או נתונים אישיים ב-`localStorage`/`sessionStorage`, אין `innerHTML` גולמי, אין CORS או כתובות מוחלטות לשרת, אין שינוי בשרת או בפרוקסי, אין ספריות UI חיצוניות.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| GET מוצלח | `GET /api/x` מחזיר 200 | הגוף כפי שהוא | N/A |
| POST עם CSRF | `POST` ראשון | נטען אסימון מ-`/api/profiles/csrf`, נשלח ב-`X-CSRF-TOKEN`, ו-`withCredentials` פעיל | N/A |
| POST שני | אותו session | האסימון נשמר ולא נטען שוב | N/A |
| כשל אסימון | 400 או 403 על אסימון | טעינה חוזרת של האסימון וניסיון חוזר אחד; כשל שני מוחזר כשגיאה | `forbidden` או `validation` |
| Turnstile | `{ turnstile: true }` | כותרת `X-Turnstile-Token` מהספק | N/A |
| ולידציה | 400 `problem+json` עם `errors` | `AppError.validation` עם שגיאות לפי שדה והודעה מהמשאבים | N/A |
| 401 | אין הרשאה | `unauthorized` עם הודעה אחידה | N/A |
| 403, 404, 409 | | `forbidden`, `notFound`, `conflict` | N/A |
| 429 | `Retry-After: 30` | `tooMany` עם `retryAfterSeconds: 30` והודעה | N/A |
| 5XX | שרת נופל | `server` עם הודעה כללית בלי גוף גולמי | N/A |
| רשת | אין חיבור או timeout | `network` עם הודעה כללית | N/A |
| קוד שרת | `extensions.code = "link-invalid"` | `AppError.code` מועבר | N/A |
| ניתוב | `/` | עמוד הבית הזמני | N/A |
| נתיב לא קיים | `/zzz` | `NotFoundPage` עם חזרה לגלריה, כותרת הדף מעודכנת | N/A |
| טעינה עצלה | נתיב `about` | הקומפוננטה נטענת רק בניווט | N/A |
| ניווט | פריט בניווט הצף | הפריט הפעיל מסומן ב-`aria-current="page"` | N/A |
| נגישות | טעינת עמוד | קישור "דלג לתוכן" ראשון, `main` יחיד, `lang="he" dir="rtl"` | N/A |
| מצבי UI | `LoadingSkeleton`, `EmptyState`, `ErrorState` | מוצגים כמצופה; כפתור הניסיון החוזר פולט אירוע; `role="alert"` ב-`ErrorState` | N/A |

</intent-contract>

## Code Map

- `client/src/app/app.ts`, `app.html`, `app.css`, `app.config.ts`, `app.routes.ts` -- מעטפת קיימת עם `RouterOutlet` ודף "האתר בהקמה"; להחליף במעטפת החדשה, לצרף `provideHttpClient(withFetch())`, `withComponentInputBinding` ו-`TitleStrategy`. `app.spec.ts` -- בדיקות קיימות להתאמה.
- `client/src/app/shared/strings.he.ts` -- קובץ המחרוזות היחיד (קיים `nav`, `required`, `designSystem`); להוסיף מפתחות לשגיאות, מצבים, עמודים סטטיים וניווט.
- `client/src/app/shared/ui/{button,chip,field,glass-card,nav-pill}` -- רכיבי מערכת העיצוב (`nav-pill` מקבל פריטים עם `id` ו-`href`; להתאים/להוסיף תמיכה ב-`routerLink` ובפריט פעיל לפי נתיב מבלי לשבור את הבדיקות הקיימות).
- `client/src/app/design-system/` -- עמוד ההדגמה; להוסיף את `LoadingSkeleton`, `EmptyState`, `ErrorState`.
- `client/src/styles.css` -- טוקנים וטיפוגרפיה; להשתמש, לא להוסיף צבעי hex.
- `client/proxy.conf.js` -- פרוקסי `/api` לשרת המקומי; לא לשנות.
- ה-API הרלוונטי (לקריאה בלבד): `GET /profiles/csrf` (anti-forgery אנונימי), תגובות `problem+json` עם `extensions.code` ו-`errors`, `Retry-After` ב-429; ראו `README.md` בשורש.
- אין לשנות: `server/`, `proxy.conf.js`, קבצי הפריסה.

## Tasks & Acceptance

**Execution:**
- `client/src/app/shared/api/` -- `ApiClient`, `AppError`, שירותי `CsrfService` ו-`TurnstileService` (ספק ניתן להחלפה) -- AR15
- `client/src/app/app.routes.ts`, `app.config.ts` -- ניתוב עצל, `NotFoundPage`, עמוד בית זמני, `about`, `join`, `edit` ו-`TitleStrategy` -- UX-DR14
- `client/src/app/app.ts`, `app.html`, `app.css` -- מעטפת עם "דלג לתוכן", ניווט צף ו-`main` -- UX-DR7, UX-DR15
- `client/src/app/shared/ui/` -- `LoadingSkeleton`, `EmptyState`, `ErrorState` ו-`design-system` מעודכן -- UX-DR14
- `client/src/app/**/*.spec.ts` -- בדיקות לשורות המטריצה -- NFR3
- `client/README.md` או סעיף ב-`README.md` -- הרצה ומבנה -- תפעול

**Acceptance Criteria:**
- Given הלקוח, when נשלחת בקשה ל-`/api/*`, then נשלחים אסימון anti-forgery ואסימון Turnstile כשנדרש, והשגיאות (401, 403, 404, 409, 429, 5XX, רשת) מתורגמות להודעות עברית אחידות.
- Given ניווט, when נפתחים נתיבי הבית, `about`, `join`, `edit` ונתיב לא מוכר, then כל אחד נטען בטעינה עצלה ונתיב לא מוכר מציג "הדף אינו נמצא" עם חזרה לגלריה.
- Given כל מסך, when הוא נטען, then הניווט הצף מוצג, יש קישור "דלג לתוכן", ושלדי הטעינה, המצב הריק ומצב השגיאה זמינים כרכיבים משותפים.

## Spec Change Log

## Review Triage Log

## Verification

**Commands:**
- `npm run build` ב-`client/` -- expected: בנייה מצליחה בלי שגיאות
- `npm test -- --watch=false` ב-`client/` -- expected: כל הבדיקות עוברות
