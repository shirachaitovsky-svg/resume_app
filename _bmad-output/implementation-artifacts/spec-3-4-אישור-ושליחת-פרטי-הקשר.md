---
title: 'סיפור 3.4: אישור ושליחת פרטי הקשר'
type: 'feature'
created: '2026-10-22'
status: 'done'
baseline_revision: '730d5c83d9ffd5c23bf623bfb9a181e0bdacc079'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-3-context.md'
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** אחרי שמשתתפת אישרה בקשת פרטי קשר, אין למנהלת דרך לאשר את המסירה, והמערכת לא שולחת את הפרטים למבקר. כל שרשרת בקשות פרטי הקשר נעצרת בלי מסירה.

**Approach:** שרת בלבד. נקודת אדמין שמאשרת מסירה (קובעת `ManagerApprovedAt`, כותבת שורת Outbox ויומן ביקורת), ותהליך הרקע שבודק מחדש את התנאים ברגע השליחה, שולח את המייל והנייד רק לכתובת המאומתת של המבקר, ומסמן `delivered` רק אחרי שליחה מוצלחת בפועל.

## Boundaries & Constraints

**Always:**
- מסירה רק כשהבקשה `participant_approved` וגם `ManagerApprovedAt` קיים. כל מעבר מצב הוא עדכון מותנה, ו-409 בהתנגשות.
- אישור המסירה: עדכון מותנה `WHERE status = participant_approved AND manager_approved_at IS NULL`, שורת Outbox עם `DeduplicationKey = contact-delivery:{requestId}` ו-`TemplateKey = contact-delivery`, ושורה ב-`AuditLog` (`approve-delivery`), באותה טרנזקציה. בשורה אין מייל, נייד או אסימון.
- בבדיקה ברגע השליחה: הפרופיל `Active`, הסכמה בתוקף (`ParticipantDecidedAt` עד 14 יום), `ParticipantContactVersion` שווה ל-`ContactVersion` הנוכחי, ויש מייל ונייד. כשל באחד: השורה `dead` עם קטגוריה (`profile-not-active`, `consent-expired`, `contact-changed`, `no-recipient`, `delivery-not-approved`), לא נשלח דבר, והסיבה גלויה למנהלת בתור.
- המייל והנייד נקראים מ-`ParticipantPrivate` ברגע השליחה, ונשלחים רק ל-`VisitorEmail` המאומת, בטקסט פשוט. `delivered` נקבע באותה טרנזקציה שבה השורה מסומנת `sent`, ולעולם לא בנתיב הכשל.
- מפתח אידמפוטנטיות יציב לשליחה (`row.Id`). שורה שנתפסה שוב אחרי שהבקשה כבר `delivered` מסומנת `sent` בלי שליחה נוספת.
- אישור חוזר על בקשה שמסירתה `dead`: מאפס את השורה ל-`pending` (ניסיון ידני), בלי שורה כפולה. בכל מצב אחר: 409.
- אין פרטי קשר של משתתפת ביומנים, בשורת ה-Outbox, בתגובות API או בתור.

**Never:**
- לא נוגעים בלקוח (`client/`). לא מוסיפים מיגרציה אלא אם בדיקה מוכיחה שנדרשת.
- לא שולחים כשהמשתתפת סירבה, לא ענתה, או כשההסכמה ישנה או שהפרטים השתנו.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| אישור מסירה | אדמין, בקשה `participant_approved` | 200, `ManagerApprovedAt` נקבע, שורת Outbox ויומן ביקורת | לא `participant_approved` (כולל סירוב, ללא תשובה, כבר `delivered`): 409 `request-not-approved`. לא קיימת או מזהה לא תקין: 404. לא אדמין: 401 או 403 |
| שליחה בפועל | שורת `contact-delivery`, כל הבדיקות עוברות | מייל עם שם המשתתפת, מייל ונייד נשלח רק ל-`VisitorEmail`, השורה `sent`, הבקשה `delivered` | ספק זמני נכשל: ניסיון חוזר בלי `delivered` |
| הסכמה פגה | `ParticipantDecidedAt` ישן מ-14 יום | לא נשלח דבר, השורה `dead`, קטגוריה `consent-expired` | הסיבה מוצגת בתור |
| פרטים השתנו | `ContactVersion` שונה מגרסת ההסכמה | לא נשלח דבר, `dead`, `contact-changed` | הסיבה מוצגת בתור |
| פרופיל הוסר | הפרופיל לא `Active` | לא נשלח דבר, `dead`, `profile-not-active` | הסיבה מוצגת בתור |
| ניסיון ידני | שורת מסירה `dead`, אישור חוזר | 200, השורה חזרה ל-`pending` ללא שורה כפולה | שורה שאינה `dead`: 409 |
| כפילות | שליחה שכבר בוצעה ושורה שנתפסה שוב | לא נשלח מייל שני, השורה `sent` | N/A |

