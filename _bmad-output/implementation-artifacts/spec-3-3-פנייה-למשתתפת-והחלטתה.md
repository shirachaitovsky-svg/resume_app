---
title: 'סיפור 3.3: פנייה למשתתפת והחלטתה'
type: 'feature'
created: '2026-10-22'
status: 'done'
baseline_revision: '55539b95c654852b3ebc41a47c788d780f912302'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-3-context.md'
warnings: []
deferred:
  - summary: >-
      בקשה במצב sent_to_participant שהקישור שלה לא נפתח אינה פגה אחרי 14 יום.
    evidence: |-
      ExpireStaleAsync מכסה רק awaiting_email_verification, והפקיעה קורית רק בפתיחת הקישור או בהסרת פרופיל. בקשה כזו נשארת בתור המנהלת ללא הגבלה.
    location: >-
      server/Infrastructure/ContactRequestRepository.cs (ExpireStaleAsync)
    severity: medium
  - summary: >-
      מייל החלטה שנכשל סופית (dead) משאיר את הבקשה sent_to_participant בלי דרך שליחה חוזרת למנהלת.
    evidence: |-
      שליחה חוזרת מחזירה 409 כי הסטטוס כבר אינו submitted, ומפתח ה-dedup חוסם שורת Outbox חדשה. נדרשת נקודת קצה לשליחה חוזרת, והספק אינו קובע אותה.
    location: >-
      server/Features/Email/EmailOutboxProcessor.cs (ProcessContactDecisionAsync)
    severity: medium
---

<intent-contract>

## Intent

**Problem:** בקשת פרטי קשר שאומתה (`submitted`) נתקעת: אין למנהלת דרך לפנות למשתתפת, ואין למשתתפת דרך להסכים או לסרב. בלי זה אי אפשר לממש את אישור המסירה (3.4) ואת מסכי הלקוח (5.9).

**Approach:** בשרת בלבד. נקודת קצה לאדמין ששולחת את הבקשה למשתתפת (מעבר ל-`sent_to_participant`, שורת Outbox, ורישום ביומן ביקורת), מייל עם קישור אישי חד-בקשתי, ושתי נקודות קצה ציבוריות מבוססות אסימון: תצוגה מקדימה שאינה צורכת, והחלטה (אישור או סירוב).

## Boundaries & Constraints

**Always:**
- הטוקן: `Purpose = contact-decision` (לפי AD-6), 256 ביט, hash בלבד, תחום לבקשה אחת, תקף 14 יום, החלטה אחת. נוצר ברגע השליחה על ידי תהליך הרקע, ואינו בשורת ה-Outbox. הנפקה חדשה מבטלת את הקודמת.
- כל מעבר מצב הוא עדכון מותנה (`WHERE status = expected`), 409 בהתנגשות. החלטות מקבילות: רק אחת מצליחה.
- בהסכמה נשמרים `ParticipantDecidedAt` וגרסת פרטי הקשר בזמן ההסכמה (עמודה חדשה ב-`contact_requests`, ממלאת את `ContactVersion` מ-`ParticipantPrivate`). מיגרציה נדרשת.
- GET אינו צורך אסימון (סורקי מייל). הקישור בנוי עם האסימון בפרגמנט `#`, והפעולות הן POST בלבד, עם `Referrer-Policy: no-referrer` ו-`no-store`.
- אין פרטי קשר של המשתתפת במייל או בתגובות. התצוגה המקדימה מחזירה רק שם המבקר והסיבה (טקסט פשוט מנוקה), בלי כתובת המבקר.
- מסירה אינה חלק מהסיפור: סירוב או היעדר תשובה אינם מאפשרים מסירה, והמבקר אינו מקבל הודעה.

