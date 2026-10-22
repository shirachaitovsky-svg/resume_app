import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, finalize, map, of, shareReplay, tap } from 'rxjs';

export const CSRF_URL = '/api/profiles/csrf';
/** Token for the signed-in admin: the server binds anti-forgery tokens to the principal. */
export const ADMIN_CSRF_URL = '/api/auth/csrf';
export type CsrfScope = 'public' | 'admin';
export const CSRF_HEADER = 'X-CSRF-TOKEN';

interface CsrfResponse {
  readonly token: string;
}

/** Loads the anti-forgery token once and keeps it in memory only. */
@Injectable({ providedIn: 'root' })
export class CsrfService {
  private readonly http = inject(HttpClient);
  private token: string | null = null;
  private pending: Observable<string> | null = null;
  private scope: CsrfScope = 'public';

  /** Switches between the anonymous token and the admin token; the cached token is dropped. */
  setScope(scope: CsrfScope): void {
    if (this.scope === scope && this.token === null) return;
    this.scope = scope;
    this.token = null;
    this.pending = null;
  }

  getToken(): Observable<string> {
    if (this.token !== null) return of(this.token);
    if (this.pending) return this.pending;
    const scope = this.scope;
    const request: Observable<string> = this.http
      .get<CsrfResponse>(scope === 'admin' ? ADMIN_CSRF_URL : CSRF_URL, { withCredentials: true })
      .pipe(
        map((response) => response.token),
        // A response from a scope that was switched away meanwhile must not be cached.
        tap((token) => {
          if (this.scope === scope && this.pending === request) this.token = token;
        }),
        finalize(() => {
          if (this.pending === request) this.pending = null;
        }),
        shareReplay({ bufferSize: 1, refCount: false }),
      );
    this.pending = request;
    return request;
  }

  /** Drops the cached token and loads a fresh one. */
  refresh(): Observable<string> {
    this.token = null;
    this.pending = null;
    return this.getToken();
  }
}
