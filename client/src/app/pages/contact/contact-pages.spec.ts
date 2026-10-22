import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { STRINGS } from '../../shared/strings.he';
import { expectNoSeriousViolations, expectSingleH1 } from '../../testing/a11y';
import { ContactDecidePage } from './decide/contact-decide';
import { ContactVerifyPage } from './verify/contact-verify';

const VERIFY = '/api/contact-requests/verify';
const PREVIEW = '/api/contact-decisions/preview';
const DECIDE = '/api/contact-decisions/decide';

describe('contact link pages', () => {
  let http: HttpTestingController;
  let harness: RouterTestingHarness;
  let el: HTMLElement;
  let replaceState: ReturnType<typeof vi.spyOn>;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'contact/verify', component: ContactVerifyPage },
          { path: 'contact/decide', component: ContactDecidePage },
        ]),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
    replaceState = vi.spyOn(window.history, 'replaceState');
  });
  afterEach(() => {
    replaceState.mockRestore();
    http.verify();
  });

  async function settle() {
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
  }

  async function open(url: string, component: typeof ContactVerifyPage | typeof ContactDecidePage) {
    await harness.navigateByUrl(url, component as never);
    el = harness.routeNativeElement as HTMLElement;
    await settle();
  }

  async function expectPost(url: string): Promise<TestRequest> {
    for (let i = 0; i < 10; i++) {
      await new Promise((r) => setTimeout(r, 0));
      http.match((r) => r.url.includes('csrf')).forEach((r) => r.flush({ token: 't' }));
      const found = http.match(url);
      if (found.length > 0) return found[0];
    }
    throw new Error(`no POST ${url}`);
  }

  const clearedFragment = () =>
    replaceState.mock.calls.some((c: unknown[]) => typeof c[2] === 'string' && !(c[2] as string).includes('#'));

  describe('verify', () => {
    it('clears the fragment and posts the token in the body once', async () => {
      await open('/contact/verify#token=tok_123-abc', ContactVerifyPage);
      expect(clearedFragment()).toBe(true);
      const req = await expectPost(VERIFY);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ token: 'tok_123-abc' });
      expect(req.request.url).not.toContain('tok_123');
      req.flush({ status: 'submitted' });
      await settle();
      expect(el.textContent).toContain(STRINGS.contactVerify.successTitle);
    await expectNoSeriousViolations(el);
    expectSingleH1(el);
    });

    it('shows the invalid message on 401', async () => {
      await open('/contact/verify#token=bad', ContactVerifyPage);
      (await expectPost(VERIFY)).flush(null, { status: 401, statusText: 'x' });
      await settle();
      expect(el.textContent).toContain(STRINGS.contactVerify.invalidTitle);
      expect(el.textContent).toContain(STRINGS.contactVerify.invalidBody);
    await expectNoSeriousViolations(el);
    expectSingleH1(el);
    });

    for (const status of [400, 409, 410]) {
      it(`shows the invalid message on ${status} without a retry`, async () => {
        await open('/contact/verify#token=old', ContactVerifyPage);
        (await expectPost(VERIFY)).flush(null, { status, statusText: 'x' });
        // a 400 without field errors looks like a rejected anti-forgery token, so the client retries once
        if (status === 400) (await expectPost(VERIFY)).flush(null, { status, statusText: 'x' });
        await settle();
        expect(el.textContent).toContain(STRINGS.contactVerify.invalidTitle);
        expect(el.querySelector('app-error-state')).toBeNull();
      });
    }

    it('does not post without a token', async () => {
      await open('/contact/verify', ContactVerifyPage);
      http.expectNone(() => true);
      expect(el.textContent).toContain(STRINGS.contactVerify.invalidTitle);
    });

    it('offers a retry on a server error', async () => {
      await open('/contact/verify#token=abc', ContactVerifyPage);
      (await expectPost(VERIFY)).flush(null, { status: 500, statusText: 'x' });
      await settle();
      expect(el.querySelector('app-error-state')).not.toBeNull();
    await expectNoSeriousViolations(el);
    expectSingleH1(el);
    });
  });

  describe('decide', () => {
    it('shows the error state (one h1, no violations) when the preview fails with 500', async () => {
      await open('/contact/decide#token=dec-token', ContactDecidePage);
      (await expectPost(PREVIEW)).flush(null, { status: 500, statusText: 'x' });
      await settle();
      expect(el.querySelector('app-error-state')).not.toBeNull();
      await expectNoSeriousViolations(el);
      expectSingleH1(el);
    });

    it('previews on load, consuming nothing, and shows only the visitor name and reason', async () => {
      await open('/contact/decide#token=dec-token', ContactDecidePage);
      expect(clearedFragment()).toBe(true);
      const req = await expectPost(PREVIEW);
      expect(req.request.body).toEqual({ token: 'dec-token' });
      req.flush({ visitorName: 'דני לוי', reason: 'רוצה **לשתף** פעולה' });
      await settle();
      http.expectNone(DECIDE);
      expect(el.textContent).toContain('דני לוי');
      expect(el.querySelector('app-safe-markdown strong')?.textContent).toBe('לשתף');
      expect(el.textContent).not.toContain('@');
      const labels = Array.from(el.querySelectorAll('button')).map((b) => b.textContent?.trim());
      expect(labels).toContain(STRINGS.contactDecide.approve);
      expect(labels).toContain(STRINGS.contactDecide.decline);
    await expectNoSeriousViolations(el);
    expectSingleH1(el);
    });

    async function ready() {
      await open('/contact/decide#token=dec-token', ContactDecidePage);
      (await expectPost(PREVIEW)).flush({ visitorName: 'דני', reason: 'סיבה' });
      await settle();
    }

    const click = (text: string) =>
      Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.trim() === text)!.click();

    it('approves with an explicit click and shows the thank-you screen', async () => {
      await ready();
      click(STRINGS.contactDecide.approve);
      const req = await expectPost(DECIDE);
      expect(req.request.body).toEqual({ token: 'dec-token', decision: 'approved' });
      req.flush({ decision: 'approved' });
      await settle();
      expect(el.textContent).toContain(STRINGS.contactDecide.approvedTitle);
      expect(el.querySelector('button')).toBeNull();
    await expectNoSeriousViolations(el);
    expectSingleH1(el);
    });

    it('declines with an explicit click', async () => {
      await ready();
      click(STRINGS.contactDecide.decline);
      const req = await expectPost(DECIDE);
      expect(req.request.body).toEqual({ token: 'dec-token', decision: 'declined' });
      req.flush({ decision: 'declined' });
      await settle();
      expect(el.textContent).toContain(STRINGS.contactDecide.declinedTitle);
    });

    for (const [status, code, message] of [
      [401, null, STRINGS.contactDecide.invalid],
      [410, 'link-expired', STRINGS.contactDecide.expired],
      [410, null, STRINGS.contactDecide.expired],
      [409, 'already-decided', STRINGS.contactDecide.alreadyDecided],
    ] as const) {
      it(`shows "${message}" on preview ${status}${code ? ` (${code})` : ''} without buttons`, async () => {
        await open('/contact/decide#token=dec-token', ContactDecidePage);
        (await expectPost(PREVIEW)).flush(code ? { extensions: { code } } : null, { status, statusText: 'x' });
        await settle();
        expect(el.textContent).toContain(message);
        expect(el.querySelector('button')).toBeNull();
        await expectNoSeriousViolations(el);
        expectSingleH1(el);
      });
    }

    it('shows the already-decided message when the decision returns 409', async () => {
      await ready();
      click(STRINGS.contactDecide.approve);
      (await expectPost(DECIDE)).flush(null, { status: 409, statusText: 'x' });
      await settle();
      expect(el.textContent).toContain(STRINGS.contactDecide.alreadyDecided);
    });

    it('allows another try after another failure', async () => {
      await ready();
      click(STRINGS.contactDecide.approve);
      (await expectPost(DECIDE)).flush(null, { status: 500, statusText: 'x' });
      await settle();
      expect(el.textContent).toContain(STRINGS.contactDecide.decideFailed);
      click(STRINGS.contactDecide.approve);
      (await expectPost(DECIDE)).flush({ decision: 'approved' });
      await settle();
      expect(el.textContent).toContain(STRINGS.contactDecide.approvedTitle);
    });

    it('does nothing without a token', async () => {
      await open('/contact/decide', ContactDecidePage);
      http.expectNone(() => true);
      expect(el.textContent).toContain(STRINGS.contactDecide.invalid);
    });
  });
});
