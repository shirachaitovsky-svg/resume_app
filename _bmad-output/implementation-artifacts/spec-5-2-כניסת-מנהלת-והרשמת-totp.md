---
title: 'סיפור 5.2: כניסת מנהלת והרשמת TOTP'
type: 'feature'
created: '2026-10-22'
status: 'done'
baseline_revision: '5adc736703bbf04fe8926d2951050b045911893b'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-resume_app-2026-10-21/DESIGN.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-resume_app-2026-10-21/EXPERIENCE.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-5-1-תשתית-הלקוח.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-5-4-טופס-הגשת-פרופיל.md'
warnings: []
deferred:
  - summary: >-
      מיירט HTTP גלובלי ל-401 באמצע סשן בכל מסכי הניהול.
    evidence: |-
      כרגע `handleUnauthorized` נקרא ידנית ממסכי ההגדרה בלבד; מסכי 5.3 ו-5.6 חייבים לקרוא לו או להשתמש במיירט.
    location: >-
      client/src/app/pages/admin/admin-session.ts
    severity: medium
---

<intent-contract>

## Intent

**Problem:** למנהלת אין דרך להיכנס מהדפדפן: אין נתיב `/admin`, אין מסך כניסה, החלפת סיסמה חד-פעמית או הרשמת TOTP, ולכן כל מסכי הניהול (סיפורים 5.3 ו-5.6) חסומים.

**Approach:** לבנות ב-`client/` אזור `/admin` עצל עם שירות סשן (signals) ומסכי כניסה, החלפת סיסמה, הרשמת TOTP (קוד QR מצויר בלקוח מה-URI, ועשרת קודי השחזור להעתקה עם אישור שמירה) ויציאה, ועמוד נחיתה ריק של הניהול שסיפורים 5.3 ו-5.6 ימלאו. הכול מעל נקודות הקצה `/auth/*` הקיימות.

## Boundaries & Constraints

