import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { STRINGS } from '../../shared/strings.he';
import { MePage, googleStartUrl } from './me';

const ME = '/api/auth/google/me';
const COHORT = '3f2b8a52-6f6c-4d7e-9a43-0d1c2b3a4f5e';
const unauth = { status: 401, statusText: 'Unauthorized' };

const OWN = {
  name: 'דנה לוי',
  productName: 'אפליקציה',
  productUrl: 'https://example.com/app',
  email: 'a@b.com',
  phone: '050-111-2222',
  description: null,
  role: null,
  workplace: null,
  introText: null,
  introAnswer1: null,
  introAnswer2: null,
  introAnswer3: null,
  avatarId: 'default',
  cv: null,
  hasPhoto: false,
  cohortName: 'מחזור ג׳',
  hasPublished: false,
  pending: '2026-10-22T10:00:00Z',
  pendingContactChange: false,
  pendingEmail: null,
  pendingPhone: null,
  returned: null,
};

describe('MePage: My Profile (create and edit)', () => {
  let http: HttpTestingController;
  let harness: RouterTestingHarness;
  let el: HTMLElement;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([{ path: 'me', component: MePage }])],
    });
    http = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
  });
  afterEach(() => http.verify());

  async function settle() {
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
  }

  async function expectCall(url: string, method = 'GET'): Promise<TestRequest> {
    for (let i = 0; i < 10; i++) {
      await new Promise((r) => setTimeout(r, 0));
      http.match((r) => r.url.includes('csrf')).forEach((r) => r.flush({ token: 't' }));
      const found = http.match((r) => r.url === url && r.method === method);
      if (found.length > 0) return found[0];
    }
    throw new Error(`no ${method} ${url}`);
  }

  async function open(url: string, me: unknown): Promise<void> {
    await harness.navigateByUrl(url, MePage);
    el = harness.routeNativeElement as HTMLElement;
    const req = await expectCall(ME);
    if (me === null) req.flush(null, unauth);
    else req.flush(me as object);
  }

  function fillIn(values: Record<string, string>) {
    for (const [f, v] of Object.entries(values)) {
      const input = el.querySelector<HTMLInputElement>(`[data-field="${f}"] input`)!;
      input.value = v;
      input.dispatchEvent(new Event('input'));
    }
  }

  function submitForm() {
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    return settle();
  }

  it('keeps a valid ?cohort= in the sign-in return url', async () => {
    await open(`/me?cohort=${COHORT}`, null);
    await settle();
    expect(el.querySelector('a.google-signin')!.getAttribute('href')).toBe(googleStartUrl(COHORT));
    expect(googleStartUrl(COHORT)).toBe(`/api/auth/google/start?returnUrl=${encodeURIComponent(`/me?cohort=${COHORT}`)}`);
  });

  it('ignores a ?cohort= that is not an id', async () => {
    await open('/me?cohort=%3Cscript%3E', null);
    await settle();
    expect(el.querySelector('a.google-signin')!.getAttribute('href')).toBe('/api/auth/google/start?returnUrl=%2Fme');
  });

  it('creates a profile: pre-selected cohort, confirmation, then the edit form with a pending banner', async () => {
    await open(`/me?cohort=${COHORT}`, { email: 'a@b.com', hasProfile: false });
    (await expectCall('/api/cohorts/active')).flush({
      items: [
        { id: COHORT, name: 'מחזור א׳' },
        { id: 'other', name: 'מחזור ב׳' },
      ],
      total: 2,
    });
    await settle();
    expect(el.querySelector<HTMLSelectElement>('[data-field="cohortId"] select')!.value).toBe(COHORT);
    expect(el.querySelector('[data-field="email"]')!.textContent).toContain('a@b.com');
    fillIn({ name: 'דנה לוי', productName: 'אפליקציה', productUrl: 'https://example.com/app', phone: '050-111-2222' });
    await submitForm();
    const post = await expectCall('/api/me/profile', 'POST');
    expect(post.request.body).toEqual({
      cohortId: COHORT,
      name: 'דנה לוי',
      productName: 'אפליקציה',
      productUrl: 'https://example.com/app',
      phone: '050-111-2222',
    });
    post.flush({ profileId: 'p', status: 'pending' }, { status: 201, statusText: 'Created' });
    (await expectCall(ME)).flush({ email: 'a@b.com', hasProfile: true, profileId: 'p' });
    (await expectCall('/api/me/profile')).flush(OWN);
    await settle();
    expect(el.textContent).toContain(STRINGS.joinForm.confirmTitle);
    expect(document.activeElement).toBe(el.querySelector('.created h2'));
    expect(el.querySelector('[data-field="cohortId"]')).toBeNull();
    expect(el.querySelector('.banner')!.textContent).toContain(STRINGS.myProfile.form.pendingBanner);
    expect(el.querySelector<HTMLInputElement>('[data-field="name"] input')!.value).toBe('דנה לוי');
  });

  it('opens the edit form with the stored profile, and a PUT refreshes it with the pending-phone note', async () => {
    await open('/me', { email: 'a@b.com', hasProfile: true, profileId: 'p' });
    (await expectCall('/api/me/profile')).flush({ ...OWN, pending: null });
    await settle();
    expect(el.querySelector('.banner')).toBeNull();
    expect(el.querySelector<HTMLInputElement>('[data-field="productName"] input')!.value).toBe('אפליקציה');
    fillIn({ phone: '052-333-4444' });
    await submitForm();
    const put = await expectCall('/api/me/profile', 'PUT');
    expect((put.request.body as { phone: string }).phone).toBe('052-333-4444');
    put.flush({ status: 'pending' }, { status: 202, statusText: 'Accepted' });
    (await expectCall('/api/me/profile')).flush({ ...OWN, phone: '052-333-4444', pendingPhone: '052-333-4444' });
    await settle();
    expect(el.querySelector('.banner')!.textContent).toContain(STRINGS.myProfile.form.pendingBanner);
    expect(el.querySelector('[role="status"].saved')!.textContent).toContain(STRINGS.myProfile.form.savedEdit);
    expect(el.textContent).toContain(STRINGS.myProfile.form.phonePending);
    expect(el.querySelector<HTMLInputElement>('[data-field="phone"] input')!.value).toBe('052-333-4444');
  });

  it('shows the returned banner and note', async () => {
    await open('/me', { email: 'a@b.com', hasProfile: true, profileId: 'p' });
    (await expectCall('/api/me/profile')).flush({ ...OWN, returned: { note: 'נא להשלים', returnedAt: 'x' } });
    await settle();
    expect(el.querySelector('.banner--returned')!.textContent).toContain('נא להשלים');
    expect(el.querySelector('button[type="submit"]')!.textContent).toContain(STRINGS.myProfile.form.submitResubmit);
  });

  it('goes to edit mode when the create is answered with 409 profile-exists', async () => {
    await open('/me', { email: 'a@b.com', hasProfile: false });
    (await expectCall('/api/cohorts/active')).flush({ items: [{ id: COHORT, name: 'מחזור א׳' }], total: 1 });
    await settle();
    const select = el.querySelector<HTMLSelectElement>('[data-field="cohortId"] select')!;
    select.value = COHORT;
    select.dispatchEvent(new Event('change'));
    fillIn({ name: 'דנה', productName: 'אפליקציה', productUrl: 'https://example.com/app', phone: '050-111-2222' });
    await submitForm();
    (await expectCall('/api/me/profile', 'POST')).flush(
      { code: 'profile-exists' },
      { status: 409, statusText: 'Conflict' },
    );
    (await expectCall(ME)).flush({ email: 'a@b.com', hasProfile: true, profileId: 'p' });
    (await expectCall('/api/me/profile')).flush(OWN);
    await settle();
    expect(el.querySelector('[data-field="cohortId"]')).toBeNull();
    expect(el.textContent).not.toContain(STRINGS.joinForm.confirmTitle);
    expect(el.querySelector<HTMLInputElement>('[data-field="name"] input')!.value).toBe('דנה לוי');
  });

  it('returns to the sign-in screen when the profile call answers 401', async () => {
    await open('/me', { email: 'a@b.com', hasProfile: true, profileId: 'p' });
    (await expectCall('/api/me/profile')).flush(null, unauth);
    await settle();
    expect(el.querySelector('a.google-signin')).not.toBeNull();
  });

  it('says so when there is no active cohort to join', async () => {
    await open('/me', { email: 'a@b.com', hasProfile: false });
    (await expectCall('/api/cohorts/active')).flush({ items: [], total: 0 });
    await settle();
    expect(el.textContent).toContain(STRINGS.myProfile.noCohorts);
    expect(el.querySelector('form')).toBeNull();
  });

  it('shows an error state with retry when the stored profile fails to load', async () => {
    await open('/me', { email: 'a@b.com', hasProfile: true, profileId: 'p' });
    (await expectCall('/api/me/profile')).flush(null, { status: 500, statusText: 'x' });
    await settle();
    expect(el.querySelector('app-error-state')).not.toBeNull();
    (el.querySelector('app-error-state app-button') as HTMLElement).click();
    (await expectCall('/api/me/profile')).flush(OWN);
    await settle();
    expect(el.querySelector('form')).not.toBeNull();
  });

  it('returns to the sign-in link when creating is answered with 401', async () => {
    await open('/me', { email: 'a@b.com', hasProfile: false });
    (await expectCall('/api/cohorts/active')).flush({ items: [{ id: COHORT, name: 'מחזור א׳' }], total: 1 });
    await settle();
    const select = el.querySelector<HTMLSelectElement>('[data-field="cohortId"] select')!;
    select.value = COHORT;
    select.dispatchEvent(new Event('change'));
    fillIn({ name: 'דנה', productName: 'אפליקציה', productUrl: 'https://example.com/app', phone: '050-111-2222' });
    await submitForm();
    (await expectCall('/api/me/profile', 'POST')).flush(null, unauth);
    await settle();
    expect(el.querySelector('a.google-signin')).not.toBeNull();
  });

  it('returns to the sign-in link when saving an edit is answered with 401', async () => {
    await open('/me', { email: 'a@b.com', hasProfile: true, profileId: 'p' });
    (await expectCall('/api/me/profile')).flush(OWN);
    await settle();
    await submitForm();
    (await expectCall('/api/me/profile', 'PUT')).flush(null, unauth);
    await settle();
    expect(el.querySelector('a.google-signin')).not.toBeNull();
  });

  it('keeps the form and says the changes were saved when only the reload fails, then retries', async () => {
    await open('/me', { email: 'a@b.com', hasProfile: true, profileId: 'p' });
    (await expectCall('/api/me/profile')).flush(OWN);
    await settle();
    fillIn({ name: 'שם חדש' });
    await submitForm();
    (await expectCall('/api/me/profile', 'PUT')).flush({ status: 'pending' }, { status: 202, statusText: 'Accepted' });
    (await expectCall('/api/me/profile')).flush(null, { status: 500, statusText: 'x' });
    await settle();
    expect(el.querySelector('form')).not.toBeNull();
    expect(el.querySelector('app-error-state')).toBeNull();
    expect(el.querySelector('[role="alert"].saved')!.textContent).toContain(STRINGS.myProfile.form.savedRefreshFailed);
    expect(el.querySelector<HTMLInputElement>('[data-field="name"] input')!.value).toBe('שם חדש');
    (el.querySelector('[role="alert"].saved app-button') as HTMLElement).click();
    (await expectCall('/api/me/profile')).flush({ ...OWN, name: 'שם חדש' });
    await settle();
    expect(el.querySelector('[role="alert"].saved')).toBeNull();
    expect(el.querySelector('[role="status"].saved')).not.toBeNull();
  });
});