</intent-contract>

## Code Map

- `server/Domain/Contacts/ContactRequest.cs` -- `VisitorEmail`, `ManagerApprovedAt`, `ParticipantContactVersion`, `DecisionLifetime`; המעבר `ParticipantApproved -> Delivered` קיים; אין מיגרציה
- `server/Domain/Email/EmailOutboxEntry.cs`, `server/Domain/Audit/AuditLogEntry.cs` -- להוסיף `contact-delivery` ומפתח dedup, ופעולת `approve-delivery`
- `server/Infrastructure/ContactRequestRepository.cs` (~128-162, דפוס `SendToParticipant`) -- להוסיף אישור מסירה עם איפוס ידני של שורה `dead`
- `server/Features/Contacts/ContactRequestService.cs`, `server/Api/Endpoints/ContactRequestAdminEndpoints.cs`, `Api/Resources/Strings.cs` -- שיטת שירות ונקודת `POST /admin/contact-requests/{id}/approve-delivery` ו-`request-not-approved`
- `server/Features/Email/{EmailOutboxProcessor.cs,IEmailOutbox.cs,ContactDecisionEmail.cs}` -- ענף רביעי בעיבוד, קריאת נמען המסירה, `ContactDeliveryEmail`, ושימוש במפתח אידמפוטנטיות יציב
- `server/Infrastructure/EmailOutboxRepository.cs` (~106, ~144) -- שיטת סימון `sent` יחד עם `delivered` בטרנזקציה אחת, בנתיב ההצלחה בלבד
- `server/Infrastructure/ContactRequestQueueRepository.cs`, `server/Features/Contacts/ContactRequestQueueService.cs` -- להוסיף לסוף `DeliveryStatus` ו-`DeliveryErrorCategory` (בלי פרטים אישיים)
- `server/Tests/ContactDecisionTests.cs` -- עוזרים: `NewSetupAsync`, `SentAndMailedAsync`, `Decide`, `ProcessAsync`, `Sql`, `FakeEmailSender`
- `README.md` -- אין סעיפים ל-3.3 ו-3.4; להוסיף
- אין לשנות: `client/`, מסלול ההחלטה של 3.3

## Tasks & Acceptance

**Execution:**
- `server/Domain`, `server/Infrastructure/ContactRequestRepository.cs`, `server/Features/Contacts`, `server/Api/Endpoints` -- אישור מסירה כעדכון מותנה עם Outbox ויומן באותה טרנזקציה, כולל איפוס ידני של שורה `dead` -- AD-3, AD-14, AD-16
- `server/Features/Email`, `server/Infrastructure/EmailOutboxRepository.cs` -- ענף עיבוד `contact-delivery`: בדיקות חוזרות, שליחה לכתובת המאומתת, וסימון `sent` ו-`delivered` יחד -- AD-15, AD-16, AD-17
- `server/Infrastructure/ContactRequestQueue*`, `server/Features/Contacts/ContactRequestQueueService.cs` -- סטטוס המסירה וקטגוריית הכשל בתור -- NFR7
- `server/Tests/ContactDeliveryTests.cs`, `README.md` -- בדיקה לכל שורה במטריצה ותיעוד -- כיסוי

**Acceptance Criteria:**
- Given מסירה שנשלחה, when נבדקים שורת ה-Outbox, היומנים והתגובות, then אין בהם מייל או נייד של המשתתפת.
- Given סירוב או היעדר תשובה, when מנסים לאשר מסירה, then 409 ולא נשלח דבר.
- Given כל הבדיקות, when הן רצות, then כולן עוברות ואין בדיקה קיימת שנשברה.

