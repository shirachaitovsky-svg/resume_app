import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { STRINGS } from '../../../shared/strings.he';
import { expectNoSeriousViolations, expectSingleH1 } from '../../../testing/a11y';
import { ADMIN_ROUTES } from '../admin.routes';

const ME = { username: 'dana', mustChangePassword: false, totpSetupRequired: false };
const T = STRINGS.admin.profiles;
const LIST = '/api/admin/profiles';
const LIST_GET = `${LIST}?limit=100`;
const ID_A = '11111111-1111-1111-1111-111111111111';
const ID_B = '22222222-2222-2222-2222-222222222222';

function profile(id: string, name: string, extra: object = {}) {
  return {
    profileId: id,
    name,
    productName: `מוצר של ${name}`,
    cohortName: 'מחזור א',
    visibility: 'active',
    hasApproved: true,
    createdAt: '2026-10-20T08:00:00Z',
    ...extra,
  };
}

describe('admin profiles screen', () => {
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

  async function openScreen(list: object[] | { status: number }) {
    const nav = harness.navigateByUrl('/admin/profiles');
    (await next('/api/auth/me')).flush(ME);
    await nav;
    const req = await next(LIST_GET);
    if (Array.isArray(list)) req.flush({ items: list, total: list.length });
    else req.flush(null, { status: list.status, statusText: 'x' });
    await settle();
  }

  const buttonByText = (root: ParentNode, text: string) =>
    Array.from(root.querySelectorAll<HTMLButtonElement>('button')).find((b) => b.textContent?.includes(text))!;
  const dialogEl = () => el.ownerDocument.querySelector('dialog')!;
  const rows = () => Array.from(el.querySelectorAll<HTMLElement>('tbody tr'));

  async function askRemove() {
    await openScreen([profile(ID_A, 'דנה'), profile(ID_B, 'רונית')]);
    buttonByText(rows()[0], T.remove).click();
    await settle();
  }

  it('redirects an anonymous visitor to the login screen', async () => {
    const nav = harness.navigateByUrl('/admin/profiles');
    (await next('/api/auth/me')).flush(null, { status: 401, statusText: 'Unauthorized' });
    await nav;
    await settle();
    expect(router.url).toBe('/admin/login');
  });

  it('lists the published profiles without contact details', async () => {
    await openScreen([profile(ID_A, 'דנה'), profile(ID_B, 'רונית')]);
    expect(rows().length).toBe(2);
    expect(rows()[0].textContent).toContain('דנה');
    expect(rows()[0].textContent).toContain('מוצר של דנה');
    expect(rows()[0].textContent).toContain(T.statusActive);
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
    (await next(LIST_GET)).flush({ items: [profile(ID_A, 'דנה')], total: 1 });
    await settle();
    expect(rows().length).toBe(1);
  });

  it('returns to login on a 401', async () => {
    await openScreen({ status: 401 });
    expect(router.url).toBe('/admin/login');
  });

  it('asks first: the dialog says the profile leaves the public site, and cancel sends nothing', async () => {
    await askRemove();
    expect(dialogEl().textContent).toContain('הפרופיל של דנה יוסר מהאתר הציבורי');
    await expectNoSeriousViolations(dialogEl());
    buttonByText(dialogEl(), T.removeCancel).click();
    await settle();
    http.expectNone((r) => r.method === 'POST');
    expect(dialogEl().open).toBe(false);
  });

  it('confirming removes the profile and updates the row in place', async () => {
    await askRemove();
    buttonByText(dialogEl(), T.removeConfirm).click();
    (await next(`${LIST}/${ID_A}/remove`, 'POST')).flush({ profileId: ID_A, visibility: 'removed' });
    await settle();
    expect(rows()[0].textContent).toContain(T.statusRemoved);
    expect(rows()[0].querySelector('button')).toBeNull();
    expect(rows()[1].querySelector('button')).not.toBeNull();
    expect(el.textContent).toContain(T.removed);
    http.expectNone(LIST_GET);
  });

  it('sends one request on a double click of confirm', async () => {
    await askRemove();
    const confirm = buttonByText(dialogEl(), T.removeConfirm);
    confirm.click();
    confirm.click();
    const post = await next(`${LIST}/${ID_A}/remove`, 'POST');
    post.flush({ profileId: ID_A, visibility: 'removed' });
    await settle();
    http.expectNone((r) => r.method === 'POST');
  });

  it('409 reloads the list quietly and shows a message', async () => {
    await askRemove();
    buttonByText(dialogEl(), T.removeConfirm).click();
    (await next(`${LIST}/${ID_A}/remove`, 'POST')).flush(
      { extensions: { code: 'profile-not-active' } },
      { status: 409, statusText: 'Conflict' },
    );
    (await next(LIST_GET)).flush({ items: [profile(ID_B, 'רונית')], total: 1 });
    await settle();
    expect(el.textContent).toContain(T.alreadyRemoved);
    expect(rows().length).toBe(1);
    expect(el.querySelector('.alert')).toBeNull();
  });

  it('requests limit=100 and shows an "X of Y" note when the total exceeds the rows shown', async () => {
    const nav = harness.navigateByUrl('/admin/profiles');
    (await next('/api/auth/me')).flush(ME);
    await nav;
    const req = await next(LIST_GET);
    expect(req.request.url).toBe(`${LIST}?limit=100`);
    req.flush({ items: [profile(ID_A, 'דנה')], total: 120 });
    await settle();
    expect(el.querySelector('.shown-note')!.textContent).toContain('מוצגות 1 מתוך 120');
  });

  it('shows no "X of Y" note when everything is shown', async () => {
    await openScreen([profile(ID_A, 'דנה')]);
    expect(el.querySelector('.shown-note')).toBeNull();
  });

  it('a failed follow-up reload after a 409 shows the alert and keeps the old rows', async () => {
    await askRemove();
    buttonByText(dialogEl(), T.removeConfirm).click();
    (await next(`${LIST}/${ID_A}/remove`, 'POST')).flush(null, { status: 409, statusText: 'Conflict' });
    (await next(LIST_GET)).flush(null, { status: 500, statusText: 'x' });
    await settle();
    expect(el.querySelector('.alert')!.textContent).toContain(STRINGS.errors.server);
    expect(rows().length).toBe(2);
  });

  it('a quiet reload still in flight cannot overwrite a row just marked removed', async () => {
    await askRemove();
    buttonByText(dialogEl(), T.removeConfirm).click();
    (await next(`${LIST}/${ID_A}/remove`, 'POST')).flush(null, { status: 404, statusText: 'Not Found' });
    const stale = await next(LIST_GET);
    await settle();
    // Remove the other profile while the reload is pending.
    buttonByText(rows()[1], T.remove).click();
    await settle();
    buttonByText(dialogEl(), T.removeConfirm).click();
    (await next(`${LIST}/${ID_B}/remove`, 'POST')).flush({ profileId: ID_B, visibility: 'removed' });
    await settle();
    expect(stale.cancelled).toBe(true);
    expect(rows()[1].textContent).toContain(T.statusRemoved);
  });

  it('404 reloads the list and shows a message', async () => {
    await askRemove();
    buttonByText(dialogEl(), T.removeConfirm).click();
    (await next(`${LIST}/${ID_A}/remove`, 'POST')).flush(null, { status: 404, statusText: 'Not Found' });
    (await next(LIST_GET)).flush({ items: [profile(ID_B, 'רונית')], total: 1 });
    await settle();
    expect(el.textContent).toContain(T.notFound);
  });

  it('other errors show a message and keep the profile active', async () => {
    await askRemove();
    buttonByText(dialogEl(), T.removeConfirm).click();
    (await next(`${LIST}/${ID_A}/remove`, 'POST')).flush(null, { status: 500, statusText: 'x' });
    await settle();
    expect(el.querySelector('.alert')!.textContent).toContain(STRINGS.errors.server);
    expect(rows()[0].querySelector('button')).not.toBeNull();
  });
});
