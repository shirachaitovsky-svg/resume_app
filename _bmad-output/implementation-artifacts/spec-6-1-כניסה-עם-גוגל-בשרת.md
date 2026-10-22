---
title: 'סיפור 6.1: כניסה עם גוגל בשרת'
type: 'feature'
created: '2026-10-22'
status: 'done'
baseline_revision: '1862022548dc881ab2e385f2c173408c24f3d1c4'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-6-context.md'
warnings: [oversized]
deferred: []
---

<intent-contract>

## Intent

**Problem:** משתתפת יכולה להיכנס היום רק בקישור אישי במייל. האפיון החדש קובע כניסה עם חשבון גוגל, אבל בשרת אין OIDC, אין זהות לפי `sub` ואין סשן משתתפת מבוסס גוגל, ובלי זה לא אפשר לבנות את "הפרופיל שלי".

**Approach:** שרת בלבד. זרימת authorization-code עם PKCE, `state` ו-`nonce` מאחורי ממשק `IOidcProvider` (מימוש גוגל אמיתי לפי discovery ו-JWKS, ומימוש מדומה בפיתוח), עוגיית סשן משתתפת, ונקודת `GET /auth/google/me`. החלפה לגוגל אמיתי היא תצורה בלבד.

## Boundaries & Constraints

**Always:**
- נקודות: `GET /auth/google/start?returnUrl=`, `GET /auth/google/callback`, `GET /auth/google/me`, `POST /auth/google/logout`. כולן ללא סיסמה, והשתיים הראשונות מחזירות הפניות (302).
- `start`: מחולל `state`, `nonce` ו-PKCE verifier (S256), שומר אותם בעוגיית `__Host-resume-oidc` (מוגנת Data Protection, `HttpOnly`, `Secure`, `SameSite=Lax`, קצרה, נמחקת בקולבק), ומפנה ל-authorize. `returnUrl` מותר רק כנתיב יחסי שמתחיל ב-`/` ואינו `//` או מכיל סכמה, אחרת ברירת מחדל `/me`.
- `callback`: דוחה בלי `state` תואם, שגיאת ספק (`error=access_denied` נחשבת "בוטל"), קוד חסר, החלפת קוד שנכשלה או `id_token` לא תקף. מאמת: חתימה מול JWKS, `iss`, `aud`, תוקף, `nonce`. מקבל רק `email_verified = true` ו-`email` לא ריק. כל כשל מפנה ל-`{PublicBaseUrl}/me?signin=cancelled|failed|unverified` בלי סשן.
- הצלחה: זהות לפי `sub`. נמצא `Profile` עם `GoogleSub` שווה (עמודה חדשה, ייחודית, nullable, מיגרציה) או שלא. מנפיקים עוגיית סשן משתתפת (`__Host-resume-participant`, `Strict`, 60 דקות, ללא חידוש) עם claims `google_sub`, `email`, `purpose=google-signin`, ואם קיים פרופיל גם `profile_id`. מפנים ל-`returnUrl`.
- מדיניות `Participant` הקיימת מקבלת `purpose` בעריכה (`edit-profile`) או `google-signin`, כך שנקודות `/me/*` הקיימות עובדות גם לסשן גוגל. סשן בלי `profile_id` אינו עובר אותה (403). ניגשים לפרופיל של עצמם בלבד.
- בכל כניסה של פרופיל מקושר: אם המייל מגוגל שונה מ-`ParticipantPrivate.Email`, מעדכנים אותו ומעלים `ContactVersion` ב-1 באותה טרנזקציה. אם העדכון מפר ייחודיות `(CohortId, Email)`, הכניסה נדחית (`signin=failed`), בלי שינוי.
- `me`: 200 עם `{email, hasProfile, profileId?}` למחוברת, 401 אחרת. `no-store`. `logout`: POST עם anti-forgery ובדיקת Origin, מוחק את העוגייה.
- תצורה ממשתני סביבה: `GOOGLE_PROVIDER` (`google` או `fake`), `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, `GOOGLE_AUTHORITY` (ברירת מחדל `https://accounts.google.com`). `redirect_uri` נבנה מ-`PUBLIC_BASE_URL` + `/api/auth/google/callback`. מחוץ ל-Development חובה `google` עם מזהה וסוד, אחרת השרת נכשל באתחול. `fake` מותר רק ב-Development.
- המימוש המדומה (`fake`): ללא רשת. נקודות `GET /dev-idp/authorize` מחזירות ישירות ל-callback עם קוד, ומאפשרות לבחור זהות (`fake_email`), `fake_verified=false` ו-`fake_error=access_denied`. `sub` נגזר דטרמיניסטית מהמייל. ברירת מחדל `participant@example.test`.
- הגבלת קצב לפי IP ל-`start` ול-`callback` (סקופ חדש, משתנה סביבה `RATE_LIMIT_GOOGLE_AUTH_IP`, ברירת מחדל 30).
- אין `id_token`, `code`, `state` או מייל ביומנים.

