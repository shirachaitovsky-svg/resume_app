import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { CSRF_HEADER } from '../../shared/api/csrf.service';
import { STRINGS } from '../../shared/strings.he';
import { AdminSession } from './admin-session';
import { ADMIN_ROUTES } from './admin.routes';
import { buildQr } from './qr-code';
import { groupKey } from './setup/admin-setup';

const ME_NEW = { username: 'dana', mustChangePassword: true, totpSetupRequired: true };
const ME_TOTP = { username: 'dana', mustChangePassword: false, totpSetupRequired: true };
const ME_DONE = { username: 'dana', mustChangePassword: false, totpSetupRequired: false };
const KEY = 'ABCDEFGHJKLMNPQR';
const URI = `otpauth://totp/resume-app:dana?secret=${KEY}&issuer=resume-app&digits=6`;
const CODES = Array.from({ length: 10 }, (_, i) => `code-${i}-xxxxx`);

describe('admin area', () => {
  let http: HttpTestingController;
  let harness: RouterTestingHarness;
  let router: Router;
  let el: HTMLElement;
  const csrfCalls: string[] = [];
  let restoreClipboard: (() => void) | null = null;

  beforeEach(async () => {
    csrfCalls.length = 0;
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([{ path: 'admin', children: ADMIN_ROUTES }])],
    });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    harness = await RouterTestingHarness.create();
  });
  afterEach(() => {
    restoreClipboard?.();
    restoreClipboard = null;
    vi.restoreAllMocks();
    http.verify();
  });

  /** Waits for a request, answering any anti-forgery token requests on the way. */
  function answerCsrf() {
    for (const r of http.match((req) => req.url.endsWith('/csrf'))) {
      csrfCalls.push(r.request.url);
      r.flush({ token: r.request.url.includes('/auth/') ? 'admin-token' : 'public-token' });
    }
  }

  async function next(url: string, method = 'GET'): Promise<TestRequest> {
    for (let i = 0; i < 20; i++) {
      await new Promise((r) => setTimeout(r, 0));
      answerCsrf();
      const found = http.match((req) => req.url === url && req.method === method);
      if (found.length > 0) {
        expect(found.length).toBe(1);
        return found[0];
      }
    }
    throw new Error(`no ${method} ${url}`);
  }

  async function settle() {
    for (let i = 0; i < 3; i++) {
      await new Promise((r) => setTimeout(r, 0));
      answerCsrf();
      harness.detectChanges();
      await harness.fixture.whenStable();
    }
    harness.detectChanges();
    el = harness.routeNativeElement ?? (harness.fixture.nativeElement as HTMLElement);
  }

  /** Opens `url`, answers `/auth/me`, and waits for the navigation to settle. */
  async function open(url: string, me: object | 'anonymous') {
    const nav = harness.navigateByUrl(url);
    const req = await next('/api/auth/me');
    if (me === 'anonymous') req.flush(null, { status: 401, statusText: 'Unauthorized' });
    else req.flush(me);
    await nav;
    await settle();
  }

  function fill(field: string, value: string) {
    const input = el.querySelector<HTMLInputElement>(`[data-field="${field}"] input`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  async function submit() {
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
  }

  function value(field: string): string {
    return el.querySelector<HTMLInputElement>(`[data-field="${field}"] input`)!.value;
  }

  const heading = () => el.querySelector('h1')?.textContent?.trim();

  describe('guard', () => {
    it('sends anonymous visitors from /admin to the login screen (401 is not an error)', async () => {
      await open('/admin', 'anonymous');
      expect(router.url).toBe('/admin/login');
      expect(heading()).toBe(STRINGS.admin.login.heading);
    });

    it('sends a one-time password to the password step', async () => {
      await open('/admin', ME_NEW);
      expect(router.url).toBe('/admin/setup');
      expect(heading()).toBe(STRINGS.admin.setup.password.heading);
    });

    it('sends a missing TOTP to the enrollment step', async () => {
      await open('/admin', ME_TOTP);
      expect(router.url).toBe('/admin/setup');
      (await next('/api/auth/totp/setup', 'POST')).flush({ key: KEY, uri: URI });
      await settle();
      expect(heading()).toBe(STRINGS.admin.setup.totp.heading);
    });

    it('shows the landing page only after everything is done', async () => {
      await open('/admin', ME_DONE);
      expect(router.url).toBe('/admin');
      expect(heading()).toBe(STRINGS.admin.heading);
      expect(el.textContent).toContain('dana');
    });

    it('moves a signed-in admin away from login and from setup', async () => {
      await open('/admin/login', ME_DONE);
      expect(router.url).toBe('/admin');
      await harness.navigateByUrl('/admin/setup');
      expect(router.url).toBe('/admin');
    });

    it('explains an expired session on the login screen', async () => {
      TestBed.inject(AdminSession).expired.set(true);
      await open('/admin', 'anonymous');
      expect(router.url).toBe('/admin/login');
      expect(el.textContent).toContain(STRINGS.admin.sessionExpired);
      expect(TestBed.inject(AdminSession).expired()).toBe(false);
    });

    it('protects sub-routes too', async () => {
      await open('/admin/setup', 'anonymous');
      expect(router.url).toBe('/admin/login');
    });
  });

  describe('login', () => {
    beforeEach(async () => {
      await open('/admin/login', 'anonymous');
    });

    it('uses numeric one-time-code input and explains the first sign-in', () => {
      const code = el.querySelector('[data-field="code"] input')!;
      expect(code.getAttribute('inputmode')).toBe('numeric');
      expect(code.getAttribute('autocomplete')).toBe('one-time-code');
      expect(el.textContent).toContain(STRINGS.admin.login.codeHint);
      expect(el.querySelector('[data-field="password"] input')!.getAttribute('type')).toBe('password');
    });

    it('shows one uniform message for any 401, clears password and code, keeps the username', async () => {
      fill('username', 'dana');
      fill('password', 'secret-password');
      fill('code', '123456');
      await submit();
      const req = await next('/api/auth/login', 'POST');
      expect(req.request.body).toEqual({ username: 'dana', password: 'secret-password', code: '123456' });
      expect(req.request.headers.get(CSRF_HEADER)).toBe('public-token');
      req.flush({ title: 'locked' }, { status: 401, statusText: 'Unauthorized' });
      await settle();
      expect(el.querySelector('.alert')!.textContent).toContain(STRINGS.admin.login.invalid);
      expect(value('password')).toBe('');
      expect(value('code')).toBe('');
      expect(value('username')).toBe('dana');
      expect(document.activeElement).toBe(el.querySelector('.alert'));
      expect(router.url).toBe('/admin/login');
    });

    it('sends the recovery code instead of the code after the switch', async () => {
      el.querySelector<HTMLButtonElement>('.toggle')!.click();
      await settle();
      expect(el.querySelector('[data-field="code"]')).toBeNull();
      fill('username', 'dana');
      fill('password', 'secret-password');
      fill('recoveryCode', 'abcde-12345');
      await submit();
      const req = await next('/api/auth/login', 'POST');
      expect(req.request.body).toEqual({ username: 'dana', password: 'secret-password', recoveryCode: 'abcde-12345' });
      req.flush(null, { status: 401, statusText: 'Unauthorized' });
      await settle();
      expect(value('recoveryCode')).toBe('');
    });

    it('does not block on a missing code and sends no code field', async () => {
      fill('username', 'dana');
      fill('password', 'secret-password');
      await submit();
      const req = await next('/api/auth/login', 'POST');
      expect(req.request.body).toEqual({ username: 'dana', password: 'secret-password' });
      req.flush(ME_NEW);
      await settle();
    });

    it('fetches a fresh admin anti-forgery token after sign-in and stores nothing in the browser', async () => {
      fill('username', 'dana');
      fill('password', 'secret-password');
      await submit();
      (await next('/api/auth/login', 'POST')).flush(ME_DONE);
      await settle();
      expect(csrfCalls).toContain('/api/auth/csrf');
      expect(router.url).toBe('/admin');
      expect(TestBed.inject(AdminSession).state().status).toBe('authenticated');
      expect(localStorage.length).toBe(0);
      expect(sessionStorage.length).toBe(0);
      expect(router.url).not.toContain('secret');
    });

    it('routes a first sign-in to the password step', async () => {
      fill('username', 'dana');
      fill('password', 'initial-password');
      await submit();
      (await next('/api/auth/login', 'POST')).flush(ME_NEW);
      await settle();
      expect(router.url).toBe('/admin/setup');
    });

    it('shows the wait time on 429 and a general message on 500 or network failure', async () => {
      fill('username', 'dana');
      fill('password', 'p');
      await submit();
      (await next('/api/auth/login', 'POST')).flush(null, {
        status: 429,
        statusText: 'Too Many',
        headers: { 'Retry-After': '30' },
      });
      await settle();
      expect(el.querySelector('.alert')!.textContent).toContain('30');
      expect(value('username')).toBe('dana');

      fill('password', 'p');
      await submit();
      (await next('/api/auth/login', 'POST')).flush(null, { status: 500, statusText: 'Server' });
      await settle();
      expect(el.querySelector('.alert')!.textContent).toContain(STRINGS.errors.server);
      expect(el.querySelector('.alert')!.textContent).not.toContain(STRINGS.admin.login.invalid);

      fill('password', 'p');
      await submit();
      (await next('/api/auth/login', 'POST')).error(new ProgressEvent('error'));
      await settle();
      expect(el.querySelector('.alert')!.textContent).toContain(STRINGS.errors.network);
      expect(value('username')).toBe('dana');
    });

    it('disables the button while sending and sends a single request on double submit', async () => {
      fill('username', 'dana');
      fill('password', 'p');
      await submit();
      await submit();
      expect(el.querySelector<HTMLButtonElement>('app-button button')!.disabled).toBe(true);
      const req = await next('/api/auth/login', 'POST');
      http.expectNone((r) => r.url === '/api/auth/login');
      req.flush(null, { status: 401, statusText: 'Unauthorized' });
      await settle();
    });

  });

  describe('setup', () => {
    async function toTotp() {
      await open('/admin', ME_TOTP);
      (await next('/api/auth/totp/setup', 'POST')).flush({ key: KEY, uri: URI });
      await settle();
    }

    it('blocks a mismatching confirmation on the client without a request', async () => {
      await open('/admin', ME_NEW);
      fill('currentPassword', 'old-password-1');
      fill('newPassword', 'new-password-123');
      fill('confirmPassword', 'other-password-1');
      await submit();
      http.expectNone((r) => r.url.includes('change-password'));
      expect(el.querySelector('[data-field="confirmPassword"]')!.textContent).toContain(STRINGS.admin.setup.password.mismatch);
      expect(value('newPassword')).toBe('new-password-123');
    });

    it('maps server 400 errors to the fields and keeps the values', async () => {
      await open('/admin', ME_NEW);
      fill('currentPassword', 'old-password-1');
      fill('newPassword', 'short');
      fill('confirmPassword', 'short');
      await submit();
      const req = await next('/api/auth/change-password', 'POST');
      expect(req.request.body).toEqual({ currentPassword: 'old-password-1', newPassword: 'short' });
      req.flush(
        { title: 'x', errors: { newPassword: ['הסיסמה קצרה מדי'], currentPassword: ['הסיסמה הנוכחית שגויה'] } },
        { status: 400, statusText: 'Bad Request' },
      );
      await settle();
      expect(el.querySelector('[data-field="newPassword"]')!.textContent).toContain('הסיסמה קצרה מדי');
      expect(el.querySelector('[data-field="currentPassword"]')!.textContent).toContain('הסיסמה הנוכחית שגויה');
      expect(value('newPassword')).toBe('short');
    });

    it('moves from the password change to TOTP enrollment', async () => {
      await open('/admin', ME_NEW);
      fill('currentPassword', 'old-password-1');
      fill('newPassword', 'new-password-123');
      fill('confirmPassword', 'new-password-123');
      await submit();
      (await next('/api/auth/change-password', 'POST')).flush(null, { status: 204, statusText: 'No Content' });
      (await next('/api/auth/me')).flush(ME_TOTP);
      (await next('/api/auth/totp/setup', 'POST')).flush({ key: KEY, uri: URI });
      await settle();
      expect(heading()).toBe(STRINGS.admin.setup.totp.heading);
      expect(router.url).toBe('/admin/setup');
    });

    it('draws the QR as SVG elements and shows the key in groups of four', async () => {
      await toTotp();
      const svg = el.querySelector('app-qr-code svg')!;
      expect(svg.getAttribute('role')).toBe('img');
      expect(svg.getAttribute('aria-label')).toBe(STRINGS.admin.setup.totp.qrLabel);
      expect(svg.querySelector('path')!.getAttribute('d')!.length).toBeGreaterThan(100);
      expect(el.querySelector('.key')!.textContent!.trim()).toBe('ABCD EFGH JKLM NPQR');
      const code = el.querySelector('[data-field="code"] input')!;
      expect(code.getAttribute('inputmode')).toBe('numeric');
      expect(code.getAttribute('autocomplete')).toBe('one-time-code');
    });

    it('shows a wrong code next to the field and clears it', async () => {
      await toTotp();
      fill('code', '000000');
      await submit();
      const req = await next('/api/auth/totp/enable', 'POST');
      expect(req.request.body).toEqual({ code: '000000' });
      req.flush({ title: 'x', errors: { code: ['הקוד שגוי'] } }, { status: 400, statusText: 'Bad Request' });
      await settle();
      expect(el.querySelector('[data-field="code"]')!.textContent).toContain('הקוד שגוי');
      expect(value('code')).toBe('');
    });

    it('shows ten recovery codes once, gates "continue" on the confirmation and wipes them afterwards', async () => {
      await toTotp();
      fill('code', '123 456');
      await submit();
      (await next('/api/auth/totp/enable', 'POST')).flush({ recoveryCodes: CODES });
      await settle();

      expect(heading()).toBe(STRINGS.admin.setup.recovery.heading);
      expect(el.querySelectorAll('.codes li').length).toBe(10);
      expect(el.querySelector('app-qr-code')).toBeNull();
      const cont = () =>
        [...el.querySelectorAll<HTMLButtonElement>('app-button button')].find(
          (b) => b.textContent!.trim() === STRINGS.admin.setup.recovery.continue,
        )!;
      expect(cont().disabled).toBe(true);
      expect(localStorage.length).toBe(0);
      expect(sessionStorage.length).toBe(0);

      const box = el.querySelector<HTMLInputElement>('.confirm input')!;
      box.checked = true;
      box.dispatchEvent(new Event('change'));
      await settle();
      expect(cont().disabled).toBe(false);

      const component = harness.routeDebugElement!.componentInstance as { recoveryCodes(): readonly string[] };
      expect(component.recoveryCodes().length).toBe(10);
      cont().click();
      await settle();
      expect(component.recoveryCodes().length).toBe(0);
      expect(router.url).toBe('/admin');
      expect(heading()).toBe(STRINGS.admin.heading);
    });

    it('copies the codes with the clipboard and falls back to selecting the text', async () => {
      await toTotp();
      fill('code', '123456');
      await submit();
      (await next('/api/auth/totp/enable', 'POST')).flush({ recoveryCodes: CODES });
      await settle();
      const copy = el.querySelector<HTMLButtonElement>('.actions app-button button')!;

      const writeText = vi.fn().mockResolvedValue(undefined);
      const original = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
      restoreClipboard = () => {
        if (original) Object.defineProperty(navigator, 'clipboard', original);
        else delete (navigator as unknown as Record<string, unknown>)['clipboard'];
      };
      Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
      copy.click();
      await settle();
      expect(writeText).toHaveBeenCalledWith(CODES.join('\n'));
      expect(el.querySelector('.status')!.textContent).toContain(STRINGS.admin.setup.recovery.copied);

      writeText.mockRejectedValue(new Error('denied'));
      copy.click();
      await settle();
      expect(el.querySelector('.status')!.textContent).toContain(STRINGS.admin.setup.recovery.copyFailed);
      expect(window.getSelection()!.toString()).toContain(CODES[0]);
    });

    it('cannot reach the landing page before confirming, and a reload does not bring the codes back', async () => {
      await toTotp();
      fill('code', '123456');
      await submit();
      (await next('/api/auth/totp/enable', 'POST')).flush({ recoveryCodes: CODES });
      await settle();
      await harness.navigateByUrl('/admin');
      expect(router.url).toBe('/admin/setup');

      // A reload starts from /auth/me with TOTP already enabled.
      TestBed.inject(AdminSession).clearRecovery();
      TestBed.inject(AdminSession).state.set({ status: 'unknown' });
      await open('/admin', ME_DONE);
      expect(router.url).toBe('/admin');
      expect(el.querySelector('.codes')).toBeNull();
    });

    it('goes back to the login screen with a notice when the session expired', async () => {
      await open('/admin', ME_NEW);
      fill('currentPassword', 'old-password-1');
      fill('newPassword', 'new-password-123');
      fill('confirmPassword', 'new-password-123');
      await submit();
      (await next('/api/auth/change-password', 'POST')).flush(null, { status: 401, statusText: 'Unauthorized' });
      await settle();
      await settle();
      expect(router.url).toBe('/admin/login');
      expect(el.textContent).toContain(STRINGS.admin.sessionExpired);
    });
  });

  describe('hardening', () => {
    async function toTotp() {
      await open('/admin', ME_TOTP);
      (await next('/api/auth/totp/setup', 'POST')).flush({ key: KEY, uri: URI });
      await settle();
    }

    async function toRecovery() {
      await toTotp();
      fill('code', '123456');
      await submit();
      (await next('/api/auth/totp/enable', 'POST')).flush({ recoveryCodes: CODES });
      await settle();
    }

    const buttonByText = (text: string) =>
      [...el.querySelectorAll<HTMLButtonElement>('app-button button')].find((b) => b.textContent!.trim() === text)!;

    function confirmSaved() {
      const box = el.querySelector<HTMLInputElement>('.confirm input')!;
      box.checked = true;
      box.dispatchEvent(new Event('change'));
    }

    it('keeps the unconfirmed codes in the session and resumes the recovery step on remount (no 409 round trip)', async () => {
      TestBed.inject(AdminSession).recoveryCodes.set(CODES);
      await open('/admin/setup', ME_TOTP);
      expect(heading()).toBe(STRINGS.admin.setup.recovery.heading);
      expect(el.querySelectorAll('.codes li').length).toBe(10);
      http.expectNone('/api/auth/totp/setup');
    });

    it('does not navigate to the landing page when setup answers 409 while codes are pending', async () => {
      await toTotp();
      TestBed.inject(AdminSession).recoveryCodes.set(CODES);
      (harness.routeDebugElement!.componentInstance as { loadTotp(): void }).loadTotp();
      (await next('/api/auth/totp/setup', 'POST')).flush(null, { status: 409, statusText: 'Conflict' });
      await settle();
      expect(router.url).toBe('/admin/setup');
      expect(heading()).toBe(STRINGS.admin.setup.recovery.heading);
    });

    it('goes to the landing page without an error when setup answers 409 and nothing is pending', async () => {
      await open('/admin', ME_TOTP);
      (await next('/api/auth/totp/setup', 'POST')).flush(null, { status: 409, statusText: 'Conflict' });
      (await next('/api/auth/me')).flush(ME_DONE);
      await settle();
      expect(router.url).toBe('/admin');
      expect(el.querySelector('[role="alert"]')).toBeNull();
    });

    it('disables sign-out on the recovery step until the codes are confirmed', async () => {
      await toRecovery();
      expect(buttonByText(STRINGS.admin.logout).disabled).toBe(true);
      confirmSaved();
      await settle();
      expect(buttonByText(STRINGS.admin.logout).disabled).toBe(false);
    });

    it('warns on unload while codes are unconfirmed and removes the listener on continue', async () => {
      const add = vi.spyOn(window, 'addEventListener');
      const remove = vi.spyOn(window, 'removeEventListener');
      await toRecovery();
      const call = add.mock.calls.find((c) => c[0] === 'beforeunload')!;
      expect(call).toBeDefined();
      const handler = call[1] as (e: BeforeUnloadEvent) => void;
      const event = new Event('beforeunload', { cancelable: true }) as BeforeUnloadEvent;
      handler(event);
      expect(event.defaultPrevented).toBe(true);

      confirmSaved();
      await settle();
      buttonByText(STRINGS.admin.setup.recovery.continue).click();
      await settle();
      expect(remove).toHaveBeenCalledWith('beforeunload', handler);
      expect(TestBed.inject(AdminSession).recoveryPending()).toBe(false);
    });

    it('wipes the pending codes on sign-out', async () => {
      const session = TestBed.inject(AdminSession);
      await toRecovery();
      expect(session.recoveryPending()).toBe(true);
      confirmSaved();
      await settle();
      buttonByText(STRINGS.admin.logout).click();
      (await next('/api/auth/logout', 'POST')).flush(null, { status: 204, statusText: 'No Content' });
      await settle();
      expect(session.recoveryPending()).toBe(false);
    });

    it('treats an enable response without recovery codes as a server error', async () => {
      await toTotp();
      fill('code', '123456');
      await submit();
      (await next('/api/auth/totp/enable', 'POST')).flush({});
      await settle();
      expect(TestBed.inject(AdminSession).recoveryPending()).toBe(false);
      expect(el.querySelector('.codes')).toBeNull();
      expect(el.querySelector('.alert')!.textContent).toContain(STRINGS.errors.server);
    });

    it('ignores a late TOTP setup response after sign-out', async () => {
      await open('/admin', ME_TOTP);
      const late = await next('/api/auth/totp/setup', 'POST');
      buttonByText(STRINGS.admin.logout).click();
      (await next('/api/auth/logout', 'POST')).flush(null, { status: 204, statusText: 'No Content' });
      await settle();
      expect(late.cancelled).toBe(true);
      await settle();
      expect(router.url).toBe('/admin/login');
      expect(el.textContent).not.toContain('ABCD');
    });

    it('downloads the codes as a text file', async () => {
      await toRecovery();
      let blob: Blob | null = null;
      vi.spyOn(URL, 'createObjectURL').mockImplementation((b) => {
        blob = b as Blob;
        return 'blob:codes';
      });
      vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
      let download = '';
      vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
        download = this.download;
      });
      buttonByText(STRINGS.admin.setup.recovery.download).click();
      expect(download).toBe(STRINGS.admin.setup.recovery.fileName);
      const text = await new Promise<string>((resolve) => {
        const reader = new FileReader();
        reader.onload = () => resolve(String(reader.result));
        reader.readAsText(blob!);
      });
      expect(text.trim().split('\n')).toEqual(CODES);
    });

    it('keeps the signed-in state when re-reading the session fails after the password change', async () => {
      await open('/admin', ME_NEW);
      fill('currentPassword', 'old-password-1');
      fill('newPassword', 'new-password-123');
      fill('confirmPassword', 'new-password-123');
      await submit();
      (await next('/api/auth/change-password', 'POST')).flush(null, { status: 204, statusText: 'No Content' });
      (await next('/api/auth/me')).flush(null, { status: 500, statusText: 'Server' });
      await settle();
      expect(TestBed.inject(AdminSession).state().status).toBe('authenticated');
      expect(router.url).toBe('/admin/setup');
      expect(el.textContent).toContain(STRINGS.admin.refreshFailed);

      el.querySelector<HTMLButtonElement>('.alert app-button button')!.click();
      (await next('/api/auth/me')).flush(ME_TOTP);
      (await next('/api/auth/totp/setup', 'POST')).flush({ key: KEY, uri: URI });
      await settle();
      expect(heading()).toBe(STRINGS.admin.setup.totp.heading);
    });

    it('shows a retryable error, not a silent login, when the initial session load fails', async () => {
      const nav = harness.navigateByUrl('/admin');
      (await next('/api/auth/me')).flush(null, { status: 500, statusText: 'Server' });
      await nav;
      await settle();
      expect(router.url).toBe('/admin/login');
      expect(TestBed.inject(AdminSession).state().status).toBe('unknown');
      expect(el.querySelector('.alert')!.textContent).toContain(STRINGS.errors.server);
      el.querySelector<HTMLButtonElement>('.alert app-button button')!.click();
      (await next('/api/auth/me')).flush(ME_DONE);
      await settle();
      expect(router.url).toBe('/admin');
    });

    it('still signs in when the follow-up anti-forgery token request fails', async () => {
      await open('/admin/login', 'anonymous');
      fill('username', 'dana');
      fill('password', 'secret-password');
      await submit();
      (await next('/api/auth/login', 'POST')).flush(ME_DONE);
      for (let i = 0; i < 5; i++) {
        await new Promise((r) => setTimeout(r, 0));
        http.match((r) => r.url === '/api/auth/csrf').forEach((r) => r.flush(null, { status: 500, statusText: 'x' }));
      }
      await settle();
      expect(TestBed.inject(AdminSession).state().status).toBe('authenticated');
      expect(router.url).toBe('/admin');
    });

    it('does not send a request for an empty username or password', async () => {
      await open('/admin/login', 'anonymous');
      fill('username', '   ');
      fill('password', 'x');
      await submit();
      fill('username', 'dana');
      fill('password', '');
      await submit();
      http.expectNone('/api/auth/login');
      expect(el.querySelector('.alert')!.textContent).toContain(STRINGS.admin.login.invalid);
    });

    it('keeps dollar-sequences in usernames literal in the greeting', async () => {
      const name = "a$&b$'c";
      await open('/admin', { ...ME_DONE, username: name });
      expect(el.querySelector('.intro')!.textContent).toBe(STRINGS.admin.greeting.replace('{username}', () => name));
    });
  });

  describe('logout', () => {
    it('signs out, clears the session and returns to login', async () => {
      await open('/admin', ME_DONE);
      el.querySelector<HTMLButtonElement>('app-button button')!.click();
      (await next('/api/auth/logout', 'POST')).flush(null, { status: 204, statusText: 'No Content' });
      await settle();
      expect(router.url).toBe('/admin/login');
      expect(TestBed.inject(AdminSession).state().status).toBe('anonymous');
    });

    it('clears locally even when the server call fails, and switches back to the public token', async () => {
      await open('/admin', ME_DONE);
      el.querySelector<HTMLButtonElement>('app-button button')!.click();
      (await next('/api/auth/logout', 'POST')).flush(null, { status: 500, statusText: 'Server' });
      await settle();
      expect(router.url).toBe('/admin/login');
      expect(TestBed.inject(AdminSession).state().status).toBe('anonymous');
      fill('username', 'dana');
      fill('password', 'p');
      await submit();
      const req = await next('/api/auth/login', 'POST');
      expect(req.request.headers.get(CSRF_HEADER)).toBe('public-token');
      req.flush(null, { status: 401, statusText: 'Unauthorized' });
      await settle();
    });
  });
});

describe('qr and key helpers', () => {
  it('builds a QR matrix with a quiet zone', () => {
    const qr = buildQr(URI)!;
    expect(qr.size).toBeGreaterThan(8 + 20);
    expect(qr.path.startsWith('M')).toBe(true);
    expect(buildQr('')).toBeNull();
  });

  it('groups the key by four', () => {
    expect(groupKey('ABCDEFGHIJ')).toBe('ABCD EFGH IJ');
    expect(groupKey('')).toBe('');
  });
});