**Never:**
- לא נוגעים בלקוח (`client/`) ובסיפור 3.4 (אישור המנהלת ושליחת הפרטים).
- לא נשמרים אסימון גולמי, מייל או נייד ב-Outbox או ביומנים.
- לא משתמשים בעוגיית סשן להחלטה: ה-POST צורך את האסימון ומחליט ישירות.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| שליחה למשתתפת | אדמין, בקשה `submitted`, פרופיל `Active` | 200, הבקשה `sent_to_participant`, שורת Outbox `contact-decision-request`, שורה ביומן הביקורת | אינה `submitted`: 409 `request-not-submitted`. לא קיימת או מזהה לא תקין: 404. לא אדמין: 401 או 403 |
| מייל למשתתפת | שורת Outbox | נשלח למייל מ-`ParticipantPrivate` עם שם המבקר, הסיבה וקישור אישי בפרגמנט, טקסט פשוט | בקשה לא במצב צפוי או פרופיל לא פעיל: השורה נסגרת בלי שליחה |
| תצוגה מקדימה | POST עם אסימון תקף | 200 עם שם המבקר והסיבה בלבד, האסימון לא נצרך | אסימון לא מוכר: 401 `link-invalid`. פג: 410 `link-expired`. נוצל: 409 `already-decided` |
| אישור | POST `decision=approved` | הבקשה `participant_approved`, נשמרים זמן וגרסת הפרטים, האסימון נצרך | החלטה מקבילה שנייה: 409 |
| סירוב | POST `decision=declined` | הבקשה `participant_declined`, האסימון נצרך | N/A |
| פג תוקף | אסימון של בקשה שלא נענתה אחרי 14 יום | 410 `link-expired`, והבקשה עוברת ל-`expired` | N/A |
| פרופיל הוסר | הסרת פרופיל עם בקשה `sent_to_participant` או `participant_approved` | הבקשה `expired` והאסימונים מבוטלים | קישור פתוח: 409 `already-decided` או 410 |

</intent-contract>

## Code Map

- `server/Domain/Contacts/ContactRequest.cs` -- מעברי מצב קיימים (שורות ~96-110) ושדות ההחלטה; להוסיף שדה גרסת פרטי הקשר בהסכמה
- `server/Domain/Email/EmailOutboxEntry.cs` -- קבועי תבנית ו-`PersonalToken`; להוסיף `contact-decision-request`, מטרת `contact-decision`, חיי 14 יום ומפתח dedup
- `server/Features/Email/{PersonalTokens.cs,EmailOutboxProcessor.cs,ContactVerificationEmail.cs,IEmailOutbox.cs}` -- תבנית הנפקה ושליחה להעתקה (`ProcessContactVerificationAsync`); להוסיף `ContactDecisionEmail` ושיטת נמען
- `server/Infrastructure/{EmailOutboxRepository.cs,ContactRequestRepository.cs,ProfileAdminRepository.cs}` -- הנפקת אסימון בנעילות פרופיל ואז בקשה, צריכה אטומית (`VerifyAsync`), והסרת פרופיל (שורות ~55-80): להוסיף `SentToParticipant` ו-`ParticipantApproved` לרשימת הסטטוסים שפגים וביטול אסימוני `contact-decision`
- `server/Api/Endpoints/{ContactRequestAdminEndpoints.cs,ContactRequestEndpoints.cs,ProfileAdminEndpoints.cs}` -- נקודת אדמין חדשה לפי `remove`, ושני POST ציבוריים לפי `verify`
- `server/Domain/Audit/AuditLogEntry.cs` -- קבוע פעולה `send-to-participant`
- `server/Api/Security/RequestIntegrityMiddleware.cs` (~55-65), `Infrastructure/DependencyInjection.cs`, `Tests/AuthTestSupport.cs` -- רישום הנתיבים החדשים כאנונימיים עם anti-forgery, היקף הגבלת קצב חדש ומשתנה סביבה לבדיקות
- `server/Infrastructure/Migrations/` -- מיגרציה חדשה (`dotnet tool restore` ואז `dotnet ef migrations add`)
- `server/Tests/{ContactRequestTests.cs,ContactRequestQueueTests.cs,AuthTestSupport.cs}` -- דפוס בדיקות עם `FakeEmailSender`, הרצת `EmailOutboxProcessor` ידנית, ו-SQL לזריעה
- אין לשנות: `client/`, מסירת פרטים (3.4)

## Tasks & Acceptance