**Never:**
- לא נוגעים בלקוח ולא מסירים את הקישור האישי הישן (סיפור 6.4). לא יוצרים פרופיל בסיפור זה (סיפור 6.5).
- לא מקשרים `GoogleSub` לפרופיל קיים לפי מייל בלבד.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| התחלה | `GET /auth/google/start` | 302 ל-authorize עם `state`, `nonce`, `code_challenge`, `redirect_uri`, ועוגיית oidc | `returnUrl` לא יחסי: ברירת מחדל `/me` |
| כניסה בלי פרופיל | callback תקין, אין `Profile` עם ה-sub | עוגיית סשן בלי `profile_id`, 302 ל-`returnUrl`, `me` מחזיר `hasProfile:false` | N/A |
| כניסה עם פרופיל | קיים `Profile` עם ה-sub | עוגייה עם `profile_id`, `me` מחזיר `hasProfile:true`, נקודת `/me/*` קיימת מחזירה את הפרופיל שלה | פרופיל של אחרת: 403 |
| מייל השתנה | מייל מגוגל שונה מהמייל השמור | המייל מתעדכן ו-`ContactVersion` +1 | התנגשות ייחודיות: `signin=failed`, בלי שינוי |
| בוטל | `error=access_denied` | 302 ל-`/me?signin=cancelled`, בלי סשן | N/A |
| מייל לא מאומת | `email_verified=false` | 302 ל-`/me?signin=unverified` | N/A |
| state או nonce שגוי | callback מזויף או חוזר | 302 ל-`signin=failed`, בלי סשן | העוגייה נמחקת |
| טוקן לא תקף | חתימה, `iss`, `aud` או תוקף שגויים | `signin=failed` | N/A |
| יציאה | `POST /auth/google/logout` עם anti-forgery | העוגייה נמחקת, `me` מחזיר 401 | בלי anti-forgery: 403 |

</intent-contract>

## Code Map

- `server/Api/Security/IdentityServiceExtensions.cs` (~62-79) -- סכמת `Participant`, מדיניות `Participant` (דורשת `purpose == edit-profile` ו-`profile_id`); להרחיב ל-`google-signin`
- `server/Api/Endpoints/ParticipantEndpoints.cs` (~37-80, 99, 144) -- בניית ה-principal והכניסה (`SignInAsync`) לחילוץ עוזר משותף, ו-`/me/*`
- `server/Domain/Profiles/Profile.cs`, `server/Infrastructure/AppDbContext.cs` (~57-68, 124-139) -- `GoogleSub` nullable ייחודי ב-`Profile`, מיגרציה (`dotnet tool restore`, `dotnet ef migrations add GoogleSub -p server/Infrastructure -s server/Api`)
- `server/Infrastructure/SubmissionReviewRepository.cs` (~254-264) -- דפוס העלאת `ContactVersion` וטיפול בהתנגשות מייל להעתקה
- `server/Api/Security/RequestIntegrityMiddleware.cs` (~20-24, 55-56) -- GET פטורים; להוסיף את `/auth/google/logout` ל-anti-forgery אנונימי אם נדרש
- `server/Infrastructure/{EmailOptions.cs,DependencyInjection.cs}`, `server/Api/Program.cs` -- דפוס אפשרויות מ-env ו-`PUBLIC_BASE_URL`; להוסיף `GoogleAuthOptions.Resolve`, ממשק `IOidcProvider`, `GoogleOidcProvider` (HttpClient מוקלד, discovery ו-JWKS עם מטמון, חבילת `Microsoft.IdentityModel.JsonWebTokens` ל-`JsonWebTokenHandler`) ו-`FakeOidcProvider`
- `server/Features/PublicForms/IRateLimiter.cs`, `PublicFormGuard` -- סקופ הגבלת קצב חדש
- `server/Tests/AuthTestSupport.cs` (~57-87, 158-164, 326-361) -- רישום env חדשים ב-`EnvSnapshot.Names` ו-`HighLimits`; דפוס `FakeEmailSender` להחלפת ספק; `ApiClient` עם `AllowAutoRedirect=false`
- `README.md` (~10-45) -- טבלת משתני סביבה; להוסיף את החדשים והנקודות
- אין לשנות: `client/`, `POST /profiles/session` והקישור האישי הקיים, הגשת פרופיל

## Tasks & Acceptance

