---
title: 'סיפור 5.5: שדות אופציונליים, אווטאר וקורות חיים בטופס'
type: 'feature'
created: '2026-10-22'
status: 'done'
baseline_revision: 'edab32f13db7f53c8a95fc50fa3575ff273cc54d'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-resume_app-2026-10-21/DESIGN.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-resume_app-2026-10-21/EXPERIENCE.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-5-4-טופס-הגשת-פרופיל.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-5-7-גלריה-ועמוד-פרופיל-ציבורי.md'
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** טופס ההגשה (סיפור 5.4) כולל רק שדות חובה. משתתפת לא יכולה להוסיף תיאור, תפקיד, היכרות, בחירת דמות או תמונה משלה, או קורות חיים, ואין תצוגה מקדימה של הכרטיס שלה.

**Approach:** להרחיב את `JoinFormPage` ב-`client/` בסעיף "אופציונלי" (כל שדה רשות): תיאור קצר עם מונה 120, תפקיד, מקום עבודה, טקסט היכרות, שלוש שאלות היכרות, בורר דמות (בת או בן ואז אחת משלוש), העלאת תמונה שמוקטנת בדפדפן, עורך קורות חיים ב-Markdown עם תצוגה מקדימה בטוחה ואזהרת פרטיות, ותצוגה מקדימה חיה של כרטיס הגלריה. הכול מעל ה-API הקיים, בלי שינוי בשרת.

## Boundaries & Constraints

**Always:** Angular standalone, `OnPush`, signals, מחרוזות רק ב-`shared/strings.he.ts`, רכיבי מערכת העיצוב והרכיבים הקיימים (`AvatarImage` ו-`AVATAR_IDS` מ-`shared/ui/avatar`, `SafeMarkdown` מ-`shared/ui/safe-markdown`, `Field`, `GlassCard`, `Button`), RTL, WCAG 2.2 AA, והטופס נשאר פעיל ותקין בלי שום שדה אופציונלי (הגשה עם חמשת שדות החובה בלבד ללא שינוי בהתנהגות מ-5.4). **שדות (שמות לשרת):** `description` (עד 120 תווים; מונה "N/120" חי; `maxlength=120`), `role` (עד 100), `workplace` (עד 100), `introText` (עד 1000, `textarea`), `introAnswer1..3` (עד 1000 כל אחת, `textarea`, עם נוסחי השאלות הקיימים ב-`STRINGS.profile.questions` כתוויות), `cv` (עד 20000, `textarea` Markdown), `avatarId`. כל שדה ריק אחרי trim אינו נשלח. שאלה ללא תשובה אינה חוסמת. הרחבת `Field` או שימוש ב-`textarea` נפרד: אם נדרש רכיב `textarea` משותף, ליצור `shared/ui/textarea/` עם אותו מראה ונגישות כמו `Field` (תווית, "אופציונלי" אינו מסומן ב"חובה", שגיאה, מונה). **בורר דמות (UX-DR9):** שלב א: שני כפתורי רדיו "בת" ו"בן"; שלב ב: שלוש דמויות מאותו מין (`girl-1..3` או `boy-1..3`) כקבוצת רדיו נגישה עם `AvatarImage` בכל אפשרות, תווית נגישה לכל אפשרות ויעדי מגע 44px לפחות; ללא בחירה נשלח `avatarId` ריק (השרת בוחר ברירת מחדל) ובתצוגה המקדימה `default`; בחירה מוסיפה `avatarId` לגוף. **העלאת תמונה:** `input type=file` עם `accept="image/jpeg,image/png,image/webp"`; הקובץ נטען ל-canvas ומוקטן בדפדפן (צלע ארוכה עד 800px, JPEG באיכות יורדת בצעדים עד שהגודל כ-300KB או פחות, לא יותר מ-400KB); קובץ שאינו תמונה נתמכת, קובץ שאי אפשר לפענח, או תמונה שאי אפשר להקטין מתחת ל-400KB מציגים שגיאה ליד השדה ואינם נשלחים; הגדלה קטנה מהמקור אינה מתבצעת; כפתור "הסרת התמונה"; תמונה שהוקטנה מחליפה את האווטאר בתצוגה המקדימה (דרך `URL.createObjectURL` שמשוחרר ב-`ngOnDestroy` ובהחלפה/הסרה; ללא `localStorage`). **שליחה:** בלי תמונה: JSON כמו ב-5.4 עם השדות הנוספים (`ApiClient.post`); עם תמונה: `FormData` דרך `postMultipart` עם אותם שמות שדות כטקסט והקובץ בשם `photo` (קובץ `image/jpeg`), עם `{ turnstile: true }`. שדות הטקסט ב-multipart נשלחים רק כשאינם ריקים. התנהגות 202/404/400/429/5XX כמו ב-5.4; מפתחות `fieldErrors` נוספים ממופים לשדות: `description`, `role`, `workplace`, `introText`, `introAnswer1..3`, `cv`, `avatarId`, `photo` (שגיאה ליד השדה ובסיכום). אימות לקוח לפני שליחה: אורכים בלבד (חלקם נאכפים ב-`maxlength` אך נבדקים גם ב-JS). **קורות חיים:** `textarea` עם תצוגה מקדימה בזמן אמת דרך `SafeMarkdown` (אין `innerHTML` ואין `bypassSecurityTrust*`), והאזהרה המדויקת תמיד גלויה (לא טולטיפ): "קורות החיים יוצגו באתר הציבורי. הסירי מהם פרטים שאינך רוצה לחשוף, כמו מייל או נייד." (מוגדרת כקבוע במשאבים). **תצוגה מקדימה של הכרטיס:** כרטיס בסגנון כרטיס הגלריה (אווטאר או תמונה, שם, שם מוצר, תיאור, תג מחזור מ-`GET /invites`) שמתעדכן מיד מהערכים; ללא אלמנטים אינטראקטיביים בו (אין קישורים או כפתורים; אין לפתוח את קישור המוצר), עם שמות מציינים-מיקום ("השם שלך", "שם המוצר") כשהשדות ריקים; מסומן `aria-label`/כותרת "תצוגה מקדימה של הכרטיס" ואינו מוכרז בכל הקלדה (`aria-hidden` לתוכן הדינמי או `aria-live=off`). אפשר להרחיב את `ProfileCard` ב-`preview` אופציונלי או ליצור רכיב `ProfilePreviewCard` ב-`pages/join/`; אסור לשבור את הגלריה. **מצב:** לאחר הצלחה (`done`) משחררים את ה-object URL ומנקים את כל השדות החדשים (כולל קורות חיים ותמונה); `load()` מאפס אותם כמו שאר הערכים. פריסה: על מסך רחב הכרטיס המקדים לצד הטופס (sticky), על מסך צר מעל כפתור השליחה; הסעיף האופציונלי בכותרת "אופציונלי" ברורה.

