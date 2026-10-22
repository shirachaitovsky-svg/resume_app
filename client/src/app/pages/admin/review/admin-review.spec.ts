import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { CSRF_HEADER } from '../../../shared/api/csrf.service';
import { STRINGS } from '../../../shared/strings.he';
import { AdminSession } from '../admin-session';
import { ADMIN_ROUTES } from '../admin.routes';
import { ReviewService } from './review.service';

const ME = { username: 'dana', mustChangePassword: false, totpSetupRequired: false };
const T = STRINGS.admin.review;
const D = T.detail;
const LIST = '/api/admin/submissions';
const LIST_GET = `${LIST}?limit=100`;
const ID_A = '11111111-1111-1111-1111-111111111111';
const ID_B = '22222222-2222-2222-2222-222222222222';

function item(id: string, name: string, extra: object = {}) {
  return {
    revisionId: id,
    name,
    productName: `מוצר של ${name}`,
    cohortName: 'מחזור א',
    kind: 'new',
    submittedAt: '2026-10-20T08:00:00Z',
    hasContactChange: false,
    profileRemoved: false,
    ...extra,
  };
}

function detail(id: string, extra: object = {}) {
  return {
    revisionId: id,
    kind: 'new',
    name: 'דנה כהן',
    productName: 'אפליקציה',
    productUrl: 'https://example.com/app',
    cohortName: 'מחזור א',
    submittedAt: '2026-10-20T08:00:00Z',
    email: 'dana@example.com',
    phone: '0501234567',
    description: 'תיאור קצר',
    role: 'מפתחת',
    workplace: 'חברה',
    introText: null,
    introAnswer1: null,
    introAnswer2: null,
    introAnswer3: null,
    avatarId: 'girl-1',
    hasPhoto: false,
    cv: '## ניסיון\n\n<script>alert(1)</script> **חזק**',
    cvInvalid: false,
    published: null,
    changes: [],
    contactChange: null,
    profileRemoved: false,
    ...extra,
  };
}