**Execution:**
- `server/Domain` ו-`server/Infrastructure/Migrations` -- שדה גרסת הפרטים בהסכמה, מטרת הטוקן ותבנית ה-Outbox, ומיגרציה -- בסיס נתונים
- `server/Infrastructure/ContactRequestRepository.cs` -- שליחה למשתתפת, תצוגה מקדימה והחלטה כעדכונים מותנים עם נעילות באותו סדר כמו `VerifyAsync`; פקיעה בעת מימוש או הסרה -- AD-3, AD-14
- `server/Features/Email` ו-`server/Infrastructure/EmailOutboxRepository.cs` -- הנפקת אסימון ומייל `contact-decision-request` -- AD-4, AD-6, AD-15
- `server/Api/Endpoints` -- `POST /admin/contact-requests/{id}/send-to-participant`, `POST /contact-decisions/preview`, `POST /contact-decisions/decide`, עם הגבלת קצב ומגן anti-forgery -- AD-5, AD-12
- `server/Tests` -- בדיקה לכל שורה במטריצה, כולל מרוץ בין שתי החלטות, ו-`ProfileAdminRepository` שמבטל -- כיסוי

**Acceptance Criteria:**
- Given בקשה שנשלחה למשתתפת, when נבדקות שורת ה-Outbox והיומנים, then אין בהם אסימון גולמי, מייל או נייד.
- Given אישור או סירוב, when הוא נשמר, then המנהלת רואה את ההחלטה בתור (`ParticipantDecision`, `ParticipantDecidedAt`).
- Given כל הבדיקות, when הן רצות, then כולן עוברות ואין בדיקה קיימת שנשברה.

## Spec Change Log

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: 19 findings (מקובצים לפי סיבת שורש מארבע השכבות) — high 0, medium 4, low 13, false 2, maybe-false 0
- findings:
  - `[medium]` `[patch]` `FirstAsync` אחרי peek בלי נעילה בטוקן — 500 במקום 401 אחיד אם הטוקן נעלם — הוחלף ב-`FirstOrDefaultAsync`
  - `[medium]` `[patch]` אין בדיקות: הגבלת קצב על שני הנתיבים, ענף `!profileActive` ב-resolver, ניקוי טקסט עוין, 415 וגוף גדול, anti-forgery חסר, כותרות — נוספו בדיקות
  - `[low]` `[patch]` משתנה הסביבה `RATE_LIMIT_CONTACT_DECISION_IP` והנתיבים לא מתועדים ב-README — נוסף תיעוד
  - `[medium]` `[defer]` בקשה `sent_to_participant` שלא נפתח הקישור שלה לא פגה בסוף 14 יום; הסוחף `ExpireStaleAsync` מכסה רק `awaiting_email_verification` (blind, edge, intent-alignment) — הספק מגדיר פקיעה בעת מימוש או הסרה בלבד
  - `[medium]` `[defer]` מייל שנכשל סופית (`dead`) משאיר את הבקשה `sent_to_participant` בלי אפשרות שליחה חוזרת מהמנהלת — תיקון דורש נקודת קצה חדשה שהספק אינו קובע
  - `[low]` `[reject]` גרסת פרטי קשר NULL בהסכמה כשאין שורת `participant_private` — השורה חובה לכל פרופיל (1:1), לא ניתן להגיע למצב
  - `[low]` `[reject]` ניסיון חוזר אחרי כשל ב-MarkSent מנפיק אסימון חדש ומבטל את הקישור שנשלח — המייל הקודם כבר נשלח, והמצב נדיר ולא מזיק
  - `[low]` `[reject]` בדיקת `!profileActive` אחרי ענף revoked — הסרת פרופיל כבר מפקיעה את הבקשות, ושינוי ישיר ב-DB אינו נתיב עסקי
  - `[low]` `[reject]` בקשה שנמחקה בין peek לנעילה מחזירה 409 במקום 404 — מרוץ שאינו מתרחש בפועל
  - `[false]` `[reject]` פעולת שליחה מאדמין ללא משתמש אמיתי בסשן — המדיניות דורשת אדמין אמיתי, ו-`NameIdentifier` מגיע ממנו
  - `[low]` `[reject]` קטגוריית `request-not-sent` מערבבת "כבר הוחלט" — אבחון בלבד, בלי נזק
  - `[low]` `[reject]` היעדר דף לקוח `/contact/decide` — מחוץ להיקף: סיפור 5.9 והלקוח לא נוגעים
  - `[low]` `[reject]` החלטת המשתתפת אינה ביומן הביקורת — AD-22 מגדיר רשימת פעולות ניהול, והחלטת משתתפת אינה בה
  - `[low]` `[reject]` המנהלת לא מקבלת התראה כשהמשתתפת מחליטה — הספק מציג את ההחלטה בתור בלבד
  - `[low]` `[reject]` preview ו-decide חולקים סקופ הגבלת קצב — שניהם POST, סורקי מייל מפעילים GET בלבד
  - `[low]` `[reject]` preview כותב ל-DB כשהקישור פג — מכוון ומתועד, ללא נזק
  - `[low]` `[reject]` כפילות קוד, `DecisionBody` ציבורי, נוסח מייל מגדרי ובלי שם המשתתפת, חישוב ימים — קוסמטי או שהתיקון מוסיף מורכבות
  - `[low]` `[reject]` האסימון נוצר בשליחת הרקע ולא בלחיצת המנהלת — מתועד ב-Design Notes, והחלון של 14 יום מתחיל בשליחה בפועל
  - `[false]` `[reject]` `expires_at` של הבקשה קוצר את חלון 14 הימים — `ExpireStaleAsync` לא נוגע בסטטוס הזה

