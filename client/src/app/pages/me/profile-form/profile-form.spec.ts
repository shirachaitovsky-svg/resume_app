import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { OwnProfile } from '../../../shared/api/profile.models';
import { CSRF_HEADER } from '../../../shared/api/csrf.service';
import { STRINGS } from '../../../shared/strings.he';
import { ProfileApi, ProfilePayload } from '../profile-api';
import { validateJoinField } from './join-validation';
import { IMAGE_DECODER } from './photo-resize';
import { ProfileForm } from './profile-form';

const GOOD = {
  name: 'נועה כהן',
  productName: 'מתכנן',
  productUrl: 'https://example.com/x',
  phone: '+972 50-123-4567',
};
const EMAIL = 'noa@example.com';
const COHORTS = [
  { id: 'c1', name: 'מחזור א׳' },
  { id: 'c2', name: 'מחזור ב׳' },
];

const OWN: OwnProfile = {
  name: 'דנה לוי',
  productName: 'אפליקציה',
  productUrl: 'https://example.com/app',
  email: EMAIL,
  phone: '050-111-2222',
  description: 'תיאור קיים',
  role: 'מפתחת',
  workplace: null,
  introText: null,
  introAnswer1: 'תשובה',
  introAnswer2: null,
  introAnswer3: null,
  avatarId: 'girl-2',
  cv: '# קורות',
  hasPhoto: false,
  cohortName: 'מחזור ג׳',
  hasPublished: true,
  pending: null,
  pendingContactChange: false,
  pendingEmail: null,
  pendingPhone: null,
  returned: null,
};

describe('validateJoinField', () => {
  it('rejects non-http urls and accepts http', () => {
    for (const v of ['ftp://x', 'javascript:alert(1)', 'example.com', 'https://u:p@x.com', 'https://a b.com'])
      expect(validateJoinField('productUrl', v)).toBe('invalid');
    expect(validateJoinField('productUrl', 'http://example.com')).toBeNull();
  });
  it('validates email, phone and lengths', () => {
    expect(validateJoinField('email', 'a@b')).toBe('invalid');
    expect(validateJoinField('email', 'a@@b.com')).toBe('invalid');
    expect(validateJoinField('phone', '12')).toBe('invalid');
    expect(validateJoinField('phone', '050-123 4567')).toBeNull();
    expect(validateJoinField('name', '')).toBe('required');
    expect(validateJoinField('name', 'x'.repeat(101))).toBe('tooLong');
  });
});