**Execution:**
- `server/Domain`, `server/Infrastructure/{AppDbContext.cs,Migrations}` -- `GoogleSub` ייחודי nullable ומיגרציה -- AD-18
- `server/Infrastructure/GoogleAuthOptions*`, `Features/Auth/IOidcProvider*`, `Infrastructure/{GoogleOidcProvider,FakeOidcProvider}.cs` -- אפשרויות עם כשל אתחול מחוץ ל-Development, וספקים -- AD-5, AD-9
- `server/Api/Endpoints/GoogleAuthEndpoints.cs`, `Api/Security/IdentityServiceExtensions.cs`, `ParticipantEndpoints.cs` -- ארבע הנקודות, עוגיית oidc, סשן והרחבת מדיניות `Participant` -- AD-5
- `server/Infrastructure` -- איתור פרופיל לפי `GoogleSub` ועדכון מייל עם `ContactVersion` -- AD-17, AR17
- `server/Tests/GoogleSignInTests.cs`, `GoogleOidcProviderTests.cs`, `README.md` -- בדיקה לכל שורה במטריצה, ובדיקות יחידה ל-`GoogleOidcProvider` עם `HttpMessageHandler` מדומה (discovery, JWKS ממפתח RSA שנוצר, החלפת קוד) לחתימה, `iss`, `aud`, תוקף ו-`nonce` שגויים, ותיעוד -- כיסוי

**Acceptance Criteria:**
- Given כל תגובה או יומן בזרימה, when נבדקים, then אין בהם `id_token`, `code` או `state`.
- Given פרופיל שאינו של המשתתפת המחוברת, when היא פונה אליו ב-`/me/*`, then 403.
- Given כל הבדיקות, when הן רצות, then כולן עוברות ואין בדיקה קיימת שנשברה.

## Spec Change Log

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: 21 findings (מקובצים לפי סיבת שורש מארבע השכבות) — high 0, medium 5, low 14, false 2, maybe-false 0
- findings:
  - `[medium]` `[patch]` חסימת הגבלת קצב ב-`start` ו-`callback` מחזירה JSON גולמי לניווט דפדפן, וחריגה מ-`ExchangeCodeAsync` נותנת 500 — הפניה ל-`/me?signin=failed` ו-try/catch
  - `[medium]` `[patch]` סקופ משותף בגודל 30 לשעה מגביל כ-15 כניסות לשעה ל-IP, וכיתה מאחורי NAT אחד נחסמת — ברירת מחדל 200
  - `[medium]` `[patch]` `GET /auth/google/me` קורא `hasProfile`/`profileId` מהעוגייה בלבד ולכן פרופיל שנוצר אחרי הכניסה (6.5) לא מופיע — קריאה מ-DB לפי `google_sub`
  - `[medium]` `[patch]` שתי קריאות callback מקבילות מעלות `ContactVersion` פעמיים — עדכון מותנה והעלאה רק כששורה עודכנה
  - `[medium]` `[patch]` חסרות בדיקות: סשן קישור אישי מול `/auth/google/me`, שרת במצב `google` (סוג הספק ו-404 ל-`/dev-idp`), החלפת `kid` ו-throttle, ושם בדיקה מטעה — נוספו
  - `[low]` `[patch]` מסמך discovery עם `issuer` שונה מה-authority מתקבל — נוספה דחייה ובדיקה
  - `[low]` `[patch]` README ללא שלבי הקמת גוגל, ובמספר קבצים נוסף BOM בשורה הראשונה — תועד והוסר
  - `[low]` `[reject]` פתיחת `start` בלשונית שנייה דורסת את עוגיית הזרימה — נדיר, התיקון מסבך את העוגייה
  - `[low]` `[reject]` callback מזויף מוחק את עוגיית הזרימה (DoS חוצה-אתר) — מוגבל בקצב, ללא חשיפת מידע
  - `[low]` `[reject]` פרופיל מוסר עדיין נכנס ומקבל גישה ל-`/me/*` עד 60 דקות — הנתונים שלו עצמו, ו-AD-13 אינו מכסה סשן גוגל
  - `[low]` `[reject]` החלפת מפתחות מסתיימת ב-throttle של דקה, כשלי discovery לא נשמרים במטמון, ושרשור נעילה — נדיר ומתועד בהערות
  - `[low]` `[reject]` טוקן ללא `azp` או `iat`, גוף תגובה ללא הגבלת גודל, ו-`CancellationToken` ב-`ValidateTokenAsync` — קשיחות מעבר להיקף
  - `[low]` `[reject]` `returnUrl` לנתיב `/api/` או `/auth/`, ובקשת `dev-idp` עם שדות null בפיתוח — רק בפיתוח או בלולאה נדירה
  - `[low]` `[reject]` יציאה מוחקת עוגייה ואינה מבטלת סשן, ואינה מיידעת את גוגל — מגבלה מובנית בעוגייה חתומה של 60 דקות
  - `[low]` `[reject]` seam של Serilog ב-`Program.cs` ו-override ב-appsettings Development כפול — נדרש לבדיקת היומנים
  - `[low]` `[reject]` שינוי מייל נכתב בשקט בלי התראה או ביקורת, וכל כשל נראה כ-`signin=failed` — מחוץ להיקף, ההודעות נקבעות ב-6.2
  - `[low]` `[reject]` אין בדיקות ל-HS256 או `alg=none`, תפוגת TTL, `sub` ארוך, `email` חסר, יומני הספק האמיתי, ומרוץ unique-violation — הספרייה דוחה אלגוריתמים לא צפויים, ובדיקות משלימות ללא סיכון
  - `[low]` `[reject]` אין קישור פרופיל ל-`GoogleSub` בשום נתיב — לפי התכנון: סיפור 6.5
  - `[low]` `[reject]` `Fake` ללא קשר ל-Development בזמן מיפוי — האפשרויות נכשלות באתחול מחוץ ל-Development
  - `[false]` `[reject]` "ParticipantEndpoints אינו בדיף ולכן `/me/*` לא נבדק" — המדיניות היא ההגנה, והבדיקות עוברות דרכה
  - `[false]` `[reject]` "אין בדיקה שהפעלת Google אמיתי עוברת דרך נקודות הקצה" — נבדק ברמת הספק מול authority מדומה, כפי שנקבע בספק