**Always:** Angular standalone, `OnPush`, signals, מחרוזות רק ב-`shared/strings.he.ts`, רכיבי מערכת העיצוב (`Field`, `Button`, `GlassCard`, `LoadingSkeleton`, `ErrorState`), RTL, WCAG 2.2 AA. קריאות דרך `ApiClient` בלבד (`/auth/login`, `/auth/logout`, `/auth/change-password`, `/auth/totp/setup`, `/auth/totp/enable`, `/auth/me`). **נתיבים (עצלים):** `/admin/login`, `/admin` (נחיתה), `/admin/setup` (החלפת סיסמה ואחריה TOTP כשלבים באותו מסך או כשני נתיבים). נתיב מוגן שאינו מחובר מנותב ל-`/admin/login`; מי שמחוברת ופותחת את הכניסה מנותבת הלאה. **שירות סשן** (`AdminSession` ב-`pages/admin/` או `shared/`): signals למצב (`unknown`, `anonymous`, `authenticated` עם `username`, `mustChangePassword`, `totpSetupRequired`), טעינה ראשונית מ-`GET /auth/me` (401 הוא מצב מנותק ולא שגיאה), ושמירה בזיכרון בלבד (אין `localStorage`/`sessionStorage`, אין סיסמה או קוד בזיכרון אחרי השליחה). **Guard:** מגן על `/admin` ועל תתי-נתיביו: מנותקת: `/admin/login`; `mustChangePassword`: שלב החלפת סיסמה; `totpSetupRequired`: שלב הרשמת TOTP; רק מי שהשלימה הכול רואה את הנחיתה. **כניסה:** שדות שם משתמש, סיסמה וקוד אימות (6 ספרות, `inputmode=numeric`, `autocomplete=one-time-code`) עם מעבר "השתמשי בקוד שחזור" שמחליף את שדה הקוד בשדה קוד שחזור (נשלח כ-`recoveryCode`); כשאין עדיין TOTP הקוד אינו נדרש (השדה אופציונלי עם הסבר "בכניסה הראשונה אין צורך בקוד"); אין חוסם בלקוח על קוד חסר. כל כשל כניסה (401) מציג הודעה אחידה אחת בלי לחשוף מה שגוי ("שם המשתמש, הסיסמה או הקוד שגויים") ובנעילה אותה הודעה; 429 עם `withRetryHint`; 5XX/רשת הודעה כללית; הכפתור מנוטרל בזמן שליחה; הסיסמה והקוד נמחקים מהשדות אחרי כשל (שם המשתמש נשמר). **אחרי כניסה מוצלחת** מביאים אסימון anti-forgery חדש (האסימון קשור למשתמש) לפני כל בקשת POST נוספת; אחרי יציאה מנקים אותו. תשובת הכניסה (`username`, `mustChangePassword`, `totpSetupRequired`) קובעת את השלב הבא. **החלפת סיסמה** (`currentPassword`, `newPassword`, אישור סיסמה חדשה בלקוח): שגיאות שרת 400 ממופות לשדות `currentPassword` ו-`newPassword` (הודעות השרת בעברית מוצגות כפי שהן מ-`fieldErrors`/title; אם אינן זמינות, נוסחים ממשאבים); סיסמה חדשה ואישור שאינם זהים נחסמים בלקוח; אחרי הצלחה (204) מרעננים את הסשן ועוברים לשלב TOTP אם נדרש. **הרשמת TOTP:** `POST /auth/totp/setup` מחזיר `{ key, uri }`; מוצגים קוד QR של ה-`uri` מצויר בלקוח כ-SVG באלמנטים של Angular (בלי `innerHTML` ובלי `bypassSecurityTrust*`; מותר תלות אחת קטנה ללא תלויות משלה, `qrcode-generator` בגרסה מקובעת, ליצירת מטריצת ה-QR בלבד), ולצידו המפתח `key` כטקסט להעתקה ידנית (לקבוצות של 4 תווים) כחלופה; שדה לקוד אימות בן 6 ספרות ושליחה ל-`/auth/totp/enable`; קוד שגוי (400 על `code`) מוצג ליד השדה. **קודי שחזור:** אחרי הצלחה מוצגים עשרת הקודים (`recoveryCodes`) פעם אחת בלבד, עם כפתור "העתקת הקודים" (`navigator.clipboard`, עם חלופה של בחירת הטקסט כשאין הרשאה) וכפתור הורדה כקובץ טקסט (אופציונלי); תיבת סימון "שמרתי את הקודים במקום בטוח" חובה, וכפתור "המשך" מנוטרל עד שמסומנת; הקודים נמחקים מהזיכרון מיד אחרי ההמשך ואינם נשמרים בשום אחסון; אי אפשר להגיע לנחיתה לפני האישור. **יציאה:** כפתור "יציאה" בנחיתה (ובשלבי ההרשמה) שולח `/auth/logout`, מנקה סשן ואסימון ומעביר ל-`/admin/login`. **נחיתת הניהול:** כותרת "ניהול", שם המשתמשת, כפתור יציאה, ופיסת תוכן ריקה עם הערה שהמסכים יתווספו (5.3, 5.6 יוסיפו את הניווט); הנתיבים `/admin/*` אינם מופיעים בניווט הציבורי הצף. **שגיאות כלליות:** 401 באמצע סשן (פג תוקף) מחזיר ל-`/admin/login` עם הודעה "פג תוקף ההתחברות". כותרות הדפים מתעדכנות.

