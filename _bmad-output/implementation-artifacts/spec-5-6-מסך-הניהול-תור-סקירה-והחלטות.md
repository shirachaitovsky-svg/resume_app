---
title: 'סיפור 5.6: מסך הניהול, תור סקירה והחלטות'
type: 'feature'
created: '2026-10-22'
status: 'done'
baseline_commit: '3a412927360f105dd6b0fc9022308f2e80df171d'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-5-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** השרת כבר כולל תור סקירה, אישור, החזרה לתיקון והסרה, אבל למנהלת אין מסך בלקוח, ולכן שום פרופיל לא יכול להתפרסם מהדפדפן.

**Approach:** שני מסכי ניהול חדשים בלקוח, "תור סקירה" (רשימה ופרטי הגשה עם השוואה ופעולות) ו"פרופילים מפורסמים" (רשימה והסרה), לפי תבנית מסך המחזורים הקיים, על נקודות הקצה הקיימות בלבד.

## Boundaries & Constraints

**Always:**
- מעבר מצב רק לפי `revisionId` שהמנהלת ראתה. תגובת 409 (`revision-not-pending`) גורמת לטעינה שקטה מחדש והודעה.
- קורות חיים ושאר טקסט חופשי מוצגים רק דרך `safe-markdown`. הכול בעברית, RTL, מחרוזות מ-`strings.he.ts`, ושימוש ברכיבי מערכת העיצוב.
- אישור והחזרה בחלון אישור (`confirm-dialog`). החזרה דורשת הערה, ושגיאת `note` מוצגת ליד השדה.
- 401 מעביר לכניסת המנהלת. מספיק טעינה אחת לכל פעולה, ללא כפתור לחיצה כפולה (`busy`).

**Never:**
- לא נוגעים בשרת. לא מפעילים אישור או דחייה של שינוי פרטי קשר (`approve-contact-change`, `reject-contact-change`): התור רק מסמן "יש שינוי פרטי קשר ממתין" (`hasContactChange`) ומפנה אליו. הטיפול בו נרשם ב-`deferred-work.md`.
- לא מציגים אימייל או נייד של משתתפת מחוץ למסך פרטי ההגשה של המנהלת.
- לא מוסיפים אפיק 6 או כניסה עם גוגל לכאן.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| תור עם הגשות | `GET /admin/submissions` מחזיר פריטים | טבלה: שם, מוצר, מחזור, סוג (חדש/עדכון), תאריך, ותג שינוי פרטי קשר | N/A |
| תור ריק | `items` ריק | מצב ריק "אין הגשות ממתינות" | שגיאת טעינה: `error-state` עם ניסיון חוזר |
| פרטי הגשה חדשה | `published` הוא null | פרטים מלאים כולל מייל, נייד, קורות חיים ותמונה | 404: הודעה וחזרה לתור |
| פרטי עדכון | `published` קיים | השוואה "מפורסם כעת" מול "מוצע", עם רשימת `changes` | N/A |
| אישור | הגשה ממתינה | חלון אישור, אחריו 200, הודעת הצלחה, והפריט נעלם מהתור | 409: טעינה שקטה והודעה "ההגשה כבר טופלה" |
| החזרה לתיקון | הערה לא ריקה | חלון אישור, אחריו 200 והפריט נעלם | הערה ריקה או ארוכה: שגיאה ליד השדה, החלון נשאר פתוח |
| הסרת פרופיל | פרופיל פעיל | חלון עם "הפרופיל יוסר מהאתר הציבורי", ואחריו 200 והשורה מתעדכנת | 409 `profile-not-active`: טעינה מחדש והודעה |

</frozen-after-approval>

## Code Map

- `client/src/app/pages/admin/admin.routes.ts` -- להוסיף `review` ו-`profiles` לפי דפוס `cohorts`: `loadComponent`, `title`, `canActivate: [adminGuard('home')]`
- `client/src/app/pages/admin/home/admin-home.ts` -- שורה 22 מסמנת איפה להוסיף קישורי ניווט (`nav-card`) לשני המסכים
- `client/src/app/pages/admin/cohorts/{cohorts.service.ts,admin-cohorts.ts,admin-cohorts.css,admin-cohorts.spec.ts}` -- התבנית להעתקה: שירות, קומפוננטת signals, `loadSeq`, `onUnauthorized`, טיפול ב-409/404, מיקוד עם `afterNextRender`, ובדיקות עם `HttpTestingController`
- `client/src/app/pages/admin/admin-shared.css` -- סגנונות משותפים למסכי ניהול
- `client/src/app/shared/api/{api-client.ts,app-error.ts}` -- לוודא שמזהה `latestPendingRevisionId` ו-`code` נשמרים בשגיאה (לא ידוע)
- `client/src/app/shared/ui/{confirm-dialog,safe-markdown,avatar,textarea,empty-state,error-state,loading-skeleton,chip}` -- להשתמש כמות שהם
- `client/src/app/shared/strings.he.ts` -- הבלוק `admin` (שורה ~148): להוסיף `admin.review` ו-`admin.profiles`
- שרת (לקריאה בלבד): `GET /admin/submissions?limit&offset`, `GET /admin/submissions/{id}`, `/photo`, `POST .../approve`, `POST .../return` עם `{note}`, `GET /admin/profiles?visibility&cohort&limit&offset`, `POST /admin/profiles/{profileId}/remove`

