---
title: 'סיפור 1.1: שלד מתפקד'
type: 'feature'
created: '2026-10-21'
status: 'done'
baseline_commit: 'NO_VCS'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-1-context.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-resume_app-2026-10-21/ARCHITECTURE-SPINE.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** אין עדיין קוד. כל סיפור הבא צריך תשתית עובדת: לקוח, שרת, בסיס נתונים ופריסה.

**Approach:** ליצור שלד greenfield: `client/` (Angular 22, עברית ו-RTL) ו-`server/` (.NET 10) עם `/healthz`, מיגרציות EF Core באתחול, `Dockerfile`, `vercel.json` עם rewrite ל-`/api/*`, ודחיית בקשות בלי `x-origin-secret`.

## Boundaries & Constraints

**Always:** מבנה תיקיות לפי הארכיטקטורה (`Api/`, `Features/`, `Domain/`, `Infrastructure/`, `Tests/`). סודות וחיבורים רק ממשתני סביבה. מחרוזות ממשק בקובץ משאבים אחד. מאפייני CSS לוגיים. מיגרציות תחת advisory lock.

**Never:** אין לוגיקה עסקית, ישויות או מסכים מעבר לדף פתיחה. אין CORS פתוח. אין סוד בקוד או בריפו. אין עיצוב מעבר לבסיס (זה סיפור 1.2).

**Decisions (אושרו על ידי המשתמשת):**
- פריסה: אני כותבת רק קבצי תצורה (`vercel.json`, `render.yaml`, `Dockerfile`) והוראות ב-README. המשתמשת מחברת GitHub, Vercel ו-Render ידנית. האימות של פריסה אוטומטית ושל `/api/healthz` דרך Vercel נעשה אחרי החיבור, ולא חוסם את סגירת הקוד.
- Postgres מקומי: המשתמשת מתקינה Docker Desktop או Postgres. המיגרציות והנעילה מאומתות מקומית על Postgres אמיתי, וה-README מתעד את ההרצה.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| בריאות | `GET /healthz` עם הסוד | 200 | N/A |
| בקשה ישירה | `GET /healthz` בלי `x-origin-secret` | נדחית (403) | N/A |
| דרך Vercel | `GET /api/healthz` בכתובת Vercel | 200 | N/A |
| אתחול | Postgres זמין | מיגרציות רצות פעם אחת תחת נעילה | כשל חיבור: השרת נכשל באתחול עם שגיאה ברורה, בלי סוד ביומן |

</frozen-after-approval>

## Code Map

- `client/`, `server/` -- לא קיימים. פרויקט greenfield; בנייה מ-`ng new` (Angular CLI 22.2.3 זמין דרך `npx`) ומ-`dotnet new` (SDK 10.0.102 מותקן).
- `_bmad-output/planning-artifacts/architecture/.../ARCHITECTURE-SPINE.md` -- AD-9 (כתובת אחת, סוד משותף), AR20 (מעטפת תפעול), מבנה הריפו.
- `_bmad-output/planning-artifacts/epics.md` -- קריטריוני הקבלה של 1.1.

## Tasks & Acceptance

**Execution:**
- [x] `client/` -- `ng new` (Angular 22), `lang="he" dir="rtl"`, דף פתיחה בעברית, קובץ משאבים למחרוזות -- AR1, NFR4
- [x] `server/` -- פרויקט .NET 10 במבנה `Api/Features/Domain/Infrastructure/Tests`, `GET /healthz`, `UseForwardedHeaders` -- AR1, AR3
- [x] `server/Api/` -- middleware שדוחה בקשות בלי `x-origin-secret` (הסוד ממשתנה סביבה) -- AR3
- [x] `server/Infrastructure/` -- EF Core + Npgsql, מיגרציה ראשונה ריקה, הרצה באתחול תחת advisory lock, חיבור ממשתנה סביבה -- AR20
- [x] `server/Tests/` -- בדיקות ל-`/healthz` ולדחיית בקשה בלי סוד -- AR21
- [x] `Dockerfile`, `vercel.json`, `render.yaml` -- בנייה, rewrite של `/api/*`, הוספת הסוד בכותרת -- AR2, AR3
- [x] `README.md` -- הרצה מקומית ומשתני הסביבה הנדרשים -- שימושיות

