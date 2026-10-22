---
title: 'סיפור 6.2: כניסה וניווט בלקוח'
type: 'feature'
created: '2026-10-22'
status: 'done'
baseline_revision: 'ce3d8ae4129ef9445df984c04dc8d065305a9ed9'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-6-context.md'
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** השרת כבר תומך בכניסה עם גוגל, אבל בדפדפן אין לה שום מסך: אין כפתור כניסה, הניווט עדיין מציג "יצירת פרופיל" ו"עריכת הפרופיל שלי" כשני דפי מציין מקום, ואין מקום ל"הפרופיל שלי".

**Approach:** לקוח בלבד. נתיב `/me` ("הפרופיל שלי") עם מצבי טעינה, לא מחוברת (כפתור "כניסה עם גוגל" והודעות שגיאה לפי `?signin=`), מחוברת (המייל, יציאה, ומקום ריק שבו 6.3 יכניס את הטופס) ושגיאת שרת; פריט ניווט יחיד במקום שניים; ו-`/join` ו-`/edit` מפנים ל-`/me`.

## Boundaries & Constraints

**Always:**
- `/me` קורא `GET /auth/google/me`. 200: מחוברת (`{email, hasProfile, profileId?}`). 401: לא מחוברת. כל שגיאה אחרת: מצב שגיאה עם ניסיון חוזר.
- כפתור "כניסה עם גוגל" הוא ניווט מלא בדפדפן (לא XHR) אל `/api/auth/google/start?returnUrl=/me` (מקודד). אסור `routerLink`.
- פרמטר `signin` בכתובת: `cancelled` מציג "הכניסה לא הושלמה. נסי שוב.", `failed` מציג "הכניסה נכשלה. נסי שוב.", `unverified` מציג "אי אפשר להתחבר עם חשבון זה.", ערך אחר מתעלם. ההודעה ב-`role="alert"` ומוצגת רק במצב לא מחוברת, והפרמטר מוסר מהכתובת (`replaceUrl`) אחרי הקריאה. כל המחרוזות ב-`strings.he.ts` בגוף נקבה למשתתפות.
- יציאה: `POST /auth/google/logout` עם ה-anti-forgery האנונימי הקיים, ואחריה המסך חוזר למצב לא מחוברת. כשל ביציאה: הודעה, והמשתמשת נשארת מחוברת.
- הניווט הצף: גלריה, אודות הקורס, "הפרופיל שלי" (`/me`, פריט פעיל כשהנתיב `/me`). הפריטים `join` ו-`edit` מוסרים.
- `/join` ו-`/edit` הופכים ל-`redirectTo` מלא אל `/me` ורכיבי מציין המקום והמחרוזות שלהם מוסרים. `/join/:token` נשאר ללא שינוי עד 6.3.
- RTL, מיקוד גלוי, תוויות, יעדי מגע 44px ורכיבי מערכת העיצוב. כותרת הדף "הפרופיל שלי".

**Never:**
- לא נוגעים בשרת. לא מוסיפים את טופס היצירה והעריכה (סיפור 6.3). לא שומרים אסימון או נתוני סשן ב-`localStorage` או `sessionStorage`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| לא מחוברת | `/me`, ו-`/auth/google/me` מחזיר 401 | כותרת, הסבר קצר וכפתור "כניסה עם גוגל" שמנווט לכתובת ההתחלה עם `returnUrl=/me` | N/A |
| מחוברת | 200 עם מייל | המייל מהחשבון, כפתור "יציאה", ומקום ריק לטופס 6.3 | N/A |
| בוטלה | `/me?signin=cancelled` | מסך כניסה עם הודעת "הכניסה לא הושלמה. נסי שוב." והפרמטר מוסר מהכתובת | N/A |
| נכשלה | `/me?signin=failed` | הודעת "הכניסה נכשלה. נסי שוב." | N/A |
| לא מאומת | `/me?signin=unverified` | הודעת "אי אפשר להתחבר עם חשבון זה." | N/A |
| ערך זר | `/me?signin=x` | ללא הודעה, והפרמטר מוסר | N/A |
| טעינה | בקשה בדרך | שלד טעינה | N/A |
| שגיאת שרת | 500 או כשל רשת ב-`me` | `error-state` עם ניסיון חוזר | ניסיון חוזר טוען מחדש |
| יציאה | לחיצה על "יציאה" | POST ל-logout, וחזרה למסך לא מחוברת | כשל: הודעה והמשתמשת נשארת מחוברת |
| הפניות | `/join` ו-`/edit` | מגיעים ל-`/me` | N/A |
| ניווט | כל דף | שלושה פריטים בלבד, "הפרופיל שלי" פעיל ב-`/me` | N/A |