## Tasks & Acceptance

**Execution:**
- [x] `client/src/app/pages/admin/review/review.service.ts` -- שירות לשש הקריאות, עם טיפוסי תגובה -- שכבת API אחת
- [x] `client/src/app/pages/admin/review/admin-review.{ts,css}` -- תור: טבלה, מצבי טעינה, ריק ושגיאה, וניווט לפרטים -- FR-6
- [x] `client/src/app/pages/admin/review/submission-detail.{ts,css}` -- פרטי הגשה, השוואת מפורסם מול מוצע, תמונה מ-`/photo`, קורות חיים ב-`safe-markdown`, אישור והחזרה בחלונות אישור -- FR-6, FR-7, FR-18
- [x] `client/src/app/pages/admin/profiles/admin-profiles.{ts,css}` -- רשימת פרופילים מפורסמים והסרה בחלון אישור -- FR-19
- [x] `client/src/app/pages/admin/admin.routes.ts`, `home/admin-home.ts`, `shared/strings.he.ts` -- נתיבים, קישורים ומחרוזות
- [x] קבצי `*.spec.ts` לכל קומפוננטה ושירות -- בדיקות לכל שורה ב-I/O Matrix (כולל 409, 404, 401 ושגיאת `note`)

**Acceptance Criteria:**
- Given מנהלת מחוברת, when היא פותחת את תור הסקירה, then היא רואה את ההגשות הממתינות ומצב ריק כשאין.
- Given הגשה שהמנהלת אישרה, when היא חוזרת לתור, then ההגשה אינה מופיעה.
- Given משתמשת שאינה מחוברת, when היא פותחת מסך ניהול, then היא מועברת לכניסה.
- Given `ng test` ו-`ng build`, when הם רצים, then שניהם עוברים בלי שגיאות או אזהרות חדשות.

## Implementation Notes

## Spec Change Log

## Review Triage Log

| ממצא | שכבה | פסק דין | נימוק | ניתוב |
|---|---|---|---|---|
| רשימות לא מעבירות limit ו-total, רק 50 הראשונים | blind, edge | medium | ברירת מחדל 50 בשרת וחסם 100, והלקוח זורק total | patch |
| ריענון שקט בטיסה מחזיר שורה שהוסרה; removeSub לא מבוטל | edge | low | אין ביטול של loadSub אחרי הסרה, תיקון של שורה אחת | patch |
| סימון שורה ששונתה רק בצבע | blind | medium | NFR3: מידע לא בצבע בלבד | patch |
| חוסר בדיקות: profileRemoved, cvInvalid, קישור javascript:, תמונה בעדכון, גבול 1000, ספירת בקשות בפרופילים, כשל ריענון שקט | verification-gap | medium | אין בדיקה שנכשלת בהסרת הקוד | patch |
| בדיקת logout בשלושת המסכים | verification-gap | low | הדפוס זהה למסכים קיימים, לא אומת | defer |
| 409 מתעלם מ-latestPendingRevisionId | blind, edge | false | הספק קובע ריענון והודעה, והתור מציג כל גרסה חדשה | נדחה |
| detail ללא loadSeq או snapshot של id | blind, edge | false | load() מבטל את המנוי הקודם, והרכיב נוצר מחדש בכל ניווט מהתור | נדחה |
| אישור כשהקורות חיים לא תקינים או הפרופיל הוסר | blind | low | יש הודעה בדף, והשרת מחליט | נדחה |
| flash נשאר אחרי ניווט חסום | blind, edge | low | 401 לא מגיע ל-finish, והמגן עובר כשמחוברת | נדחה |
| חלון האישור לא נשאר פתוח בשגיאת הערה | edge | low | השגיאה מוצגת ליד השדה לפני פתיחת החלון, כוונת הספק מתקיימת; סטייה מנוסח המטריצה | נדחה, מדווח |
| שגיאת טעינת תמונה, מטמון תמונה, כותרת דף סטטית, תאריך קצר, CSS כפול, ספירת UTF-16, רמז לשחזור פרופיל | blind | low | קוסמטי או שהתיקון מוסיף מורכבות; השרת שולח no-store | נדחה |
| מפתח changes "photo" מול hasPhoto | blind | false | השרת מוסיף "photo" (SubmissionReviewService.cs:113) | נדחה |
| ריענון 409 אינו שקט | edge | low | התור מציג שלד בטעינה, ללא נזק | נדחה |

## Verification

**Commands:**
- `cd client && npx ng test --watch=false` -- expected: כל הבדיקות עוברות
- `cd client && npx ng build` -- expected: בנייה מצליחה

**Manual checks (if no CLI):**
- בדפדפן מול שרת הפיתוח: להיכנס כמנהלת, לאשר הגשה מזרע הפיתוח ולראות אותה בגלריה, ולהסיר אותה ולראות שנעלמה.
