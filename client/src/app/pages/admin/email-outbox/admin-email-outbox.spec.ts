import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { STRINGS } from '../../../shared/strings.he';
import { expectNoSeriousViolations, expectSingleH1 } from '../../../testing/a11y';
import { ADMIN_ROUTES } from '../admin.routes';

const ME = { username: 'dana', mustChangePassword: false, totpSetupRequired: false };
const T = STRINGS.admin.emailOutbox;
const PAGING = STRINGS.admin.paging;
const LIST = '/api/admin/email-outbox';
const LIST_GET = `${LIST}/stuck?limit=100`;
const ID_A = '11111111-1111-1111-1111-111111111111';
const ID_B = '22222222-2222-2222-2222-222222222222';

function email(id: string, extra: object = {}) {
  return {
    id,
    templateKey: 'contact-delivery',
    profileId: null,
    requestId: '99999999-9999-9999-9999-999999999999',
    status: 'dead',
    createdAt: '2026-10-20T08:00:00Z',
    ageSeconds: 7200,
    attempts: 5,
    lastErrorCategory: 'send-rejected',
    ...extra,
  };
}

const stuckPending = (id: string) =>
  email(id, {
    status: 'pending',
    templateKey: 'submission-returned',
    profileId: ID_A,
    requestId: null,
    lastErrorCategory: null,
    attempts: 0,
    ageSeconds: 1800,
  });

