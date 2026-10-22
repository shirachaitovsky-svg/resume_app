---
title: 'סיפור 5.10: עמודי תוכן, נגישות ובדיקות קצה לקצה'
type: 'feature'
created: '2026-10-23'
status: 'done'
baseline_revision: '2a091dad30e8c265b36ecdb146d7927f8e1cc0c7'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-5-context.md'
warnings: [multiple-goals, oversized]
deferred: []
---

<intent-contract>

## Intent

**Problem:** עמוד "אודות הקורס" הוא עדיין מציין מקום, אין בדיקות נגישות שיטתיות למסכים הראשיים, ואף פעם לא הורצה הזרימה המלאה בדפדפן אמיתי מול שרת אמיתי: החוזה בין הלקוח לשרת נבדק רק מול תגובות מדומות ומול HTTP בשרת.

**Approach:** שלושה חלקים מסודרים. (א) תוכן אמיתי לעמוד "אודות". (ב) בדיקות נגישות אוטומטיות עם axe-core למסכים הראשיים, ותיקון הפרות חוסמות. (ג) בדיקת קצה לקצה בדפדפן עם Playwright (Chrome מותקן) מול סביבה מקומית מלאה, שמריצה את כל התרחיש: מנהלת, מחזור, משתתפת עם גוגל מדומה, אישור, גלריה, בקשת פרטי קשר ומסירה.

## Boundaries & Constraints

