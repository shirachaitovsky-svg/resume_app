import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { STRINGS } from '../../../shared/strings.he';
import { expectNoSeriousViolations, expectSingleH1 } from '../../../testing/a11y';
import { AdminSession } from '../admin-session';
import { ADMIN_ROUTES } from '../admin.routes';

const ME = { username: 'dana', mustChangePassword: false, totpSetupRequired: false };
const T = STRINGS.admin.contactRequests;
const LIST = '/api/admin/contact-requests?status=all&limit=100';
const BASE = '/api/admin/contact-requests';
const ID_A = '11111111-1111-1111-1111-111111111111';
const ID_B = '22222222-2222-2222-2222-222222222222';
const ID_C = '33333333-3333-3333-3333-333333333333';

function item(id: string, status: string, extra: object = {}) {
  return {
    requestId: id,
    visitorName: `מבקש ${id.slice(0, 2)}`,
    visitorEmail: 'visitor@example.com',
    reason: 'סיבה עם **הדגשה** <script>alert(1)</script>',
    profileId: 'aaaaaaaa-0000-0000-0000-000000000000',
    profileName: 'נועה כהן',
    productName: 'מתכנן',
    cohortName: 'מחזור א',
    profileVisibility: 'active',
    createdAt: '2026-10-20T08:00:00Z',
    verifiedAt: '2026-10-20T09:00:00Z',
    status,
    participantDecision: null,
    participantDecidedAt: null,
    managerApprovedAt: null,
    notificationStatus: 'none',
    deliveryStatus: 'none',
    deliveryErrorCategory: null,
    ...extra,
  };
}

const SUBMITTED = item(ID_A, 'submitted');
const APPROVED = item(ID_B, 'participant_approved', { participantDecision: 'approved' });
const SENT = item(ID_C, 'sent_to_participant');

