import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiClient, REQUEST_TIMEOUT_MS } from './api-client';
import { AppError } from './app-error';
import { CSRF_URL } from './csrf.service';
import { TURNSTILE } from './turnstile.service';
import { STRINGS } from '../strings.he';

describe('ApiClient', () => {
  let api: ApiClient;
  let http: HttpTestingController;
  let turnstileCalls: number;

  beforeEach(() => {
    turnstileCalls = 0;
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: TURNSTILE,
          useValue: {
            getToken: () => {
              turnstileCalls++;
              return Promise.resolve('ts-token');
            },
          },
        },
      ],
    });
    api = TestBed.inject(ApiClient);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  /** Header building is Promise-based, so wait for the request to appear. */
  async function next(url: string) {
    let found: ReturnType<HttpTestingController['expectOne']> | undefined;
    await vi.waitFor(() => {
      found = http.expectOne(url);
    });
    return found!;
  }

  function capture<T>(obs: ReturnType<ApiClient['get']>) {
    const result: { value?: T; error?: AppError } = {};
    obs.subscribe({
      next: (v) => (result.value = v as T),
      error: (e) => (result.error = e as AppError),
    });
    return result;
  }

  it('returns the body of a successful GET untouched and sends credentials without CSRF', async () => {
    const result = capture<{ a: number }>(api.get('/x'));
    const req = await next('/api/x');
    expect(req.request.withCredentials).toBe(true);
    expect(req.request.headers.has('X-CSRF-TOKEN')).toBe(false);
    expect(req.request.headers.has('X-Turnstile-Token')).toBe(false);
    req.flush({ a: 1 });
    expect(result.value).toEqual({ a: 1 });
  });

  it('does not double the /api prefix', async () => {
    capture(api.get('/api/y'));
    (await next('/api/y')).flush({});
  });

  it('loads the CSRF token on the first POST and sends it with credentials', async () => {
    const result = capture(api.post('/things', { n: 1 }));
    const csrf = await next(CSRF_URL);
    expect(csrf.request.withCredentials).toBe(true);
    csrf.flush({ token: 'tok-1', headerName: 'X-CSRF-TOKEN' });
    const req = await next('/api/things');
    expect(req.request.headers.get('X-CSRF-TOKEN')).toBe('tok-1');
    expect(req.request.withCredentials).toBe(true);
    expect(req.request.body).toEqual({ n: 1 });
    req.flush({ ok: true });
    expect(result.value).toEqual({ ok: true });
  });

  it('reuses the cached token on later mutating requests (POST, PUT, DELETE)', async () => {
    capture(api.post('/a', {}));
    (await next(CSRF_URL)).flush({ token: 'tok-1' });
    (await next('/api/a')).flush({});

    capture(api.put('/b', {}));
    const put = await next('/api/b');
    expect(put.request.method).toBe('PUT');
    expect(put.request.headers.get('X-CSRF-TOKEN')).toBe('tok-1');
    put.flush({});

    capture(api.delete('/c'));
    const del = await next('/api/c');
    expect(del.request.method).toBe('DELETE');
    expect(del.request.headers.get('X-CSRF-TOKEN')).toBe('tok-1');
    del.flush({});
  });

  it('sends multipart bodies as FormData with the CSRF token', async () => {
    const form = new FormData();
    form.append('f', 'v');
    capture(api.postMultipart('/upload', form));
    (await next(CSRF_URL)).flush({ token: 'tok-1' });
    const req = await next('/api/upload');
    expect(req.request.body).toBe(form);
    expect(req.request.headers.get('X-CSRF-TOKEN')).toBe('tok-1');
    req.flush({});
  });

  it('sends the Turnstile header only when requested', async () => {
    capture(api.get('/plain'));
    (await next('/api/plain')).flush({});
    expect(turnstileCalls).toBe(0);

    capture(api.get('/guarded', { turnstile: true }));
    const req = await next('/api/guarded');
    expect(req.request.headers.get('X-Turnstile-Token')).toBe('ts-token');
    req.flush({});
    expect(turnstileCalls).toBe(1);
  });

  it('refreshes the token and retries once when the server rejects it (403)', async () => {
    const result = capture(api.post('/guarded', {}));
    (await next(CSRF_URL)).flush({ token: 'old' });
    (await next('/api/guarded')).flush({}, { status: 403, statusText: 'Forbidden' });
    (await next(CSRF_URL)).flush({ token: 'new' });
    const retry = await next('/api/guarded');
    expect(retry.request.headers.get('X-CSRF-TOKEN')).toBe('new');
    retry.flush({ done: true });
    expect(result.value).toEqual({ done: true });
  });

  it('returns an error when the retry also fails, without a third attempt', async () => {
    const result = capture(api.post('/guarded', {}));
    (await next(CSRF_URL)).flush({ token: 'old' });
    (await next('/api/guarded')).flush({}, { status: 400, statusText: 'Bad Request' });
    (await next(CSRF_URL)).flush({ token: 'new' });
    (await next('/api/guarded')).flush({}, { status: 403, statusText: 'Forbidden' });
    expect(result.error?.kind).toBe('forbidden');
  });

  it('does not retry a 400 that carries field errors', async () => {
    const result = capture(api.post('/form', {}));
    (await next(CSRF_URL)).flush({ token: 'tok' });
    (await next('/api/form')).flush({ errors: { name: ['חסר'] } }, { status: 400, statusText: 'Bad Request' });
    expect(result.error?.kind).toBe('validation');
    expect(result.error?.fieldErrors).toEqual({ name: ['חסר'] });
  });

  it('does not retry GET failures', async () => {
    const result = capture(api.get('/x'));
    (await next('/api/x')).flush({}, { status: 403, statusText: 'Forbidden' });
    expect(result.error?.kind).toBe('forbidden');
  });

  it('fails with a network AppError when the token cannot be loaded', async () => {
    const result = capture(api.post('/a', {}));
    (await next(CSRF_URL)).error(new ProgressEvent('error'));
    await vi.waitFor(() => expect(result.error?.kind).toBe('network'));
  });

  describe('error normalization', () => {
    async function failGet(status: number, body: object | string | null = null, headers: Record<string, string> = {}) {
      const result = capture(api.get('/x'));
      (await next('/api/x')).flush(body, { status, statusText: 'err', headers });
      return result.error!;
    }

    it('maps 400 problem+json to validation with field errors', async () => {
      const error = await failGet(400, {
        title: 'internal detail',
        errors: { email: ['לא תקין', 'קצר'], name: 'חסר' },
        extensions: { code: 'bad' },
      });
      expect(error.kind).toBe('validation');
      expect(error.status).toBe(400);
      expect(error.code).toBe('bad');
      expect(error.fieldErrors).toEqual({ email: ['לא תקין', 'קצר'], name: ['חסר'] });
      expect(error.message).toBe(STRINGS.errors.validation);
    });

    it.each([
      [401, 'unauthorized', STRINGS.errors.unauthorized],
      [403, 'forbidden', STRINGS.errors.forbidden],
      [404, 'notFound', STRINGS.errors.notFound],
      [409, 'conflict', STRINGS.errors.conflict],
    ])('maps %i to %s', async (status, kind, message) => {
      const error = await failGet(status);
      expect(error).toBeInstanceOf(AppError);
      expect(error.kind).toBe(kind);
      expect(error.status).toBe(status);
      expect(error.message).toBe(message);
    });

    it('maps 429 with Retry-After seconds', async () => {
      const error = await failGet(429, null, { 'Retry-After': '30' });
      expect(error.kind).toBe('tooMany');
      expect(error.retryAfterSeconds).toBe(30);
      expect(error.message).toBe(STRINGS.errors.tooMany);
    });

    it('leaves retryAfterSeconds null for 429 without the header', async () => {
      expect((await failGet(429)).retryAfterSeconds).toBeNull();
    });

    it('maps 5XX to a generic server error without leaking the body', async () => {
      const error = await failGet(503, 'Stack trace: secret internals');
      expect(error.kind).toBe('server');
      expect(error.message).toBe(STRINGS.errors.server);
      expect(JSON.stringify(error)).not.toContain('secret');
    });

    it('passes the server code from extensions.code', async () => {
      const error = await failGet(409, { extensions: { code: 'link-invalid' } });
      expect(error.code).toBe('link-invalid');
    });

    it('maps a connection failure to network', async () => {
      const result = capture(api.get('/x'));
      (await next('/api/x')).error(new ProgressEvent('error'));
      expect(result.error?.kind).toBe('network');
      expect(result.error?.status).toBe(0);
      expect(result.error?.message).toBe(STRINGS.errors.network);
    });

    it('maps a timeout to network', async () => {
      vi.useFakeTimers();
      try {
        const result = capture(api.get('/slow'));
        await vi.advanceTimersByTimeAsync(REQUEST_TIMEOUT_MS + 1);
        expect(result.error?.kind).toBe('network');
        http.expectOne('/api/slow');
      } finally {
        vi.useRealTimers();
      }
    });
  });
});