## Design Notes

אין דרך ליצור פרופיל בסיפור הזה: `GoogleSub` נקבע רק בהגשה מחוברת (סיפור 6.5), ולכן בסיפור זה פרופיל מקושר נוצר רק ב-SQL או ב-seed של בדיקות. ה-fake אינו חותם JWT; בדיקת האימות של טוקנים נעשית על `GoogleOidcProvider` מול handler מדומה עם מפתח RSA שנוצר בבדיקה. עוגיית ה-oidc היא `Lax` כי החזרה מגוגל היא ניווט חוצה-אתר, והסשן עצמו נשאר `Strict`.

## Verification

**Commands:**
- `dotnet build server` -- expected: בנייה מצליחה בלי אזהרות חדשות
- `dotnet test server` -- expected: כל הבדיקות עוברות (נדרש Docker, כ-11 דקות)

## Auto Run Result

Status: done

- **שינוי שמומש (שרת בלבד):** כניסה עם גוגל לפי authorization-code עם PKCE, `state` ו-`nonce` מאחורי `IOidcProvider` (`GoogleOidcProvider` עם discovery ו-JWKS ואימות `id_token`, ו-`FakeOidcProvider` לפיתוח), נקודות `start`, `callback`, `me`, `logout` ו-`/dev-idp/authorize` (פיתוח בלבד), עמודת `Profile.GoogleSub` ייחודית (מיגרציה), הרחבת מדיניות `Participant` ל-`google-signin`, עדכון מייל והעלאת `ContactVersion` בכניסה, והגבלת קצב. נוסף סיפור 6.5 (יצירת פרופיל בשרת) כי אף סיפור לא ממש את שינוי 1.6.
- **ממצאי סקירה:** 21 ממצאים מקובצים. תוקנו 5 בדרגת medium ועוד 2 low: הפניה ל-`signin=failed` בחסימת קצב ובכשל החלפה, ברירת מחדל 200 להגבלת הקצב, `me` קורא מה-DB לפי `google_sub`, עדכון מייל מותנה, בדיקת issuer ב-discovery, בדיקות חסרות, README והסרת BOM. נדחו 14 ממצאי low ו-2 false, עם נימוק בלוג.
- **המלצה לסקירת המשך:** כן. סיכון לא מאומת: הכניסה נבדקה רק מול authority מדומה, לא מול גוגל אמיתי, והזרימה דרך ה-rewrite של Vercel (ספייק callback ועוגיות) עדיין פתוחה.
- **אימות:** `dotnet build server` הצליח. `dotnet test server`: 835 עברו, 0 נכשלו (10 דקות 50 שניות). בריצה קודמת נכשלה בדיקה אחת לא קשורה ועברה לבד, ובריצה אחרת נעצרה ריצה בגלל תהליך בדיקות שהוסר במקביל; בריצה נקייה הכול עבר.
- **סיכונים נותרים:** בבדיקת המארח במצב `google`, נתיב לא ממופה מחזיר 401 (מדיניות ברירת המחדל) ולא 404. אין דרך ליצור פרופיל מקושר בשום קוד (סיפור 6.5). הכניסה לא בודקת הסרת פרופיל.