**Never:** אין שינוי בשרת, בפרוקסי או בסכמה; אין שדה LinkedIn; אין `innerHTML` או עקיפת הסניטייזר; אין `localStorage`/`sessionStorage`; אין ספריות חיצוניות לתמונה או Markdown; אין שליחת תמונה מעל 400KB; אין חשיפה של מייל או נייד בתצוגה המקדימה; אין שבירת התנהגות 5.4 ללא שדות אופציונליים; אין העלאת קורות חיים כקובץ (עריכה באתר בלבד).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| בלי אופציונליים | רק חמשת שדות החובה | JSON בלי שדות אופציונליים, כמו ב-5.4 | N/A |
| מונה תיאור | הקלדה בשדה התיאור | "N/120" מתעדכן, נעצר ב-120, התצוגה המקדימה מתעדכנת | N/A |
| שאלות היכרות | שאלה אחת מולאה | רק היא נשלחת; אחרות אינן חוסמות | N/A |
| בחירת בת/בן | בחירת "בת" | מוצגות `girl-1..3`; "בן" מחליף ל-`boy-1..3` ומאפס בחירה קודמת של מין אחר | N/A |
| בחירת דמות | לחיצה על דמות | נבחרת (`aria-checked`), נשלחת כ-`avatarId`, והכרטיס המקדים מתעדכן מיד | N/A |
| בלי בחירה | לא נבחרה דמות | `avatarId` לא נשלח; תצוגה `default` | N/A |
| תמונה תקינה | JPEG/PNG/WebP גדולים | מוקטנת עד כ-300KB, נשלחת כ-multipart בשם `photo`, מחליפה את האווטאר בתצוגה | N/A |
| תמונה קטנה | קובץ קטן מהיעד | לא מוגדלת ולא מופחתת איכות מעבר לצורך | N/A |
| קובץ לא נתמך | `text/plain` או קובץ פגום | שגיאה ליד שדה התמונה, אין שליחה של הקובץ | הודעה אחידה |
| תמונה שלא מתכווצת | לא מצליח מתחת ל-400KB | שגיאה ליד השדה | הודעה אחידה |
| הסרת תמונה | לחיצה על "הסרת התמונה" | חוזר לאווטאר ושחרור ה-URL; הבקשה JSON שוב | N/A |
| קורות חיים | Markdown עם כותרת, רשימה, `<script>` | תצוגה בטוחה באלמנטים של Angular; ה-HTML כטקסט | N/A |
| אזהרת קורות חיים | הטופס נטען | האזהרה המדויקת גלויה תמיד | N/A |
| קורות חיים ארוכים | מעל 20000 | שגיאה ליד השדה, אין שליחה, הערכים נשמרים | הודעת אורך |
| שגיאת שרת באופציונלי | 400 עם `errors.photo` או `errors.cv` | השגיאה ליד השדה ובסיכום, הערכים נשמרים | `AppError` |
| 503 עיבוד תמונה | `photo-processing-busy` | הודעת שגיאה כללית בראש הטופס, הערכים והתמונה נשמרים לניסיון חוזר | `AppError` |
| ניקוי אחרי הצלחה | 202 | השדות, התמונה וה-URL נוקו; מסך האישור בלי פרטים | N/A |
| תצוגה מקדימה | הקלדת שם, מוצר, תיאור | הכרטיס מתעדכן מיד, בלי קישורים, בלי מייל או נייד | N/A |
| נגישות | מקלדת | בורר הדמות כקבוצת רדיו עם חיצים, תוויות לכל שדה, יעדי מגע 44px, התצוגה אינה מוכרזת בכל הקלדה | N/A |

