-- Dev-only sample data for the public gallery (story 5.7). Idempotent: removes the rows with the fixed
-- seed ids below and recreates them. Touches nothing else. Run via scripts/dev-seed.ps1.
-- Ids are fixed GUID v4 values (version nibble 4, variant 8). Table/column names: server/Infrastructure/Migrations.
BEGIN;

CREATE TEMP TABLE seed_cohorts (id uuid, name text, token text) ON COMMIT DROP;
INSERT INTO seed_cohorts VALUES
  ('5eed00c1-0000-4000-8000-000000000001', 'מחזור א׳', rpad('seed-cohort-a-', 43, 'x')),
  ('5eed00c1-0000-4000-8000-000000000002', 'מחזור ב׳', rpad('seed-cohort-b-', 43, 'x'));

CREATE TEMP TABLE seed_profiles (
  n int, cohort uuid, visibility text, rev_status text, name text, product text, url text, descr text,
  avatar text, role text, workplace text, a1 text, a2 text, a3 text, intro text, cv text
) ON COMMIT DROP;

INSERT INTO seed_profiles VALUES
(1, '5eed00c1-0000-4000-8000-000000000001', 'Active', 'Approved', 'נועה כהן', 'מתכנן ארוחות חכם', 'https://example.com/meal-planner',
 'עוזר מבוסס בינה מלאכותית שמתכנן תפריט שבועי לפי מה שיש במקרר.', 'girl-1', 'אנליסטית נתונים', 'הלשכה המרכזית לסטטיסטיקה',
 'עבדתי שמונה שנים בניתוח נתונים ובבניית דוחות לקובעי מדיניות.', 'המוצר עוזר למשפחות לצמצם בזבוז מזון ולתכנן קניות בקלות.', 'למדתי לכתוב הנחיות מדויקות ולבנות סוכנים, ואני כבר משתמשת בזה בעבודה.',
 'אני אוהבת לפתור בעיות יומיומיות בעזרת נתונים. בקורס גיליתי כמה אפשר לבנות במהירות.',
 E'# קורות חיים\n\n## ניסיון\n\n- **אנליסטית נתונים** בלשכה המרכזית לסטטיסטיקה, 2018 עד היום\n- מפתחת דוחות ולוחות מחוונים\n\n## השכלה\n\nתואר ראשון בסטטיסטיקה, *האוניברסיטה העברית*.\n\nעוד על העבודה שלי: [האתר שלי](https://example.com/noa)'),
(2, '5eed00c1-0000-4000-8000-000000000001', 'Active', 'Approved', 'דניאל לוי', 'סיכום פגישות בקול', 'https://example.com/meeting-notes',
 'מתמלל פגישות ומפיק מהן סיכום ומשימות לביצוע בלחיצה אחת.', 'boy-1', 'מנהל מוצר', 'סטארט-אפ בתל אביב',
 'מנהל מוצר עם רקע בהנדסת תוכנה.', NULL, 'למדתי לבנות אבות טיפוס מהר ולבדוק אותם מול משתמשים.',
 NULL,
 E'## ניסיון\n\n1. מנהל מוצר, 2020 עד היום\n2. מפתח תוכנה, 2014 עד 2020\n\n## כישורים\n\n- ניהול מוצר\n- ראיונות משתמשים\n- **Python** ו-*TypeScript*'),
(3, '5eed00c1-0000-4000-8000-000000000001', 'Active', 'Approved', 'מיכל אברהם', 'מורה פרטי לעברית', 'https://example.com/hebrew-tutor',
 'מלווה לומדים חדשים בתרגול שיחה בעברית, עם משוב מיידי על ניסוח והגייה, ובלי לחץ.', 'girl-2', 'מורה לעברית', 'בית ספר תיכון בחיפה',
 NULL, 'הכלי עוזר לעולים חדשים לתרגל עברית מדוברת בכל שעה ביום.', NULL,
 'אחרי עשרים שנה בהוראה רציתי להביא כלים חדשים לכיתה.',
 NULL),
