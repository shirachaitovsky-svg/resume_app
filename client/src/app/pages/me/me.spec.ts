import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { STRINGS } from '../../shared/strings.he';
import { MePage } from './me';

const ME = '/api/auth/google/me';
const LOGOUT = '/api/auth/google/logout';

describe('MePage', () => {
  let http: HttpTestingController;
  let harness: RouterTestingHarness;
  let el: HTMLElement;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([{ path: 'me', component: MePage }])],
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

  async function open(url: string): Promise<TestRequest> {
    await harness.navigateByUrl(url, MePage);
    el = harness.routeNativeElement as HTMLElement;
    return expectGet();
  }

  async function expectGet(): Promise<TestRequest> {
    for (let i = 0; i < 10; i++) {
      await new Promise((r) => setTimeout(r, 0));
      const found = http.match(ME);
      if (found.length > 0) return found[0];
    }
    throw new Error('no GET me');
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

  const unauth = { status: 401, statusText: 'Unauthorized' };

  it('shows a loading skeleton while the request is pending', async () => {
    const req = await open('/me');
    harness.detectChanges();
    expect(el.querySelector('app-loading-skeleton')).not.toBeNull();
    req.flush(null, unauth);
    await settle();
  });

  it('shows the Google sign-in link when signed out', async () => {
    (await open('/me')).flush(null, unauth);
    await settle();
    expect(el.querySelector('h1')?.textContent).toContain(STRINGS.myProfile.heading);
    const link = el.querySelector('a.google-signin') as HTMLAnchorElement;
    expect(link.textContent).toContain(STRINGS.myProfile.signIn);
    expect(link.getAttribute('href')).toBe('/api/auth/google/start?returnUrl=%2Fme');
    expect(link.hasAttribute('routerLink')).toBe(false);
    expect(el.querySelector('[role="alert"]')).toBeNull();
  });

  it('shows the email and sign-out when signed in, without any signin message', async () => {
    (await open('/me?signin=failed')).flush({ email: 'a@b.com', hasProfile: false });
    await settle();
    expect(el.textContent).toContain('a@b.com');
    expect(el.textContent).toContain(STRINGS.myProfile.signOut);
    expect(el.querySelector('[role="alert"]')).toBeNull();
    expect(el.querySelector('a.google-signin')).toBeNull();
  });

  it.each([
    ['cancelled', STRINGS.myProfile.signinCancelled],
    ['failed', STRINGS.myProfile.signinFailed],
    ['unverified', STRINGS.myProfile.signinUnverified],
  ])('shows the message for signin=%s and removes the parameter', async (value, message) => {
    (await open(`/me?signin=${value}`)).flush(null, unauth);
    await settle();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain(message);
    expect(TestBed.inject(Router).url).toBe('/me');
  });

  it('ignores an unknown signin value but still removes it', async () => {
    (await open('/me?signin=x')).flush(null, unauth);
    await settle();
    expect(el.querySelector('[role="alert"]')).toBeNull();
    expect(TestBed.inject(Router).url).toBe('/me');
  });

  it('shows an error state on 500 and retries', async () => {
    (await open('/me')).flush({}, { status: 500, statusText: 'Server Error' });
    await settle();
    expect(el.querySelector('app-error-state')).not.toBeNull();
    (el.querySelector('app-error-state app-button') as HTMLElement).click();
    (await expectGet()).flush({ email: 'a@b.com', hasProfile: true, profileId: 'p' });
    await settle();
    expect(el.textContent).toContain('a@b.com');
  });

  it('posts logout and returns to the signed-out screen', async () => {
    (await open('/me')).flush({ email: 'a@b.com', hasProfile: false });
    await settle();
    (el.querySelector('app-button') as HTMLElement).click();
    (await expectPost(LOGOUT)).flush(null);
    await settle();
    expect(el.querySelector('a.google-signin')).not.toBeNull();
    expect(el.textContent).not.toContain('a@b.com');
  });

  it('does not show a stale signin message after signing in and out', async () => {
    (await open('/me?signin=failed')).flush({ email: 'a@b.com', hasProfile: false });
    await settle();
    (el.querySelector('app-button') as HTMLElement).click();
    (await expectPost(LOGOUT)).flush(null);
    await settle();
    expect(el.querySelector('a.google-signin')).not.toBeNull();
    expect(el.querySelector('[role="alert"]')).toBeNull();
  });

  it('does not re-show the message or re-navigate on retry from the error state', async () => {
    (await open('/me?signin=failed')).flush({}, { status: 500, statusText: 'Server Error' });
    await settle();
    const nav = vi.spyOn(TestBed.inject(Router), 'navigate');
    (el.querySelector('app-error-state app-button') as HTMLElement).click();
    (await expectGet()).flush(null, unauth);
    await settle();
    expect(el.querySelector('a.google-signin')).not.toBeNull();
    expect(el.querySelector('[role="alert"]')).toBeNull();
    expect(nav).not.toHaveBeenCalled();
  });

  it('treats a 401 on logout as signed out', async () => {
    (await open('/me')).flush({ email: 'a@b.com', hasProfile: false });
    await settle();
    (el.querySelector('app-button') as HTMLElement).click();
    (await expectPost(LOGOUT)).flush(null, unauth);
    await settle();
    expect(el.querySelector('a.google-signin')).not.toBeNull();
    expect(el.querySelector('[role="alert"]')).toBeNull();
  });

  it('stays signed in with a message when logout fails', async () => {
    (await open('/me')).flush({ email: 'a@b.com', hasProfile: false });
    await settle();
    (el.querySelector('app-button') as HTMLElement).click();
    (await expectPost(LOGOUT)).flush({}, { status: 500, statusText: 'Server Error' });
    await settle();
    expect(el.textContent).toContain('a@b.com');
    expect(el.querySelector('[role="alert"]')?.textContent).toContain(STRINGS.myProfile.logoutFailed);
  });
});
