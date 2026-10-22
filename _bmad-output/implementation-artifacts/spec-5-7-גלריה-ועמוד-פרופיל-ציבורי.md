---
title: 'סיפור 5.7: גלריה ועמוד פרופיל ציבורי'
type: 'feature'
created: '2026-10-22'
status: 'ready-for-dev'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-resume_app-2026-10-21/DESIGN.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-resume_app-2026-10-21/EXPERIENCE.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-5-1-תשתית-הלקוח.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-1-12-גלריה-ציבורית.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-1-13-עמוד-פרופיל-ציבורי.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** האתר מציג כרגע רק "האתר בהקמה". מבקר לא יכול לראות את המשתתפות והפרויקטים, לסנן לפי מחזור או לפתוח פרופיל מלא, ואין נתוני דוגמה שמאפשרים לראות את זה בפיתוח.

**Approach:** לבנות ב-`client/` את דף הבית כגלריה ציבורית (כרטיסים, צ'יפי מחזור, שלדי טעינה, מצבים), עמוד פרופיל ציבורי מלא עם רנדרר Markdown בטוח וששת איורי האווטאר, ולהוסיף סקריפט זריעת נתוני דוגמה למסד הפיתוח כדי שהגלריה לא תהיה ריקה. הכול מעל ה-API הקיים (`GET /profiles`, `GET /cohorts`, `GET /profiles/{id}`, `GET /profiles/{id}/photo`).

## Boundaries & Constraints

**Always:** הכול ב-`client/src/app/` (חוץ מסקריפט הזריעה). Angular standalone, `OnPush`, signals, מחרוזות רק ב-`shared/strings.he.ts`, טוקנים ורכיבי מערכת העיצוב הקיימים (כרטיס זכוכית, כפתור, צ'יפ, מצבי `LoadingSkeleton`, `EmptyState`, `ErrorState`), נגישות WCAG 2.2 AA ו-RTL. קריאות דרך `ApiClient` (סיפור 5.1). **עמוד הבית (`/`) הוא הגלריה** ומחליף את העמוד הזמני: כותרת ראשית גדולה (`display-xl`) "משתתפות ופרויקטים" ושורת תת-כותרת, צ'יפי מחזור (UX-DR6): "כל המחזורים" (ברירת מחדל, נבחר בכחול) ואחד לכל מחזור מ-`GET /cohorts`; הבחירה נשמרת בפרמטר הכתובת `?cohort={id}` ונקראת ממנו בטעינה (מזהה מחזור שאינו קיים או שגוי מוצג כרשימה ריקה או כ"כל המחזורים", ללא קריסה); רשת כרטיסים 3 עמודות מ-1024px, 2 מ-640 עד 1023, 1 מתחת (UX-DR10, UX-DR17). **כרטיס:** אווטאר או תמונה בטבעת כחולה (`accent-mid`), שם, שם המוצר, תיאור קצר (אם קיים), ותג מחזור (`accent-soft`); לחיצה על הכרטיס פותחת את קישור המוצר (`productUrl`) בלשונית חדשה עם `rel="noopener noreferrer"`; כפתור "לפרופיל" (משני) מנווט ל-`/profile/:id`; ריחוף מחזק את הטבעת; כל הכרטיס נגיש במקלדת (קישור המוצר ו"לפרופיל" שני יעדי מיקוד נפרדים עם שמות נגישים, ללא קינון אינטראקטיבי בתוך אינטראקטיבי). מצבים: טעינה (`LoadingSkeleton` של כרטיסים), ריק עם הטקסט "עדיין אין פרופילים במחזור הזה." (`EmptyState`), שגיאה עם ניסיון חוזר (`ErrorState`), 429 עם הודעה מתאימה. סינון משנה את הרשימה ללא טעינת עמוד ואינו מציג בזמן הטעינה את תוצאות הבחירה הקודמת כאילו שייכות לחדשה. **עמוד פרופיל (`/profile/:id`):** `GET /api/profiles/{id}`; כרטיס זכוכית עם רקע עליון בגרדיאנט כחול עדין (`accent-soft` → `accent-haze` → `accent-haze-strong`, אזור כחול גדול אחד בלבד במסך), אווטאר או תמונה בטבעת, שם (`headline-lg`), שם המוצר כתת-כותרת, תפקיד ומקום עבודה אם קיימים, התיאור הקצר, תג המחזור, כפתור שחור "לפתיחת המוצר: {שם המוצר}" שנפתח בלשונית חדשה עם `rel="noopener noreferrer"`, שאלות ההיכרות שנענו בלבד ממוספרות עם הנוסחים הקבועים (1: "מה הרקע המקצועי שלך?", 2: "איזו בעיה המוצר שבנית פותר, ולמי?", 3: "מה למדת בקורס שאת כבר מיישמת?"; נוסחים במשאבים), טקסט ההיכרות אם קיים, וקורות החיים. כל מחרוזות הנוסחים והכותרות ("היכרות", "קורות חיים", "שאלות") במשאבים. 404 מהשרת (או מזהה פגום): "הפרופיל אינו זמין" עם קישור חזרה לגלריה, בלי לחשוף אם הפרופיל קיים; שגיאות אחרות: `ErrorState` עם ניסיון חוזר. אין בעמוד כפתור "בקשת פרטי קשר" (סיפור 5.9) ואין מייל או נייד. כותרת הדף מתעדכנת לשם המשתתפת. **תמונות:** `photoUrl` יחסי (`/profiles/{id}/photo`) נטען דרך `/api` בלבד; בטעינה כושלת של תמונה חוזרים לאווטאר; לתמונות `alt` עם שם המשתתפת. **`AvatarImage`** (`shared/ui/avatar/`): קומפוננטה עם קלטים `avatarId`, `photoUrl`, `name`, `size`; מציגה את התמונה אם יש, אחרת איור SVG לפי `avatarId` מתוך שש דמויות (`girl-1`, `girl-2`, `girl-3`, `boy-1`, `boy-2`, `boy-3`) ודמות ברירת מחדל ל-`default` או לערך לא מוכר; איורים שטוחים ועדינים בגווני עור ושיער מגוונים (בהיר, בינוני, זית, כהה; שיער ארוך, קצר, מסולסל) כשרקע הדמות וצבע הבגדים רק מגווני הכחול של המערכת (הגוונים הטבעיים של עור ושיער הם היוצא מן הכלל היחיד מכלל הטוקנים, ומוגדרים כקבועים באיורים), `role="img"` ו-`aria-label`/`alt`. **`SafeMarkdown`** (`shared/ui/safe-markdown/`): קומפוננטה שמקבלת מחרוזת Markdown מוגבלת ומרנדרת אותה בלי `innerHTML` גולמי ובלי `DomSanitizer.bypassSecurityTrust*`: מנתח פנימי (ללא ספרייה חיצונית) שבונה עץ צמתים (פסקאות, כותרות `#` עד `###`, רשימות ממוספרות ולא ממוספרות, הדגשה `**`, נטוי `*`, שבירת שורה, וקישורים `[טקסט](כתובת)`), והתבנית מציגה אותו באלמנטים של Angular; קישור שאינו `http` או `https` מוחלט מוצג כטקסט רגיל ללא קישור; קישורים תקינים נפתחים בלשונית חדשה עם `rel="noopener noreferrer nofollow"`; כל תו `<` או `>` או HTML בקלט מוצג כטקסט בלבד; קלט ריק מציג דבר. בדיקות XSS: `<script>`, `<img onerror>`, `javascript:`, `data:`, כתובות יחסיות. **סקריפט זריעה** (`scripts/dev-seed.ps1` ו-`scripts/dev-seed.sql` בשורש הריפו): מכניס למסד הפיתוח (קונטיינר `resume-dev-pg`, מסד `resume`, באמצעות `docker exec ... psql`) שני מחזורים (למשל "מחזור א׳" ו"מחזור ב׳") ולפחות שמונה פרופילים `Active` עם גרסה `Approved` ו-`ApprovedRevisionId` תקין, עם שמות עבריים, שמות מוצרים וקישורים לדוגמה (`https://example.com/...`), תיאורים קצרים (חלקם ארוכים עד 120), תפקיד ומקום עבודה, תשובות היכרות (חלק מהשאלות בלבד), קורות חיים ב-Markdown נקי, וששת האווטארים לפחות פעם אחת, פרופיל אחד עם `default`; בנוסף פרופיל אחד `Removed` ופרופיל אחד עם גרסה `Pending` בלבד (כדי להוכיח שאינם מוצגים) ושורת `participant_private` תואמת לכל פרופיל (כתובות `@example.test`). הסקריפט אידמפוטנטי (מזהי GUID v4 קבועים, `ON CONFLICT DO NOTHING` או מחיקה ויצירה מחדש של נתוני הזריעה בלבד לפי המזהים), מדפיס סיכום, ואינו נוגע בנתונים אחרים. שמות העמודות והטבלאות נלקחים מהמיגרציות הקיימות (`server/Infrastructure/Migrations/`). לאחר המימוש יש להריץ אותו מול `resume-dev-pg` ולוודא ש-`GET /api/profiles` מחזיר את הפרופילים. בדיקות `vitest` לכל ההתנהגויות, כולל סינון ופרמטר הכתובת, כל המצבים, 404, תקינות הקישורים והתכונות `rel`/`target`, נרמול מקרי קצה, `AvatarImage` ו-`SafeMarkdown` (כולל בדיקות XSS). `client/README.md` ו-`README.md`: איך מזריעים ומריצים.

**Never:** אין שינוי בשרת, בפרוקסי או בסכמת בסיס הנתונים; אין טפסים, כניסה או ניהול; אין כפתור "בקשת פרטי קשר"; אין `innerHTML` גולמי או עקיפת הסניטייזר של Angular; אין שמירת נתונים ב-`localStorage`; אין ספריות UI או Markdown חיצוניות; אין מייל, נייד או `InviteToken` בשום מקום בלקוח; אין שינוי של נתונים שאינם נתוני הזריעה.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| גלריה עם פרופילים | `GET /profiles` מחזיר כרטיסים | רשת כרטיסים עם אווטאר או תמונה, שם, מוצר, תיאור ותג מחזור | N/A |
| טעינה | בקשה בדרך | `LoadingSkeleton` עם `aria-busy` | N/A |
| גלריה ריקה | `items: []` | "עדיין אין פרופילים במחזור הזה." | N/A |
| שגיאה | 5XX או רשת | `ErrorState` עם "ניסיון חוזר" שטוען מחדש | `AppError` |
| 429 | חריגת קצב | הודעה מתאימה ו-`Retry-After` | N/A |
| סינון | לחיצה על צ'יפ מחזור | הרשימה מסוננת, `?cohort=` בכתובת, הצ'יפ נבחר | N/A |
| כתובת עם מחזור | `/?cohort={id}` | נטען מסונן והצ'יפ הנכון נבחר | מזהה שגוי: רשימה ריקה או כל המחזורים, בלי קריסה |
| כל המחזורים | לחיצה על "כל המחזורים" | הפרמטר מוסר והרשימה המלאה | N/A |
| כרטיס | לחיצה על הכרטיס | קישור המוצר בלשונית חדשה עם `rel="noopener noreferrer"` | N/A |
| לפרופיל | לחיצה על "לפרופיל" | ניווט ל-`/profile/:id` | N/A |
| תמונה | `photoUrl` קיים | נטענת דרך `/api`; בכשל חוזרים לאווטאר | N/A |
| אווטאר | כל אחד מששת המזהים ו-`default` ומזהה לא מוכר | איור מתאים או ברירת מחדל, עם `role="img"` ותווית | N/A |
| פרופיל מלא | `GET /profiles/{id}` 200 | כל השדות, שאלות שנענו בלבד ממוספרות, קורות חיים מרונדרים בטוח, כותרת הדף לפי השם | N/A |
| שדות חסרים | בלי תפקיד, תיאור, היכרות או קורות חיים | הסעיפים אינם מוצגים | N/A |
| פרופיל לא זמין | 404 או מזהה פגום | "הפרופיל אינו זמין" וחזרה לגלריה, בלי חשיפה | N/A |
| שגיאת פרופיל | 5XX או רשת | `ErrorState` עם ניסיון חוזר | `AppError` |
| קורות חיים | Markdown תקין עם כותרות, רשימות, הדגשה וקישור `https` | רינדור באלמנטים של Angular | N/A |
| XSS | `<script>`, `<img onerror=...>`, `javascript:`, `data:`, קישור יחסי | מוצג כטקסט או ללא קישור; אין צמתים מסוכנים בעץ | N/A |
| קישור מוצר | `rel` ו-`target` | `target="_blank"` ו-`rel` כולל `noopener` | N/A |
| פרטיות | כל המסכים | אין מייל, נייד או `InviteToken` | N/A |
| נגישות | ניווט במקלדת | טבעת מיקוד, יעדי מגע 44px, כותרות היררכיות, `alt` לתמונות | N/A |
| זריעה | הרצת הסקריפט פעמיים | אותם נתונים ללא כפילות; הפרופיל שהוסר והממתין לא מוצגים | N/A |

</intent-contract>

## Code Map

- `client/src/app/pages/home/` -- עמוד הבית הזמני מסיפור 5.1; להחליף בגלריה (או ליצור `gallery/` ולהפנות אליו את `''`). `client/src/app/app.routes.ts` -- ניתוב עצל; להוסיף `profile/:id`.
- `client/src/app/shared/api/` -- `ApiClient`, `AppError` ושירותי CSRF/Turnstile מסיפור 5.1; הקריאות לגלריה הן `get`.
- `client/src/app/shared/ui/{glass-card,button,chip,loading-skeleton,empty-state,error-state,info-page}` -- רכיבי מערכת העיצוב והמצבים; `chip` תומך במצב נבחר בכחול.
- `client/src/app/shared/strings.he.ts` -- קובץ המחרוזות היחיד; להוסיף מפתחות לגלריה, לפרופיל, לשאלות ולמצבים.
- `client/src/styles.css` -- טוקנים (גרדיאנט הכחול, `accent-*`), טיפוגרפיה (`display-xl`, `headline-lg`, ...); לא להוסיף צבעי hex מחוץ לאיורי האווטאר.
- `client/src/app/design-system/` -- להוסיף הדגמה של `AvatarImage` ו-`SafeMarkdown`.
- API לקריאה בלבד: `GET /profiles?cohort&limit&offset` מחזיר `{ items, total }` עם כרטיסים (`id`, `name`, `productName`, `productUrl`, `description`, `avatarId`, `photoUrl`, `cohort: { id, name }`); `GET /cohorts` מחזיר `{ items: [{ id, name }], total }`; `GET /profiles/{id}` מחזיר את אלה בתוספת `role`, `workplace`, `introText`, `introAnswers: [{ number, text }]`, `cv` (או `null`); `GET /profiles/{id}/photo`. ראו `README.md` בשורש.
- `server/Infrastructure/Migrations/` ו-`server/Domain/` -- לקריאה בלבד לצורך שמות הטבלאות והעמודות בסקריפט הזריעה.
- `scripts/` -- ספרייה חדשה לסקריפט הזריעה. אין לשנות: `server/` (קוד), `proxy.conf.js`.

## Tasks & Acceptance

**Execution:**
- `client/src/app/shared/ui/avatar/` -- `AvatarImage` עם שישה איורים, ברירת מחדל, תמונה וחזרה לאווטאר בכשל -- UX-DR9
- `client/src/app/shared/ui/safe-markdown/` -- מנתח Markdown מוגבל ורנדרר בטוח בלי `innerHTML` -- AR13
- `client/src/app/pages/gallery/` -- גלריה עם צ'יפי מחזור, פרמטר כתובת, כרטיסים, ומצבי טעינה, ריק ושגיאה -- FR-8, FR-9, FR-10, UX-DR6, UX-DR10, UX-DR14, UX-DR17
- `client/src/app/pages/profile/` -- עמוד פרופיל ציבורי מלא ו"הפרופיל אינו זמין" -- FR-8, FR-9, UX-DR11, UX-DR14
- `client/src/app/app.routes.ts` -- `''` לגלריה ו-`profile/:id` עצל -- UX-DR14
- `scripts/dev-seed.ps1`, `scripts/dev-seed.sql` -- זריעת נתוני דוגמה אידמפוטנטית והרצתה מול מסד הפיתוח -- תפעול
- `client/src/app/**/*.spec.ts` -- בדיקות לשורות המטריצה, כולל XSS -- NFR3
- `client/README.md`, `README.md` -- הרצה וזריעה -- תפעול

**Acceptance Criteria:**
- Given הדף הראשי, when הוא נטען, then מוצגת גלריה של כרטיסים (3/2/1 עמודות) עם אווטאר או תמונה, שם, מוצר, תיאור קצר ותג מחזור, שלדי טעינה ומצב ריק "עדיין אין פרופילים במחזור הזה."; צ'יפי מחזור מסננים והבחירה נשמרת בכתובת.
- Given כרטיס, when לוחצים עליו, then המוצר נפתח בלשונית חדשה, וכפתור "לפרופיל" פותח את עמוד הפרופיל, שמציג תמונה או אווטאר עם טבעת, שם, תיאור, מחזור, "לפתיחת המוצר: {שם}", שאלות שנענו ממוספרות וקורות חיים מרונדרים בטוח, ו"הפרופיל אינו זמין" כשאין פרופיל.
- Given נתוני הזריעה, when מריצים את הסקריפט מול מסד הפיתוח, then הגלריה בדפדפן מציגה לפחות שמונה פרופילים בשני מחזורים, ופרופילים שהוסרו או ממתינים אינם מוצגים.

## Spec Change Log

## Review Triage Log

## Verification

**Commands:**
- `npm run build` ב-`client/` -- expected: בנייה מצליחה בלי שגיאות
- `npm test -- --watch=false` ב-`client/` -- expected: כל הבדיקות עוברות
- `powershell -File scripts/dev-seed.ps1` -- expected: סיכום של הנתונים שנזרעו; `curl http://localhost:4200/api/profiles` -- expected: 200 עם הפרופילים