(4, '5eed00c1-0000-4000-8000-000000000001', 'Active', 'Approved', 'יונתן שרון', 'בודק קוד ידידותי', 'https://example.com/code-buddy',
 'סורק בקשות מיזוג ומציע שיפורים בשפה פשוטה.', 'boy-2', NULL, NULL,
 NULL, NULL, NULL, NULL,
 E'# יונתן שרון\n\n### מפתח Full Stack\n\nשלוש שנים בפיתוח, מתעניין בכלי מפתחים.\n\n- C# ו-.NET\n- Angular'),
(5, '5eed00c1-0000-4000-8000-000000000002', 'Active', 'Approved', 'שירה מזרחי', 'מפת טיולים אישית', 'https://example.com/trip-map',
 'בונה מסלולי טיול יומיים לפי תחומי עניין, תקציב וכמות הזמן הפנוי.', 'girl-3', 'מעצבת חוויית משתמש', 'משרד עיצוב בירושלים',
 'מעצבת עם עשר שנות ניסיון בממשקים.', 'מי שרוצה לטייל ולא רוצה לבזבז שעות על תכנון.', 'למדתי איך משלבים בינה מלאכותית בתהליך העיצוב.',
 'אני מאמינה שטכנולוגיה צריכה להרגיש אנושית.',
 E'## ניסיון\n\nמעצבת UX בכירה, ניהול צוות של ארבעה מעצבים.\n\n## תיק עבודות\n\n[תיק עבודות](https://example.com/shira)\n\nשורה ראשונה  \nשורה שנייה'),
(6, '5eed00c1-0000-4000-8000-000000000002', 'Active', 'Approved', 'איתי גולן', 'עוזר למתן משכנתאות', 'https://example.com/mortgage-helper',
 'מסביר בשפה פשוטה את התנאים של הצעות משכנתא ומשווה ביניהן.', 'boy-3', 'יועץ פיננסי', 'בנק בתל אביב',
 'יועץ פיננסי בבנק, עוסק במשכנתאות.', 'זוגות צעירים שמתבלבלים בין ההצעות שהם מקבלים.', NULL,
 'חשוב לי שאנשים יבינו את ההחלטות הכלכליות הגדולות שלהם.',
 E'## ניסיון\n\n- יועץ משכנתאות, 2016 עד היום\n- אנליסט אשראי, 2012 עד 2016\n\n**הסמכות:** יועץ משכנתאות מוסמך'),
(7, '5eed00c1-0000-4000-8000-000000000002', 'Active', 'Approved', 'רוני פרידמן', 'יומן למידה', 'https://example.com/learning-journal',
 'מסכם כל יום למידה ומציע חזרה מרווחת על החומר.', 'default', 'סטודנטית', 'הטכניון',
 NULL, NULL, 'למדתי שמה שחשוב הוא לשאול את השאלה הנכונה.',
 'סטודנטית להנדסה ומתעניינת בלמידה.',
 NULL),
(8, '5eed00c1-0000-4000-8000-000000000002', 'Active', 'Approved', 'תמר בן דוד', 'מנהל משימות קולי', 'https://example.com/voice-tasks',
 'מוסיף משימות ותזכורות בעזרת פקודות קוליות בעברית, גם כשהידיים עסוקות במטבח.', 'girl-1', 'רכזת פרויקטים', 'עמותה בבאר שבע',
 'מרכזת פרויקטים בעמותה חברתית.', 'אנשים עם ריבוי משימות שרוצים לתעד אותן בלי להקליד.', 'למדתי לחבר בין כלי דיבור לטקסט ולמשימות.',
 'אני מחפשת דרכים לחסוך זמן לצוותים קטנים.',
 E'## ניסיון\n\nרכזת פרויקטים, ניהול מתנדבים ותקציבים.\n\n1. תכנון\n2. ביצוע\n3. הערכה'),
