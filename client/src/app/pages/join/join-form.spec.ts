import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { CSRF_HEADER } from '../../shared/api/csrf.service';
import { STRINGS } from '../../shared/strings.he';
import { JoinFormPage } from './join-form';
import { validateJoinField } from './join-validation';

const TOKEN = 'abc123_-XYZ';
const GOOD = {
  name: 'נועה כהן',
  productName: 'מתכנן',
  productUrl: 'https://example.com/x',
  email: 'noa@example.com',
  phone: '+972 50-123-4567',
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

describe('JoinFormPage', () => {
  let http: HttpTestingController;
  let harness: RouterTestingHarness;
  let el: HTMLElement;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'join/:token', component: JoinFormPage }], withComponentInputBinding()),
      ],
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

  async function open(status = 200, token = TOKEN) {
    await harness.navigateByUrl(`/join/${token}`, JoinFormPage);
    el = harness.routeNativeElement as HTMLElement;
    harness.detectChanges();
    if (token !== TOKEN) return;
    expect(el.querySelector('app-loading-skeleton')).not.toBeNull();
    await new Promise((r) => setTimeout(r, 0));
    const req = http.expectOne(`/api/invites/${token}`);
    if (status === 200) req.flush({ name: 'מחזור א׳' });
    else req.flush(null, { status, statusText: 'x' });
    await settle();
  }

  function fill(values: Record<string, string>) {
    for (const [f, v] of Object.entries(values)) {
      const input = el.querySelector<HTMLInputElement>(`[data-field="${f}"] input`)!;
      input.value = v;
      input.dispatchEvent(new Event('input'));
    }
  }

  async function submit() {
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
  }

  async function expectPost(): Promise<TestRequest> {
    for (let i = 0; i < 10; i++) {
      await new Promise((r) => setTimeout(r, 0));
      http.match((r) => r.url.includes('csrf')).forEach((r) => r.flush({ token: 't' }));
      const found = http.match(`/api/invites/${TOKEN}/profiles`);
      if (found.length > 0) {
        expect(found.length).toBe(1);
        return found[0];
      }
    }
    throw new Error('no POST');
  }

  it('renders the form with cohort name, five required fields, notice and no LinkedIn', async () => {
    await open();
    expect(el.textContent).toContain('מחזור א׳');
    expect(el.querySelectorAll('app-field').length).toBe(5);
    expect(el.querySelector('[data-field="productUrl"] input')!.getAttribute('type')).toBe('url');
    expect(el.querySelector('[data-field="email"] input')!.getAttribute('type')).toBe('email');
    expect(el.querySelector('[data-field="phone"] input')!.getAttribute('type')).toBe('tel');
    const note = el.querySelector('[role="note"]')!;
    expect(note.textContent).toContain(STRINGS.joinForm.privacyContact);
    expect(note.textContent).toContain(STRINGS.joinForm.privacyRelease);
    expect(el.textContent!.toLowerCase()).not.toContain('linkedin');
  });

  it('shows the invalid-link screen on 404 without a form', async () => {
    await open(404);
    expect(el.textContent).toContain(STRINGS.joinForm.invalidTitle);
    expect(el.querySelector('form')).toBeNull();
  });

  it('shows the invalid-link screen for a malformed token without a request', async () => {
    await open(200, 'bad.token');
    await settle();
    http.expectNone(() => true);
    expect(el.textContent).toContain(STRINGS.joinForm.invalidTitle);
  });

  it('shows an error state with retry on 500', async () => {
    await open(500);
    expect(el.querySelector('app-error-state')).not.toBeNull();
    el.querySelector<HTMLButtonElement>('app-error-state button')!.click();
    await new Promise((r) => setTimeout(r, 0));
    http.expectOne(`/api/invites/${TOKEN}`).flush({ name: 'מחזור' });
    await settle();
    expect(el.querySelector('form')).not.toBeNull();
  });

  it('blocks submit on missing/invalid fields, shows summary, focuses it and keeps values', async () => {
    await open();
    fill({ ...GOOD, name: '   ', productUrl: 'javascript:alert(1)' });
    await submit();
    await settle();
    http.expectNone(() => true);
    const summary = el.querySelector<HTMLElement>('.summary')!;
    expect(summary.getAttribute('role')).toBe('alert');
    expect(summary.querySelectorAll('li').length).toBe(2);
    expect(el.querySelectorAll('[aria-invalid="true"]').length).toBe(2);
    expect(document.activeElement).toBe(summary);
    expect(el.querySelector<HTMLInputElement>('[data-field="email"] input')!.value).toBe(GOOD.email);
  });

  it('clears a field error as soon as the field is edited', async () => {
    await open();
    fill({ ...GOOD, email: 'a@b' });
    await submit();
    expect(el.querySelector('[data-field="email"] .error')).not.toBeNull();
    expect(el.querySelectorAll('.summary li').length).toBe(1);
    fill({ email: 'a@b.com' });
    await settle();
    expect(el.querySelector('[data-field="email"] .error')).toBeNull();
    expect(el.querySelector('[data-field="email"] input')!.hasAttribute('aria-invalid')).toBe(false);
    expect(el.querySelector('.summary')).toBeNull();
  });

  it('submits trimmed required fields once, then shows the confirmation without email or phone', async () => {
    await open();
    fill({ ...GOOD, name: '  נועה כהן  ' });
    await submit();
    await submit();
    const req = await expectPost();
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.has('X-Turnstile-Token')).toBe(true);
    expect(req.request.headers.has(CSRF_HEADER)).toBe(true);
    expect(req.request.body).toEqual(GOOD);
    expect(el.querySelector('button[type="submit"]')!.hasAttribute('disabled')).toBe(true);
    req.flush({ status: 'pending' }, { status: 202, statusText: 'Accepted' });
    await settle();
    expect(el.textContent).toContain(STRINGS.joinForm.confirmTitle);
    expect(el.textContent).not.toContain(GOOD.email);
    expect(el.textContent).not.toContain('050');
    expect(el.querySelector('form')).toBeNull();
  });

  it('maps server field errors to fields and keeps values', async () => {
    await open();
    fill(GOOD);
    await submit();
    const req = await expectPost();
    req.flush({ errors: { email: ['bad'], weird: ['x'] } }, { status: 400, statusText: 'Bad' });
    await settle();
    expect(el.querySelector('[data-field="email"] .error')).not.toBeNull();
    expect(el.querySelector('.summary')!.textContent).toContain(STRINGS.joinForm.unknownFieldError);
    expect(el.querySelector<HTMLInputElement>('[data-field="phone"] input')!.value).toBe(GOOD.phone);
  });

  it('shows the invalid-link screen when the token is revoked during submit', async () => {
    await open();
    fill(GOOD);
    await submit();
    (await expectPost()).flush(null, { status: 404, statusText: 'NF' });
    await settle();
    expect(el.textContent).toContain(STRINGS.joinForm.invalidTitle);
  });

  it('shows a retry-after message on 429 and a generic one on 503, keeping data', async () => {
    await open();
    fill(GOOD);
    await submit();
    (await expectPost()).flush(null, { status: 429, statusText: 'TM', headers: { 'Retry-After': '30' } });
    await settle();
    expect(el.querySelector('.summary')!.textContent).toContain('30');
    await submit();
    (await expectPost()).flush(null, { status: 503, statusText: 'SU' });
    await settle();
    expect(el.querySelector('.summary')!.textContent).toContain(STRINGS.joinForm.submitFailed);
    expect(el.querySelector<HTMLInputElement>('[data-field="name"] input')!.value).toBe(GOOD.name);
  });
});