**Always:**
- אודות הקורס: תוכן אמיתי בעברית ב-`strings.he.ts` (מהו האתר, איך משתתפת מצטרפת בכניסה עם גוגל ואישור המנהלת, פרטיות פרטי הקשר ומסירה רק אחרי שני אישורים), עם כותרת אחת `h1`, פסקאות וקישורי חזרה, בלי שמות או פרטים אמיתיים. אותו עיצוב כמו הדפים הקיימים. נוסח "הדף אינו נמצא" נשאר.
- נגישות: `axe-core` כחבילת פיתוח של הלקוח, בבדיקות הקיימות (vitest + jsdom). כל מסך ראשי נבדק בכל מצב מרכזי שלו: גלריה (טעינה, תוכן, ריק), עמוד פרופיל (כולל טופס בקשת פרטי קשר), `/me` (לא מחוברת, יצירה, עריכה, ממתין, הוחזר), דפי `contact/verify` ו-`contact/decide`, אודות, לא נמצא, וכניסת מנהלת ומסכי הניהול (תור סקירה, פרטי הגשה, פרופילים, מחזורים, בקשות פרטי קשר). הכללים: תוויות, שמות נגישים, תפקידים, היררכיית כותרות, מיקוד, `lang`/`dir`, ללא `color-contrast` (jsdom לא מחשב צבעים). הפרות חוסמות (`critical`, `serious`) מתוקנות בקוד, ולא מושתקות. כל כלל שמושבת מתועד עם נימוק.
- ידני בקוד (ומתועד ב-`## Implementation Notes`): לכל מסך שנסרק, בדיקה של `h1` יחיד, אינדיקציות שאינן בצבע בלבד (צ'יפים ותגיות סטטוס), יעדי מגע של 44px, מיקוד גלוי, `prefers-reduced-motion`, ו-skip link ו-`main`. ממצאים נפתרים באותו סיפור.
- קצה לקצה: חבילה נפרדת `e2e/` (`@playwright/test`, Chrome המותקן דרך `channel: 'chrome'`, ללא הורדת דפדפנים), עם `npm run e2e`. הסקריפט מכין סביבה מבודדת: מסד נתונים `resume_e2e` בקונטיינר `resume-dev-pg` (מחיקה ויצירה מחדש בכל ריצה), שרת .NET בפורט ייעודי עם `ASPNETCORE_ENVIRONMENT=Development`, `GOOGLE_PROVIDER=fake`, `ORIGIN_SECRET`, `ADMIN_USERNAME`/`ADMIN_INITIAL_PASSWORD` ו-`DATABASE_URL`, ו-`ng serve` בפורט ייעודי עם פרוקסי שמצביע לשרת (להוסיף ל-`proxy.conf.js` משתנה סביבה `API_TARGET` עם ברירת מחדל `http://localhost:5000`, בלי לשנות את ברירת המחדל). Mailpit (`resume-mailpit`) קורא מיילים דרך ה-API שלו, ומנוקה בתחילת ריצה.
- התרחיש (בדפדפן, דרך הממשק, מלבד קריאות עזר מוגדרות): כניסת מנהלת ראשונה, החלפת סיסמה והרשמת TOTP (המפתח נקרא מעמוד ההרשמה ומחושב קוד RFC 6238 בעזרת `crypto` של Node), יצירת מחזור; משתתפת נכנסת עם גוגל מדומה (`fake_email`) ויוצרת פרופיל במחזור שנבחר; המנהלת מאשרת את ההגשה והפרופיל מופיע בגלריה ובעמוד הפרופיל; מבקר שולח בקשת פרטי קשר ומאמת מהמייל; המנהלת שולחת למשתתפת; המשתתפת מאשרת מהמייל; המנהלת מאשרת מסירה, והמבקר מקבל את המייל והנייד (קריאה מ-Mailpit). בנוסף: משתתפת עורכת (ממתין לאישור), המנהלת מחזירה לתיקון עם הערה, וההערה מופיעה אצלה ב-`/me`.
- באגים שהבדיקה מגלה (חוזה לא תואם, תגובה אחרת מהצפוי, מצב לא מטופל) מתוקנים באותו סיפור ובבדיקת רגרסיה מתאימה (בלקוח או בשרת), ומתועדים ב-`## Implementation Notes`.
- אם בפועל לא ניתן להריץ (אין docker, כרום או אינטרנט ל-Turnstile של בדיקת Cloudflare), ההחלטה והמגבלה מתועדות ב-README, ונוספת חלופה: בדיקת `E2E` בשרת (`dotnet test --filter E2E`) שמריצה את השרשרת בקריאות HTTP בלבד מול Postgres אמיתי. מהותה: להראות את הסיבה ולא להשאיר את החור שקט.
- ריצה מלאה אחת של שרת בכל פעם, בלי להרוג `testhost`, ו-`TestResults`, `node_modules`, `e2e/test-results` ו-`playwright-report` לא נכנסים לקומיט (`.gitignore`).

**Never:**
- לא משנים התנהגות מוצר מעבר לתיקוני נגישות ובאגים שהבדיקה מגלה. לא נוגעים בפריסה. לא מוסיפים תלות בשירות חיצוני בזמן ריצת בדיקות הלקוח.
- לא משביתים כלל נגישות כדי "להעביר" בדיקה בלי נימוק.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| אודות | `/about` | `h1` יחיד, תוכן אמיתי, כותרת דף, קישור חזרה | N/A |
| סריקת נגישות | כל מסך ראשי בכל מצב מרכזי | אין הפרות `critical` או `serious` | הפרה מתוקנת בקוד, כלל מושבת מתועד |
| e2e: מנהלת | הרצה נקייה | כניסה, החלפת סיסמה, TOTP והמחזור נוצרים | כשל מציג שלב ושגיאה |
| e2e: משתתפת | כניסה עם גוגל מדומה | פרופיל נוצר `pending`, ו-`/me` עובר לעריכה | N/A |
| e2e: אישור | המנהלת מאשרת | הפרופיל בגלריה ובעמוד פרופיל ללא נייד ומייל | N/A |
| e2e: פרטי קשר | בקשה, אימות, פנייה, החלטה, מסירה | המבקר מקבל מייל עם המייל והנייד בלבד | N/A |
| e2e: החזרה לתיקון | המנהלת מחזירה עם הערה | המשתתפת רואה באנר והערה ב-`/me` | N/A |
| סביבה חסרה | אין docker, כרום או רשת | הסקריפט נכשל בהודעה ברורה, והחלופה בשרת קיימת | מתועד ב-README |

</intent-contract>

## Code Map

- `client/src/app/pages/about/about.ts`, `shared/ui/info-page/info-page.ts`, `shared/strings.he.ts` (~94 `about`, ~`notFoundPage`), `pages/not-found/not-found.ts` -- עמוד אודות כמציין מקום; להחליף בתוכן אמיתי, ובפריסת עמוד תוכן (כותרת, פסקאות, רשימה)
- `client/src/app/app.html`, `index.html`, `styles.css` (~56, 158, 163) -- skip link, `main`, `lang="he" dir="rtl"`, מיקוד גלוי, יעד מגע, reduced-motion
- `client/package.json`, `angular.json` (test builder `@angular/build:unit-test`, vitest, jsdom) -- הוספת `axe-core` כחבילת פיתוח וקובץ עזר `a11y-test-helper.ts` (`expectNoSeriousViolations(fixture/element)`)
- `client/src/app/pages/{gallery,profile,me,contact,admin}/**` -- המסכים לסריקה ולתיקון
- `client/proxy.conf.js` (~`/api` עם `ORIGIN_SECRET`) -- להוסיף `API_TARGET`
- שרת: `server/Api/Program.cs`, `AuthEndpoints.cs`, `GoogleAuthEndpoints.cs`, `CloudflareTurnstileVerifier.cs`, Mailpit API `http://localhost:8025/api/v1/messages`; README
- חדש: `e2e/{package.json,playwright.config.ts,global-setup.ts,tests/*.spec.ts,helpers/{totp.ts,mailpit.ts,env.ts}}`
- `.gitignore` -- `e2e/node_modules`, `e2e/test-results`, `e2e/playwright-report`

## Tasks & Acceptance

**Execution:**
- `client/src/app/pages/about/*`, `strings.he.ts`, `app.routes.ts`/`not-found` -- תוכן אמיתי לאודות
- `client/package.json`, `client/src/app/testing/a11y.ts` -- `axe-core` ועוזר -- NFR3
- בדיקות נגישות למסכים ולמצבים, ותיקוני הקוד שהם חושפים -- NFR3, UX-DR15
- `e2e/**`, `client/proxy.conf.js`, `.gitignore`, `README.md` -- חבילת e2e והתרחיש המלא -- AR22
- תיקוני באגים שהריצה חושפת, עם בדיקה

**Acceptance Criteria:**
- Given `ng test --watch=false` ו-`ng build`, when הם רצים, then עוברים בלי אזהרות חדשות, כולל בדיקות axe למסכים הראשיים.
- Given `npm run e2e` בתיקיית `e2e`, when הוא רץ על מחשב עם docker, Chrome ורשת, then התרחיש המלא עובר.
- Given הסביבה אינה מאפשרת e2e בדפדפן, when נבדק, then קיימת חלופה בשרת ותיעוד ברור של המגבלה.

## Spec Change Log

## Review Triage Log

### 2026-10-23 — Review pass
- verdicts: 16 findings (מקובצים לפי סיבת שורש מארבע השכבות) — high 0, medium 5, low 9, false 2, maybe-false 0
- findings:
  - `[medium]` `[patch]` בדיקת "h1 יחיד" ב-`app.spec` מקבלת אפס (`<= 1`), ו-`h1` לא נבדק בגלריה, ב-`my-profile` ובמצבי שגיאה של `join-redirect` ו-`contact-decide` — הוחמרה ל-`h1` אחד בדיוק ונוספו בדיקות
  - `[medium]` `[patch]` מחיקת כל תיבת הדואר המשותפת של Mailpit בכל ריצה, ושגיאת רשת בודדת בסקר מבטלת את התרחיש — מחיקה ממוקדת לכתובות התרחיש וניסיון חוזר
  - `[medium]` `[patch]` `ensureFree` מבוסס `fetch` ומזהה פורט תפוס שאינו HTTP כפנוי, וה-teardown לא מגיע לתהליכי הבן — בדיקת TCP, `detached` ו-SIGINT/SIGTERM
  - `[medium]` `[patch]` בדיקות e2e חלשות: לולאת TOTP שגורמת לכשל כוזב, `toHaveCount(0)` שעלול לעבור לפני טעינת הפרופיל, ובדיקת `'050'` — הוקשחו
  - `[medium]` `[patch]` עוזר axe: תיעוד שגוי ו-`getContext` של canvas שנדרס ואינו משוחזר, `safe-markdown` עם seed שגוי, ו-`content: '✓' / ''` בלי fallback — תוקנו
  - `[low]` `[patch]` משתני חיבור ל-Postgres קבועים, ו-README בלי דרישות Chrome ורשת — נקראים ממשתני סביבה, עם הגנה על שם המסד, ותועדו
  - `[low]` `[reject]` `x-forwarded-proto: https` קבוע בפרוקסי הפיתוח — נדרש כי מדיניות העוגייה `Always` בשרת, ומתועד ב-README ובקוד
  - `[low]` `[reject]` `DROP DATABASE WITH (FORCE)` דורש Postgres 13+ — הקונטיינר הוא 16
  - `[low]` `[reject]` תוכן "אודות" נכתב ללא אישור בעלת התוכן, וכולל התחייבויות פרטיות — טיוטה לפי הבריף והאפיון, והמנהלת תאשר נוסח לפני עלייה
  - `[low]` `[reject]` אין CI לחבילת e2e ובשורש, וגרסאות TypeScript שונות מהלקוח — אין CI בפרויקט, והבדיקה נשארת ידנית לפי ההחלטה
  - `[low]` `[reject]` ניגודיות הצבעים מנוטרלת ב-axe ללא בדיקה בקוד ואין axe בריצת Playwright — מגבלה מתועדת: jsdom בלי צבעים
  - `[low]` `[reject]` כשלים לא נבדקים ב-e2e (בקשה שנדחתה, קישור שפג, הגבלת קצב) ו-`retries: 0` — מכוסים בבדיקות השרת והלקוח
  - `[low]` `[reject]` בדיקת e2e מוסיפה `fake_email` להפניה מ-`start` בנתב — נדרש כי הספק המדומה בוחר זהות לפי פרמטר
  - `[low]` `[reject]` קוסמטי: prettier ב-`contact-pages.spec`, קבצי CSS משותפים לעמוד אודות, גרסאות חבילה
  - `[false]` `[reject]` "כותרות אודות ולא נמצא מכפילות את InfoPage" — משתמשות באותו רכיב
  - `[false]` `[reject]` "שרת לא שונה למרות שהבדיקה מצאה באג" — הבאג היה בפרוקסי הפיתוח בלקוח, ותוקן שם

## Implementation Notes

- אודות: `about.ts` מציג `h1`, פסקת פתיחה, שלושה סעיפים (`h2` ופסקאות) וקישור חזרה, מתוך `STRINGS.about`.
- נגישות: `axe-core` + `client/src/app/testing/a11y.ts` (`expectNoSeriousViolations`, `expectSingleH1`), משולבים בבדיקות הקיימות (גלריה, פרופיל וטופס בקשת פרטי קשר, `/me`, `my-profile`, contact verify/decide, אודות ולא נמצא ב-`app.spec.ts`, כניסה והגדרת מנהלת, תור סקירה ופרטי הגשה, פרופילים, מחזורים, בקשות פרטי קשר, חלונות אישור). כללים מושבתים בנימוק (ב-`a11y.ts`): `color-contrast` (jsdom), `region`, `page-has-heading-one`, `landmark-one-main` (מסך נבדק בלי מעטפת). גם הפרות `moderate` מכשילות. `src/app/testing/**` הוצא מ-`tsconfig.app.json`.
- ממצאים שתוקנו: (1) `safe-markdown`: קורות חיים שמתחילים ב-`##` יצרו `h2` ואז `h4` (דילוג רמה); הרמות עכשיו יחסיות לרמה הרדודה ביותר בטקסט (+ בדיקת רגרסיה). (2) `error-state` שמחליף עמוד שלם (פרופיל, `/me`, contact verify/decide, join) לא היה לו `h1`; נוסף קלט `asPage` שמציג `h1`. (3) צ'יפ נבחר סומן בצבע בלבד; נוסף סימון V ובמשקל גופן (`aria-pressed` נשאר ההכרזה).
- ידני בקוד: `h1` יחיד נבדק לכל מסך שנסרק; תגיות סטטוס כוללות טקסט (שורה שונתה מציגה "(שונה)" בטקסט); יעדי מגע 44px דרך `--size-touch-min`/`--size-button` ו-`::before` של הצ'יפ; `:focus-visible` גלובלי; `prefers-reduced-motion` גלובלי; skip link ו-`main` נבדקים ב-`app.spec.ts`.
- e2e: `e2e/` (`npm run e2e`), `global-setup.ts` מכין מסד `resume_e2e`, Mailpit, שרת בפורט 5100 ו-`ng serve` בפורט 4300 ומפרק רק את מה שהפעיל. הכניסה עם גוגל המדומה: `fake_email` נוסף בהפניה מ-`/api/auth/google/start` (route.fetch). 7 שלבים עוברים (כ-40 שניות).
- באג שהריצה חשפה: בפיתוח מקומי כל בקשת שינוי נכשלה בשרת (`The antiforgery system has the configuration value AntiforgeryOptions.Cookie.SecurePolicy = Always, but the current request is not an SSL request`), כי הפרוקסי של הלקוח מעביר http בלי `x-forwarded-proto`. תוקן ב-`client/proxy.conf.js` (כותרת `x-forwarded-proto: https`, כמו Vercel); בדיקת הרגרסיה היא תרחיש ה-e2e עצמו. בנוסף נדרש `ALLOWED_ORIGINS` לכתובת הלקוח, כי הפרוקסי מחליף `Host` (מתועד ב-README).
- נייד נשמר ונמסר כספרות בלבד (`0501234567`), וזו התנהגות קיימת.
- הערה לתהליך: סוכן המימוש פגע בקובץ המפרט הזה בזמן הסקירה (נמחקו סעיפים), והקובץ שוחזר במלואו מהמקור, עם הרישומים לעיל.

## Design Notes

jsdom אינו מחשב פריסה או צבעים, ולכן axe שם מכסה תוויות, שמות, תפקידים והיררכיה, ואילו הניגודיות והיעדים הפיזיים נבדקים בקוד ובקריאה של הטוקנים. בדיקת הדפדפן מכסה את מה שמסך אחד לא מראה: פרוקסי `/api`, עוגיות `__Host-`/`Secure` על `localhost`, ה-redirect לספק המדומה והחזרה, והחוזה האמיתי. TOTP מחושב מהמפתח שמוצג בעמוד ההרשמה, כך שהבדיקה עוברת דרך הממשק האמיתי.

## Verification

**Commands:**
- `cd client && npx ng test --watch=false` -- expected: כל הבדיקות עוברות
- `cd client && npx ng build` -- expected: בנייה מצליחה בלי אזהרות חדשות
- `cd e2e && npm ci && npm run e2e` -- expected: התרחיש עובר (דורש docker, Chrome ורשת)
- `dotnet test server` -- expected: רק אם שונה קוד שרת בגלל באג שנחשף

## Auto Run Result

Status: done

- **שינוי שמומש:** (א) עמוד "אודות הקורס" עם `h1`, פסקת פתיחה ושלושה סעיפים. (ב) בדיקות נגישות עם `axe-core` (עוזר `testing/a11y.ts`) לכל המסכים הראשיים ומצביהם, ותיקונים: `safe-markdown` עם רמות כותרת יחסיות, `error-state` עם `asPage` ו-`h1`, וצ'יפ נבחר עם סימן V. (ג) חבילת `e2e/` עם Playwright (Chrome מותקן) שמריצה את התרחיש המלא מול סביבה מקומית מבודדת (מסד `resume_e2e`, שרת בפורט 5100, `ng serve` בפורט 4300, Mailpit): מנהלת (סיסמה, TOTP, מחזור), משתתפת עם גוגל מדומה, אישור, גלריה, בקשת פרטי קשר, פנייה, החלטה, מסירה, והחזרה לתיקון עם הערה.
- **באג אמיתי שנחשף ותוקן:** בפיתוח מקומי כל בקשת שינוי נכשלה בשרת כי הפרוקסי לא שלח `x-forwarded-proto` (מדיניות העוגייה `Always`). תוקן ב-`client/proxy.conf.js`, ו-`ALLOWED_ORIGINS` תועד.
- **ממצאי סקירה:** 16 ממצאים מקובצים. תוקנו 5 בדרגת medium ו-1 low (בדיקות `h1` מחמירות, ניקוי ממוקד של Mailpit וסקר חסין, בדיקת פורטים ב-TCP וסגירת תהליכים, הקשחת התרחיש, עוזר axe ו-`safe-markdown` והצ'יפ). נדחו 9 low ו-2 false, עם נימוק בלוג.
- **המלצה לסקירת המשך:** כן. סיכון לא מאומת: תוכן עמוד "אודות" כולל התחייבויות פרטיות ודורש אישור נוסח של מנהלת הקורס; ניגודיות הצבעים וגודל יעדי המגע לא נבדקים אוטומטית; בדיקת e2e דורשת docker, Chrome ורשת (Turnstile).
- **אימות:** `ng test --watch=false`: 384 בדיקות ב-30 קבצים עברו. `ng build` נקי. `npm run e2e`: 7 מתוך 7 עברו (כ-43 שניות), בריצה שלי ובריצת הסוכן. לא הורצה ריצת שרת מלאה (לא שונה קוד שרת).
- **סיכונים נותרים:** e2e תלוי ברשת (Turnstile של Cloudflare) ובקונטיינרים המקומיים; התוכן ב"אודות" טיוטה.