**Never:** אין שינוי בשרת, בפרוקסי או בסכמה; אין ניהול משתמשים, איפוס סיסמה או איפוס TOTP; אין שמירת סיסמה, קוד, מפתח TOTP או קודי שחזור באחסון דפדפן, ביומן או בכתובת; אין חשיפה של האם שם המשתמש קיים; אין `innerHTML`; אין ספרייה נוספת מלבד זו שצוינה; אין מסכי מחזורים או תור סקירה (5.3, 5.6).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| גישה בלי סשן | פתיחת `/admin` ללא כניסה | הפניה ל-`/admin/login` | N/A |
| כניסה תקינה מלאה | שם, סיסמה, קוד נכונים, TOTP פעיל | הסשן נקבע, אסימון חדש, מעבר לנחיתה | N/A |
| כניסה כושלת | 401 מכל סיבה | הודעה אחידה, הסיסמה והקוד נמחקים, השם נשמר | אחיד לנעילה ולכל סיבה |
| קוד שחזור | מעבר לקוד שחזור | נשלח `recoveryCode`; הצלחה מעבירה הלאה | כמו כשל |
| סיסמה חד-פעמית | `mustChangePassword: true` | מסך החלפת סיסמה לפני כל דבר אחר | N/A |
| סיסמה חלשה | 400 על `newPassword` | הודעה ליד השדה | הערכים נשמרים |
| אישור לא תואם | סיסמה חדשה ואישור שונים | שגיאה בלקוח, אין בקשה | N/A |
| הרשמת TOTP | `totpSetupRequired: true` | QR, מפתח טקסטואלי ושדה קוד | N/A |
| קוד הרשמה שגוי | 400 על `code` | שגיאה ליד השדה | הקוד נמחק |
| הרשמה מוצלחת | 200 עם 10 קודים | הקודים מוצגים פעם אחת עם העתקה; "המשך" מנוטרל עד אישור שמירה | N/A |
| סיום הרשמה | סימון ולחיצה על "המשך" | הקודים נמחקים מהזיכרון, מעבר לנחיתה | N/A |
| רענון בזמן הקודים | רענון הדף | הקודים אינם מוצגים שוב; חוזרים לנחיתה (TOTP כבר פעיל) | N/A |
| 429 | חריגת קצב | הודעה עם זמן המתנה | `AppError` |
| 5XX או רשת | כשל | הודעה כללית, שם המשתמש נשמר | `AppError` |
| פג תוקף | 401 בקריאה מוגנת | חזרה ל-`/admin/login` עם הודעה | N/A |
| יציאה | לחיצה על "יציאה" | `POST /auth/logout`, סשן ואסימון נוקו, `/admin/login` | כשל יציאה: עדיין מנקים מקומית |
| שליחה כפולה | שתי לחיצות | בקשה אחת | N/A |
| פרטיות | כל מסך | אין סיסמה, קוד או מפתח ב-URL, באחסון או ביומן | N/A |
| נגישות | מקלדת וקורא מסך | תוויות, שגיאות מוכרזות, מיקוד לשגיאה או לכותרת השלב | N/A |

</intent-contract>

## Code Map

- `client/src/app/app.routes.ts` -- להוסיף `admin` עצל עם ילדים (`login`, `setup`, נחיתה) ו-guard; נתיב `**` נשאר אחרון.
- `client/src/app/shared/api/api-client.ts`, `csrf.service.ts` -- `post` עם CSRF; `CsrfService.refresh()` לאסימון חדש אחרי כניסה ויציאה. לבדוק אם `CSRF_URL` (`/api/profiles/csrf`) מחזיר אסימון תקף למשתמשת מחוברת או שיש להשתמש ב-`/auth/csrf` לאזור הניהול (שרת: `AuthEndpoints.cs` מחזיר `{ token, headerName }`; האסימון קשור למשתמש); לא לשבור את קריאות הציבור.
- `client/src/app/shared/api/app-error.ts` -- `AppError.kind`/`status`/`fieldErrors`, `withRetryHint`; 401 בכניסה נבדל ממצב מנותק.
- `client/src/app/shared/ui/{field,button,glass-card,loading-skeleton,error-state}`, `pages/join/join-form.*` -- דפוס טופס, סיכום שגיאות ומיקוד שכבר קיים ב-5.4 (להשתמש באותו דפוס).
- `client/src/app/shared/strings.he.ts` -- קבוצת `admin` חדשה (כניסה, החלפת סיסמה, TOTP, קודי שחזור, הודעות).
- `client/package.json` -- תלות `qrcode-generator` מקובעת (בלי `^`); בלי תלות נוספת.
- שרת (לקריאה בלבד): `server/Api/Endpoints/AuthEndpoints.cs` (תשובות ושגיאות), `README.md` סעיפי "אימות דו-שלבי" ו"כניסת מנהלת" (חסימת 403 `totp-setup-required` עד הרשמה; דרישת `Origin` תואם וכותרת `X-CSRF-TOKEN`; ייתכן שבפיתוח דרך ה-proxy נדרש `ALLOWED_ORIGINS`; לתעד ב-README של הלקוח ולא לשנות שרת).