describe('admin contact requests', () => {
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
      const found = http.match((req) => req.urlWithParams === url && req.method === method);
      if (found.length > 0) return found[0];
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

  async function openQueue(answer: object | { status: number } | null) {
    const nav = harness.navigateByUrl('/admin/contact-requests');
    (await next('/api/auth/me')).flush(ME);
    await nav;
    const req = await next(LIST);
    if (answer === null) return req;
    if ('status' in answer) req.flush(null, { status: answer.status, statusText: 'x' });
    else req.flush(answer);
    await settle();
    return req;
  }

  const row = (id: string) => el.querySelector<HTMLElement>(`tr[data-request="${id}"]`)!;
  const btn = (id: string, which: 'send' | 'approve') =>
    row(id).querySelector<HTMLButtonElement>(`app-button.${which} button`)!;
  const openDialog = () =>
    Array.from(el.ownerDocument.querySelectorAll('dialog')).find((d) => d.hasAttribute('open') || d.open)!;
  const confirmButton = () => openDialog().querySelector<HTMLButtonElement>('.confirm button')!;

  it('redirects an anonymous visitor to the login screen', async () => {
    const nav = harness.navigateByUrl('/admin/contact-requests');
    (await next('/api/auth/me')).flush(null, { status: 401, statusText: 'Unauthorized' });
    await nav;
    await settle();
    expect(router.url).toBe('/admin/login');
  });

  it('lists requests with both approvals, delivery state, safe reason and no participant contact details', async () => {
    await openQueue({
      items: [
        SUBMITTED,
        item(ID_B, 'participant_approved', {
          participantDecision: 'approved',
          managerApprovedAt: '2026-10-21T08:00:00Z',
          deliveryStatus: 'dead',
          deliveryErrorCategory: 'smtp-rejected',
        }),
      ],
      total: 2,
    });
    expect(el.querySelectorAll('tbody tr').length).toBe(2);
    expect(row(ID_A).textContent).toContain('נועה כהן');
    expect(row(ID_A).textContent).toContain('מחזור א');
    expect(row(ID_A).querySelector('[data-col="participant"]')!.textContent).toContain(T.participantApproval.pending);
    expect(row(ID_A).querySelector('[data-col="manager"]')!.textContent).toContain(T.managerApproval.pending);
    expect(row(ID_B).querySelector('[data-col="participant"]')!.textContent).toContain(T.participantApproval.approved);
    expect(row(ID_B).querySelector('[data-col="manager"]')!.textContent).toContain(T.managerApproval.approved);
    expect(row(ID_B).querySelector('[data-col="delivery"]')!.textContent).toContain(T.delivery['dead']);
    expect(row(ID_B).querySelector('[data-col="delivery"]')!.textContent).toContain('smtp-rejected');
    expect(row(ID_A).querySelector('app-safe-markdown strong')?.textContent).toBe('הדגשה');
    expect(row(ID_A).querySelector('script')).toBeNull();
    expect(el.textContent).not.toMatch(/\d{7,}/);
    await expectNoSeriousViolations(el);
    expectSingleH1(el);
  });

  it('shows the empty state', async () => {
    await openQueue({ items: [], total: 0 });
    expect(el.textContent).toContain(T.empty);
    await expectNoSeriousViolations(el);
    expectSingleH1(el);
  });

  it('shows an error state with a retry that reloads', async () => {
    await openQueue({ status: 500 });
    expect(el.querySelector('app-error-state')).not.toBeNull();
    await expectNoSeriousViolations(el);
    expectSingleH1(el);
    el.querySelector<HTMLButtonElement>('app-error-state button')!.click();
    (await next(LIST)).flush({ items: [SUBMITTED], total: 1 });
    await settle();
    expect(el.querySelectorAll('tbody tr').length).toBe(1);
  });

  it('goes to login on a 401', async () => {
    await openQueue({ status: 401 });
    expect(router.url).toBe('/admin/login');
  });

  it('enables send only for submitted and approve only for participant_approved, explaining the lock', async () => {
    await openQueue({ items: [SUBMITTED, SENT, APPROVED], total: 3 });
    expect(btn(ID_A, 'send').disabled).toBe(false);
    expect(btn(ID_A, 'approve').disabled).toBe(true);
    expect(row(ID_A).textContent).toContain(T.lockedSubmitted);
    expect(btn(ID_C, 'send').disabled).toBe(true);
    expect(btn(ID_C, 'approve').disabled).toBe(true);
    expect(row(ID_C).textContent).toContain(T.lockedSent);
    await expectNoSeriousViolations(el);
    expectSingleH1(el);
    expect(btn(ID_B, 'send').disabled).toBe(true);
    expect(btn(ID_B, 'approve').disabled).toBe(false);
  });

  it('sends to the participant only after confirming, then reloads', async () => {
    await openQueue({ items: [SUBMITTED], total: 1 });
    btn(ID_A, 'send').click();
    await settle();
    http.expectNone((r) => r.method === 'POST');
    expect(openDialog().textContent).toContain('נועה כהן');
    await expectNoSeriousViolations(openDialog());
    confirmButton().click();
    (await next(`${BASE}/${ID_A}/send-to-participant`, 'POST')).flush({ requestId: ID_A, status: 'sent_to_participant' });
    (await next(LIST)).flush({ items: [item(ID_A, 'sent_to_participant')], total: 1 });
    await settle();
    expect(el.textContent).toContain(T.sentNotice);
    expect(btn(ID_A, 'send').disabled).toBe(true);
  });

  it('on 409 request-not-submitted reloads quietly with a message', async () => {
    await openQueue({ items: [SUBMITTED], total: 1 });
    btn(ID_A, 'send').click();
    await settle();
    confirmButton().click();
    (await next(`${BASE}/${ID_A}/send-to-participant`, 'POST')).flush(
      { extensions: { code: 'request-not-submitted' } },
      { status: 409, statusText: 'Conflict' },
    );
    (await next(LIST)).flush({ items: [item(ID_A, 'sent_to_participant')], total: 1 });
    await settle();
    expect(el.textContent).toContain(T.staleNotice);
    expect(el.querySelector('app-error-state')).toBeNull();
  });

  it('approves delivery with a confirmation that names the result, then shows pending delivery', async () => {
    await openQueue({ items: [APPROVED], total: 1 });
    btn(ID_B, 'approve').click();
    await settle();
    const text = openDialog().textContent!;
    expect(text).toContain('נועה כהן');
    expect(text).toContain(`מבקש ${ID_B.slice(0, 2)}`);
    confirmButton().click();
    (await next(`${BASE}/${ID_B}/approve-delivery`, 'POST')).flush({ requestId: ID_B, status: 'participant_approved', delivery: 'pending' });
    (await next(LIST)).flush({
      items: [item(ID_B, 'participant_approved', { participantDecision: 'approved', managerApprovedAt: '2026-10-22T08:00:00Z', deliveryStatus: 'pending' })],
      total: 1,
    });
    await settle();
    expect(row(ID_B).querySelector('[data-col="delivery"]')!.textContent).toContain(T.delivery['pending']);
    expect(btn(ID_B, 'approve').disabled).toBe(true);
  });

  it('on 409 request-not-approved reloads and explains', async () => {
    await openQueue({ items: [APPROVED], total: 1 });
    btn(ID_B, 'approve').click();
    await settle();
    confirmButton().click();
    (await next(`${BASE}/${ID_B}/approve-delivery`, 'POST')).flush(
      { extensions: { code: 'request-not-approved' } },
      { status: 409, statusText: 'Conflict' },
    );
    (await next(LIST)).flush({ items: [item(ID_B, 'participant_declined', { participantDecision: 'declined' })], total: 1 });
    await settle();
    expect(el.textContent).toContain(T.staleNotice);
    expect(btn(ID_B, 'approve').disabled).toBe(true);
    expect(row(ID_B).textContent).toContain(T.lockedDeclined);
  });

  it('allows a manual retry after a dead delivery', async () => {
    await openQueue({
      items: [item(ID_B, 'participant_approved', { participantDecision: 'approved', managerApprovedAt: '2026-10-22T08:00:00Z', deliveryStatus: 'dead' })],
      total: 1,
    });
    expect(btn(ID_B, 'approve').disabled).toBe(false);
    expect(btn(ID_B, 'approve').textContent).toContain(T.retryDelivery);
  });

  it('cancelling the dialog sends nothing', async () => {
    await openQueue({ items: [APPROVED], total: 1 });
    btn(ID_B, 'approve').click();
    await settle();
    openDialog().querySelector<HTMLButtonElement>('.cancel button')!.click();
    await settle();
    http.expectNone((r) => r.method === 'POST');
  });

  it('the landing page links to the screen', async () => {
    const nav = harness.navigateByUrl('/admin');
    (await next('/api/auth/me')).flush(ME);
    await nav;
    await settle();
    expect(el.querySelector('a[href="/admin/contact-requests"]')!.textContent).toContain(T.navLink);
    expect(TestBed.inject(AdminSession).state().status).toBe('authenticated');
  });
});
