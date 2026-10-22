import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, defer, firstValueFrom, from, switchMap, throwError, timeout } from 'rxjs';
import { hasFieldErrors, toAppError } from './app-error';
import { CSRF_HEADER, CsrfService } from './csrf.service';
import { TURNSTILE } from './turnstile.service';

export const TURNSTILE_HEADER = 'X-Turnstile-Token';
export const REQUEST_TIMEOUT_MS = 30_000;

export interface ApiRequestOptions {
  /** Attach a Turnstile token in `X-Turnstile-Token` (public submissions). */
  readonly turnstile?: boolean;
}

type Method = 'GET' | 'POST' | 'PUT' | 'DELETE';

/** Single entry point for server calls: relative `/api/...`, CSRF, Turnstile and normalized errors. */
@Injectable({ providedIn: 'root' })
export class ApiClient {
  private readonly http = inject(HttpClient);
  private readonly csrf = inject(CsrfService);
  private readonly turnstile = inject(TURNSTILE);

  get<T>(path: string, options?: ApiRequestOptions): Observable<T> {
    return this.request<T>('GET', path, undefined, options);
  }

  post<T>(path: string, body?: unknown, options?: ApiRequestOptions): Observable<T> {
    return this.request<T>('POST', path, body, options);
  }

  put<T>(path: string, body?: unknown, options?: ApiRequestOptions): Observable<T> {
    return this.request<T>('PUT', path, body, options);
  }

  delete<T>(path: string, options?: ApiRequestOptions): Observable<T> {
    return this.request<T>('DELETE', path, undefined, options);
  }

  postMultipart<T>(path: string, body: FormData, options?: ApiRequestOptions): Observable<T> {
    return this.request<T>('POST', path, body, options);
  }

  private request<T>(method: Method, path: string, body: unknown, options?: ApiRequestOptions): Observable<T> {
    const url = path.startsWith('/api/') ? path : `/api${path.startsWith('/') ? '' : '/'}${path}`;
    const mutating = method !== 'GET';

    const attempt = (refreshToken: boolean): Observable<T> =>
      defer(() => from(this.buildHeaders(mutating, refreshToken, options))).pipe(
        switchMap((headers) => this.http.request<T>(method, url, { body, headers, withCredentials: true })),
        timeout(REQUEST_TIMEOUT_MS),
      );

    return attempt(false).pipe(
      catchError((error: unknown) => (mutating && isTokenFailure(error) ? attempt(true) : throwError(() => error))),
      catchError((error: unknown) => throwError(() => toAppError(error))),
    );
  }

  private async buildHeaders(
    mutating: boolean,
    refreshToken: boolean,
    options?: ApiRequestOptions,
  ): Promise<HttpHeaders> {
    let headers = new HttpHeaders();
    if (mutating) {
      const token = await firstValueFrom(refreshToken ? this.csrf.refresh() : this.csrf.getToken());
      headers = headers.set(CSRF_HEADER, token);
    }
    if (options?.turnstile) {
      headers = headers.set(TURNSTILE_HEADER, await this.turnstile.getToken());
    }
    return headers;
  }
}

/** 403, or 400 without field errors, is how a rejected anti-forgery token looks. */
function isTokenFailure(error: unknown): boolean {
  const status = (error as { status?: number } | null)?.status;
  return status === 403 || (status === 400 && !hasFieldErrors(error));
}