describe('ProfileForm', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<ProfileForm>;
  let el: HTMLElement;
  let saved: number;
  let exists: number;
  let unauthorized: number;

  beforeEach(() => {
    saved = exists = unauthorized = 0;
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:test/0');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: IMAGE_DECODER,
          useValue: async () => ({
            width: 100,
            height: 100,
            encode: async () => new Blob([new Uint8Array(1024)], { type: 'image/jpeg' }),
          }),
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function mount(mode: 'create' | 'edit', inputs: Record<string, unknown> = {}) {
    const api = TestBed.inject(ProfileApi);
    fixture = TestBed.createComponent(ProfileForm);
    const defaults: Record<string, unknown> =
      mode === 'create'
        ? { cohorts: COHORTS, initialCohortId: null }
        : { own: OWN };
    const all = {
      mode,
      email: EMAIL,
      submitFn: (p: ProfilePayload) => (mode === 'create' ? api.create(p) : api.update(p)),
      ...defaults,
      ...inputs,
    };
    for (const [k, v] of Object.entries(all)) fixture.componentRef.setInput(k, v);
    fixture.componentInstance.saved.subscribe(() => saved++);
    fixture.componentInstance.profileExists.subscribe(() => exists++);
    fixture.componentInstance.unauthorized.subscribe(() => unauthorized++);
    el = fixture.nativeElement as HTMLElement;
    await settle();
  }

  function fill(values: Record<string, string>) {
    for (const [f, v] of Object.entries(values)) {
      const input = el.querySelector<HTMLInputElement | HTMLTextAreaElement>(`[data-field="${f}"] :is(input, textarea)`)!;
      input.value = v;
      input.dispatchEvent(new Event('input'));
    }
  }

  function pickCohort(id: string) {
    const select = el.querySelector<HTMLSelectElement>('[data-field="cohortId"] select')!;
    select.value = id;
    select.dispatchEvent(new Event('change'));
  }

  async function submit() {
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
  }

  async function expectRequest(method: 'POST' | 'PUT'): Promise<TestRequest> {
    for (let i = 0; i < 10; i++) {
      await new Promise((r) => setTimeout(r, 0));
      http.match((r) => r.url.includes('csrf')).forEach((r) => r.flush({ token: 't' }));
      const found = http.match('/api/me/profile');
      if (found.length > 0) {
        expect(found.length).toBe(1);
        expect(found[0].request.method).toBe(method);
        return found[0];
      }
    }
    throw new Error(`no ${method}`);
  }

  describe('create mode', () => {
    it('renders the cohort list, read-only email, four typed fields, notice and no LinkedIn', async () => {
      await mount('create');
      const options = [...el.querySelectorAll<HTMLOptionElement>('[data-field="cohortId"] option')];
      expect(options.map((o) => o.textContent!.trim())).toEqual([STRINGS.myProfile.form.cohortPlaceholder, 'מחזור א׳', 'מחזור ב׳']);
      expect(el.querySelectorAll('.fields > app-field').length).toBe(4);
      expect(el.querySelector('[data-field="email"] input')).toBeNull();
      expect(el.querySelector('[data-field="email"]')!.textContent).toContain(EMAIL);
      expect(el.textContent).toContain(STRINGS.myProfile.form.emailNote);
      expect(el.querySelector('[data-field="productUrl"] input')!.getAttribute('type')).toBe('url');
      expect(el.querySelector('[data-field="phone"] input')!.getAttribute('type')).toBe('tel');
      const note = el.querySelector('[role="note"]')!;
      expect(note.textContent).toContain(STRINGS.joinForm.privacyContact);
      expect(note.textContent).toContain(STRINGS.joinForm.privacyRelease);
      expect(el.textContent!.toLowerCase()).not.toContain('linkedin');
      expect(el.querySelector('.banner')).toBeNull();
    });

    it('preselects a cohort from the list and ignores an unknown one', async () => {
      await mount('create', { initialCohortId: 'c2' });
      expect(el.querySelector<HTMLSelectElement>('[data-field="cohortId"] select')!.value).toBe('c2');
      expect(el.querySelector('app-profile-preview')!.textContent).toContain('מחזור ב׳');
      fixture.destroy();
      await mount('create', { initialCohortId: 'nope' });
      expect(el.querySelector<HTMLSelectElement>('[data-field="cohortId"] select')!.value).toBe('');
    });

    it('requires a cohort and the typed fields, shows the summary, focuses it and keeps values', async () => {
      await mount('create');
      fill({ ...GOOD, name: '   ', productUrl: 'javascript:alert(1)' });
      await submit();
      http.expectNone(() => true);
      const summary = el.querySelector<HTMLElement>('.summary')!;
      expect(summary.getAttribute('role')).toBe('alert');
      expect(summary.querySelectorAll('li').length).toBe(3);
      expect(el.querySelector('[data-field="cohortId"] .error')!.textContent).toContain(STRINGS.myProfile.form.cohortRequired);
      expect(document.activeElement).toBe(summary);
      expect(el.querySelector<HTMLInputElement>('[data-field="phone"] input')!.value).toBe(GOOD.phone);
    });

    it('clears a field error as soon as the field is edited', async () => {
      await mount('create');
      fill({ ...GOOD, phone: '12' });
      pickCohort('c1');
      await submit();
      expect(el.querySelector('[data-field="phone"] .error')).not.toBeNull();
      fill({ phone: '050-123-4567' });
      await settle();
      expect(el.querySelector('[data-field="phone"] .error')).toBeNull();
      expect(el.querySelector('.summary')).toBeNull();
    });

    it('posts trimmed fields with the cohort once, without email or Turnstile, and reports success', async () => {
      await mount('create');
      pickCohort('c1');
      fill({ ...GOOD, name: '  נועה כהן  ' });
      await submit();
      await submit();
      const req = await expectRequest('POST');
      expect(req.request.headers.has(CSRF_HEADER)).toBe(true);
      expect(req.request.headers.has('X-Turnstile-Token')).toBe(false);
      expect(req.request.body).toEqual({ cohortId: 'c1', ...GOOD });
      expect('email' in (req.request.body as object)).toBe(false);
      expect(el.querySelector('button[type="submit"]')!.hasAttribute('disabled')).toBe(true);
      req.flush({ profileId: 'p', status: 'pending' }, { status: 201, statusText: 'Created' });
      await settle();
      expect(saved).toBe(1);
    });

    it('maps server field errors (including cohortId) next to the fields and keeps values', async () => {
      await mount('create');
      pickCohort('c1');
      fill(GOOD);
      await submit();
      (await expectRequest('POST')).flush(
        { errors: { cohortId: ['bad'], phone: ['bad'], weird: ['x'] } },
        { status: 400, statusText: 'Bad' },
      );
      await settle();
      expect(el.querySelector('[data-field="cohortId"] .error')).not.toBeNull();
      expect(el.querySelector('[data-field="phone"] .error')).not.toBeNull();
      expect(el.querySelector('.summary')!.textContent).toContain(STRINGS.joinForm.unknownFieldError);
      expect(el.querySelector<HTMLInputElement>('[data-field="name"] input')!.value).toBe(GOOD.name);
      expect(saved).toBe(0);
    });

    it('shows a clear message on 409 email-taken and keeps the data', async () => {
      await mount('create');
      pickCohort('c1');
      fill(GOOD);
      await submit();
      (await expectRequest('POST')).flush({ code: 'email-taken' }, { status: 409, statusText: 'Conflict' });
      await settle();
      expect(el.querySelector('.summary')!.textContent).toContain(STRINGS.myProfile.form.emailTaken);
      expect(el.querySelector<HTMLInputElement>('[data-field="name"] input')!.value).toBe(GOOD.name);
      expect(exists).toBe(0);
    });

    it('emits profileExists on 409 profile-exists and unauthorized on 401', async () => {
      await mount('create');
      pickCohort('c1');
      fill(GOOD);
      await submit();
      (await expectRequest('POST')).flush({ code: 'profile-exists' }, { status: 409, statusText: 'Conflict' });
      await settle();
      expect(exists).toBe(1);
      await submit();
      (await expectRequest('POST')).flush(null, { status: 401, statusText: 'Unauthorized' });
      await settle();
      expect(unauthorized).toBe(1);
    });

    it('shows a retry-after message on 429 and a generic one on 503, keeping data', async () => {
      await mount('create');
      pickCohort('c1');
      fill(GOOD);
      await submit();
      (await expectRequest('POST')).flush(null, { status: 429, statusText: 'TM', headers: { 'Retry-After': '30' } });
      await settle();
      expect(el.querySelector('.summary')!.textContent).toContain('30');
      await submit();
      (await expectRequest('POST')).flush(null, { status: 503, statusText: 'SU' });
      await settle();
      expect(el.querySelector('.summary')!.textContent).toContain(STRINGS.joinForm.submitFailed);
      expect(el.querySelector<HTMLInputElement>('[data-field="name"] input')!.value).toBe(GOOD.name);
    });
  });

  describe('edit mode', () => {
    it('fills the form from the stored profile with a read-only email and no cohort field', async () => {
      await mount('edit');
      expect(el.querySelector('[data-field="cohortId"]')).toBeNull();
      expect(el.querySelector<HTMLInputElement>('[data-field="name"] input')!.value).toBe(OWN.name);
      expect(el.querySelector<HTMLInputElement>('[data-field="phone"] input')!.value).toBe(OWN.phone);
      expect(el.querySelector<HTMLInputElement>('[data-field="description"] input')!.value).toBe('תיאור קיים');
      expect(el.querySelector<HTMLTextAreaElement>('[data-field="cv"] textarea')!.value).toBe('# קורות');
      expect(radio('girl-2').checked).toBe(true);
      expect(el.querySelector('[data-field="email"] input')).toBeNull();
      expect(el.querySelector('[data-field="email"]')!.textContent).toContain(EMAIL);
      expect(el.querySelector('.cohort')!.textContent).toContain('מחזור ג׳');
      expect(el.querySelector('.banner')).toBeNull();
    });

    const radio = (value: string) => el.querySelector<HTMLInputElement>(`app-avatar-picker input[value="${value}"]`)!;

    it('shows the pending banner when changes await approval', async () => {
      await mount('edit', { own: { ...OWN, pending: '2026-10-22T10:00:00Z' } });
      expect(el.querySelector('.banner')!.textContent).toContain(STRINGS.myProfile.form.pendingBanner);
      expect(el.querySelector('.banner--returned')).toBeNull();
    });

    it('shows the returned banner with the note and a resubmit action', async () => {
      await mount('edit', { own: { ...OWN, pending: '2026-10-22T10:00:00Z', returned: { note: 'חסר תיאור', returnedAt: 'x' } } });
      const banner = el.querySelector('.banner--returned')!;
      expect(banner.textContent).toContain(STRINGS.myProfile.form.returnedBanner);
      expect(banner.textContent).toContain('חסר תיאור');
      expect(el.querySelector('button[type="submit"]')!.textContent).toContain(STRINGS.myProfile.form.submitResubmit);
      expect(el.textContent).not.toContain(STRINGS.myProfile.form.pendingBanner);
    });

    it('puts the pending-phone note in view', async () => {
      await mount('edit', { own: { ...OWN, phone: '052-999-8888', pendingPhone: '052-999-8888' } });
      expect(el.textContent).toContain(STRINGS.myProfile.form.phonePending);
    });

    it('PUTs the edited fields with the unchanged email, and reports success', async () => {
      await mount('edit');
      fill({ name: '  דנה כהן ', phone: '052-333-4444', description: '' });
      await submit();
      const req = await expectRequest('PUT');
      expect(req.request.body).toEqual({
        name: 'דנה כהן',
        productName: OWN.productName,
        productUrl: OWN.productUrl,
        phone: '052-333-4444',
        email: EMAIL,
        role: 'מפתחת',
        introAnswer1: 'תשובה',
        avatarId: 'girl-2',
        cv: '# קורות',
      });
      req.flush({ status: 'pending' }, { status: 202, statusText: 'Accepted' });
      await settle();
      expect(saved).toBe(1);
    });

    it('offers to keep, replace or remove an existing photo and sends removePhoto', async () => {
      await mount('edit', { own: { ...OWN, hasPhoto: true } });
      expect(el.querySelector('[data-field="photo"]')!.textContent).toContain(STRINGS.myProfile.form.hasPhoto);
      el.querySelector<HTMLElement>('[data-field="photo"] app-button')!.click();
      await settle();
      expect(el.querySelector('[data-field="photo"]')!.textContent).toContain(STRINGS.myProfile.form.photoWillBeRemoved);
      await submit();
      const req = await expectRequest('PUT');
      expect((req.request.body as Record<string, unknown>)['removePhoto']).toBe(true);
      req.flush({ status: 'pending' }, { status: 202, statusText: 'Accepted' });
      await settle();
    });

    it('PUTs multipart with the new photo, the unchanged email and the CSRF header', async () => {
      await mount('edit', { own: { ...OWN, hasPhoto: true } });
      const input = el.querySelector<HTMLInputElement>('[data-field="photo"] input[type="file"]')!;
      Object.defineProperty(input, 'files', { configurable: true, value: [new File(['x'], 'a.png', { type: 'image/png' })] });
      input.dispatchEvent(new Event('change'));
      await new Promise((r) => setTimeout(r, 0));
      await settle();
      await submit();
      const req = await expectRequest('PUT');
      const body = req.request.body as FormData;
      expect(body instanceof FormData).toBe(true);
      expect((body.get('photo') as File).type).toBe('image/jpeg');
      expect(body.get('email')).toBe(EMAIL);
      expect(body.has('removePhoto')).toBe(false);
      expect(req.request.headers.has(CSRF_HEADER)).toBe(true);
      req.flush({ status: 'pending' }, { status: 202, statusText: 'Accepted' });
      await settle();
    });

    it('does not send removePhoto when the photo is kept', async () => {
      await mount('edit', { own: { ...OWN, hasPhoto: true } });
      await submit();
      const req = await expectRequest('PUT');
      expect('removePhoto' in (req.request.body as object)).toBe(false);
      req.flush({ status: 'pending' }, { status: 202, statusText: 'Accepted' });
      await settle();
    });

    it('maps server field errors to fields and keeps the data', async () => {
      await mount('edit');
      await submit();
      (await expectRequest('PUT')).flush({ errors: { productUrl: ['bad'] } }, { status: 400, statusText: 'Bad' });
      await settle();
      expect(el.querySelector('[data-field="productUrl"] .error')).not.toBeNull();
      expect(el.querySelector<HTMLInputElement>('[data-field="name"] input')!.value).toBe(OWN.name);
    });

    it('emits unauthorized on 401', async () => {
      await mount('edit');
      await submit();
      (await expectRequest('PUT')).flush(null, { status: 401, statusText: 'Unauthorized' });
      await settle();
      expect(unauthorized).toBe(1);
    });

    it('never shows an email or phone other than the account and the stored profile', async () => {
      await mount('edit');
      const text = el.textContent!;
      expect(text).toContain(EMAIL);
      expect(text).not.toMatch(/@(?!example\.com)/);
    });
  });
});