</intent-contract>

## Code Map

- `client/src/app/pages/join/join-form.{ts,html,css}` -- הטופס מסיפור 5.4; להרחיב: signals לערכים חדשים, `setValue`/`load`/איפוס, מיפוי `fieldErrors`, בניית הגוף ו-`FormData`, `ngOnDestroy` לשחרור URL.
- `client/src/app/pages/join/join-validation.ts` -- אורכים ל-`description`/`role`/`workplace`/`introText`/`introAnswer*`/`cv`; להוסיף שדות אופציונליים בלי לשבור את `JOIN_FIELDS` של החובה.
- `client/src/app/shared/ui/avatar/avatar.ts` -- `AvatarImage`, `AVATAR_IDS`, `avatarFigure` (שישה איורים + `default`); `photoUrl` יכול להיות object URL מקומי (לבדוק ש-`photoSrc` ב-`shared/url.ts` לא מעוות `blob:`).
- `client/src/app/shared/ui/safe-markdown/` -- `SafeMarkdown` לתצוגה המקדימה; `client/src/app/pages/gallery/profile-card.ts` -- כרטיס הגלריה (מקור הסגנון).
- `client/src/app/shared/ui/field/` -- תווית, שגיאה, `hint`, `maxlength`; ליצור `textarea` תואם אם צריך.
- `client/src/app/shared/api/api-client.ts` -- `post` ו-`postMultipart(path, FormData, { turnstile: true })`.
- `client/src/app/shared/strings.he.ts` -- להוסיף `joinForm.optional*`, שמות השדות, נוסחי הבורר, אזהרת הקורות חיים, הודעות התמונה והתצוגה; נוסחי השאלות כבר ב-`profile.questions`.
- שרת (לקריאה בלבד): `server/Api/Endpoints/ProfileEndpoints.cs` (JSON או multipart, שמות שדות: `description`, `role`, `workplace`, `introText`, `introAnswer1..3`, `avatarId`, `cv`, `photo`; מגבלות 160KB JSON ו-512KB multipart), `server/Domain/Profiles/Profile.cs` (אורכים: תיאור 120, intro 1000, cv 20000). אין לשנות.

## Tasks & Acceptance

**Execution:**
- `client/src/app/shared/strings.he.ts` -- מחרוזות הסעיף, הבורר, התמונה, האזהרה המדויקת והתצוגה -- NFR4
- `client/src/app/shared/ui/textarea/` (אם נדרש) -- שדה רב-שורתי עם תווית, שגיאה ומונה -- UX-DR5
- `client/src/app/pages/join/avatar-picker.ts` ו-`photo-resize.ts` (או מיקום מקביל ב-`pages/join/`) -- בורר דמות והקטנת תמונה בדפדפן כפונקציה נבדקת -- UX-DR9
- `client/src/app/pages/join/join-form.*` -- שילוב השדות, הקורות חיים עם `SafeMarkdown`, תצוגת הכרטיס, בניית JSON/FormData ומיפוי שגיאות -- FR-3, UX-DR12
- `client/src/app/**/*.spec.ts` -- בדיקות לכל שורות המטריצה, כולל הקטנת תמונה (עם canvas מדומה או פונקציה טהורה לחישוב מידות), גוף JSON מול `FormData`, מיפוי שגיאות והעדר `innerHTML` -- NFR3
- `client/README.md` -- שורה על השדות האופציונליים -- תפעול

**Acceptance Criteria:**
- Given הטופס, when מזינים תיאור, then מוצג מונה 120 והשדה מוגבל.
- Given בורר הדמות, when בוחרים בת או בן, then מוצגות שלוש דמויות והתצוגה המקדימה של הכרטיס מתעדכנת מיד.
- Given העלאת תמונה, when נבחר קובץ, then הדפדפן מקטין אותו לכ-300KB לפני השליחה והתמונה מחליפה את האווטאר בתצוגה המקדימה.
- Given עורך קורות החיים, when כותבים Markdown, then מוצגת תצוגה מקדימה בטוחה בלי `innerHTML` והאזהרה המדויקת גלויה.
- Given הגשה בלי שום שדה אופציונלי, when שולחים, then ההתנהגות זהה ל-5.4.

