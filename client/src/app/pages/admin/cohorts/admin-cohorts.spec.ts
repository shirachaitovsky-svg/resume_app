import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { CSRF_HEADER } from '../../../shared/api/csrf.service';
import { STRINGS } from '../../../shared/strings.he';
import { expectNoSeriousViolations, expectSingleH1 } from '../../../testing/a11y';
import { AdminSession } from '../admin-session';
import { ADMIN_ROUTES } from '../admin.routes';

const ME = { username: 'dana', mustChangePassword: false, totpSetupRequired: false };
const T = STRINGS.admin.cohorts;
const LIST = '/api/admin/cohorts';

function cohort(id: string, name: string, extra: object = {}) {
  return {
    id,
    name,
    inviteToken: `tok-${id}`,
    inviteUrl: `http://localhost:4200/join/tok-${id}`,
    createdAt: '2026-10-20T08:00:00Z',
    revoked: false,
    revokedAt: null,
    ...extra,
  };
}

describe('admin cohorts screen', () => {
  let http: HttpTestingController;
  let harness: RouterTestingHarness;
  let router: Router;
  let el: HTMLElement;
  let clipboardBackup: PropertyDescriptor | undefined;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([{ path: 'admin', children: ADMIN_ROUTES }])],
    });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    harness = await RouterTestingHarness.create();
    clipboardBackup = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
  });
  afterEach(() => {
    el?.remove();
    if (clipboardBackup) Object.defineProperty(navigator, 'clipboard', clipboardBackup);
    else delete (navigator as unknown as Record<string, unknown>)['clipboard'];
    vi.restoreAllMocks();
    vi.useRealTimers();
    http.verify();
  });

  function answerCsrf() {
    for (const r of http.match((req) => req.url.endsWith('/csrf'))) r.flush({ token: 'admin-token' });
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

  /** Opens the screen as a signed-in admin and answers the list request. `list` is a status or the items. */
  async function openScreen(list: object[] | { status: number } | 'pending' = []) {
    const nav = harness.navigateByUrl('/admin/cohorts');
    (await next('/api/auth/me')).flush(ME);
    await nav;
    const req = await next(LIST);
    if (list === 'pending') return req;
    if (Array.isArray(list)) req.flush({ items: list, total: list.length });
    else req.flush(null, { status: list.status, statusText: 'x', headers: list.status === 429 ? { 'Retry-After': '7' } : {} });
    await settle();
    return req;
  }

  async function submitName(value: string) {
    const input = el.querySelector<HTMLInputElement>('[data-field="name"] input')!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
  }

  /** Puts the screen into the document (needed for focus assertions); the afterEach removes it. */
  function attach() {
    el.ownerDocument.body.appendChild(el);
  }

  const rows = () => Array.from(el.querySelectorAll<HTMLElement>('.cohort'));
  const buttonByText = (root: ParentNode, text: string) =>
    Array.from(root.querySelectorAll<HTMLButtonElement>('button')).find((b) => b.textContent?.includes(text))!;
  const dialogEl = () => el.ownerDocument.querySelector('dialog')!;
  const dialogOpen = () => dialogEl().hasAttribute('open') || dialogEl().open;

  it('redirects an anonymous visitor to the login screen', async () => {
    const nav = harness.navigateByUrl('/admin/cohorts');
    (await next('/api/auth/me')).flush(null, { status: 401, statusText: 'Unauthorized' });
    await nav;
    await settle();
    expect(router.url).toBe('/admin/login');
  });

  it('shows a busy skeleton while loading', async () => {
    const req = await openScreen('pending');
    harness.detectChanges();
    el = harness.routeNativeElement!;
    expect(el.querySelector('app-loading-skeleton[aria-busy="true"]')).not.toBeNull();
    req.flush({ items: [], total: 0 });
    await settle();
    expect(el.querySelector('app-loading-skeleton')).toBeNull();
  });

  it('lists active and revoked cohorts: only the active one has the link, copy and revoke', async () => {
    await openScreen([cohort('a', 'מחזור א'), cohort('b', 'מחזור ב', { revoked: true, revokedAt: '2026-10-21T08:00:00Z' })]);
    expect(rows().length).toBe(2);
    expect(rows()[0].textContent).toContain('מחזור א');
    expect(rows()[0].textContent).toContain(T.statusActive);
    expect(rows()[0].querySelector('[data-link]')!.textContent).toBe('http://localhost:4200/join/tok-a');
    expect(rows()[0].querySelector('[data-link]')!.getAttribute('dir')).toBe('ltr');
    expect(buttonByText(rows()[0], T.copy)).toBeTruthy();
    expect(buttonByText(rows()[0], T.revoke)).toBeTruthy();
    expect(rows()[1].textContent).toContain('בוטל ב-');
    expect(rows()[1].querySelector('[data-link]')).toBeNull();
    expect(rows()[1].querySelector('button')).toBeNull();
    expect(el.textContent).not.toContain('tok-b');
    await expectNoSeriousViolations(el);
    expectSingleH1(el);
  });

  it('shows the empty state', async () => {
    await openScreen([]);
    expect(el.textContent).toContain(T.empty);
    await expectNoSeriousViolations(el);
    expectSingleH1(el);
  });

  it('shows an error state with a retry that reloads', async () => {
    await openScreen({ status: 500 });
    expect(el.querySelector('app-error-state')).not.toBeNull();
    await expectNoSeriousViolations(el);
    expectSingleH1(el);
    buttonByText(el.querySelector('app-error-state')!, STRINGS.states.retry).click();
    (await next(LIST)).flush({ items: [cohort('a', 'מחזור א')], total: 1 });
    await settle();
    expect(rows().length).toBe(1);
  });

  it('shows the wait time on 429', async () => {
    await openScreen({ status: 429 });
    expect(el.querySelector('app-error-state')!.textContent).toContain('7');
  });

  it('returns to login with the expired notice on a 401 mid-session', async () => {
    await openScreen({ status: 401 });
    expect(router.url).toBe('/admin/login');
    expect(el.textContent).toContain(STRINGS.admin.sessionExpired);
    expect(TestBed.inject(AdminSession).state().status).toBe('anonymous');
  });

  describe('create', () => {
    it('blocks an empty or too long name with no request and keeps the value', async () => {
      await openScreen([]);
      await submitName('   ');
      expect(el.querySelector('[data-field="name"] .error')!.textContent).toContain(T.nameRequired);
      expect(el.querySelector('.create .alert')!.textContent).toContain(T.nameRequired);
      const long = 'ש'.repeat(101);
      const input = el.querySelector<HTMLInputElement>('[data-field="name"] input')!;
      input.removeAttribute('maxlength');
      await submitName(long);
      expect(el.querySelector('[data-field="name"] .error')!.textContent).toContain(T.nameTooLong);
      expect(el.querySelector<HTMLInputElement>('[data-field="name"] input')!.value).toBe(long);
      http.expectNone((r) => r.method === 'POST');
    });

    it('adds the new cohort on top with the link, a status message and a cleared, focused field', async () => {
      await openScreen([cohort('a', 'מחזור א')]);
      attach();
      await submitName('  מחזור חדש ');
      const req = await next(LIST, 'POST');
      expect(req.request.body).toEqual({ name: 'מחזור חדש' });
      expect(req.request.headers.get(CSRF_HEADER)).toBe('admin-token');
      const { revoked, revokedAt, ...created } = cohort('n', 'מחזור חדש');
      void revoked;
      void revokedAt;
      req.flush(created, { status: 201, statusText: 'Created' });
      await settle();
      expect(rows().length).toBe(2);
      expect(rows()[0].textContent).toContain('מחזור חדש');
      const status = el.querySelector('.status-region')!;
      expect(status.getAttribute('role')).toBe('status');
      expect(status.textContent).toContain('http://localhost:4200/join/tok-n');
      expect(buttonByText(status, T.copy)).toBeTruthy();
      const input = el.querySelector<HTMLInputElement>('[data-field="name"] input')!;
      expect(input.value).toBe('');
      expect(document.activeElement).toBe(input);
    });

    it('keeps existing cohorts when a create succeeds while the first list load is still pending', async () => {
      const first = await openScreen('pending');
      harness.detectChanges();
      el = harness.routeNativeElement!;
      await submitName('מחזור חדש');
      const post = await next(LIST, 'POST');
      const created = cohort('n', 'מחזור חדש');
      post.flush(created, { status: 201, statusText: 'Created' });
      // The stale load is superseded; the refetch (made after the create) supplies the list.
      expect(first.cancelled).toBe(true);
      const refetch = await next(LIST);
      refetch.flush({ items: [created, cohort('a', 'מחזור א')], total: 2 });
      await settle();
      expect(rows().map((r) => r.querySelector('h3')!.textContent)).toEqual(['מחזור חדש', 'מחזור א']);
    });

    it('clears the field error and the summary as soon as the user types', async () => {
      await openScreen([]);
      await submitName('  ');
      expect(el.querySelector('.create .alert')).not.toBeNull();
      const input = el.querySelector<HTMLInputElement>('[data-field="name"] input')!;
      input.value = 'מ';
      input.dispatchEvent(new Event('input'));
      await settle();
      expect(el.querySelector('.create .alert')).toBeNull();
      expect(el.querySelector('[data-field="name"] .error')).toBeNull();
    });

    it('sends one request on a double submit', async () => {
      await openScreen([]);
      const input = el.querySelector<HTMLInputElement>('[data-field="name"] input')!;
      input.value = 'מחזור';
      input.dispatchEvent(new Event('input'));
      const form = el.querySelector('form')!;
      form.dispatchEvent(new Event('submit'));
      form.dispatchEvent(new Event('submit'));
      const req = await next(LIST, 'POST');
      expect(buttonByText(el.querySelector('form')!, T.submitting).disabled).toBe(true);
      req.flush(cohort('n', 'מחזור'), { status: 201, statusText: 'Created' });
      await settle();
      http.expectNone((r) => r.method === 'POST' && r.url === LIST);
    });

    it('shows a server 400 on name beside the field and in the summary, keeping the value', async () => {
      await openScreen([]);
      await submitName('מחזור');
      (await next(LIST, 'POST')).flush(
        { errors: { name: ['השם אינו תקין'] } },
        { status: 400, statusText: 'Bad Request' },
      );
      await settle();
      expect(el.querySelector('[data-field="name"] .error')!.textContent).toContain('השם אינו תקין');
      await expectNoSeriousViolations(el);
      expectSingleH1(el);
      expect(el.querySelector('.create .alert')).not.toBeNull();
      expect(el.querySelector<HTMLInputElement>('[data-field="name"] input')!.value).toBe('מחזור');
    });

    it('shows a top message and keeps the value on a server error', async () => {
      await openScreen([]);
      await submitName('מחזור');
      (await next(LIST, 'POST')).flush(null, { status: 500, statusText: 'x' });
      await settle();
      expect(el.querySelector('.create .alert')!.textContent).toContain(STRINGS.errors.server);
      expect(el.querySelector<HTMLInputElement>('[data-field="name"] input')!.value).toBe('מחזור');
    });
  });

  describe('copy', () => {
    it('writes the link to the clipboard and announces it, then clears the message', async () => {
      const writeText = vi.fn().mockResolvedValue(undefined);
      Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
      await openScreen([cohort('a', 'מחזור א')]);
      vi.useFakeTimers({ toFake: ['setTimeout'] });
      buttonByText(rows()[0], T.copy).click();
      await Promise.resolve();
      await Promise.resolve();
      harness.detectChanges();
      expect(writeText).toHaveBeenCalledWith('http://localhost:4200/join/tok-a');
      expect(el.querySelector('.status-region')!.textContent).toContain(T.copied);
      vi.advanceTimersByTime(5000);
      harness.detectChanges();
      expect(el.querySelector('.status-region')!.textContent).not.toContain(T.copied);
    });

    it('selects the link text for Ctrl+C when the clipboard API is missing', async () => {
      Object.defineProperty(navigator, 'clipboard', { configurable: true, value: undefined });
      await openScreen([cohort('a', 'מחזור א')]);
      buttonByText(rows()[0], T.copy).click();
      await settle();
      expect(el.querySelector('.status-region')!.textContent).toContain(T.copyManual);
      expect(window.getSelection()!.toString()).toBe('http://localhost:4200/join/tok-a');
    });

    it('does not store the link in the URL or browser storage', async () => {
      await openScreen([cohort('a', 'מחזור א')]);
      expect(router.url).not.toContain('tok-a');
      expect(JSON.stringify({ ...localStorage })).not.toContain('tok-a');
      expect(JSON.stringify({ ...sessionStorage })).not.toContain('tok-a');
    });
  });

  describe('revoke', () => {
    async function ask() {
      await openScreen([cohort('a', 'מחזור א'), cohort('b', 'מחזור ב')]);
      buttonByText(rows()[0], T.revoke).click();
      await settle();
    }

    it('asks first: the dialog states the consequence and focuses "keep"', async () => {
      await ask();
      expect(dialogOpen()).toBe(true);
      expect(dialogEl().textContent).toContain('ביטול המחזור "מחזור א" יחסום את קישור ההזמנה שלו');
      expect(dialogEl().textContent).toContain('פרופילים שכבר הוגשו לא יימחקו');
      expect(document.activeElement).toBe(buttonByText(dialogEl(), T.revokeCancel));
      await expectNoSeriousViolations(dialogEl());
      http.expectNone((r) => r.url.includes('/revoke'));
    });

    it('cancelling closes without a request', async () => {
      await ask();
      buttonByText(dialogEl(), T.revokeCancel).click();
      await settle();
      expect(dialogOpen()).toBe(false);
      http.expectNone((r) => r.url.includes('/revoke'));
      expect(rows()[0].querySelector('[data-link]')).not.toBeNull();
    });

    it('Escape closes without a request', async () => {
      await ask();
      dialogEl().dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
      await settle();
      expect(dialogOpen()).toBe(false);
      http.expectNone((r) => r.url.includes('/revoke'));
    });

    it('confirming marks the cohort revoked in place, removes link and actions, without reloading, and keeps focus', async () => {
      await ask();
      attach();
      buttonByText(dialogEl(), T.revokeConfirm).click();
      (await next(`${LIST}/a/revoke`, 'POST')).flush(null, { status: 204, statusText: 'No Content' });
      await settle();
      expect(dialogOpen()).toBe(false);
      expect(rows()[0].textContent).toContain('בוטל ב-');
      expect(rows()[0].querySelector('[data-link]')).toBeNull();
      expect(rows()[0].querySelector('button')).toBeNull();
      expect(rows()[1].querySelector('[data-link]')).not.toBeNull();
      expect(document.activeElement).toBe(rows()[0]);
      http.expectNone(LIST);
    });

    it('removes the "created" notice when the just-created cohort is revoked', async () => {
      await openScreen([]);
      await submitName('מחזור');
      (await next(LIST, 'POST')).flush(cohort('n', 'מחזור'), { status: 201, statusText: 'Created' });
      await settle();
      expect(el.querySelector('.created')).not.toBeNull();
      buttonByText(rows()[0], T.revoke).click();
      await settle();
      buttonByText(dialogEl(), T.revokeConfirm).click();
      (await next(`${LIST}/n/revoke`, 'POST')).flush(null, { status: 204, statusText: 'No Content' });
      await settle();
      expect(el.querySelector('.created')).toBeNull();
    });

    it('409 refreshes the list quietly, showing the cohort as revoked and no error', async () => {
      await ask();
      attach();
      buttonByText(dialogEl(), T.revokeConfirm).click();
      (await next(`${LIST}/a/revoke`, 'POST')).flush(null, { status: 409, statusText: 'Conflict' });
      (await next(LIST)).flush({
        items: [cohort('a', 'מחזור א', { revoked: true, revokedAt: '2026-10-21T08:00:00Z' }), cohort('b', 'מחזור ב')],
        total: 2,
      });
      await settle();
      expect(el.querySelector('.alert')).toBeNull();
      expect(rows()[0].textContent).toContain('בוטל ב-');
      expect(document.activeElement).toBe(rows()[0]);
    });

    it('404 refreshes the list and shows a short notice', async () => {
      await ask();
      attach();
      buttonByText(dialogEl(), T.revokeConfirm).click();
      (await next(`${LIST}/a/revoke`, 'POST')).flush(null, { status: 404, statusText: 'Not Found' });
      (await next(LIST)).flush({ items: [cohort('b', 'מחזור ב')], total: 1 });
      await settle();
      expect(el.textContent).toContain(T.revokeNotFound);
      expect(rows().length).toBe(1);
      expect(document.activeElement).toBe(el.querySelector('.list-notice'));
    });

    it('other errors show a message above the list and keep the cohort active', async () => {
      await ask();
      buttonByText(dialogEl(), T.revokeConfirm).click();
      (await next(`${LIST}/a/revoke`, 'POST')).flush(null, { status: 500, statusText: 'x' });
      await settle();
      expect(el.querySelector('.list-heading + .alert')!.textContent).toContain(STRINGS.errors.server);
      expect(rows()[0].querySelector('[data-link]')).not.toBeNull();
    });
  });

  describe('navigation', () => {
    it('the landing page links to the cohorts screen', async () => {
      const nav = harness.navigateByUrl('/admin');
      (await next('/api/auth/me')).flush(ME);
      await nav;
      await settle();
      const link = el.querySelector<HTMLAnchorElement>('a[href="/admin/cohorts"]');
      expect(link).not.toBeNull();
      expect(link!.textContent).toContain(T.navLink);
    });

    it('the cohorts screen links back to the landing page and has a logout button', async () => {
      await openScreen([]);
      expect(el.querySelector('a.back')!.getAttribute('href')).toBe('/admin');
      expect(buttonByText(el, STRINGS.admin.logout)).toBeTruthy();
    });
  });
});