## Spec Change Log

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: 17 findings (מקובצים לפי סיבת שורש מארבע השכבות) — high 0, medium 2, low 14, false 1, maybe-false 0
- findings:
  - `[medium]` `[patch]` INSERT ל-Outbox עם `ON CONFLICT DO NOTHING` שלא נוסף אינו מבטל את האישור, ו-`manager_approved_at` נשאר בלי שורת מסירה — נוספה בדיקת מספר שורות עם rollback ו-409
  - `[medium]` `[patch]` חסרות בדיקות: `manager_approved_at` ריק, מייל ריק, `participant_contact_version` ריק, כשל או איבוד claim בסימון `delivered`, 401 מול 403 מדויקים, כותרת `no-store` — נוספו בדיקות
  - `[low]` `[reject]` האישור אינו בודק מראש גיל הסכמה, פרופיל פעיל וגרסת פרטים (ושורה מתה נוצרת) — הכוונה מאפשרת עצירה עם סיבה גלויה, והבדיקה המוקדמת מוסיפה קודי תגובה חדשים
  - `[low]` `[reject]` אין נעילת `FOR SHARE` על הפרופיל באישור — העדכון המותנה על הסטטוס מגן, והסרה מעבירה ל-`expired`
  - `[low]` `[reject]` TOCTOU בין הבדיקה החוזרת לשליחה — מובנה בשליחת מייל, מתועד ב-README כ-at-least-once
  - `[low]` `[reject]` מפתח אידמפוטנטיות יציב עלול לדכא ניסיון ידני שהספק כבר קיבל — שורה `dead` לא נשלחה בפועל
  - `[low]` `[reject]` ניקוי ניקוי-תווים שמשאיר ערך ריק — הנתונים מאומתים בעת ההגשה
  - `[low]` `[reject]` אותה פעולת ביקורת `approve-delivery` לניסיון ידני — אותה שורת Outbox נשמרת, ושורות היומן נושאות זמן
  - `[low]` `[reject]` ניסיון ידני אחרי `consent-expired` או `no-recipient` ימות שוב — נדרש לטפל במנהלת, ולא מכיל נזק
  - `[low]` `[reject]` שתי תת-שאילתות בתור ללא `OrderBy` — מפתח ה-dedup ייחודי, ולכן שורה אחת לכל היותר
  - `[low]` `[reject]` קבוע ברירת מחדל `NotificationNone` משמש גם למסירה, ושמות הערכים בתיעוד — דבר קוסמטי
  - `[low]` `[reject]` נוסח "פעם אחת בלבד" במייל מול at-least-once — הנוסח מתייחס למסירה שאושרה, והכפילות נדירה ומתועדת
  - `[low]` `[reject]` 403 במקום 401 כשחסר NameIdentifier — מדיניות האדמין מבטיחה את ה-claim
  - `[low]` `[reject]` אין ממשק למנהלת לכפתור האישור ולמצבי המסירה — מחוץ להיקף: הלקוח לא נוגע
  - `[low]` `[reject]` בדיקות משתמשות ב-`DateTime.UtcNow` ואין בדיקת גבול 14 יום מדויק — קוסמטי
  - `[low]` `[reject]` סגירת שורה כשהבקשה כבר `delivered` אינה מתועדת ביומן — תקין, ללא נזק
  - `[false]` `[reject]` איפוס ידני אינו מנקה עמודות claim — איפוס מנקה את הלוס ומוני הניסיונות, ואין עמודות נוספות לבעלות

## Design Notes

אישור חוזר על `dead` הוא "ניסיון ידני": הוא משמר את `ManagerApprovedAt` ואת מפתח ה-dedup, ורק מחזיר את השורה ל-`pending` עם מונים מאופסים. כשל בבדיקות ברגע השליחה הוא סופי (`dead`) כי ניסיון חוזר לא ישנה את הנתונים; מנהלת שרוצה לנסות שוב אחרי תיקון (למשל אישור חוזר של משתתפת) משתמשת באישור חוזר.

## Verification

**Commands:**
- `dotnet build server` -- expected: בנייה מצליחה בלי אזהרות חדשות
- `dotnet test server` -- expected: כל הבדיקות עוברות (נדרש Docker, כ-11 דקות)

## Auto Run Result

Status: done

- **שינוי שמומש (שרת בלבד):** `POST /admin/contact-requests/{id}/approve-delivery` (עדכון מותנה, שורת Outbox `contact-delivery`, יומן ביקורת, ושליחה ידנית חוזרת של שורה `dead`), ענף עיבוד רביעי בתהליך הרקע שבודק מחדש (פרופיל פעיל, הסכמה עד 14 יום, גרסת פרטים, נמענים), שולח רק ל-`VisitorEmail` המאומת ומסמן `sent` ו-`delivered` באותה טרנזקציה, ושדות `deliveryStatus` ו-`deliveryErrorCategory` בתור.
- **ממצאי סקירה:** 17 ממצאים מקובצים. תוקנו 2 בדרגת medium: בדיקת מספר שורות ב-INSERT של ה-Outbox באישור, ובדיקות חסרות (תנאי הבדיקה החוזרת, כשל ואיבוד claim בסימון, 401 מול 403, `no-store`). נדחו 14 ממצאי low ו-false אחד, עם נימוק בלוג. לא נדחה דבר לרשימת `deferred`.
- **המלצה לסקירת המשך:** כן. סיכון לא מאומת: מסירה היא at-least-once (קריסה בין השליחה לסימון עלולה לשלוח פעמיים כשהספק אינו מכבד מפתח אידמפוטנטיות), ויש חלון TOCTOU קצר בין הבדיקה החוזרת לשליחה.
- **אימות:** `dotnet build server` הצליח. `dotnet test server`: 794 עברו, 0 נכשלו (10 דקות 15 שניות). ריצה קודמת של הסוכן קרסה בבדיקת TOTP לא קשורה, ולא חזרה בריצה המלאה.
- **סיכונים נותרים:** אין ממשק למנהלת לכפתור האישור ולמצבי המסירה (סיפור 5.9). ניסיון ידני אחרי `consent-expired` או `no-recipient` ימות שוב.