describe('admin review queue and submission detail', () => {
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

  async function open(url: string, answer: object | { status: number } | null, listUrl: string) {
    const nav = harness.navigateByUrl(url);
    (await next('/api/auth/me')).flush(ME);
    await nav;
    const req = await next(listUrl);
    if (answer === null) return req;
    if ('status' in answer) req.flush(null, { status: answer.status, statusText: 'x' });
    else req.flush(answer);
    await settle();
    return req;
  }

  const openQueue = (answer: object | { status: number } | null) => open('/admin/review', answer, LIST_GET);
  const openDetail = (id: string, answer: object | { status: number }) => open(`/admin/review/${id}`, answer, `${LIST}/${id}`);

  const buttonByText = (root: ParentNode, text: string) =>
    Array.from(root.querySelectorAll<HTMLButtonElement>('button')).find((b) => b.textContent?.includes(text))!;
  const dialogs = () => Array.from(el.ownerDocument.querySelectorAll('dialog'));
  const openDialog = () => dialogs().find((d) => d.hasAttribute('open') || d.open)!;
  const rows = () => Array.from(el.querySelectorAll<HTMLElement>('tbody tr'));

  describe('queue', () => {
    it('redirects an anonymous visitor to the login screen', async () => {
      const nav = harness.navigateByUrl('/admin/review');
      (await next('/api/auth/me')).flush(null, { status: 401, statusText: 'Unauthorized' });
      await nav;
      await settle();
      expect(router.url).toBe('/admin/login');
    });

    it('lists submissions with name, product, cohort, kind, date and a contact-change badge', async () => {
      await openQueue({
        items: [item(ID_A, 'דנה', { kind: 'update', hasContactChange: true }), item(ID_B, 'רונית')],
        total: 2,
      });
      expect(rows().length).toBe(2);
      expect(rows()[0].textContent).toContain('דנה');
      expect(rows()[0].textContent).toContain('מוצר של דנה');
      expect(rows()[0].textContent).toContain('מחזור א');
      expect(rows()[0].textContent).toContain(T.kindUpdate);
      expect(rows()[0].textContent).toContain(T.contactChange);
      expect(rows()[1].textContent).toContain(T.kindNew);
      expect(rows()[1].textContent).not.toContain(T.contactChange);
      expect(rows()[0].querySelector('a')!.getAttribute('href')).toBe(`/admin/review/${ID_A}`);
      // No contact details in the queue.
      expect(el.textContent).not.toContain('@');
    });

    it('shows a busy skeleton while loading', async () => {
      const req = await openQueue(null);
      harness.detectChanges();
      el = harness.routeNativeElement!;
      expect(el.querySelector('app-loading-skeleton[aria-busy="true"]')).not.toBeNull();
      req.flush({ items: [], total: 0 });
      await settle();
      expect(el.querySelector('app-loading-skeleton')).toBeNull();
    });

    it('shows the empty state', async () => {
      await openQueue({ items: [], total: 0 });
      expect(el.textContent).toContain(T.empty);
      expect(el.querySelector('table')).toBeNull();
    });

    it('shows an error state with a retry that reloads', async () => {
      await openQueue({ status: 500 });
      expect(el.querySelector('app-error-state')).not.toBeNull();
      buttonByText(el.querySelector('app-error-state')!, STRINGS.states.retry).click();
      (await next(LIST_GET)).flush({ items: [item(ID_A, 'דנה')], total: 1 });
      await settle();
      expect(rows().length).toBe(1);
    });

    it('returns to login on a 401', async () => {
      await openQueue({ status: 401 });
      expect(router.url).toBe('/admin/login');
      expect(TestBed.inject(AdminSession).state().status).toBe('anonymous');
    });

    it('the landing page links to the queue and to the profiles screen', async () => {
      const nav = harness.navigateByUrl('/admin');
      (await next('/api/auth/me')).flush(ME);
      await nav;
      await settle();
      expect(el.querySelector('a[href="/admin/review"]')!.textContent).toContain(T.navLink);
      expect(el.querySelector('a[href="/admin/profiles"]')!.textContent).toContain(STRINGS.admin.profiles.navLink);
    });
  });

  describe('detail of a new submission', () => {
    it('shows full details including contact data, cv via safe markdown and the photo from /photo', async () => {
      await openDetail(ID_A, detail(ID_A, { hasPhoto: true }));
      expect(el.textContent).toContain('dana@example.com');
      expect(el.textContent).toContain('0501234567');
      expect(el.querySelector('.compare')).toBeNull();
      expect(el.querySelector('[data-field="cv"] app-safe-markdown')).not.toBeNull();
      // HTML in the CV stays literal text.
      expect(el.querySelector('[data-field="cv"] script')).toBeNull();
      expect(el.querySelector('[data-field="cv"]')!.textContent).toContain('<script>');
      expect(el.querySelector('[data-field="photo"] img')!.getAttribute('src')).toBe(`${LIST}/${ID_A}/photo`);
      expect(el.querySelector('[data-field="avatarId"] app-avatar')).not.toBeNull();
    });

    it('404 shows a message with a way back to the queue', async () => {
      await openDetail(ID_A, { status: 404 });
      expect(el.textContent).toContain(D.notFound);
      expect(el.querySelector('a.link-button')!.getAttribute('href')).toBe('/admin/review');
    });

    it('401 returns to login', async () => {
      await openDetail(ID_A, { status: 401 });
      expect(router.url).toBe('/admin/login');
    });

    it('shows a marker and the pending values for a contact change, without any action', async () => {
      await openDetail(ID_A, detail(ID_A, { contactChange: { pendingEmail: 'new@example.com', pendingPhone: null } }));
      expect(el.querySelector('.contact-change')!.textContent).toContain('new@example.com');
      expect(el.querySelector('.contact-change button')).toBeNull();
    });
  });

  describe('detail of an update', () => {
    it('compares published and proposed values and lists the changed fields', async () => {
      const published = {
        name: 'דנה כהן',
        productName: 'ישן',
        productUrl: 'https://example.com/old',
        description: null,
        role: 'מפתחת',
        workplace: 'חברה',
        introText: null,
        introAnswer1: null,
        introAnswer2: null,
        introAnswer3: null,
        avatarId: 'girl-1',
        hasPhoto: false,
        cv: null,
        cvInvalid: false,
      };
      await openDetail(
        ID_A,
        detail(ID_A, { kind: 'update', published, changes: ['productName', 'productUrl', 'description', 'cv'] }),
      );
      const headers = Array.from(el.querySelectorAll('.compare thead th')).map((h) => h.textContent);
      expect(headers).toEqual([D.field, D.publishedNow, D.proposed]);
      const productName = el.querySelector('tr[data-field="productName"]')!;
      expect(productName.classList.contains('changed')).toBe(true);
      expect(productName.querySelectorAll('td')[0].textContent).toContain('ישן');
      expect(productName.querySelectorAll('td')[1].textContent).toContain('אפליקציה');
      expect(el.querySelector('tr[data-field="name"]')!.classList.contains('changed')).toBe(false);
      const listed = Array.from(el.querySelectorAll('.changes-list li')).map((l) => l.textContent);
      expect(listed).toEqual([D.fields.productName, D.fields.productUrl, D.fields.description, D.fields.cv]);
    });
  });

  describe('decisions', () => {
    async function ready() {
      await openDetail(ID_A, detail(ID_A));
    }

    it('approving asks first, then posts the seen revision id and returns to a queue without it', async () => {
      await ready();
      buttonByText(el, D.approve).click();
      await settle();
      expect(openDialog().textContent).toContain('דנה כהן');
      http.expectNone((r) => r.method === 'POST');
      buttonByText(openDialog(), D.approveConfirm).click();
      const post = await next(`${LIST}/${ID_A}/approve`, 'POST');
      expect(post.request.headers.get(CSRF_HEADER)).toBe('admin-token');
      post.flush({ revisionId: ID_A, status: 'approved' });
      const list = await next(LIST_GET);
      list.flush({ items: [item(ID_B, 'רונית')], total: 1 });
      await settle();
      expect(router.url).toBe('/admin/review');
      expect(el.textContent).toContain(T.approvedNotice);
      expect(rows().length).toBe(1);
      expect(el.querySelector(`[data-revision="${ID_A}"]`)).toBeNull();
    });

    it('cancelling the approval sends nothing', async () => {
      await ready();
      buttonByText(el, D.approve).click();
      await settle();
      buttonByText(openDialog(), D.approveCancel).click();
      await settle();
      http.expectNone((r) => r.method === 'POST');
      expect(dialogs().some((d) => d.open)).toBe(false);
    });

    it('sends a single approve request on a double click of confirm', async () => {
      await ready();
      buttonByText(el, D.approve).click();
      await settle();
      const confirm = buttonByText(openDialog(), D.approveConfirm);
      confirm.click();
      confirm.click();
      const post = await next(`${LIST}/${ID_A}/approve`, 'POST');
      expect(buttonByText(openDialog(), D.approveConfirm).disabled).toBe(true);
      post.flush({}, { status: 200, statusText: 'OK' });
      (await next(LIST_GET)).flush({ items: [], total: 0 });
      await settle();
    });

    it('409 on approve goes to a freshly loaded queue with the already-handled message', async () => {
      await ready();
      buttonByText(el, D.approve).click();
      await settle();
      buttonByText(openDialog(), D.approveConfirm).click();
      (await next(`${LIST}/${ID_A}/approve`, 'POST')).flush(
        { extensions: { code: 'revision-not-pending', latestPendingRevisionId: ID_B } },
        { status: 409, statusText: 'Conflict' },
      );
      (await next(LIST_GET)).flush({ items: [item(ID_B, 'רונית')], total: 1 });
      await settle();
      expect(router.url).toBe('/admin/review');
      expect(el.textContent).toContain(T.alreadyHandled);
      expect(el.querySelector('.alert')).toBeNull();
    });

    it('returning with an empty note shows an error beside the field and opens no window', async () => {
      await ready();
      buttonByText(el, D.returnAction).click();
      await settle();
      expect(el.querySelector('[data-field="note"] .error')!.textContent).toContain(D.noteRequired);
      expect(dialogs().some((d) => d.open)).toBe(false);
      http.expectNone((r) => r.method === 'POST');
    });

    it('returning with a too long note shows an error beside the field', async () => {
      await ready();
      const area = el.querySelector<HTMLTextAreaElement>('[data-field="note"] textarea')!;
      area.value = 'א'.repeat(1001);
      area.dispatchEvent(new Event('input'));
      buttonByText(el, D.returnAction).click();
      await settle();
      expect(el.querySelector('[data-field="note"] .error')!.textContent).toContain(D.noteTooLong);
    });

    it('returns with a trimmed note after confirmation and leaves the queue', async () => {
      await ready();
      const area = el.querySelector<HTMLTextAreaElement>('[data-field="note"] textarea')!;
      area.value = '  נא לתקן את הקישור ';
      area.dispatchEvent(new Event('input'));
      buttonByText(el, D.returnAction).click();
      await settle();
      expect(openDialog().textContent).toContain('דנה כהן');
      buttonByText(openDialog(), D.returnConfirm).click();
      const post = await next(`${LIST}/${ID_A}/return`, 'POST');
      expect(post.request.body).toEqual({ note: 'נא לתקן את הקישור' });
      post.flush({ revisionId: ID_A, status: 'returned' });
      (await next(LIST_GET)).flush({ items: [], total: 0 });
      await settle();
      expect(router.url).toBe('/admin/review');
      expect(el.textContent).toContain(T.returnedNotice);
      expect(el.textContent).toContain(T.empty);
    });

    it('a server note error shows beside the field and keeps the typed note', async () => {
      await ready();
      const area = el.querySelector<HTMLTextAreaElement>('[data-field="note"] textarea')!;
      area.value = 'הערה';
      area.dispatchEvent(new Event('input'));
      buttonByText(el, D.returnAction).click();
      await settle();
      buttonByText(openDialog(), D.returnConfirm).click();
      (await next(`${LIST}/${ID_A}/return`, 'POST')).flush(
        { errors: { note: ['ההערה אינה מתאימה'] } },
        { status: 400, statusText: 'Bad Request' },
      );
      await settle();
      expect(el.querySelector('[data-field="note"] .error')!.textContent).toContain('ההערה אינה מתאימה');
      expect(el.querySelector<HTMLTextAreaElement>('[data-field="note"] textarea')!.value).toBe('הערה');
      expect(router.url).toBe(`/admin/review/${ID_A}`);
    });

    it('other errors show a message and keep the submission on screen', async () => {
      await ready();
      buttonByText(el, D.approve).click();
      await settle();
      buttonByText(openDialog(), D.approveConfirm).click();
      (await next(`${LIST}/${ID_A}/approve`, 'POST')).flush(null, { status: 500, statusText: 'x' });
      await settle();
      expect(el.querySelector('.action-error')!.textContent).toContain(STRINGS.errors.server);
      expect(router.url).toBe(`/admin/review/${ID_A}`);
    });

    it('a 401 on approve returns to login', async () => {
      await ready();
      buttonByText(el, D.approve).click();
      await settle();
      buttonByText(openDialog(), D.approveConfirm).click();
      (await next(`${LIST}/${ID_A}/approve`, 'POST')).flush(null, { status: 401, statusText: 'Unauthorized' });
      await settle();
      expect(router.url).toBe('/admin/login');
    });
  });

  describe('extra coverage', () => {
    it('requests limit=100 and shows an "X of Y" note only when the total exceeds the items shown', async () => {
      const req = await openQueue(null);
      expect(req.request.url).toBe(`${LIST}?limit=100`);
      req.flush({ items: [item(ID_A, 'דנה')], total: 150 });
      await settle();
      expect(el.querySelector('.shown-note')!.textContent).toContain('מוצגות 1 מתוך 150');
    });

    it('shows no "X of Y" note when everything is shown', async () => {
      await openQueue({ items: [item(ID_A, 'דנה')], total: 1 });
      expect(el.querySelector('.shown-note')).toBeNull();
    });

    it('shows the profile-removed badge only for such items', async () => {
      await openQueue({ items: [item(ID_A, 'דנה', { profileRemoved: true }), item(ID_B, 'רונית')], total: 2 });
      expect(rows()[0].textContent).toContain(T.profileRemoved);
      expect(rows()[1].textContent).not.toContain(T.profileRemoved);
    });

    it('shows the removed warning on the detail only when the profile is removed', async () => {
      await openDetail(ID_A, detail(ID_A, { profileRemoved: true }));
      expect(el.textContent).toContain(D.removedWarning);
    });

    it('shows no removed warning when the profile is not removed', async () => {
      await openDetail(ID_A, detail(ID_A));
      expect(el.textContent).not.toContain(D.removedWarning);
    });

    it('shows the invalid-cv notice', async () => {
      await openDetail(ID_A, detail(ID_A, { cv: null, cvInvalid: true }));
      expect(el.textContent).toContain(D.cvInvalid);
    });

    it('renders no link for a javascript: product URL', async () => {
      await openDetail(ID_A, detail(ID_A, { productUrl: 'javascript:alert(1)' }));
      expect(el.querySelector('[data-field="productUrl"] a')).toBeNull();
      expect(el.querySelector('[data-field="productUrl"]')!.textContent).toContain('javascript:alert(1)');
    });

    it('renders an https product URL as a link with rel noopener', async () => {
      await openDetail(ID_A, detail(ID_A));
      const link = el.querySelector('[data-field="productUrl"] a')!;
      expect(link.getAttribute('href')).toBe('https://example.com/app');
      expect(link.getAttribute('rel')).toContain('noopener');
    });

    it('in an update with a photo change, shows the has-photo text published and the image proposed', async () => {
      const published = {
        name: 'דנה כהן', productName: 'אפליקציה', productUrl: 'https://example.com/app', description: null, role: null,
        workplace: null, introText: null, introAnswer1: null, introAnswer2: null, introAnswer3: null,
        avatarId: 'girl-1', hasPhoto: true, cv: null, cvInvalid: false,
      };
      await openDetail(ID_A, detail(ID_A, { kind: 'update', hasPhoto: true, published, changes: ['photo'] }));
      const cells = el.querySelectorAll('tr[data-field="photo"] td');
      expect(cells[0].textContent).toContain(D.hasPhoto);
      expect(cells[0].querySelector('img')).toBeNull();
      expect(cells[1].querySelector('img')!.getAttribute('src')).toBe(`${LIST}/${ID_A}/photo`);
      expect(el.querySelector('tr[data-field="photo"] th .sr-only')!.textContent).toContain(STRINGS.admin.changedMark);
    });

    it('accepts a return note of exactly 1000 characters and rejects 1001', async () => {
      await openDetail(ID_A, detail(ID_A));
      const area = () => el.querySelector<HTMLTextAreaElement>('[data-field="note"] textarea')!;
      area().value = 'א'.repeat(1001);
      area().dispatchEvent(new Event('input'));
      buttonByText(el, D.returnAction).click();
      await settle();
      expect(el.querySelector('[data-field="note"] .error')).not.toBeNull();
      expect(dialogs().some((d) => d.open)).toBe(false);
      area().value = 'א'.repeat(1000);
      area().dispatchEvent(new Event('input'));
      buttonByText(el, D.returnAction).click();
      await settle();
      expect(el.querySelector('[data-field="note"] .error')).toBeNull();
      expect(openDialog()).toBeTruthy();
      buttonByText(openDialog(), D.returnConfirm).click();
      const post = await next(`${LIST}/${ID_A}/return`, 'POST');
      expect((post.request.body as { note: string }).note.length).toBe(1000);
      post.flush({ status: 'returned' });
      (await next(LIST_GET)).flush({ items: [], total: 0 });
      await settle();
    });
  });

  describe('review service', () => {
    it('builds the photo URL from the revision id', () => {
      expect(TestBed.inject(ReviewService).photoUrl(ID_A)).toBe(`/api/admin/submissions/${ID_A}/photo`);
    });
  });
});
