import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { STRINGS } from '../../../shared/strings.he';
import { ProfileApi } from '../profile-api';
import { DecodedImage, IMAGE_DECODER } from './photo-resize';
import { ProfileForm } from './profile-form';

const GOOD = {
  name: 'נועה כהן',
  productName: 'מתכנן',
  productUrl: 'https://example.com/x',
  phone: '+972 50-123-4567',
};
const BODY = { cohortId: 'c1', ...GOOD };
const EMAIL = 'noa@example.com';

let decodeResult: () => Promise<DecodedImage>;
const created: string[] = [];
const revoked: string[] = [];

describe('ProfileForm optional section', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<ProfileForm>;
  let el: HTMLElement;
  let saved = 0;

  beforeEach(async () => {
    created.length = 0;
    revoked.length = 0;
    saved = 0;
    let n = 0;
    vi.spyOn(URL, 'createObjectURL').mockImplementation(() => {
      const url = `blob:test/${n++}`;
      created.push(url);
      return url;
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation((u) => void revoked.push(u));
    decodeResult = async () => ({
      width: 2000,
      height: 1000,
      encode: async () => new Blob([new Uint8Array(100 * 1024)], { type: 'image/jpeg' }),
    });
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: IMAGE_DECODER, useValue: () => decodeResult() }],
    });
    http = TestBed.inject(HttpTestingController);
    const api = TestBed.inject(ProfileApi);
    fixture = TestBed.createComponent(ProfileForm);
    fixture.componentRef.setInput('mode', 'create');
    fixture.componentRef.setInput('email', EMAIL);
    fixture.componentRef.setInput('cohorts', [{ id: 'c1', name: 'מחזור א׳' }]);
    fixture.componentRef.setInput('initialCohortId', 'c1');
    fixture.componentRef.setInput('submitFn', (p: Parameters<ProfileApi['create']>[0]) => api.create(p));
    fixture.componentInstance.saved.subscribe(() => saved++);
    el = fixture.nativeElement as HTMLElement;
    await settle();
  });
  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function fill(values: Record<string, string>) {
    for (const [f, v] of Object.entries(values)) {
      const input = el.querySelector<HTMLInputElement | HTMLTextAreaElement>(`[data-field="${f}"] :is(input, textarea)`)!;
      input.value = v;
      input.dispatchEvent(new Event('input'));
    }
  }

  async function submit() {
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
  }

  async function expectPost(): Promise<TestRequest> {
    for (let i = 0; i < 10; i++) {
      await new Promise((r) => setTimeout(r, 0));
      http.match((r) => r.url.includes('csrf')).forEach((r) => r.flush({ token: 't' }));
      const found = http.match('/api/me/profile');
      if (found.length > 0) return found[0];
    }
    throw new Error('no POST');
  }

  async function pickFile(type = 'image/png', name = 'a.png') {
    const input = el.querySelector<HTMLInputElement>('[data-field="photo"] input[type="file"]')!;
    Object.defineProperty(input, 'files', { configurable: true, value: [new File(['x'], name, { type })] });
    input.dispatchEvent(new Event('change'));
    await new Promise((r) => setTimeout(r, 0));
    await settle();
  }

  const radio = (value: string) => el.querySelector<HTMLInputElement>(`app-avatar-picker input[value="${value}"]`)!;
  const cardText = () => el.querySelector('app-profile-preview')!.textContent!;

  it('shows the optional section with a live description counter limited to 120', async () => {
    expect(el.querySelector('.optional h2')!.textContent).toContain(STRINGS.joinForm.optional.heading);
    const input = el.querySelector<HTMLInputElement>('[data-field="description"] input')!;
    expect(input.getAttribute('maxlength')).toBe('120');
    expect(el.querySelector('[data-field="description"] .counter')!.textContent!.trim()).toBe('0/120');
    fill({ description: 'שלום' });
    await settle();
    expect(el.querySelector('[data-field="description"] .counter')!.textContent!.trim()).toBe('4/120');
    expect(cardText()).toContain('שלום');
  });

  it('labels the intro answers with the profile questions and the optional fields are not marked required', () => {
    for (const n of [1, 2, 3])
      expect(el.querySelector(`[data-field="introAnswer${n}"] label`)!.textContent).toContain(STRINGS.profile.questions[n]);
    expect(el.querySelectorAll('.optional .required').length).toBe(0);
    expect(el.querySelector('[data-field="introText"] textarea')!.getAttribute('maxlength')).toBe('1000');
    expect(el.querySelector('[data-field="cv"] textarea')!.getAttribute('maxlength')).toBe('20000');
  });

  it('submits only the cohort and the four typed fields as JSON when no optional field is filled', async () => {
    fill(GOOD);
    await submit();
    const req = await expectPost();
    expect(req.request.body).toEqual(BODY);
    req.flush({ status: 'pending' }, { status: 202, statusText: 'Accepted' });
    await settle();
  });

  it('sends only non-empty optional fields (trimmed), and one answered question does not block', async () => {
    fill({ ...GOOD, description: '  קצר  ', introAnswer2: ' תשובה ', role: '   ', cv: '# כותרת' });
    await submit();
    const req = await expectPost();
    expect(req.request.body).toEqual({ ...BODY, description: 'קצר', introAnswer2: 'תשובה', cv: '# כותרת' });
    req.flush({ status: 'pending' }, { status: 202, statusText: 'Accepted' });
    await settle();
  });

  it('shows three figures per gender, resets a figure of the other gender and updates the card', async () => {
    expect(el.querySelectorAll('app-avatar-picker .figures input').length).toBe(0);
    radio('girl').click();
    await settle();
    expect([...el.querySelectorAll<HTMLInputElement>('app-avatar-picker .figures input')].map((i) => i.value)).toEqual([
      'girl-1',
      'girl-2',
      'girl-3',
    ]);
    radio('girl-2').click();
    await settle();
    expect(radio('girl-2').checked).toBe(true);
    expect(radio('girl-2').getAttribute('aria-checked')).toBe('true');
    expect(el.querySelector('app-profile-preview app-avatar svg')!.getAttribute('data-figure')).toBe('girl-2');
    radio('boy').click();
    await settle();
    expect([...el.querySelectorAll<HTMLInputElement>('app-avatar-picker .figures input')].map((i) => i.value)).toEqual([
      'boy-1',
      'boy-2',
      'boy-3',
    ]);
    expect(el.querySelector('app-profile-preview app-avatar svg')!.getAttribute('data-figure')).toBe('default');
    radio('boy-3').click();
    await settle();
    fill(GOOD);
    await submit();
    const req = await expectPost();
    expect(req.request.body).toEqual({ ...BODY, avatarId: 'boy-3' });
    req.flush({ status: 'pending' }, { status: 202, statusText: 'Accepted' });
    await settle();
  });

  it('does not send avatarId when no figure was chosen', async () => {
    radio('girl').click();
    await settle();
    fill(GOOD);
    await submit();
    const req = await expectPost();
    expect('avatarId' in (req.request.body as object)).toBe(false);
    req.flush({ status: 'pending' }, { status: 202, statusText: 'Accepted' });
    await settle();
  });

  it('resizes a valid image, previews it and sends multipart with the photo as image/jpeg', async () => {
    await pickFile();
    expect(created.length).toBe(1);
    expect(el.querySelector('app-profile-preview img')!.getAttribute('src')).toBe(created[0]);
    expect(el.querySelector('[data-field="photo"] input')!.getAttribute('accept')).toBe('image/jpeg,image/png,image/webp');
    radio('girl').click();
    await settle();
    radio('girl-1').click();
    fill({ ...GOOD, description: 'מתאר' });
    await submit();
    const req = await expectPost();
    const body = req.request.body as FormData;
    expect(body instanceof FormData).toBe(true);
    for (const [k, v] of Object.entries({ ...BODY, description: 'מתאר', avatarId: 'girl-1' })) expect(body.get(k)).toBe(v);
    expect(body.has('role')).toBe(false);
    const photo = body.get('photo') as File;
    expect(photo.type).toBe('image/jpeg');
    expect(photo.size).toBe(100 * 1024);
    expect(req.request.headers.has('X-Turnstile-Token')).toBe(false);
    req.flush({ status: 'pending' }, { status: 202, statusText: 'Accepted' });
    await settle();
    expect(revoked).toContain(created[0]);
    expect(saved).toBe(1);
  });

  it('removes the photo, releases the URL and goes back to JSON', async () => {
    await pickFile();
    el.querySelector<HTMLElement>('[data-field="photo"] app-button')!.click();
    await settle();
    expect(revoked).toEqual([created[0]]);
    expect(el.querySelector('app-profile-preview img')).toBeNull();
    fill(GOOD);
    await submit();
    const req = await expectPost();
    expect(req.request.body).toEqual(BODY);
    req.flush({ status: 'pending' }, { status: 202, statusText: 'Accepted' });
    await settle();
  });

  it('replacing the photo revokes the previous URL', async () => {
    await pickFile();
    await pickFile();
    expect(created.length).toBe(2);
    expect(revoked).toEqual([created[0]]);
  });

  it('shows an error next to the photo field for an unsupported or undecodable file and never sends it', async () => {
    await pickFile('text/plain', 'a.txt');
    expect(el.querySelector('[data-field="photo"] .error')!.textContent).toContain(STRINGS.joinForm.photo.unsupported);
    decodeResult = () => Promise.reject(new Error('corrupt'));
    await pickFile('image/png');
    expect(el.querySelector('[data-field="photo"] .error')).not.toBeNull();
    expect(created.length).toBe(0);
    fill(GOOD);
    await submit();
    const req = await expectPost();
    expect(req.request.body).toEqual(BODY);
    req.flush({ status: 'pending' }, { status: 202, statusText: 'Accepted' });
    await settle();
  });

  it('shows an error when the image cannot be shrunk under 400KB', async () => {
    decodeResult = async () => ({
      width: 3000,
      height: 3000,
      encode: async () => new Blob([new Uint8Array(900 * 1024)]),
    });
    await pickFile();
    expect(el.querySelector('[data-field="photo"] .error')!.textContent).toContain(STRINGS.joinForm.photo.tooLarge);
    expect(created.length).toBe(0);
  });

  it('shows the exact CV warning always, and renders Markdown safely without innerHTML', async () => {
    expect(el.textContent).toContain(STRINGS.joinForm.optional.cvWarning);
    expect(STRINGS.joinForm.optional.cvWarning).toBe(
      'קורות החיים יוצגו באתר הציבורי. הסירי מהם פרטים שאינך רוצה לחשוף, כמו מייל או נייד.',
    );
    fill({ cv: '## כותרת\n\n- אחד\n- שניים\n\n<script>alert(1)</script>' });
    await settle();
    const preview = el.querySelector('.cv-preview app-safe-markdown')!;
    expect(preview.querySelector('h3')).not.toBeNull();
    expect(preview.querySelectorAll('li').length).toBe(2);
    expect(preview.querySelector('script')).toBeNull();
    expect(preview.textContent).toContain('<script>alert(1)</script>');
  });

  it('blocks a CV over 20000 characters, keeps values and points at the field', async () => {
    fill(GOOD);
    const cv = el.querySelector<HTMLTextAreaElement>('[data-field="cv"] textarea')!;
    cv.removeAttribute('maxlength');
    fill({ cv: 'א'.repeat(20001) });
    await submit();
    http.expectNone(() => true);
    expect(el.querySelector('[data-field="cv"] .error')).not.toBeNull();
    expect(el.querySelectorAll('.summary li').length).toBe(1);
    expect(el.querySelector<HTMLInputElement>('[data-field="name"] input')!.value).toBe(GOOD.name);
  });

  it('maps server errors for photo, cv and avatarId to their fields and keeps the data', async () => {
    await pickFile();
    fill({ ...GOOD, cv: 'x' });
    await submit();
    const req = await expectPost();
    req.flush({ errors: { photo: ['bad'], cv: ['bad'], avatarId: ['bad'] } }, { status: 400, statusText: 'Bad' });
    await settle();
    expect(el.querySelector('[data-field="photo"] .error')).not.toBeNull();
    expect(el.querySelector('[data-field="cv"] .error')).not.toBeNull();
    expect(el.querySelector('[data-field="avatarId"] .error')).not.toBeNull();
    expect(el.querySelectorAll('.summary li').length).toBe(3);
    expect(el.querySelector('app-profile-preview img')).not.toBeNull();
    expect(el.querySelector<HTMLTextAreaElement>('[data-field="cv"] textarea')!.value).toBe('x');
  });

  it('keeps the photo and values on a 503 photo-processing error', async () => {
    await pickFile();
    fill(GOOD);
    await submit();
    (await expectPost()).flush({ title: 'photo-processing-busy' }, { status: 503, statusText: 'SU' });
    await settle();
    expect(el.querySelector('.summary')!.textContent).toContain(STRINGS.joinForm.submitFailed);
    expect(el.querySelector('app-profile-preview img')).not.toBeNull();
    expect(revoked).toEqual([]);
  });

  it('updates the preview card live, without links or buttons, and shows placeholders when empty', async () => {
    expect(cardText()).toContain(STRINGS.joinForm.preview.namePlaceholder);
    expect(cardText()).toContain(STRINGS.joinForm.preview.productPlaceholder);
    fill({ ...GOOD, description: 'תיאור' });
    await settle();
    const card = el.querySelector('app-profile-preview')!;
    expect(card.textContent).toContain(GOOD.name);
    expect(card.textContent).toContain(GOOD.productName);
    expect(card.textContent).toContain('מחזור א׳');
    expect(card.textContent).not.toContain(EMAIL);
    expect(card.textContent).not.toContain('050');
    expect(card.querySelector('a, button')).toBeNull();
    expect(card.querySelector('[aria-hidden="true"]')).not.toBeNull();
    expect(card.querySelector('section')!.getAttribute('aria-label')).toBe(STRINGS.joinForm.preview.heading);
  });

  it('clears all new fields and the photo after success', async () => {
    await pickFile();
    fill({ ...GOOD, cv: 'abc', description: 'd' });
    await submit();
    (await expectPost()).flush({ status: 'pending' }, { status: 202, statusText: 'Accepted' });
    await settle();
    expect(revoked).toEqual([created[0]]);
    expect(saved).toBe(1);
    expect(el.querySelector<HTMLTextAreaElement>('[data-field="cv"] textarea')!.value).toBe('');
    expect(el.querySelector<HTMLInputElement>('[data-field="name"] input')!.value).toBe('');
  });

  it('releases the object URL on destroy', async () => {
    await pickFile();
    fixture.destroy();
    expect(revoked).toContain(created[0]);
  });

  it('cancelling the file dialog removes the photo', async () => {
    await pickFile();
    const input = el.querySelector<HTMLInputElement>('[data-field="photo"] input[type="file"]')!;
    Object.defineProperty(input, 'files', { configurable: true, value: [] });
    input.dispatchEvent(new Event('change'));
    await settle();
    expect(revoked).toEqual([created[0]]);
    expect(el.querySelector('app-profile-preview img')).toBeNull();
    fill(GOOD);
    await submit();
    const req = await expectPost();
    expect(req.request.body).toEqual(BODY);
    req.flush({ status: 'pending' }, { status: 202, statusText: 'Accepted' });
    await settle();
  });

  it('blocks submit while the photo is processing and shows the status', async () => {
    let release!: () => void;
    decodeResult = () =>
      new Promise((resolve) => {
        release = () =>
          resolve({ width: 10, height: 10, encode: async () => new Blob([new Uint8Array(10)], { type: 'image/jpeg' }) });
      });
    const input = el.querySelector<HTMLInputElement>('[data-field="photo"] input[type="file"]')!;
    Object.defineProperty(input, 'files', { configurable: true, value: [new File(['x'], 'a.png', { type: 'image/png' })] });
    input.dispatchEvent(new Event('change'));
    await settle();
    fill(GOOD);
    expect(el.querySelector('button[type="submit"]')!.hasAttribute('disabled')).toBe(true);
    expect(el.querySelector('[data-field="photo"] [role="status"]')!.textContent).toContain(STRINGS.joinForm.photo.processing);
    await submit();
    await new Promise((r) => setTimeout(r, 0));
    http.expectNone('/api/me/profile');
    expect(el.querySelector('.summary')!.textContent).toContain(STRINGS.joinForm.photo.processing);
    release();
    await new Promise((r) => setTimeout(r, 0));
    await settle();
  });

  it('a slow earlier pick never overrides a later one', async () => {
    const releases: (() => void)[] = [];
    const mk = (size: number) => () =>
      new Promise<DecodedImage>((resolve) => {
        releases.push(() =>
          resolve({ width: 10, height: 10, encode: async () => new Blob([new Uint8Array(size)], { type: 'image/jpeg' }) }),
        );
      });
    const input = el.querySelector<HTMLInputElement>('[data-field="photo"] input[type="file"]')!;
    const pick = (size: number) => {
      decodeResult = mk(size);
      Object.defineProperty(input, 'files', { configurable: true, value: [new File(['x'], 'a.png', { type: 'image/png' })] });
      input.dispatchEvent(new Event('change'));
    };
    pick(111);
    pick(222);
    releases[1]();
    await new Promise((r) => setTimeout(r, 0));
    releases[0]();
    await new Promise((r) => setTimeout(r, 0));
    await settle();
    expect(created.length).toBe(1);
    expect(el.querySelector('app-profile-preview img')!.getAttribute('src')).toBe(created[0]);
    fill(GOOD);
    await submit();
    const req = await expectPost();
    expect(((req.request.body as FormData).get('photo') as File).size).toBe(222);
    req.flush({ status: 'pending' }, { status: 202, statusText: 'Accepted' });
    await settle();
  });
});