## Tasks & Acceptance

**Execution:**
- `client/src/app/shared/strings.he.ts` -- מחרוזות האזור -- NFR4
- `client/src/app/pages/admin/admin-session.ts` ו-`admin.guard.ts` -- שירות סשן ו-guard -- FR-17
- `client/src/app/pages/admin/login/`, `setup/` (החלפת סיסמה, TOTP, קודי שחזור), `home/` -- המסכים -- FR-17, UX-DR5
- `client/src/app/pages/admin/qr-code.ts` -- רכיב SVG שמצייר מטריצת QR מה-`uri` בלי `innerHTML` -- UX-DR14
- `client/src/app/app.routes.ts` -- נתיבי `admin` עצלים -- UX-DR14
- `client/src/app/**/*.spec.ts` -- בדיקות לכל שורות המטריצה (כולל מדיניות אחידות ההודעה, רענון אסימון אחרי כניסה, אי-שמירה באחסון, השער של "שמרתי את הקודים", ניקוי הקודים, ניתוב ה-guard לכל מצב, יציאה) -- NFR3
- `client/README.md` -- איך נכנסים בפיתוח (משתני `ADMIN_*`, `Origin`, הרשמת TOTP) -- תפעול

**Acceptance Criteria:**
- Given מסך הכניסה, when מוזנים שם משתמש, סיסמה וקוד, then המנהלת נכנסת, וכל כשל מציג הודעה אחידה בלי לחשוף מה שגוי.
- Given סיסמה חד-פעמית, when המנהלת נכנסת, then מוצג מסך החלפת סיסמה, ואחריו הרשמת TOTP עם קוד QR מצויר בלקוח מה-URI ועשרת קודי השחזור להעתקה, ואי אפשר להמשיך עד שמאשרים ששמרה אותם.
- Given יציאה, when לוחצים "יציאה", then הסשן מסתיים והמשתמשת מועברת לדף הכניסה.

## Spec Change Log

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: 41 findings — high 0, medium 9, low 14, false 2, maybe-false 16
- findings:
  - `[medium]` `patch` מרוץ אסימון CSRF בין scope ציבורי למנהלת — האסימון נשמר רק אם ה-scope לא השתנה, נוסף מבחן
  - `[medium]` `patch` כשל לא-401 ב-`/auth/me` הוציא את המנהלת בשקט — רק 401 מנתק, אחרת נשמר המצב עם ניסיון חוזר
  - `[medium]` `patch` כשל ברענון אסימון אחרי כניסה מוצלחת הכשיל את הכניסה — נבלע, האסימון נטען בבקשה הבאה
  - `[medium]` `patch` קודי השחזור אבדו כשהרכיב נטען מחדש (409 הוביל לנחיתה) — הקודים וסימון ההמתנה נשמרים בסשן עד אישור, אזהרת `beforeunload` ויציאה מנוטרלת עד אישור
  - `[medium]` `patch` יציאה בזמן בקשת הגדרה מחזירה מפתח ו-QR אחרי ניקוי — ביטול מנוי, דגל destroyed ותשובה בלי קודים נחשבת שגיאה
  - `[low]` `patch` כניסה עם שדות ריקים שולחת בקשה — נחסם בלקוח
  - `[low]` `patch` `$&` בשם משתמש משבש את הברכה — פונקציית החלפה
  - `[medium]` `patch` חסרים מבחנים: 409, הורדת קודים, שחזור clipboard
  - `[low]` `patch` מחרוזות לא בשימוש — נוקו או שולבו
  - `[low]` `defer` מיירט 401 גלובלי לכל מסכי הניהול — יידרש ב-5.3 ו-5.6
  - `[low]` `reject` תמיכת noindex, הצגת כללי סיסמה, `aria-label` על פסקה, ניסוח נקבה, כפל CSS — לא נדרשים, קוסמטי או עקבי עם האתר
  - `[false]` `reject` הודעת נעילה שונה מ-401 חושפת קיום משתמש — השרת מחזיר 401 אחיד לנעילה
  - `[false]` `reject` נקודת קצה `/auth/csrf` לא קיימת — היא קיימת ב-`AuthEndpoints.cs`
  - `[maybe-false]` `reject` הערות על בדיקות דפדפן אמיתי, פענוח QR, כשל יציאה — מחוץ להיקף בדיקות היחידה, מצוין כסיכון

