import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TURNSTILE_HEADER } from '../../shared/api/api-client';
import { DEV_TURNSTILE_TOKEN } from '../../shared/api/turnstile.service';
import { STRINGS } from '../../shared/strings.he';
import { expectNoSeriousViolations } from '../../testing/a11y';
import { ContactRequest } from './contact-request';

const ID = '5eed0001-0000-4000-8000-000000000001';
const T = STRINGS.profile.contact;
const URL = `/api/profiles/${ID}/contact-requests`;

describe('ContactRequest (profile form)', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<ContactRequest>;
  let el: HTMLElement;

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ContactRequest);
    fixture.componentRef.setInput('profileId', ID);
    el = fixture.nativeElement;
    await settle();
  });
  afterEach(() => http.verify());

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function fill(values: Record<string, string>) {
    for (const [f, v] of Object.entries(values)) {
      const input = el.querySelector<HTMLInputElement>(`[data-field="${f}"] :is(input, textarea)`)!;
      input.value = v;
      input.dispatchEvent(new Event('input'));
    }
  }

  async function open() {
    el.querySelector<HTMLButtonElement>('app-button.open button')!.click();
    await settle();
  }

  async function submit() {
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
  }

  async function expectPost(): Promise<TestRequest> {
    for (let i = 0; i < 10; i++) {
      await new Promise((r) => setTimeout(r, 0));
      http.match((r) => r.url.includes('csrf')).forEach((r) => r.flush({ token: 't' }));
      const found = http.match(URL);
      if (found.length > 0) return found[0];
    }
    throw new Error('no POST');
  }

  const GOOD = { name: 'דנה', email: 'dana@example.com', reason: 'רוצה להכיר את המוצר' };

  it('shows only a blue request button until opened, with no contact details anywhere', async () => {
    const button = el.querySelector('app-button.open')!;
    expect(button.textContent).toContain(T.open);
    expect(button.classList.contains('variant-accent')).toBe(true);
    expect(el.querySelector('form')).toBeNull();
    await open();
    expect(el.querySelectorAll('[data-field]').length).toBe(3);
    expect(el.textContent).not.toMatch(/\d{9}/);
    await expectNoSeriousViolations(el);
  });

  it('blocks submit with per-field errors and a summary, and keeps the values', async () => {
    await open();
    fill({ name: '   ', email: 'bad', reason: '' });
    await submit();
    http.expectNone(() => true);
    expect(el.querySelector('.summary')).not.toBeNull();
    expect(el.querySelectorAll('.summary li').length).toBe(3);
    expect(el.querySelector('[data-field="name"] .error')?.textContent).toContain(T.errors.name.required);
    expect(el.querySelector('[data-field="email"] .error')?.textContent).toContain(T.errors.email.invalid);
    await expectNoSeriousViolations(el);
    expect(el.querySelector('[data-field="reason"] .error')?.textContent).toContain(T.errors.reason.required);
    expect(el.querySelector<HTMLInputElement>('[data-field="email"] input')!.value).toBe('bad');
  });

  it('posts once with Turnstile, then shows the same confirmation without contact details', async () => {
    await open();
    fill(GOOD);
    await submit();
    await submit(); // a second submit while busy must not send again
    const req = await expectPost();
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(GOOD);
    expect(req.request.headers.get(TURNSTILE_HEADER)).toBe(DEV_TURNSTILE_TOKEN);
    req.flush({ status: 'verification-sent' }, { status: 202, statusText: 'Accepted' });
    await settle();
    expect(el.querySelector('[role="status"]')?.textContent).toContain(T.sentTitle);
    expect(el.textContent).toContain(T.sentBody);
    await expectNoSeriousViolations(el);
    expect(el.querySelector('form')).toBeNull();
  });

  it('keeps the data and shows a wait message on 429', async () => {
    await open();
    fill(GOOD);
    await submit();
    const req = await expectPost();
    req.flush(null, { status: 429, statusText: 'x', headers: { 'Retry-After': '30' } });
    await settle();
    expect(el.querySelector('.summary')?.textContent).toContain('30');
    expect(el.querySelector<HTMLInputElement>('[data-field="name"] input')!.value).toBe(GOOD.name);
  });

  it('keeps the data and shows a general message on 403 and 500', async () => {
    await open();
    fill(GOOD);
    await submit();
    let req = await expectPost();
    req.flush(null, { status: 500, statusText: 'x' });
    await settle();
    expect(el.querySelector('.summary')?.textContent).toContain(T.submitFailed);
    expect(el.querySelector<HTMLInputElement>('[data-field="email"] input')!.value).toBe(GOOD.email);
    await submit();
    req = await expectPost();
    req.flush(null, { status: 403, statusText: 'x' });
    req = await expectPost(); // the client retries once with a fresh anti-forgery token
    req.flush(null, { status: 403, statusText: 'x' });
    await settle();
    expect(el.querySelector('.summary')?.textContent).toContain(T.submitFailed);
  });

  it('maps server field errors next to the fields', async () => {
    await open();
    fill(GOOD);
    await submit();
    const req = await expectPost();
    req.flush({ errors: { reason: ['x'] } }, { status: 400, statusText: 'Bad Request' });
    await settle();
    expect(el.querySelector('[data-field="reason"] .error')?.textContent).toContain(T.errors.reason.invalid);
  });
});