describe('admin stuck emails screen', () => {
  let http: HttpTestingController;
  let harness: RouterTestingHarness;
  let router: Router;
  let el: HTMLElement;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([{ path: 'admin', children: ADMIN_ROUTES }])],
    });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    harness = await RouterTestingHarness.create();
  });
  afterEach(() => {
    el?.remove();
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

  async function openScreen(list: object[] | { status: number }, total?: number) {
    const nav = harness.navigateByUrl('/admin/email-outbox');
    (await next('/api/auth/me')).flush(ME);
    await nav;
    const req = await next(LIST_GET);
    if (Array.isArray(list)) req.flush({ items: list, total: total ?? list.length });
    else req.flush(null, { status: list.status, statusText: 'x' });
    await settle();
  }

  const buttonByText = (root: ParentNode, text: string) =>
    Array.from(root.querySelectorAll<HTMLButtonElement>('button')).find((b) => b.textContent?.includes(text))!;
  const dialogEl = () => el.ownerDocument.querySelector('dialog')!;
  const rows = () => Array.from(el.querySelectorAll<HTMLElement>('tbody tr'));

  async function askRetry() {
    await openScreen([email(ID_A), stuckPending(ID_B)]);
    buttonByText(rows()[0], T.retry).click();
    await settle();
  }

  it('redirects an anonymous visitor to the login screen', async () => {
    const nav = harness.navigateByUrl('/admin/email-outbox');
    (await next('/api/auth/me')).flush(null, { status: 401, statusText: 'Unauthorized' });
    await nav;
    await settle();
    expect(router.url).toBe('/admin/login');
  });

  it('lists dead and stuck rows with type, entity, state, age and attempts, and no address or text', async () => {
    await openScreen([email(ID_A), stuckPending(ID_B)]);
    expect(rows().length).toBe(2);
    expect(rows()[0].textContent).toContain(T.templates['contact-delivery']);
    expect(rows()[0].textContent).toContain('בקשה 99999999');
    expect(rows()[0].textContent).toContain(T.statusDead);
    expect(rows()[0].textContent).toContain('2 שעות');
    expect(rows()[0].textContent).toContain('send-rejected');
    expect(rows()[1].textContent).toContain(T.statusPending);
    expect(rows()[1].textContent).toContain('30 דקות');
    expect(rows()[1].textContent).toContain('פרופיל 11111111');
    expect(el.textContent).not.toContain('@');
    // Only a dead row can be retried.
    expect(rows()[0].querySelector('button')).not.toBeNull();
    expect(rows()[1].querySelector('button')).toBeNull();
    await expectNoSeriousViolations(el);
    expectSingleH1(el);
  });

  it('shows the empty state', async () => {
    await openScreen([]);
    expect(el.textContent).toContain(T.empty);
    await expectNoSeriousViolations(el);
    expectSingleH1(el);
  });

  it('shows an error state that reloads on retry', async () => {
    await openScreen({ status: 500 });
    await expectNoSeriousViolations(el);
    expectSingleH1(el);
    buttonByText(el.querySelector('app-error-state')!, STRINGS.states.retry).click();
    (await next(LIST_GET)).flush({ items: [email(ID_A)], total: 1 });
    await settle();
    expect(rows().length).toBe(1);
  });

  it('returns to login on a 401', async () => {
    await openScreen({ status: 401 });
    expect(router.url).toBe('/admin/login');
  });

  it('asks first, and cancel sends nothing', async () => {
    await askRetry();
    expect(dialogEl().textContent).toContain('יחזור לתור ויישלח שוב');
    await expectNoSeriousViolations(dialogEl());
    buttonByText(dialogEl(), T.retryCancel).click();
    await settle();
    http.expectNone((r) => r.method === 'POST');
    expect(dialogEl().open).toBe(false);
  });

  it('confirming retries the email, reloads the list and shows a success message', async () => {
    await askRetry();
    buttonByText(dialogEl(), T.retryConfirm).click();
    (await next(`${LIST}/${ID_A}/retry`, 'POST')).flush({ id: ID_A, status: 'pending' });
    (await next(LIST_GET)).flush({ items: [stuckPending(ID_B)], total: 1 });
    await settle();
    expect(el.textContent).toContain(T.retried);
    expect(rows().length).toBe(1);
    expect(el.querySelector('.alert')).toBeNull();
  });

  it('sends one request on a double click of confirm', async () => {
    await askRetry();
    const confirm = buttonByText(dialogEl(), T.retryConfirm);
    confirm.click();
    confirm.click();
    (await next(`${LIST}/${ID_A}/retry`, 'POST')).flush({ id: ID_A, status: 'pending' });
    (await next(LIST_GET)).flush({ items: [], total: 0 });
    await settle();
    http.expectNone((r) => r.method === 'POST');
  });

  it('409 reloads the list quietly and says the email is no longer failed', async () => {
    await askRetry();
    buttonByText(dialogEl(), T.retryConfirm).click();
    (await next(`${LIST}/${ID_A}/retry`, 'POST')).flush(null, { status: 409, statusText: 'Conflict' });
    (await next(LIST_GET)).flush({ items: [stuckPending(ID_B)], total: 1 });
    await settle();
    expect(el.textContent).toContain(T.notDead);
    expect(rows().length).toBe(1);
    expect(el.querySelector('.alert')).toBeNull();
  });

  it('404 reloads the list and says the email was not found', async () => {
    await askRetry();
    buttonByText(dialogEl(), T.retryConfirm).click();
    (await next(`${LIST}/${ID_A}/retry`, 'POST')).flush(null, { status: 404, statusText: 'Not Found' });
    (await next(LIST_GET)).flush({ items: [stuckPending(ID_B)], total: 1 });
    await settle();
    expect(el.textContent).toContain(T.notFound);
  });

  it('a 401 on retry returns to the login screen', async () => {
    await askRetry();
    buttonByText(dialogEl(), T.retryConfirm).click();
    (await next(`${LIST}/${ID_A}/retry`, 'POST')).flush(null, { status: 401, statusText: 'Unauthorized' });
    await settle();
    expect(router.url).toBe('/admin/login');
  });

  it('other errors show a message and keep the rows', async () => {
    await askRetry();
    buttonByText(dialogEl(), T.retryConfirm).click();
    (await next(`${LIST}/${ID_A}/retry`, 'POST')).flush(null, { status: 500, statusText: 'x' });
    await settle();
    expect(el.querySelector('.alert')!.textContent).toContain(STRINGS.errors.server);
    expect(rows().length).toBe(2);
  });

  it('pages with limit and offset, and disables previous on the first page', async () => {
    await openScreen([email(ID_A)], 150);
    expect(el.querySelector('.range-note')!.textContent).toContain('מוצגות 1 עד 1 מתוך 150');
    expect(buttonByText(el.querySelector('nav.pager')!, PAGING.previous).disabled).toBe(true);
    buttonByText(el.querySelector('nav.pager')!, PAGING.next).click();
    (await next(`${LIST}/stuck?limit=100&offset=100`)).flush({ items: [email(ID_B)], total: 150 });
    await settle();
    expect(el.querySelector('.range-note')!.textContent).toContain('מוצגות 101 עד 101 מתוך 150');
    expect(buttonByText(el.querySelector('nav.pager')!, PAGING.next).disabled).toBe(true);
    buttonByText(el.querySelector('nav.pager')!, PAGING.previous).click();
    (await next(LIST_GET)).flush({ items: [email(ID_A)], total: 150 });
    await settle();
    expect(rows().length).toBe(1);
  });

  it('a failed quiet reload after a successful retry shows the failure and no success notice', async () => {
    await askRetry();
    buttonByText(dialogEl(), T.retryConfirm).click();
    (await next(`${LIST}/${ID_A}/retry`, 'POST')).flush({ id: ID_A, status: 'pending' });
    (await next(LIST_GET)).flush(null, { status: 500, statusText: 'x' });
    await settle();
    expect(el.querySelector('.alert')!.textContent).toContain(STRINGS.errors.server);
    expect(el.querySelector('.list-notice')).toBeNull();
    expect(el.textContent).not.toContain(T.retried);
  });

  it('shows no NaN when the age is missing or not a number', async () => {
    await openScreen([email(ID_A, { ageSeconds: undefined }), email(ID_B, { ageSeconds: 'x' })]);
    expect(rows().length).toBe(2);
    expect(el.textContent).not.toContain('NaN');
  });

  it('shows no pager when everything fits on one page', async () => {
    await openScreen([email(ID_A)]);
    expect(el.querySelector('nav.pager')).toBeNull();
  });
});
