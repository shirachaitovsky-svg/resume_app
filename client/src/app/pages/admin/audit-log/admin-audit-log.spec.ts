import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { STRINGS } from '../../../shared/strings.he';
import { expectNoSeriousViolations, expectSingleH1 } from '../../../testing/a11y';
import { ADMIN_ROUTES } from '../admin.routes';

const ME = { username: 'dana', mustChangePassword: false, totpSetupRequired: false };
const T = STRINGS.admin.auditLog;
const PAGING = STRINGS.admin.paging;
const LIST = '/api/admin/audit-log';
const LIST_GET = `${LIST}?limit=50`;
const ID_A = '11111111-1111-1111-1111-111111111111';
const ID_B = '22222222-2222-2222-2222-222222222222';
const ENTITY = '44444444-4444-4444-4444-444444444444';

function entry(id: string, extra: object = {}) {
  return {
    id,
    at: '2026-10-20T08:00:00Z',
    actorId: '33333333-3333-3333-3333-333333333333',
    actorName: 'dana',
    action: 'retry-email',
    entityType: 'email-outbox',
    entityId: ENTITY,
    ...extra,
  };
}

describe('admin audit log screen', () => {
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
    const nav = harness.navigateByUrl('/admin/audit-log');
    (await next('/api/auth/me')).flush(ME);
    await nav;
    const req = await next(LIST_GET);
    if (Array.isArray(list)) req.flush({ items: list, total: total ?? list.length });
    else req.flush(null, { status: list.status, statusText: 'x' });
    await settle();
  }

  const buttonByText = (root: ParentNode, text: string) =>
    Array.from(root.querySelectorAll<HTMLButtonElement>('button')).find((b) => b.textContent?.includes(text))!;
  const rows = () => Array.from(el.querySelectorAll<HTMLElement>('tbody tr'));
  const rangeNote = () => Array.from(el.querySelectorAll<HTMLElement>('.range-note')).at(-1)!;

  it('redirects an anonymous visitor to the login screen', async () => {
    const nav = harness.navigateByUrl('/admin/audit-log');
    (await next('/api/auth/me')).flush(null, { status: 401, statusText: 'Unauthorized' });
    await nav;
    await settle();
    expect(router.url).toBe('/admin/login');
  });

  it('lists entries read-only, with actor, action and entity identifiers, no buttons and no addresses', async () => {
    await openScreen([entry(ID_A), entry(ID_B, { action: 'something-new', actorName: null })]);
    expect(rows().length).toBe(2);
    expect(rows()[0].textContent).toContain('dana');
    expect(rows()[0].textContent).toContain(T.actions['retry-email']);
    expect(rows()[0].textContent).toContain(T.entities['email-outbox']);
    expect(rows()[0].textContent).toContain(ENTITY);
    expect(rows()[1].textContent).toContain('something-new'); // an unknown action is shown as it is
    expect(rows()[1].textContent).toContain(T.actorUnknown);
    expect(el.querySelector('tbody button')).toBeNull();
    expect(el.querySelector('form, input, textarea')).toBeNull();
    expect(el.textContent).toContain(T.readOnly);
    expect(el.textContent).not.toContain('@');
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
    (await next(LIST_GET)).flush({ items: [entry(ID_A)], total: 1 });
    await settle();
    expect(rows().length).toBe(1);
  });

  it('returns to login on a 401', async () => {
    await openScreen({ status: 401 });
    expect(router.url).toBe('/admin/login');
  });

  it('pages with limit and offset', async () => {
    await openScreen([entry(ID_A)], 120);
    expect(rangeNote().textContent).toContain('מוצגות 1 עד 1 מתוך 120');
    expect(buttonByText(el.querySelector('nav.pager')!, PAGING.previous).disabled).toBe(true);
    buttonByText(el.querySelector('nav.pager')!, PAGING.next).click();
    (await next(`${LIST}?limit=50&offset=50`)).flush({ items: [entry(ID_B)], total: 120 });
    await settle();
    expect(rangeNote().textContent).toContain('מוצגות 51 עד 51 מתוך 120');
    buttonByText(el.querySelector('nav.pager')!, PAGING.previous).click();
    (await next(LIST_GET)).flush({ items: [entry(ID_A)], total: 120 });
    await settle();
    expect(rows().length).toBe(1);
  });

  it('shows no pager when everything fits on one page', async () => {
    await openScreen([entry(ID_A)]);
    expect(el.querySelector('nav.pager')).toBeNull();
  });
});
