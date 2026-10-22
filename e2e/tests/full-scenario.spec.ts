import { BrowserContext, Page, expect, test } from '@playwright/test';
import { ADMIN_INITIAL_PASSWORD, ADMIN_NEW_PASSWORD, ADMIN_USERNAME, BASE_URL, PARTICIPANT_EMAIL, VISITOR_EMAIL } from '../helpers/env';
import { linkFrom, waitForMail } from '../helpers/mailpit';
import { totp } from '../helpers/totp';

// The Hebrew strings below are the product's own (client/src/app/shared/strings.he.ts); the scenario drives the real UI.
const COHORT_NAME = 'מחזור בדיקה';
const PARTICIPANT_PHONE = '050-123-4567';
const PARTICIPANT_PHONE_DIGITS = '0501234567';
const PARTICIPANT_NAME = 'דנה בדיקה';
const PRODUCT_NAME = 'אפליקציית בדיקה';
const VISITOR_NAME = 'מבקר בדיקה';
const RETURN_NOTE = 'נא להוסיף תיאור קצר יותר';

let adminContext: BrowserContext;
let participantContext: BrowserContext;
let visitorContext: BrowserContext;
let admin: Page;
let participant: Page;
let visitor: Page;
let profileId = '';

/** The participant signs in with the fake Google provider: the e2e adds `fake_email` to the identity provider's authorize call. */
async function signInAsParticipant(page: Page): Promise<void> {
  await page.route('**/api/auth/google/start*', async (route) => {
    const response = await route.fetch({ maxRedirects: 0 });
    const location = response.headers()['location'];
    if (!location) return route.fulfill({ response });
    const target = new URL(location, BASE_URL);
    target.searchParams.set('fake_email', PARTICIPANT_EMAIL);
    return route.fulfill({ response, headers: { ...response.headers(), location: target.toString() } });
  });
  await page.goto('/me');
  await page.getByRole('link', { name: 'כניסה עם גוגל' }).click();
  await expect(page.getByText(PARTICIPANT_EMAIL).first()).toBeVisible();
}

async function confirmInDialog(page: Page, buttonName: string): Promise<void> {
  const dialog = page.locator('dialog[open]');
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: buttonName, exact: true }).click();
}

