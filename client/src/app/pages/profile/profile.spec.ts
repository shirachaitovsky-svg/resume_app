import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { PublicProfileDetail } from '../../shared/api/profile.models';
import { STRINGS } from '../../shared/strings.he';
import { expectNoSeriousViolations, expectSingleH1 } from '../../testing/a11y';
import { ProfilePage } from './profile';

const ID = '5eed0001-0000-4000-8000-000000000001';
const full: PublicProfileDetail = {
  id: ID,
  name: 'נועה כהן',
  productName: 'מתכנן ארוחות',
  productUrl: 'https://example.com/meal',
  description: 'תיאור קצר',
  avatarId: 'girl-1',
  photoUrl: null,
  cohort: { id: 'c1', name: 'מחזור א׳' },
  role: 'אנליסטית',
  workplace: 'הלשכה',
  introText: 'טקסט היכרות',
  introAnswers: [
    { number: 2, text: 'תשובה שתיים' },
    { number: 3, text: 'תשובה שלוש' },
  ],
  cv: '## ניסיון\n\n- **א**\n\n[קישור](https://example.com/cv)\n\n<script>alert(1)</script>',
};

describe('ProfilePage', () => {
  let http: HttpTestingController;
  let harness: RouterTestingHarness;
  let el: HTMLElement;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([{ path: 'profile/:id', component: ProfilePage }], withComponentInputBinding())],
    });
    http = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
  });
  afterEach(() => http.verify());

  async function open(id: string) {
    await harness.navigateByUrl(`/profile/${id}`, ProfilePage);
    el = harness.routeNativeElement as HTMLElement;
    harness.detectChanges();
    await harness.fixture.whenStable();
  }

  it('renders the full profile and updates the page title', async () => {
    await open(ID);
    expect(el.querySelector('app-loading-skeleton')).not.toBeNull();
    await expectNoSeriousViolations(el);
    http.expectOne(`/api/profiles/${ID}`).flush(full);
    await harness.fixture.whenStable();
    expect(el.querySelector('h1')?.textContent).toBe('נועה כהן');
    expect(el.textContent).toContain('מתכנן ארוחות');
    expect(el.textContent).toContain('אנליסטית · הלשכה');
    expect(el.textContent).toContain('תיאור קצר');
    expect(el.textContent).toContain('מחזור א׳');
    expect(el.querySelector('app-avatar svg')).not.toBeNull();
    expect(TestBed.inject(Title).getTitle()).toBe(`נועה כהן | ${STRINGS.appTitle}`);

    const open_ = el.querySelector('a.link-button--primary')!;
    expect(open_.textContent?.trim()).toBe('לפתיחת המוצר: מתכנן ארוחות');
    expect(open_.getAttribute('target')).toBe('_blank');
    expect(open_.getAttribute('rel')).toContain('noopener');

    // only answered questions, numbered, with the fixed wording
    const items = Array.from(el.querySelectorAll('.questions li'));
    expect(items.length).toBe(2);
    expect(items[0].querySelector('.num')?.textContent).toBe('2');
    expect(items[0].querySelector('h3')?.textContent).toBe(STRINGS.profile.questions[2]);
    expect(items[1].querySelector('h3')?.textContent).toBe(STRINGS.profile.questions[3]);
    expect(el.textContent).not.toContain(STRINGS.profile.questions[1]);
    expect(el.textContent).toContain('טקסט היכרות');

    // CV rendered safely
    expect(el.querySelector('app-safe-markdown strong')?.textContent).toBe('א');
    expect(el.querySelector('app-safe-markdown a')?.getAttribute('rel')).toBe('noopener noreferrer nofollow');
    expect(el.querySelector('script')).toBeNull();
    await expectNoSeriousViolations(el);
    expectSingleH1(el);
  });

  it('hides sections for missing fields', async () => {
    await open(ID);
    http.expectOne(`/api/profiles/${ID}`).flush({
      ...full,
      role: null,
      workplace: null,
      description: null,
      introText: null,
      introAnswers: [],
      cv: null,
    });
    await harness.fixture.whenStable();
    expect(el.querySelector('h1')).not.toBeNull();
    expect(el.querySelector('.role')).toBeNull();
    expect(el.querySelector('.description')).toBeNull();
    expect(el.querySelectorAll('h2').length).toBe(0);
  });

  it('offers the contact request button but shows no email or phone', async () => {
    await open(ID);
    http.expectOne(`/api/profiles/${ID}`).flush(full);
    await harness.fixture.whenStable();
    expect(el.querySelector('app-contact-request button')?.textContent).toContain(STRINGS.profile.contact.open);
    expect(el.querySelector('form')).toBeNull();
    expect(el.textContent).not.toMatch(/@/);
  });

  it('shows "unavailable" with a link back on 404', async () => {
    await open(ID);
    http.expectOne(`/api/profiles/${ID}`).flush({}, { status: 404, statusText: 'Not Found' });
    await harness.fixture.whenStable();
    expect(el.querySelector('h1')?.textContent).toBe('הפרופיל אינו זמין');
    expect(el.querySelector('a.back')?.getAttribute('href')).toBe('/');
    await expectNoSeriousViolations(el);
    expectSingleH1(el);
  });

  it('shows "unavailable" for a malformed id without calling the server', async () => {
    await open('abc');
    await harness.fixture.whenStable();
    http.expectNone((r) => r.url.startsWith('/api/profiles/'));
    expect(el.querySelector('h1')?.textContent).toBe('הפרופיל אינו זמין');
  });

  it('shows an error state with retry on 5xx', async () => {
    await open(ID);
    http.expectOne(`/api/profiles/${ID}`).flush('x', { status: 500, statusText: 'err' });
    await harness.fixture.whenStable();
    const err = el.querySelector('app-error-state')!;
    expect(err.getAttribute('role')).toBe('alert');
    await expectNoSeriousViolations(el);
    expectSingleH1(el);
    (err.querySelector('button') as HTMLButtonElement).click();
    await harness.fixture.whenStable();
    http.expectOne(`/api/profiles/${ID}`).flush(full);
    await harness.fixture.whenStable();
    expect(el.querySelector('h1')?.textContent).toBe('נועה כהן');
  });
});
