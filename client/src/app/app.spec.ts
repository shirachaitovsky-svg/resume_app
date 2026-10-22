import { Location } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { Router, TitleStrategy, provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { App, activeNavId } from './app';
import { routes } from './app.routes';
import { AppTitleStrategy } from './shared/app-title.strategy';
import { STRINGS } from './shared/strings.he';

describe('join routes', () => {
  it('renders the form for /join/:token and redirects /join and /edit to /me', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter(routes, withComponentInputBinding())],
    });
    const http = TestBed.inject(HttpTestingController);
    const harness = await RouterTestingHarness.create();
    const el = harness.fixture.nativeElement as HTMLElement;
    await harness.navigateByUrl('/join/abc');
    await new Promise((r) => setTimeout(r, 0));
    http.expectOne('/api/invites/abc').flush({ name: 'מחזור' });
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(el.querySelector('app-join-form-page form')).not.toBeNull();
    for (const url of ['/join', '/edit']) {
      await harness.navigateByUrl(url);
      harness.detectChanges();
      expect(TestBed.inject(Router).url).toBe('/me');
      expect(el.querySelector('app-me-page')).not.toBeNull();
      http.match('/api/auth/google/me').forEach((r) => r.flush(null, { status: 401, statusText: 'Unauthorized' }));
    }
  });
});

describe('App shell', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter(routes), { provide: TitleStrategy, useClass: AppTitleStrategy }],
    }).compileComponents();
  });

  async function boot(url: string) {
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);
    await router.navigateByUrl(url);
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement, router };
  }

  it('creates the app', () => {
    expect(TestBed.createComponent(App).componentInstance).toBeTruthy();
  });

  it('has the skip link as the first focusable element and a single main', async () => {
    const { el } = await boot('/');
    const first = el.querySelector('a, button, input') as HTMLAnchorElement;
    expect(first.classList.contains('skip-link')).toBe(true);
    expect(first.textContent).toContain(STRINGS.skipToContent);
    expect(el.querySelectorAll('main').length).toBe(1);
    expect(el.querySelector('main')?.id).toBe('main-content');
  });

  it('moves focus to main when the skip link is used', async () => {
    const { fixture, el } = await boot('/');
    document.body.appendChild(el);
    (el.querySelector('.skip-link') as HTMLElement).click();
    expect(document.activeElement).toBe(el.querySelector('main'));
    el.remove();
    fixture.destroy();
  });

  it('renders the three navigation items and not the under-construction message', async () => {
    const { el } = await boot('/');
    const hrefs = Array.from(el.querySelectorAll('nav a')).map((a) => a.getAttribute('href'));
    expect(hrefs).toEqual(['/', '/about', '/me']);
    expect(el.textContent).not.toContain('האתר בהקמה');
  });

  it.each([
    ['/', STRINGS.nav.gallery],
    ['/about', STRINGS.nav.about],
    ['/me', STRINGS.nav.myProfile],
  ])('marks the active item for %s with aria-current', async (url, label) => {
    const { el } = await boot(url);
    const current = el.querySelectorAll('nav [aria-current="page"]');
    expect(current.length).toBe(1);
    expect(current[0].textContent?.trim()).toBe(label);
  });

  it('updates the active item on navigation', async () => {
    const { fixture, el, router } = await boot('/');
    await router.navigateByUrl('/about');
    await fixture.whenStable();
    expect(el.querySelector('nav [aria-current="page"]')?.textContent?.trim()).toBe(STRINGS.nav.about);
  });

  it('titles the production /me route', async () => {
    const { fixture } = await boot('/me');
    const http = TestBed.inject(HttpTestingController);
    for (let i = 0; i < 10 && http.match('/api/auth/google/me').map((q) => q.flush(null, { status: 401, statusText: 'Unauthorized' })).length === 0; i++) {
      await new Promise((res) => setTimeout(res, 0));
    }
    await fixture.whenStable();
    expect(TestBed.inject(Title).getTitle()).toBe(`${STRINGS.myProfile.title} | ${STRINGS.appTitle}`);
  });

  it('shows the gallery at /', async () => {
    const { el } = await boot('/');
    expect(el.querySelector('main h1')?.textContent).toContain(STRINGS.gallery.heading);
    expect(TestBed.inject(Title).getTitle()).toBe(`${STRINGS.home.title} | ${STRINGS.appTitle}`);
  });

  it.each([
    ['/about', STRINGS.about.heading, STRINGS.about.title],
  ])('renders %s with its text, a back link and a page title', async (url, text, title) => {
    const { el } = await boot(url);
    expect(el.querySelector('main')?.textContent).toContain(text);
    expect(el.querySelector('main a.back')?.getAttribute('href')).toBe('/');
    expect(TestBed.inject(Title).getTitle()).toBe(`${title} | ${STRINGS.appTitle}`);
  });

  it.each([
    ['/contact/verify#token=abc', STRINGS.contactVerify.title],
    ['/contact/decide#token=abc', STRINGS.contactDecide.title],
  ])('renders the real route %s with its page title', async (url, title) => {
    const { el } = await boot(url);
    expect(el.querySelector('main')?.textContent).not.toContain(STRINGS.notFoundPage.heading);
    expect(el.querySelector('app-contact-verify-page, app-contact-decide-page')).not.toBeNull();
    expect(TestBed.inject(Title).getTitle()).toBe(`${title} | ${STRINGS.appTitle}`);
  });

  it('shows "page not found" with a link back to the gallery for unknown paths', async () => {
    const { el } = await boot('/zzz');
    expect(el.querySelector('main h1')?.textContent).toContain(STRINGS.notFoundPage.heading);
    const back = el.querySelector('main a.back') as HTMLAnchorElement;
    expect(back.getAttribute('href')).toBe('/');
    expect(TestBed.inject(Title).getTitle()).toBe(`${STRINGS.notFoundPage.title} | ${STRINGS.appTitle}`);
    back.click();
    await TestBed.inject(Router).navigateByUrl('/');
    expect(TestBed.inject(Location).path()).toBe('');
  });

  it('declares every product route as lazy', () => {
    for (const route of routes) {
      expect(route.redirectTo ?? route.loadComponent ?? route.loadChildren).toBeDefined();
      if (!route.redirectTo) expect(route.loadComponent ?? route.loadChildren).toBeTypeOf('function');
      expect(route.component).toBeUndefined();
    }
  });

  it('loads a lazy route only when navigated to', async () => {
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl('/about');
    expect(component).toBeTruthy();
  });
});

describe('activeNavId', () => {
  it('ignores query, fragment and trailing slash and returns null for unknown paths', () => {
    expect(activeNavId('/about?x=1#y')).toBe('about');
    expect(activeNavId('/me/')).toBe('me');
    expect(activeNavId('/?c=1')).toBe('gallery');
    expect(activeNavId('/nope')).toBeNull();
  });
});