## Design Notes

מזהה המטרה: האפיון (AD-6) קובע `contact-decision`, והסיפור המקורי כתב `decide-contact`. נבחר `contact-decision`. החלטה נשמרת בעמודה חדשה ב-`contact_requests` כי `ContactVersion` חי ב-`ParticipantPrivate`. מצב הבקשה עובר ל-`sent_to_participant` ברגע הלחיצה, והאסימון נוצר רק בשליחה בפועל; שורת Outbox שנכשלה סופית נשארת גלויה למנהלת כ-`dead`.

## Verification

**Commands:**
- `dotnet test server` -- expected: כל הבדיקות עוברות (נדרש Docker ל-Postgres)
- `dotnet build server` -- expected: בנייה מצליחה בלי אזהרות חדשות

## Auto Run Result

Status: done

- **שינוי שמומש (שרת בלבד):** נקודת אדמין `POST /admin/contact-requests/{id}/send-to-participant`, מייל `contact-decision-request` עם קישור אישי (אסימון נוצר בעת השליחה בפועל), ושתי נקודות ציבוריות `POST /contact-decisions/preview` ו-`/decide`. בהסכמה נשמרים זמן ההחלטה וגרסת פרטי הקשר (מיגרציה `ContactDecision`). הסרת פרופיל מפקיעה בקשות `sent_to_participant` ו-`participant_approved` ומבטלת אסימונים.
- **ממצאי סקירה:** 19 ממצאים מקובצים. תוקנו 2 ממצאים בדרגת medium ועוד תיעוד אחד (README): טוקן שנעלם (500 במקום 401), ובדיקות חסרות (הגבלת קצב, פרופיל שאינו פעיל, ניקוי טקסט, 415 וגוף גדול, anti-forgery, כותרות). נדחו לרשימת `deferred`: פקיעה אוטומטית של בקשות שלא נענו, ושליחה חוזרת של מייל שנכשל. נדחו 13 ממצאי low ו-2 false, עם נימוק בלוג.
- **המלצה לסקירת המשך:** כן. סיכון לא מאומת: פקיעת בקשות שלא נענו ושליחה חוזרת תלויות בהחלטה עתידית, וגם נוסח המייל והזרימה מול דף הלקוח עוד לא נבדקו מקצה לקצה (דף `/contact/decide` ייבנה בסיפור 5.9).
- **אימות:** `dotnet build server` הצליח. `dotnet test server`: 774 עברו, 0 נכשלו (10 דקות 40 שניות).
- **סיכונים נותרים:** אין דף לקוח לקישור במייל. המייל נשלח לפי ברירת מחדל בעברית נקבית.