(9, '5eed00c1-0000-4000-8000-000000000001', 'Removed', 'Approved', 'פרופיל שהוסר', 'מוצר שהוסר', 'https://example.com/removed',
 'פרופיל שהוסר ואינו אמור להופיע בגלריה.', 'default', NULL, NULL,
 NULL, NULL, NULL, NULL, NULL),
(10, '5eed00c1-0000-4000-8000-000000000002', 'Active', 'Pending', 'פרופיל ממתין', 'מוצר ממתין', 'https://example.com/pending',
 'הגשה שממתינה לאישור ואינה אמורה להופיע בגלריה.', 'default', NULL, NULL,
 NULL, NULL, NULL, NULL, NULL);

-- cleanup of earlier seed runs (only rows with the fixed seed ids)
DELETE FROM participant_private WHERE profile_id IN (SELECT ('5eed0001-0000-4000-8000-' || lpad(n::text, 12, '0'))::uuid FROM seed_profiles);
UPDATE profiles SET approved_revision_id = NULL WHERE id IN (SELECT ('5eed0001-0000-4000-8000-' || lpad(n::text, 12, '0'))::uuid FROM seed_profiles);
DELETE FROM profile_revisions WHERE profile_id IN (SELECT ('5eed0001-0000-4000-8000-' || lpad(n::text, 12, '0'))::uuid FROM seed_profiles);
DELETE FROM profiles WHERE id IN (SELECT ('5eed0001-0000-4000-8000-' || lpad(n::text, 12, '0'))::uuid FROM seed_profiles);
DELETE FROM cohorts WHERE id IN (SELECT id FROM seed_cohorts);

INSERT INTO cohorts (id, name, invite_token, created_at, revoked_at)
SELECT id, name, token, now(), NULL FROM seed_cohorts;

INSERT INTO profiles (id, cohort_id, visibility, approved_revision_id, created_at)
SELECT ('5eed0001-0000-4000-8000-' || lpad(n::text, 12, '0'))::uuid, cohort, visibility, NULL, now() - make_interval(days => 30 - n)
FROM seed_profiles;

INSERT INTO profile_revisions (id, profile_id, status, name, product_name, product_url, submitted_at, avatar_id, description,
  intro_answer_1, intro_answer_2, intro_answer_3, intro_text, role, workplace, cv)
SELECT ('5eed1001-0000-4000-8000-' || lpad(n::text, 12, '0'))::uuid, ('5eed0001-0000-4000-8000-' || lpad(n::text, 12, '0'))::uuid,
  rev_status, name, product, url, now() - make_interval(days => 30 - n), avatar, descr, a1, a2, a3, intro, role, workplace, cv
FROM seed_profiles;

UPDATE profiles p SET approved_revision_id = ('5eed1001-0000-4000-8000-' || lpad(s.n::text, 12, '0'))::uuid
FROM seed_profiles s
WHERE p.id = ('5eed0001-0000-4000-8000-' || lpad(s.n::text, 12, '0'))::uuid AND s.rev_status = 'Approved';

INSERT INTO participant_private (profile_id, cohort_id, email, phone, contact_version)
SELECT ('5eed0001-0000-4000-8000-' || lpad(n::text, 12, '0'))::uuid, cohort, 'seed' || n || '@example.test', '050000' || lpad(n::text, 4, '0'), 1
FROM seed_profiles;

COMMIT;

SELECT 'cohorts' AS seeded, count(*) FROM cohorts WHERE id IN ('5eed00c1-0000-4000-8000-000000000001', '5eed00c1-0000-4000-8000-000000000002')
UNION ALL SELECT 'profiles (all)', count(*) FROM profiles WHERE id::text LIKE '5eed0001-%'
UNION ALL SELECT 'profiles (public: Active + Approved)', count(*) FROM profiles p
  JOIN profile_revisions r ON r.id = p.approved_revision_id AND r.profile_id = p.id AND r.status = 'Approved'
  WHERE p.id::text LIKE '5eed0001-%' AND p.visibility = 'Active';
