---
title: 'סיפור 5.9: בקשות פרטי קשר (לקוח)'
type: 'feature'
created: '2026-10-22'
status: 'done'
baseline_revision: '1b777e5c0969c17a307ddf2660f0b9055ca00a24'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-5-context.md'
warnings: [oversized]
deferred:
  - 'ווידג׳ט Turnstile אמיתי בטופס בקשת פרטי הקשר (אין מפתחות): נשאר הטוקן המדומה DEV_TURNSTILE_TOKEN'
---

<intent-contract>

## Intent

**Problem:** כל שרשרת בקשות פרטי קשר כבר קיימת בשרת (3.1 עד 3.4), אבל בדפדפן אין לה שום מסך: אין כפתור בקשה בפרופיל, אין דפי `/contact/verify` ו-`/contact/decide` שאליהם מפנים המיילים, ואין למנהלת מסך לטפל בבקשות.

**Approach:** לקוח בלבד, על נקודות הקצה הקיימות. טופס בקשה בעמוד הפרופיל הציבורי, דף אימות כתובת המבקר, דף החלטת המשתתפת (תצוגה מקדימה, אישור או סירוב), ומסך ניהול "בקשות פרטי קשר" עם שליחה למשתתפת ואישור מסירה.

## Boundaries & Constraints

**Always:**
- הטוקן ב-`#token=` בלבד: נקרא מהפרגמנט, מנוקה מהכתובת (`replaceState`) מיד, ונשלח ב-POST בגוף. דפים אלה אינם צורכים אסימון בטעינה (GET), והפעולה היחידה שצורכת היא לחיצה או POST מפורש.
- הטופס הציבורי: שם, מייל וסיבה חובה, שגיאה ליד השדה ובסיכום, הנתונים נשמרים בשגיאה, הגשה אחת בכל פעם (`busy`). התגובה זהה לכל פרופיל (202), והמסך אינו מגלה אם הפרופיל קיים. אין בו שום פרט קשר של המשתתפת.
- דף ההחלטה מציג רק שם המבקר והסיבה (דרך `safe-markdown`), בלי כתובת המבקר. 401/410/409 מוצגים בהודעה נפרדת: "הקישור אינו תקף", "הקישור פג", "כבר ענית על הבקשה הזו". קוד 410 מזוהה לפי `err.code` או `err.status`, לא לפי `kind`.
- מסך הניהול: כל טקסט חופשי דרך `safe-markdown`; לכל בקשה מוצגים בנפרד "אישור משתתפת" ו"אישור מנהלת"; "שליחה למשתתפת" פעיל רק ב-`submitted`; "אישור ושליחה" פעיל רק ב-`participant_approved`, נעול ומסביר מה חסר בשאר המצבים, ובחלון אישור עם ניסוח התוצאה ולמי יישלחו הפרטים. מצב `deliveryStatus` ו-`deliveryErrorCategory` מוצגים, ו-`dead` מאפשר ניסיון ידני (אישור חוזר). 409 גורם לטעינה שקטה והודעה. 401 מעביר לכניסה.
- הכול בעברית, RTL, מחרוזות מ-`strings.he.ts`, ושימוש ברכיבי מערכת העיצוב.

**Never:**
- לא נוגעים בשרת. לא מציגים כתובת מבקר למשתתפת. לא מציגים מייל או נייד של משתתפת בשום מסך.
- לא מחליפים את שירות ה-Turnstile המדומה לווידג'ט אמיתי (אין מפתחות): נשאר הטוקן המדומה, ונרשם ב-`deferred`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| הגשת בקשה | פרופיל ציבורי, שם, מייל וסיבה תקינים | 202, "שלחנו אליך מייל" והסבר, בלי פרטי קשר | שדה חסר או לא תקין: שגיאה ליד השדה. 429: הודעה עם המתנה. 403/5xx: הודעה כללית והנתונים נשמרים |
| אימות כתובת | `/contact/verify#token=...` | הפרגמנט מנוקה, POST, "הכתובת אומתה והבקשה נשלחה" | 401: "הקישור אינו תקף או פג" |
| תצוגה מקדימה | `/contact/decide#token=...` | POST preview, שם המבקר והסיבה, כפתורי "אישור" ו"סירוב" | 401/410/409: הודעה מתאימה בלי כפתורים |
| החלטה | לחיצה על אישור או סירוב | POST decide, מסך תודה, בלי הודעה למבקר | 409/410: ההודעות לעיל. שגיאה אחרת: ניסיון חוזר |
| תור בקשות | מנהלת מחוברת | טבלה: מבקר, פרופיל, מחזור, תאריך, מצב, שני האישורים, מסירה | ריק: מצב ריק. 401: כניסה |
| שליחה למשתתפת | בקשה `submitted` | חלון אישור, אחריו 200 והמצב מתעדכן | 409 `request-not-submitted`: רענון והודעה |
| אישור ושליחה | בקשה `participant_approved` | חלון אישור עם ניסוח, אחריו 200 ו-`deliveryStatus` pending | 409 `request-not-approved`: רענון והודעה. בקשה אחרת: הכפתור נעול עם הסבר |