</intent-contract>

## Code Map

- `client/src/app/app.ts` (~8-25) -- `NAV_ITEMS` ו-`ACTIVE_BY_PATH`; להחליף את `join` ו-`edit` בפריט `me` (`/me`)
- `client/src/app/app.routes.ts` -- להוסיף `me` (טעינה עצלה, כותרת), ולהמיר `join` ו-`edit` ל-`redirectTo: 'me'` עם `pathMatch: 'full'`; `join/:token` נשאר
- `client/src/app/pages/{join/join.ts,edit/edit.ts}` -- מציני מקום להסרה, יחד עם `strings.join` ו-`strings.edit` אם אינם בשימוש אחר
- `client/src/app/pages/contact/contact-verify.ts`, `token-fragment.ts` -- דפוס דף ציבורי עם `ActivatedRoute`; ניקוי כתובת: להשתמש ב-`router.navigate([], {queryParams:{signin:null}, queryParamsHandling:'merge', replaceUrl:true})`
- `client/src/app/shared/ui/{button,glass-card,loading-skeleton,error-state}` -- ל-`app-button` אין וריאנט קישור; לכפתור הכניסה להוסיף קלט `href` אופציונלי ל-`Button` או להשתמש בעוגן מעוצב באותם סגנונות
- `client/src/app/shared/api/{api-client.ts,csrf.service.ts,app-error.ts}` -- `get`/`post`, CSRF ציבורי (`/profiles/csrf`), `AppError`
- `client/src/app/shared/strings.he.ts` (~489 `nav`) -- להוסיף `nav.myProfile` ו-`myProfile.*`
- `client/proxy.conf.js` -- `/api` עם חיתוך הקידומת לפיתוח; `client/vercel.json`, `client/api/proxy.mjs` -- אין לשנות
- מפרטים לדוגמה: `pages/contact/contact-pages.spec.ts`, `app.spec.ts` (שורות ~105-122 מחזיקות `/join` ו-`/edit` ויש לעדכן)

## Tasks & Acceptance

**Execution:**
- `client/src/app/pages/me/*` -- שירות סשן (`me`, `logout`) ודף `/me` עם מצביו, פרמטר `signin` וניקויו -- FR-11, UX-DR21
- `client/src/app/app.ts`, `app.routes.ts`, הסרת מציני המקום, `strings.he.ts` -- ניווט, הפניות ומחרוזות -- UX-DR7
- קבצי `*.spec.ts` (כולל עדכון `app.spec.ts`) -- בדיקה לכל שורה במטריצה, כולל ניווט מלא לכתובת ההתחלה ו-`http.verify()`

**Acceptance Criteria:**
- Given כל דף באתר, when הניווט מוצג, then הוא כולל בדיוק גלריה, אודות הקורס והפרופיל שלי.
- Given `ng test --watch=false` ו-`ng build`, when הם רצים, then שניהם עוברים בלי שגיאות או אזהרות חדשות.

