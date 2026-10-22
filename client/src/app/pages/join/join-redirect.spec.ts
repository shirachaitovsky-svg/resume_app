import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { STRINGS } from '../../shared/strings.he';
import { JoinRedirectPage } from './join-redirect';

const TOKEN = 'abc123_-XYZ';
const COHORT = '3f2b8a52-6f6c-4d7e-9a43-0d1c2b3a4f5e';

describe('JoinRedirectPage', () => {
  let http: HttpTestingController;
  let harness: RouterTestingHarness;
  let el: HTMLElement;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(
          [
            { path: 'join/:token', component: JoinRedirectPage },
            { path: 'me', component: JoinRedirectPage },
          ],
          withComponentInputBinding(),
        ),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
  });
  afterEach(() => http.verify());

  async function open(token: string) {
    await harness.navigateByUrl(`/join/${token}`);
    el = harness.routeNativeElement as HTMLElement;
    harness.detectChanges();
    await new Promise((r) => setTimeout(r, 0));
  }

  async function settle() {
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
  }

  it('replaces the page with /me and the cohort pre-selected', async () => {
    await open(TOKEN);
    http.expectOne(`/api/invites/${TOKEN}`).flush({ cohortName: 'מחזור', cohortId: COHORT });
    await settle();
    expect(TestBed.inject(Router).url).toBe(`/me?cohort=${COHORT}`);
  });

  it('shows "the link is not valid" with a way on to /me when the token is unknown', async () => {
    await open(TOKEN);
    http.expectOne(`/api/invites/${TOKEN}`).flush(null, { status: 404, statusText: 'NF' });
    await settle();
    expect(el.textContent).toContain(STRINGS.joinForm.invalidTitle);
    expect(el.querySelector('a[href="/me"]')).not.toBeNull();
    expect(TestBed.inject(Router).url).toBe(`/join/${TOKEN}`);
  });

  it('treats a malformed token as invalid without a request', async () => {
    await open('bad.token');
    http.expectNone(() => true);
    expect(el.textContent).toContain(STRINGS.joinForm.invalidTitle);
  });

  it('shows an error state with retry on 500', async () => {
    await open(TOKEN);
    http.expectOne(`/api/invites/${TOKEN}`).flush(null, { status: 500, statusText: 'x' });
    await settle();
    expect(el.querySelector('app-error-state')).not.toBeNull();
    el.querySelector<HTMLButtonElement>('app-error-state button')!.click();
    await new Promise((r) => setTimeout(r, 0));
    http.expectOne(`/api/invites/${TOKEN}`).flush({ cohortName: 'מחזור', cohortId: COHORT });
    await settle();
    expect(TestBed.inject(Router).url).toBe(`/me?cohort=${COHORT}`);
  });
});