</intent-contract>

## Code Map

- `client/src/app/pages/profile/profile.{ts,html,css}` -- עמוד הפרופיל (מצבי loading/ready/unavailable/error); להוסיף כפתור "בקשת פרטי קשר" (כחול) וטופס בקשה
- `client/src/app/app.routes.ts` -- להוסיף `contact/verify` ו-`contact/decide` לפני `**`
- `client/src/app/shared/api/{api-client.ts,csrf.service.ts,turnstile.service.ts,app-error.ts}` -- `post(..., {turnstile:true})`, CSRF אנונימי, `AppError.code/status`; אין לשנות
- `client/src/app/pages/join/join-form.ts` -- דפוס טופס, שגיאות שדה והגשה להעתקה
- `client/src/app/pages/admin/review/{review.service.ts,admin-review.ts,submission-detail.ts}`, `admin/admin.routes.ts`, `admin/home/admin-home.ts` -- דפוס מסך ניהול, שירות, `loadSeq`, `ConfirmDialog`, ניווט
- `client/src/app/shared/strings.he.ts` -- להוסיף `profile.contact`, `contactVerify`, `contactDecide`, `admin.contactRequests`
- `client/src/app/shared/ui/*` -- `field`, `textarea`, `button`, `confirm-dialog`, `safe-markdown`, `empty-state`, `error-state`, `loading-skeleton`
- מפרטים לדוגמה: `pages/profile/profile.spec.ts`, `pages/join/join-form.spec.ts`, `pages/admin/review/admin-review.spec.ts`
- שרת (לקריאה בלבד): `POST /profiles/{id}/contact-requests` (`{name,email,reason}`, 202), `POST /contact-requests/verify` (`{token}`), `POST /contact-decisions/preview` ו-`/decide` (`{token,decision}`), `GET /admin/contact-requests?status&limit=100`, `POST /admin/contact-requests/{id}/send-to-participant` ו-`/approve-delivery`

## Tasks & Acceptance

**Execution:**
- `client/src/app/pages/profile/*`, `shared/strings.he.ts` -- כפתור וטופס בקשת פרטי קשר עם אימות שדות, מצב הצלחה ושגיאות -- FR-13
- `client/src/app/pages/contact/{verify,decide}/*`, `app.routes.ts` -- דפי אימות והחלטה עם קריאת פרגמנט וניקויו ומצבי שגיאה -- FR-13, FR-15
- `client/src/app/pages/admin/contact-requests/*`, `admin.routes.ts`, `admin-home.ts` -- שירות ומסך תור עם שליחה למשתתפת ואישור ושליחה -- FR-14, FR-15, FR-16
- קבצי `*.spec.ts` -- בדיקות לכל שורה במטריצה, כולל ניקוי הפרגמנט ואי-צריכת אסימון בטעינה

**Acceptance Criteria:**
- Given טעינת `/contact/decide` או `/contact/verify` עם פרגמנט, when הדף נטען, then הפרגמנט נמחק מהכתובת ולא נשלחת בקשת צריכה עד לפעולה מפורשת (באימות: POST אחד אוטומטי, כי הקישור מיועד לכך).
- Given כל מסכי הבקשות, when נבדק התוכן, then אין בהם מייל או נייד של משתתפת.
- Given `ng test --watch=false` ו-`ng build`, when הם רצים, then שניהם עוברים בלי שגיאות או אזהרות חדשות.