## Design Notes

מסך ההרשמה הוא מכונת מצבים קטנה (`password` ← `totp` ← `recovery` ← `done`) הנגזרת מ-`mustChangePassword`/`totpSetupRequired`; אחרי רענון באמצע `recovery` אי אפשר לשחזר את הקודים (השרת מחזיר אותם פעם אחת) ולכן הרענון מביא לנחיתה (TOTP כבר פעיל). QR: `qrcode-generator` נותן `isDark(row, col)` ו-`getModuleCount()`; מציירים `<path>` אחד או `<rect>` לכל מודול עם `viewBox`, שוליים שקטים של 4 מודולים, וכותרת נגישה (`role="img"` ו-`aria-label`).

## Verification

**Commands:**
- `npm run build` ב-`client/` -- expected: בנייה מצליחה בלי שגיאות
- `npm test -- --watch=false` ב-`client/` -- expected: כל הבדיקות עוברות

**Manual checks (if no CLI):**
- מול שרת הפיתוח עם `ADMIN_USERNAME` ו-`ADMIN_INITIAL_PASSWORD`: כניסה ראשונה, החלפת סיסמה, סריקת ה-QR באפליקציית authenticator, הפעלה, קודי שחזור, יציאה וכניסה מחדש עם קוד.

## Auto Run Result

Status: done

- **שינוי:** אזור `/admin` עצל: כניסה (עם קוד TOTP או קוד שחזור, הודעת כשל אחידה), החלפת סיסמה חד-פעמית, הרשמת TOTP עם קוד QR מצויר בלקוח (`qrcode-generator` 2.0.4) ומפתח טקסטואלי, עשרת קודי שחזור עם העתקה והורדה ואישור שמירה, יציאה ונחיתת ניהול ריקה. שירות סשן בזיכרון בלבד ו-guard לכל שלב.
- **שינוי משלים:** `CsrfService.setScope` מעביר את אסימון ה-anti-forgery ל-`/auth/csrf` אחרי כניסה ובחזרה לציבורי אחרי יציאה; קריאות הציבור ללא שינוי.
- **סקירה:** 9 תיקונים הוחלו, 1 נדחה למעקב (מיירט 401 גלובלי), השאר נדחו עם נימוק ביומן.
- **המלצת סקירה חוזרת:** false.
- **אימות:** `npm run build` הצליח; `npm test -- --watch=false`: 18 קבצים, 200 בדיקות עברו. לא נבדק זרם הכניסה האמיתי (סריקת QR באפליקציית authenticator, קודי שחזור, כניסה חוזרת) מול שרת חי.
- **סיכונים:** בדיקת `Origin` והעוגיה דרך ה-proxy של `ng serve` וה-rewrite של Vercel לא נבדקו; ייתכן שיידרש `ALLOWED_ORIGINS` (מתועד ב-`client/README.md`).
