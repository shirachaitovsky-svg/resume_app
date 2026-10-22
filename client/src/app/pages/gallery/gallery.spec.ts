import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { CohortRef, PublicProfileCard } from '../../shared/api/profile.models';
import { STRINGS } from '../../shared/strings.he';
import { GalleryPage } from './gallery';

const A = '5eed00c1-0000-4000-8000-000000000001';
const B = '5eed00c1-0000-4000-8000-000000000002';
const cohorts: CohortRef[] = [
  { id: A, name: 'מחזור א׳' },
  { id: B, name: 'מחזור ב׳' },
];
const card = (n: number, cohort: CohortRef, extra: Partial<PublicProfileCard> = {}): PublicProfileCard => ({
  id: `5eed0001-0000-4000-8000-00000000000${n}`,
  name: `שם ${n}`,
  productName: `מוצר ${n}`,
  productUrl: `https://example.com/${n}`,
  description: `תיאור ${n}`,
  avatarId: 'girl-1',
  photoUrl: null,
  cohort,
  ...extra,
});

describe('GalleryPage', () => {
  let http: HttpTestingController;
  let harness: RouterTestingHarness;
  let el: HTMLElement;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: '', component: GalleryPage }], withComponentInputBinding()),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => http.verify());

  async function open(url: string) {
    await harness.navigateByUrl(url, GalleryPage);
    el = harness.routeNativeElement as HTMLElement;
    harness.detectChanges();
    await harness.fixture.whenStable();
  }
  const settle = () => harness.fixture.whenStable();

  function answerCohorts() {
    http.expectOne('/api/cohorts').flush({ items: cohorts, total: 2 });
  }

  it('shows a busy skeleton, then cards with all fields', async () => {
    await open('/');
    expect(el.querySelector('app-loading-skeleton')?.getAttribute('aria-busy')).toBe('true');
    answerCohorts();
    await settle();
    http.expectOne('/api/profiles').flush({ items: [card(1, cohorts[0]), card(2, cohorts[1])], total: 2 });
    await settle();
    expect(el.querySelector('app-loading-skeleton')).toBeNull();
    const cards = el.querySelectorAll('app-profile-card');
    expect(cards.length).toBe(2);
    expect(cards[0].textContent).toContain('שם 1');
    expect(cards[0].textContent).toContain('מוצר 1');
    expect(cards[0].textContent).toContain('תיאור 1');
    expect(cards[0].textContent).toContain('מחזור א׳');
    expect(cards[0].querySelector('app-avatar svg[role="img"]')).not.toBeNull();
    expect(el.querySelector('h1')?.textContent).toBe(STRINGS.gallery.heading);
  });

  it('card: product link opens in a new tab safely; profile link navigates; no nested interactives', async () => {
    await open('/');
    answerCohorts();
    await settle();
    http.expectOne('/api/profiles').flush({ items: [card(1, cohorts[0])], total: 1 });
    await settle();
    const product = el.querySelector('a.product-link')!;
    expect(product.getAttribute('href')).toBe('https://example.com/1');
    expect(product.getAttribute('target')).toBe('_blank');
    expect(product.getAttribute('rel')).toContain('noopener');
    expect(product.getAttribute('rel')).toContain('noreferrer');
    expect(product.getAttribute('aria-label')).toBe('שם 1, מוצר 1, פתיחת המוצר');
    const profile = el.querySelector('a.to-profile')!;
    expect(profile.getAttribute('href')).toBe('/profile/5eed0001-0000-4000-8000-000000000001');
    expect(profile.textContent).toContain(STRINGS.gallery.toProfile);
    expect(el.querySelectorAll('a a, a button, button a').length).toBe(0);
    expect(el.querySelectorAll('app-profile-card a').length).toBe(2);
  });

  it('a card with a non-http product URL has no product link', async () => {
    await open('/');
    answerCohorts();
    await settle();
    http.expectOne('/api/profiles').flush({ items: [card(1, cohorts[0], { productUrl: 'javascript:alert(1)' })], total: 1 });
    await settle();
    expect(el.querySelector('a.product-link')).toBeNull();
    expect(el.querySelectorAll('app-profile-card a').length).toBe(1);
  });

  it('shows the empty state text', async () => {
    await open('/');
    answerCohorts();
    await settle();
    http.expectOne('/api/profiles').flush({ items: [], total: 0 });
    await settle();
    expect(el.querySelector('app-empty-state')?.textContent).toContain('עדיין אין פרופילים במחזור הזה.');
  });

  it('shows an error with retry that reloads', async () => {
    await open('/');
    answerCohorts();
    await settle();
    http.expectOne('/api/profiles').flush('x', { status: 500, statusText: 'err' });
    await settle();
    const err = el.querySelector('app-error-state')!;
    expect(err.getAttribute('role')).toBe('alert');
    (err.querySelector('button') as HTMLButtonElement).click();
    await settle();
    http.expectOne('/api/profiles').flush({ items: [card(1, cohorts[0])], total: 1 });
    await settle();
    expect(el.querySelectorAll('app-profile-card').length).toBe(1);
  });

  it('shows a rate-limit message with the wait time on 429', async () => {
    await open('/');
    answerCohorts();
    await settle();
    http.expectOne('/api/profiles').flush('x', { status: 429, statusText: 'x', headers: { 'Retry-After': '30' } });
    await settle();
    expect(el.querySelector('app-error-state')?.textContent).toContain('30');
  });

  it('renders the all-cohorts chip selected plus one chip per cohort', async () => {
    await open('/');
    answerCohorts();
    await settle();
    http.expectOne('/api/profiles').flush({ items: [], total: 0 });
    await settle();
    const chips = Array.from(el.querySelectorAll('app-chip'));
    expect(chips.map((c) => c.textContent?.trim())).toEqual([STRINGS.gallery.allCohorts, 'מחזור א׳', 'מחזור ב׳']);
    expect(chips[0].classList.contains('selected')).toBe(true);
    expect(chips[1].classList.contains('selected')).toBe(false);
  });

  it('filters by clicking a chip: updates the URL, reloads, never shows the previous result while loading', async () => {
    await open('/');
    answerCohorts();
    await settle();
    http.expectOne('/api/profiles').flush({ items: [card(1, cohorts[0]), card(2, cohorts[1])], total: 2 });
    await settle();

    (el.querySelectorAll('app-chip button')[2] as HTMLButtonElement).click();
    await settle();
    expect(TestBed.inject(Router).url).toBe(`/?cohort=${B}`);
    expect(el.querySelectorAll('app-profile-card').length).toBe(0);
    expect(el.querySelector('app-loading-skeleton')).not.toBeNull();
    http.expectOne(`/api/profiles?cohort=${B}`).flush({ items: [card(2, cohorts[1])], total: 1 });
    await settle();
    expect(el.querySelectorAll('app-profile-card').length).toBe(1);
    expect(el.querySelectorAll('app-chip')[2].classList.contains('selected')).toBe(true);
    expect(el.querySelectorAll('app-chip')[0].classList.contains('selected')).toBe(false);

    (el.querySelectorAll('app-chip button')[0] as HTMLButtonElement).click();
    await settle();
    expect(TestBed.inject(Router).url).toBe('/');
    http.expectOne('/api/profiles').flush({ items: [card(1, cohorts[0]), card(2, cohorts[1])], total: 2 });
    await settle();
    expect(el.querySelectorAll('app-profile-card').length).toBe(2);
  });

  it('ignores a late answer for a previous selection', async () => {
    await open('/');
    answerCohorts();
    await settle();
    const first = http.expectOne('/api/profiles');
    (el.querySelectorAll('app-chip button')[1] as HTMLButtonElement).click();
    await settle();
    expect(first.cancelled).toBe(true);
    http.expectOne(`/api/profiles?cohort=${A}`).flush({ items: [card(1, cohorts[0])], total: 1 });
    await settle();
    expect(el.querySelectorAll('app-profile-card').length).toBe(1);
  });

  it('reads ?cohort= on load and selects the right chip', async () => {
    await open(`/?cohort=${B}`);
    answerCohorts();
    await settle();
    http.expectOne(`/api/profiles?cohort=${B}`).flush({ items: [card(2, cohorts[1])], total: 1 });
    await settle();
    expect(el.querySelectorAll('app-chip')[2].classList.contains('selected')).toBe(true);
    expect(el.querySelectorAll('app-profile-card').length).toBe(1);
  });

  it('treats an unknown cohort id as all cohorts without crashing', async () => {
    await open('/?cohort=zzz-not-a-cohort');
    answerCohorts();
    await settle();
    http.expectOne('/api/profiles').flush({ items: [card(1, cohorts[0])], total: 1 });
    await settle();
    expect(el.querySelectorAll('app-chip')[0].classList.contains('selected')).toBe(true);
    expect(el.querySelectorAll('app-profile-card').length).toBe(1);
  });

  it('shows an empty list when the server rejects a cohort id (400) and the cohort list failed', async () => {
    await open('/?cohort=bad');
    http.expectOne('/api/cohorts').flush('x', { status: 500, statusText: 'x' });
    await settle();
    http.expectOne('/api/profiles?cohort=bad').flush({ title: 'bad' }, { status: 400, statusText: 'Bad Request' });
    await settle();
    expect(el.querySelector('app-empty-state')?.textContent).toContain('עדיין אין פרופילים במחזור הזה.');
  });
});