test.describe.serial('full scenario', () => {
  test.beforeAll(async ({ browser }) => {
    adminContext = await browser.newContext({ baseURL: BASE_URL });
    participantContext = await browser.newContext({ baseURL: BASE_URL });
    visitorContext = await browser.newContext({ baseURL: BASE_URL });
    [admin, participant, visitor] = await Promise.all([adminContext.newPage(), participantContext.newPage(), visitorContext.newPage()]);
  });

  test.afterAll(async () => {
    await Promise.all([adminContext?.close(), participantContext?.close(), visitorContext?.close()]);
  });

  test('admin: first sign-in, password change, TOTP enrollment and a new cohort', async () => {
    await admin.goto('/admin');
    await expect(admin).toHaveURL(/\/admin\/login$/);
    await admin.locator('[data-field="username"] input').fill(ADMIN_USERNAME);
    await admin.locator('[data-field="password"] input').fill(ADMIN_INITIAL_PASSWORD);
    await admin.getByRole('button', { name: 'כניסה', exact: true }).click();

    await expect(admin.getByRole('heading', { level: 1, name: 'החלפת סיסמה' })).toBeVisible();
    await admin.locator('[data-field="currentPassword"] input').fill(ADMIN_INITIAL_PASSWORD);
    await admin.locator('[data-field="newPassword"] input').fill(ADMIN_NEW_PASSWORD);
    await admin.locator('[data-field="confirmPassword"] input').fill(ADMIN_NEW_PASSWORD);
    await admin.getByRole('button', { name: 'החלפת סיסמה', exact: true }).click();

    await expect(admin.getByRole('heading', { level: 1, name: 'הפעלת אימות דו-שלבי' })).toBeVisible();
    const key = (await admin.locator('.key').innerText()).replace(/\s/g, '');
    expect(key).toMatch(/^[A-Z2-7]{16,}$/);
    // The code is computed from the key shown on the page. If the 30 second step rolls over mid-request the code is retried for the
    // current time and the neighbouring steps, but only while the enrollment step is still on screen.
    const codeInput = admin.locator('[data-field="code"] input');
    const recovery = admin.getByRole('heading', { level: 1, name: 'קודי שחזור' });
    for (const offset of [0, -30_000, 30_000]) {
      if (await recovery.isVisible()) break;
      if (!(await codeInput.isVisible())) break;
      await codeInput.fill(totp(key, Date.now() + offset));
      await admin.getByRole('button', { name: 'הפעלה', exact: true }).click();
      if (await recovery.waitFor({ timeout: 5000 }).then(() => true, () => false)) break;
    }
    await expect(admin.getByRole('heading', { level: 1, name: 'קודי שחזור' })).toBeVisible();
    await expect(admin.locator('.codes li')).toHaveCount(10);
    await admin.getByLabel('שמרתי את הקודים במקום בטוח').check();
    await admin.getByRole('button', { name: 'המשך', exact: true }).click();
    await expect(admin.getByRole('heading', { level: 1, name: 'ניהול' })).toBeVisible();

    await admin.getByRole('link', { name: /^מחזורים/ }).click();
    await admin.locator('[data-field="name"] input').fill(COHORT_NAME);
    await admin.getByRole('button', { name: 'יצירת מחזור', exact: true }).click();
    await expect(admin.getByText(`המחזור "${COHORT_NAME}" נוצר`)).toBeVisible();
    await expect(admin.getByRole('heading', { level: 3, name: COHORT_NAME })).toBeVisible();
  });

  test('participant: Google sign-in (fake) and profile creation in the chosen cohort', async () => {
    await signInAsParticipant(participant);
    await participant.locator('[data-field="cohortId"] select').selectOption({ label: COHORT_NAME });
    await participant.locator('[data-field="name"] input').fill(PARTICIPANT_NAME);
    await participant.locator('[data-field="productName"] input').fill(PRODUCT_NAME);
    await participant.locator('[data-field="productUrl"] input').fill('https://example.com/e2e-app');
    await participant.locator('[data-field="phone"] input').fill(PARTICIPANT_PHONE);
    await participant.getByRole('button', { name: 'שליחת הפרופיל', exact: true }).click();

    await expect(participant.getByRole('heading', { name: 'הפרופיל הוגש וממתין לאישור המנהלת' })).toBeVisible();
    await expect(participant.getByText('השינויים ממתינים לאישור')).toBeVisible();
    // /me switched to the edit form: the cohort picker is gone and the stored values are there.
    await expect(participant.locator('[data-field="cohortId"]')).toHaveCount(0);
    await expect(participant.locator('[data-field="name"] input')).toHaveValue(PARTICIPANT_NAME);
  });

  test('admin approves, the profile shows in the gallery and on its page without email or phone', async () => {
    await admin.goto('/admin');
    await admin.getByRole('link', { name: /^תור סקירה/ }).click();
    await admin.getByRole('link', { name: new RegExp(`פתיחת ההגשה של ${PARTICIPANT_NAME}`) }).click();
    await expect(admin.getByRole('heading', { level: 1, name: 'פרטי הגשה' })).toBeVisible();
    await expect(admin.getByText(PARTICIPANT_EMAIL).first()).toBeVisible();
    await admin.getByRole('button', { name: 'אישור ופרסום', exact: true }).click();
    await confirmInDialog(admin, 'אישור ופרסום');
    await expect(admin.getByText('ההגשה אושרה והפרופיל פורסם.')).toBeVisible();

    await visitor.goto('/');
    const card = visitor.locator('app-profile-card', { hasText: PARTICIPANT_NAME });
    await expect(card).toBeVisible();
    await expect(card).toContainText(PRODUCT_NAME);
    await expect(card).toContainText(COHORT_NAME);
    await card.getByRole('link', { name: /^לפרופיל/ }).click();
    await expect(visitor.getByRole('heading', { level: 1, name: PARTICIPANT_NAME })).toBeVisible();
    profileId = new URL(visitor.url()).pathname.split('/').pop()!;
    expect(profileId).toMatch(/^[0-9a-f-]{36}$/);
    const body = await visitor.locator('main').innerText();
    expect(body).not.toContain(PARTICIPANT_EMAIL);
    expect(body).not.toContain(PARTICIPANT_PHONE_DIGITS);
    expect(body).not.toContain(PARTICIPANT_PHONE);
    expect(body).not.toContain('@');
  });

  test('visitor requests contact details and verifies the address from the mail', async () => {
    await visitor.getByRole('button', { name: 'בקשת פרטי קשר', exact: true }).click();
    await visitor.locator('[data-field="name"] input').fill(VISITOR_NAME);
    await visitor.locator('[data-field="email"] input').fill(VISITOR_EMAIL);
    await visitor.locator('[data-field="reason"] textarea').fill('רוצה להכיר את המוצר ולשתף פעולה');
    await visitor.getByRole('button', { name: 'שליחת הבקשה', exact: true }).click();
    await expect(visitor.getByText('שלחנו אליך מייל')).toBeVisible();

    const mail = await waitForMail(VISITOR_EMAIL, 'אימות כתובת המייל לבקשת יצירת קשר');
    await visitor.goto(linkFrom(mail, '/contact/verify'));
    await expect(visitor.getByRole('heading', { level: 1, name: 'הכתובת אומתה והבקשה נשלחה' })).toBeVisible();
  });

  test('admin sends the request to the participant, who approves from the mail', async () => {
    await admin.goto('/admin');
    await admin.getByRole('link', { name: /^בקשות פרטי קשר/ }).click();
    const row = admin.locator('tbody tr', { hasText: VISITOR_NAME });
    await expect(row).toBeVisible();
    await row.getByRole('button', { name: /שליחה למשתתפת/ }).click();
    await confirmInDialog(admin, 'שליחה למשתתפת');
    await expect(admin.getByText('הבקשה נשלחה למשתתפת.')).toBeVisible();

    const mail = await waitForMail(PARTICIPANT_EMAIL, 'בקשה לקבלת פרטי הקשר שלך');
    expect(mail.Text).not.toContain(VISITOR_EMAIL);
    await participant.goto(linkFrom(mail, '/contact/decide'));
    await expect(participant.getByRole('heading', { level: 1, name: 'בקשת פרטי קשר' })).toBeVisible();
    await expect(participant.getByText(VISITOR_NAME).first()).toBeVisible();
    await participant.getByRole('button', { name: 'אישור', exact: true }).click();
    await expect(participant.getByRole('heading', { level: 1, name: 'תודה, האישור התקבל' })).toBeVisible();
  });

  test('admin approves the delivery and the visitor receives email and phone', async () => {
    await admin.goto('/admin/contact-requests');
    const row = admin.locator('tbody tr', { hasText: VISITOR_NAME });
    await expect(row).toContainText('אושרה על ידי המשתתפת');
    await row.getByRole('button', { name: /אישור ושליחה/ }).click();
    await confirmInDialog(admin, 'אישור ושליחה');
    await expect(admin.getByText('האישור נרשם ופרטי הקשר בדרך למבקש.')).toBeVisible();

    const mail = await waitForMail(VISITOR_EMAIL, 'פרטי הקשר שביקשת');
    expect(mail.Text).toContain(`מייל: ${PARTICIPANT_EMAIL}`);
    // The server stores the phone as digits only.
    expect(mail.Text).toContain(`נייד: ${PARTICIPANT_PHONE_DIGITS}`);
  });

  test('participant edits (pending), admin returns it with a note, the note shows on /me', async () => {
    await participant.goto('/me');
    await expect(participant.locator('[data-field="productName"] input')).toHaveValue(PRODUCT_NAME);
    await participant.locator('[data-field="productName"] input').fill(`${PRODUCT_NAME} 2`);
    await participant.getByRole('button', { name: 'שמירה והגשה', exact: true }).click();
    await expect(participant.getByText('השינויים נשמרו והוגשו לאישור המנהלת.')).toBeVisible();
    await expect(participant.getByText('השינויים ממתינים לאישור')).toBeVisible();

    await admin.goto('/admin/review');
    await admin.getByRole('link', { name: new RegExp(`פתיחת ההגשה של ${PARTICIPANT_NAME}`) }).click();
    await expect(admin.getByText('עדכון לפרופיל קיים')).toBeVisible();
    await admin.locator('[data-field="note"] textarea').fill(RETURN_NOTE);
    await admin.getByRole('button', { name: 'החזרה לתיקון', exact: true }).click();
    await confirmInDialog(admin, 'החזרה לתיקון');
    await expect(admin.getByText('ההגשה הוחזרה לתיקון.')).toBeVisible();

    await participant.reload();
    const banner = participant.locator('.banner--returned');
    await expect(banner).toContainText('הוחזר עם הערה');
    await expect(banner).toContainText(RETURN_NOTE);
    // The published version stays on the public site while the edit is returned.
    await visitor.goto(`/profile/${profileId}`);
    await expect(visitor.getByRole('heading', { level: 1, name: PARTICIPANT_NAME })).toBeVisible();
    await expect(visitor.getByText(`${PRODUCT_NAME} 2`)).toHaveCount(0);
    await expect(visitor.getByText(PRODUCT_NAME, { exact: true }).first()).toBeVisible();
  });
});