## Spec Change Log

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: 36 findings — high 0, medium 6, low 14, false 1, maybe-false 15
- findings:
  - `[medium]` `patch` ביטול דיאלוג הקבצים משאיר תמונה ישנה — נקראת הסרת התמונה, נוסף מבחן
  - `[medium]` `patch` קובץ בלי `file.type` נדחה לפני פענוח — מפענח מנסה, נוספו מבחנים
  - `[medium]` `patch` קידוד חוזר זהה לתמונות קטנות וחוסר תקרת גודל קלט — דילוג על צעד זהה ותקרת 25MB, נוספו מבחנים
  - `[medium]` `patch` Enter בזמן עיבוד תמונה נבלע בשקט — מוצגת הודעה ומיקוד לסיכום
  - `[medium]` `patch` בחירת קובץ והסרה פעילים בזמן שליחה — מנוטרלים בזמן שליחה
  - `[medium]` `patch` מצב busy ומרוץ `photoSeq` ללא מבחנים — נוספו שני מבחנים
  - `[low]` `reject` `photoSrc` מקבל `blob:` — כתובת blob אינה ניתנת לזיוף מהשרת והיא מקומית בלבד
  - `[low]` `reject` תמונה קודמת נמחקת כשהחלפה נכשלת — החלטה מכוונת כדי שהתצוגה לא תסתור את השגיאה
  - `[low]` `reject` מונה לא מופעל ב-textarea — לא נדרש במפרט
  - `[low]` `reject` תצוגה מקדימה רחוקה בנייד, ARIA כפול, ניסוח נקבה, כפל קוד Textarea/Field — החלטות עיצוב או עקביות קיימת
  - `[low]` `reject` `createObjectURL` שזורק, מפתחות שגיאה מקוננים, 413/415, איפוס חיצוני של הבורר — לא ניתנים להשגה בפועל
  - `[maybe-false]` `reject` שאר הממצאים (פענוח ב-thread ראשי, מונה UTF-16, בדיקות דפדפן אמיתי) — סיכון נמוך, ללא עלות תיקון קטנה

## Design Notes

הקטנת התמונה: `createImageBitmap` או `Image` + `canvas.toBlob('image/jpeg', q)` בלולאה יורדת (q מ-0.85 עד 0.4, ואם עדיין גדול: הקטנת צלע ב-0.8); פונקציית חישוב המידות טהורה כדי שתיבדק בלי DOM. ה-`FormData` נבנה רק כשיש תמונה.

## Verification

**Commands:**
- `npm run build` ב-`client/` -- expected: בנייה מצליחה בלי שגיאות
- `npm test -- --watch=false` ב-`client/` -- expected: כל הבדיקות עוברות

**Manual checks (if no CLI):**
- מול שרת הפיתוח: הגשה עם תמונה, דמות וקורות חיים יוצרת הגשה `pending` עם אותם נתונים (בתור הסקירה או בטבלאות).

## Auto Run Result

Status: done

- **שינוי:** הרחבת טופס ההגשה בסעיף "אופציונלי": תיאור עם מונה 120, תפקיד, מקום עבודה, היכרות ושלוש שאלות, בורר דמות (בת/בן ואז שלוש), העלאת תמונה עם הקטנה בדפדפן לכ-300KB, עורך קורות חיים עם תצוגה בטוחה ואזהרת פרטיות, ותצוגה מקדימה חיה של הכרטיס. שליחה כ-JSON או כ-multipart כשיש תמונה.
- **קבצים:** `pages/join/{join-form,avatar-picker,profile-preview,photo-resize}.*`, `shared/ui/textarea/`, `shared/ui/field/*`, `shared/url.ts`, `strings.he.ts`, בדיקות (`join-optional.spec.ts`, `photo-resize.spec.ts`), `client/README.md`.
- **סקירה:** 6 תיקונים הוחלו, אין דחיות למעקב, השאר נדחו עם נימוק ביומן.
- **המלצת סקירה חוזרת:** false.
- **אימות:** `npm run build` הצליח; `npm test -- --watch=false`: 16 קבצים, 157 בדיקות עברו. לא נבדקו בדפדפן: מסלול הקטנת התמונה האמיתי (canvas), פריסה רספונסיבית, נגישות וקורא מסך, והגשה מול שרת חי.
- **סיכונים:** מסלול `createImageBitmap` וה-canvas נבדק רק דרך מפענח מדומה.