## Spec Change Log

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: 16 findings (מקובצים לפי סיבת שורש מארבע השכבות) — high 0, medium 2, low 12, false 2, maybe-false 0
- findings:
  - `[medium]` `[patch]` הודעת `signin` נשמרת מצילום מצב שאינו מתנקה, ולכן אחרי יציאה או ניסיון חוזר היא עלולה להופיע שוב בלי פרמטר — אות (signal) שמתנקה ביציאה ובניסיון חוזר, עם בדיקות
  - `[medium]` `[patch]` 401 ב-`POST /auth/google/logout` מציג "היציאה נכשלה" ומשאיר מחוברת — מטופל כיציאה מוצלחת, עם בדיקה
  - `[low]` `[patch]` כותרת הדף של `/me` אינה נבדקת בנתיב האמיתי — נוספה בדיקה ב-`app.spec.ts`
  - `[low]` `[reject]` משתתפת מחוברת רואה רק מייל ויציאה, בלי פעולה — מכוון: הטופס נכנס בסיפור 6.3
  - `[low]` `[reject]` `/join` הפך להפניה ללא הסבר על הזמנה — הזמנה אינה נדרשת עוד, והקישור `/join/:token` ממשיך לעבוד
  - `[low]` `[reject]` מחרוזות `nav.createProfile` ו-`nav.editProfile` נשארו בשימוש הדגמת מערכת העיצוב בלבד — ללא נזק
  - `[low]` `[reject]` `GOOGLE_START_URL` יחסי בלבד ותלוי בפרוקסי — מתועד בהערות המפרט ובפיתוח `proxy.conf.js` קיים
  - `[low]` `[reject]` שימוש ב-snapshot במקום `queryParamMap` תגובתי — הקולבק הוא טעינת דף מלאה
  - `[low]` `[reject]` תגובה מאוחרת של `me` שדורסת יציאה, ו-`email` חסר בתגובה — מרוצים ותגובות לא תקינות שאינם נראים בפועל
  - `[low]` `[reject]` בדיקות חלשות: lazy-routes עם redirect, `expectPost` שמרוקן כל csrf, כפל לחיצה ביציאה, מקלדת — דפוס מועתק, ללא סיכון ממשי
  - `[low]` `[reject]` נגישות: ללא הכרזה בהחלפת מצב, ובלי מיקוד אחרי יציאה, עיצוב לטקסט מייל ארוך — מחוץ להיקף
  - `[low]` `[reject]` הודעת `unverified` ללא הסבר מה לעשות — הנוסח נקבע במפרט ובעיצוב
  - `[low]` `[reject]` הנחות על חוזה השרת (`MeSession`, שמות הפרמטרים) — נבדקו מול 6.1 ו-6.5 בעת כתיבת המפרט
  - `[low]` `[reject]` אין בדיקת קצה לקצה בדפדפן — נרשם כבדיקה ידנית ב-Verification
  - `[false]` `[reject]` "ייבוא Router ו-HttpTestingController חסר ב-app.spec" — הבדיקות עוברות ומהדרות
  - `[false]` `[reject]` "הניווט ל-/join/:token לא פעיל" — הנתיב לא נכלל בהיקף ונשאר ללא שינוי

## Design Notes

כפתור הכניסה מנווט את הדפדפן כולו כי הזרימה היא redirect לספק וחזרה עם עוגיות; XHR לא יכול לבצע אותה. אחרי הכניסה השרת מפנה חזרה ל-`returnUrl` (`/me`), והדף קורא שוב ל-`me`. בפיתוח, `/api` עובר דרך `proxy.conf.js`, ו-redirect_uri חייב להתאים ל-`localhost:4200`.

## Verification

**Commands:**
- `cd client && npx ng test --watch=false` -- expected: כל הבדיקות עוברות
- `cd client && npx ng build` -- expected: בנייה מצליחה בלי אזהרות חדשות

**Manual checks (if no CLI):**
- ב-`ng serve` מול השרת בפיתוח (`GOOGLE_PROVIDER=fake`): `/me` מציג כניסה, לחיצה מחזירה מחוברת עם המייל הדמה, ויציאה חוזרת למצב לא מחוברת.

## Auto Run Result

Status: done

- **שינוי שמומש (לקוח בלבד):** דף `/me` ("הפרופיל שלי") עם מצבי טעינה, לא מחוברת (כפתור "כניסה עם גוגל" כניווט מלא ל-`/api/auth/google/start?returnUrl=%2Fme`, והודעות `?signin=cancelled|failed|unverified` שמתנקות מהכתובת), מחוברת (המייל, יציאה ומקום ריק לטופס 6.3) ושגיאה עם ניסיון חוזר; הניווט הצף עם שלושה פריטים; `/join` ו-`/edit` מפנים ל-`/me`; ומציני המקום והמחרוזות הישנים הוסרו.
- **ממצאי סקירה:** 16 ממצאים מקובצים. תוקנו 2 בדרגת medium ו-1 low: הודעת `signin` שחזרה אחרי יציאה או ניסיון חוזר (עכשיו אות שמתנקה), 401 ביציאה כיציאה מוצלחת, ובדיקת כותרת `/me` בנתיב האמיתי. נדחו 12 ממצאי low ו-2 false, עם נימוק בלוג.
- **המלצה לסקירת המשך:** לא. אין סיכון לא מאומת מעבר לבדיקה הידנית של הזרימה המלאה בדפדפן מול השרת במצב `fake`.
- **אימות:** `ng test --watch=false`: 345 בדיקות ב-27 קבצים עברו. `ng build` הצליח בלי אזהרות.
- **סיכונים נותרים:** הזרימה המלאה (פרוקסי `/api`, redirect_uri ועוגיות) לא נבדקה בדפדפן. מסך מחוברת מציג רק מייל ויציאה עד סיפור 6.3.