## Spec Change Log

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: 18 findings (מקובצים לפי סיבת שורש מארבע השכבות) — high 0, medium 3, low 13, false 2, maybe-false 0
- findings:
  - `[medium]` `[patch]` דף האימות מציג מצב שגיאה עם ניסיון חוזר גם ל-410, 409 ו-400, וניסיון חוזר לא יצליח — הוכרעו כקישור לא תקף או פג, ונוספו בדיקות
  - `[medium]` `[patch]` נוסח הטופס הציבורי ודף האימות במין נקבה, בניגוד לכלל נייטרלי למבקרים (EXPERIENCE.md) — נוסחו מחדש בלשון נייטרלית
  - `[medium]` `[patch]` הנתיבים האמיתיים `/contact/verify` ו-`/contact/decide` אינם נבדקים ב-`app.spec` (המפרט בונה נתב פרטי), ושגיאת כתיבה תשבור את קישורי המייל בלי שבדיקה תיכשל — נוספו שני מקרים ב-`app.spec.ts`
  - `[low]` `[patch]` אין בדיקת יחידה ל-`takeTokenFromFragment` — נוספה
  - `[low]` `[patch]` כותרת בדיקה מטעה בתור הניהול — שונתה
  - `[low]` `[reject]` ריענון אחרי ניקוי הפרגמנט מציג "קישור לא תקף" — הניקוי נדרש בספק, וקישור המייל נשאר תקף (תצוגה מקדימה אינה צורכת אסימון)
  - `[false]` `[reject]` `+` בטוקן מפוענח כרווח — הטוקן base64url ללא `+`
  - `[low]` `[reject]` מפתחות `token` כפולים או `?token=` בשאילתה — לא נוצר על ידי השרת
  - `[low]` `[reject]` הודעה שגויה ל-`participant_approved` עם `deliveryStatus=sent` — השרת מסמן `delivered` באותה טרנזקציה, המצב אינו נשמר
  - `[low]` `[reject]` `ask()` לא בודק מחדש את הרשאת הפעולה ושורה ישנה — כפתור מנוטרל, חלון אישור, ו-409 מהשרת מגינים
  - `[low]` `[reject]` ריענון שקט שנכשל אחרי הצלחה, כשל logout, דגל loading בריענון שקט — דפוס מועתק ממסכי ניהול קיימים, ללא נזק
  - `[false]` `[reject]` 404 לפרופיל שהוסר בטופס הציבורי — השרת מחזיר 202 אחיד בכל מקרה
  - `[low]` `[reject]` הגשה כפולה משאירה מנוי יתום — `busy` חוסם הגשה שנייה
  - `[low]` `[reject]` ניקוי שגיאות בסגירת הטופס, 429 בלי רמז בדף ההחלטה, מיפוי מפתחות שגיאה מקוננים — קוסמטי ונדיר
  - `[low]` `[reject]` ניהול focus אחרי החלפת מצב, טקסט פרטיות בטופס, כללי "ללא קישורים" בלקוח — הנוסחים והמדיניות נקבעים ב-UX, ולא בסיפור זה
  - `[low]` `[reject]` התור בלי סינון, דפדוף ושדות נוספים (נראות פרופיל, תאריכים) — קבוע 100 והודעת "מוצגות X מתוך Y"; הרחבות מחוץ להיקף
  - `[low]` `[reject]` בדיקת "אין פרטי קשר" חלשה ב-`/\d{7,}/`, וחסרות בדיקות 429 ופיילוט כשל תצוגה מקדימה — בדיקות משלימות, ללא סיכון
  - `[low]` `[reject]` Turnstile מדומה, והבדיקות בנתב פרטי — מתועד ב-`deferred` והבדיקה האמיתית נוספה ב-`app.spec`

## Design Notes

אימות הכתובת (`/contact/verify`) הוא היחיד שמבצע POST אוטומטי בטעינה, כי המבקר כבר לחץ על הקישור במייל וזו כל מטרתו; הקישור במייל אינו GET שצורך (שרת POST בלבד), ולכן סורקי מייל אינם מאמתים. דף ההחלטה, לעומת זאת, דורש לחיצה על כפתור. ב-`deferred` נרשם ווידג'ט Turnstile אמיתי, כי אין מפתחות.

## Verification

**Commands:**
- `cd client && npx ng test --watch=false` -- expected: כל הבדיקות עוברות
- `cd client && npx ng build` -- expected: בנייה מצליחה בלי אזהרות חדשות

**Manual checks (if no CLI):**
- בדפדפן מול שרת הפיתוח ו-Mailpit: להגיש בקשה, ללחוץ על קישור האימות, להעביר למשתתפת, להחליט, ולאשר מסירה כמנהלת.

## Auto Run Result

Status: done

- **שינוי שמומש (לקוח בלבד):** כפתור וטופס "בקשת פרטי קשר" בעמוד הפרופיל; דפי `/contact/verify` (אימות כתובת, POST אחד בטעינה) ו-`/contact/decide` (תצוגה מקדימה בטעינה, אישור או סירוב רק בלחיצה), עם קריאת הטוקן מהפרגמנט וניקויו מיד; ומסך ניהול `/admin/contact-requests` עם "שליחה למשתתפת", "אישור ושליחה" (בחלון אישור), מצב מסירה וניסיון חוזר ל-`dead`.
- **ממצאי סקירה:** 18 ממצאים מקובצים. תוקנו 3 בדרגת medium ו-2 low: מיפוי 400/409/410 בדף האימות לקישור לא תקף, נוסח נייטרלי למבקרים, בדיקת הנתיבים האמיתיים ב-`app.spec`, בדיקות ל-`takeTokenFromFragment` ושינוי כותרת בדיקה. נדחו 11 ממצאי low ו-2 false, עם נימוק בלוג. נרשם ב-`deferred` ווידג'ט Turnstile אמיתי (אין מפתחות).
- **המלצה לסקירת המשך:** כן. סיכון לא מאומת: זרימת הקצה לקצה בדפדפן מול שרת ו-Mailpit לא נבדקה, ושליחת הבקשה משתמשת בטוקן Turnstile מדומה.
- **אימות:** `ng test --watch=false`: 334 בדיקות ב-26 קבצים עברו. `ng build` הצליח בלי אזהרות.
- **סיכונים נותרים:** ריענון דף ההחלטה אחרי ניקוי הפרגמנט מציג "קישור לא תקף" (יש לפתוח שוב את המייל). הטבלה מוגבלת ל-100 בקשות. סימון נוסח למבקר בלשון נייטרלית נבדק רק בטקסטים של הטופס ושל דף האימות.