**Acceptance Criteria:**
- Given הרצה מקומית, when פותחים את הלקוח, then מוצג דף עברית ב-RTL, ו-`GET /healthz` מחזיר 200.
- Given `Dockerfile`, when בונים אותו, then נוצר אימג' שמריץ את השרת.
- Given בקשה בלי `x-origin-secret`, when מגיעה לשרת, then נדחית; ועם הסוד, מצליחה.
- Given שרת עם Postgres, when עולה, then מיגרציות רצות תחת נעילה ואין סוד בקוד.

## Implementation Notes

- נבנו `server/` (5 פרויקטים), `client/` (Angular 22.2.3), `Dockerfile`, `vercel.json`, `middleware.js` (הוספת הסוד ב-Vercel, כי `vercel.json` לא קורא משתני סביבה), `render.yaml`, `README.md`.
- לא אומת: מיגרציות ונעילה מול Postgres אמיתי, בניית Docker, זרימת Vercel (אין כלים או חיבור). ה-README מתעד את האימות המקומי והידני.
- `FallbackPolicy` נדחה לסיפור 1.3 (אין עדיין סכמת אימות). `render.yaml` בלי `healthCheckPath` כי `/healthz` דורש את הסוד.

## Spec Change Log

## Review Triage Log

| ממצא | פסיקה | ראיה |
|---|---|---|
| `middleware.js`: נתיב `/api//evil.com` מוביל ליעד אחר והסוד דולף | high (patch) | `new URL('//evil.com', target)` נפתר כ-protocol-relative; הכותרת `x-origin-secret` נשלחת למארח זר |
| ייבוא `rewrite` ו-`installCommand` שלא מתקין תלויות שורש | medium (patch) | `installCommand` רץ רק ב-`client/`; הייבוא נעשה מנתיב השורש |
| `KnownIPNetworks`/`KnownProxies` ריקים הם no-op | medium (patch) | אתחול אוסף ריק לא מנקה את ברירות המחדל (loopback); כותרות מ-Render יתעלמו |
| `FromUri` מאבד `sslmode` | medium (patch) | `uri.Query` לא נקרא; חיבורים חיצוניים ידחו |
| אין בדיקות ל-`ConnectionStringResolver` ולמקרה בלי בסיס נתונים | medium (patch) | נבדק בקוד הבדיקות: רק שלוש בדיקות `/healthz` |
| עטיפת `PostgresException` כ"שגיאת חיבור" ו-unlock שעלול להסתיר שגיאה | medium (patch) | `NpgsqlException` כולל `PostgresException`; `finally` בלי הגנה |
| `.dockerignore` תופס רק את שורש הריפו | low (patch) | הדפוס `bin/` ללא `**/`; תיקון ישיר |
| `render.yaml` בלי `plan` לשירות | low (patch) | ברירת המחדל של Render בתשלום |
| פורט proxy 5000 מול launchSettings 5271 וסוד ריק | low (patch) | אי-התאמה בקבצים |
| תיקיית `Designer.cs`, `favicon.ico`, `package-lock.json` חסרים | false | הקבצים קיימים; הוחרגו רק מקובץ ה-diff |
| `/healthz` דרך שרת הלקוח המקומי | false | ה-AC מחייב שהשרת עונה ל-200, לא שה-dev server של הלקוח יעביר אותו |
| כותרת `index.html` קשיחה | false | `index.html` סטטי ואינו יכול לייבא TS; אין נזק |
| Dockerfile כ-root, בלי HEALTHCHECK ובלי מטמון restore | low (rejected) | לא סביר שיפגע בשימוש רגיל; התיקון מוסיף מורכבות |
| נעילת advisory בלי timeout; סוד עם רווח; EF כבר נועל | low (rejected) | תרחיש לא מוכח; תיקון מוסיף ענפים |
| בדיקת סדר middleware מול ForwardedHeaders; בדיקת `middleware.js`; נקודת בריאות בלי סוד ל-Render; CI | defer | אין השפעה כל עוד קיימת רק `/healthz`; נרשם ב-deferred-work |

## Verification

**Commands:**
- `dotnet build server` ו-`dotnet test server` -- expected: עוברים
- `npm run build` ב-`client/` -- expected: בנייה מצליחה
