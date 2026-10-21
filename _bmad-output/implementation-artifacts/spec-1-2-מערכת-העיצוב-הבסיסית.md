---
title: 'סיפור 1.2: מערכת העיצוב הבסיסית'
type: 'feature'
created: '2026-10-21'
status: 'done'
baseline_revision: '45e6e076375531f3f14fe7134f85848d3a17e1ca'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-resume_app-2026-10-21/DESIGN.md'
warnings: []
deferred:
  - summary: >-
      ניווט הצף משתמש בקישורי href רגילים ובמצב פעיל ידני במקום routerLink
    evidence: |-
      בהדגמה הקישורים מובילים ל-/#id בגלל base href; מסכים אמיתיים (סיפורים 1.5 ואילך) צריכים ניווט מונחה ניתוב
    location: >-
      client/src/app/shared/ui/nav-pill/nav-pill.html
    severity: low
  - summary: >-
      נתיב /design-system נפרס גם בייצור
    evidence: |-
      הנתיב רשום ללא תנאי; אין נתונים רגישים בעמוד, אך כדאי להגביל לפני עלייה לאוויר
    location: >-
      client/src/app/app.routes.ts
    severity: low---

<intent-contract>

## Intent

**Problem:** ללקוח אין טוקנים ורכיבי בסיס, ולכן כל מסך בא יבנה בעיצוב ובנגישות שונים.

**Approach:** להגדיר ב-`client/` טוקני עיצוב כמשתני CSS לפי `DESIGN.md` ורכיבי Angular בסיסיים (כרטיס זכוכית, כפתור, שדה, צ'יפ, ניווט צף), עם נגישות מובנית, ועמוד הדגמה פנימי לבדיקה.

## Boundaries & Constraints

**Always:** משתני CSS ב-`:root` בלבד, בלי צבעי hex מחוץ לטוקנים. מאפייני CSS לוגיים. כל מחרוזת בקובץ המשאבים `strings.he.ts`. רכיבי standalone עם `ChangeDetectionStrategy.OnPush`. Heebo נטען מקומית (חבילת `@fontsource/heebo`, משקלים 400, 500, 600, 800, 900, תת-קבוצת עברית ולטינית), בלי בקשה לשירות חיצוני. יעדי מגע 44px לפחות, טבעת מיקוד גלויה, `prefers-reduced-motion` נכבד.

**Never:** אין מסכי מוצר, ניתוב אמיתי או לוגיקה עסקית. אין צבעים מחוץ לסקאלה של `DESIGN.md`. אין ספריית רכיבים חיצונית (Material וכד').

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| זכוכית נתמכת | דפדפן עם `backdrop-filter` | כרטיס `rgba(255,255,255,.58)`, טשטוש 24px, רדיוס 32px | No error expected |
| זכוכית לא נתמכת | `@supports not (backdrop-filter)` | כרטיס לבן אטום | No error expected |
| שדה עם שגיאה | `error` מוגדר | הודעה ליד השדה, `aria-invalid`, `aria-describedby` תואם | שגיאה לא מוצגת כשאין `error` |
| שדה חובה | `required` | מוצג הטקסט "חובה" בתווית | No error expected |
| תנועה מופחתת | `prefers-reduced-motion: reduce` | אין מעברים ואנימציות | No error expected |

</intent-contract>

## Code Map

- `client/src/styles.css` -- ריק כרגע; כאן טוקנים, רקע עם שלושה כתמי צבע, סקאלת טיפוגרפיה, `@font-face`/ייבוא Heebo, איפוס בסיסי.
- `client/src/app/shared/strings.he.ts` -- מחרוזות; להוסיף "חובה", תוויות ניווט, טקסטי עמוד ההדגמה.
- `client/src/app/app.ts`, `app.html`, `app.routes.ts` -- נקודת הרכבה; `routes` ריק. להוסיף רק את הנתיב `design-system` (טעינה עצלה).
- `client/src/app/app.spec.ts` -- מבנה בדיקות vitest (`ng test`) להעתקה.
- `_bmad-output/planning-artifacts/ux-designs/ux-resume_app-2026-10-21/DESIGN.md` -- מקור הערכים (צבעים, גדלים, רדיוסים, רכיבים).
- אין לשנות: `server/`, `vercel.json`, `proxy.conf.js`.

## Tasks & Acceptance

**Execution:**
- `client/package.json` -- הוספת `@fontsource/heebo` -- פונט מקומי
- `client/src/styles.css` -- טוקני צבע (כולל המבטא הכחול) ורקע עם כתמים, סקאלת טיפוגרפיה בגדלים ובמשקלים, מובייל מתחת ל-640px -- UX-DR1, UX-DR2
- `client/src/app/shared/ui/glass-card/` -- רכיב כרטיס זכוכית עם נפילה ללבן -- UX-DR3
- `client/src/app/shared/ui/button/` -- כפתור גלולה בגובה 48: ראשי שחור, משני, מבטא כחול, סכנה -- UX-DR4
- `client/src/app/shared/ui/field/` -- שדה בגובה 52, תווית מעל, "חובה", שגיאה ליד השדה, קישור ARIA -- UX-DR5
- `client/src/app/shared/ui/chip/` -- צ'יפ, מצב נבחר בכחול -- UX-DR6
- `client/src/app/shared/ui/nav-pill/` -- ניווט צף בגלולה שחורה: גלריה, אודות הקורס, יצירת פרופיל, עריכת הפרופיל שלי (קלט פריטים; בנייד סרגל תחתון) -- UX-DR7
- `client/src/app/design-system/` -- עמוד הדגמה בנתיב `/design-system` שמציג את כל הרכיבים ומצביהם
- `client/src/app/**/*.spec.ts` -- בדיקות לשורות המטריצה: שגיאת שדה וקישור ARIA, "חובה", מצב נבחר בצ'יפ, נפילה ללבן (CSS) -- כיסוי המטריצה

**Acceptance Criteria:**
- Given האפליקציה נפתחת, when נבדק `:root`, then כל טוקני הצבע של `DESIGN.md` מוגדרים כמשתני CSS, הרקע כולל כתמי צבע ללא נקודות, ו-Heebo נטען מהנכסים המקומיים.
- Given עמוד `/design-system`, when מרנדרים אותו, then כל רכיב מוצג, `lang="he" dir="rtl"` נשמר, ו-Tab מציג טבעת מיקוד על כל רכיב אינטראקטיבי.
- Given כל רכיב אינטראקטיבי, when נמדד, then גובהו או שטח המגע שלו לפחות 44px.

## Spec Change Log

## Review Triage Log

### 2026-10-21 — Review pass
- verdicts: 36 findings — high 0, medium 2, low 7, false 3, maybe-false 0 (השאר: ממצאים כפולים או low שנדחו)
- findings:
  - `[medium]` `[patch]` הודעת שגיאת שדה לא מוכרזת לקורא מסך — נוסף role="alert"
  - `[medium]` `[patch]` טבעת מיקוד לבנה על פריט פעיל לבן בניווט — נוספה טבעת מנוגדת
  - `[low]` `[patch]` שדה בפוקוס במצב שגיאה מאבד גבול אדום; מסגרת ה-fallback של הזכוכית נעלמת; ניווט תחתון בלי safe-area; main מקונן בעמוד ההדגמה; חסרות בדיקות ל-Button, NavPill ו-compact — תוקנו
  - `[low]` `[defer]` nav-pill עם href רגיל, ונתיב /design-system בייצור — נרשמו ב-deferred
  - `[low]` `[reject]` hover ל-primary/danger, min-inline-size לצ'יפ, error ברווחים בלבד, מונה id גלובלי, nav ריק, 10 ייבואי פונט, overflow-wrap בכותרות, forced-colors, aria-required — לא סביר שייפגעו בשימוש רגיל והתיקון מוסיף מורכבות
  - `[false]` `[reject]` lang/dir חסרים — קיימים ב-index.html מסיפור 1.1; package-lock חסר — קיים ב-client ומוחרג מה-diff; הסרת BOM — לא משפיעה
## Verification

**Commands:**
- `npm run build` ב-`client/` -- expected: בנייה מצליחה בלי שגיאות
- `npm test -- --watch=false` ב-`client/` -- expected: כל הבדיקות עוברות

## Auto Run Result

- **שינוי:** טוקני עיצוב ב-`client/src/styles.css`, Heebo מקומי, ורכיבי glass-card, button, field, chip ו-nav-pill, עם עמוד הדגמה בנתיב `/design-system`.
- **קבצים:** `client/src/styles.css` (טוקנים, רקע, טיפוגרפיה); `client/src/app/shared/ui/*` (חמישה רכיבים ובדיקות); `client/src/app/design-system/` (הדגמה); `strings.he.ts`, `app.routes.ts`, `package.json`, `tsconfig.spec.json`.
- **סקירה:** 2 תיקוני medium ו-7 תיקוני low הוחלו (הכרזת שגיאה, טבעת מיקוד בניווט, safe-area, fallback של זכוכית, main מקונן, בדיקות חסרות). 2 פריטים נדחו ל-`deferred` (ניווט מונחה ניתוב, נתיב ההדגמה בייצור). שאר ה-low נדחו כפי שמתועד ביומן.
- **המלצה לסקירת המשך:** כן. שני תיקוני medium הוחלו, והסיכון שלא אומת הוא תצוגה בדפדפן אמיתי: ניגודיות הטקסט על הזכוכית, גובה 48/52, יעדי מגע 44px וטבעות המיקוד נבדקו רק ב-CSS ולא בעין.
- **אימות:** `npm run build` ו-`npm test -- --watch=false` ב-`client/`: בנייה תקינה, 18 מתוך 18 בדיקות עוברות.
- **סיכונים שנותרו:** הכרעות עיצוב של הסוכן: צבע הקנבס `#F3F6FB` (ב-DESIGN.md יש גם `#F4F4F4` בכותרת), וצ'יפ נבחר בכחול לפי המפרט ולא בשחור כמו `chip-selected` ב-DESIGN.md.
