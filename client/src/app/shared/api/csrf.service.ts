import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, finalize, map, of, shareReplay, tap } from 'rxjs';

export const CSRF_URL = '/api/profiles/csrf';
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

  getToken(): Observable<string> {
    if (this.token !== null) return of(this.token);
    this.pending ??= this.http.get<CsrfResponse>(CSRF_URL, { withCredentials: true }).pipe(
      map((response) => response.token),
      tap((token) => (this.token = token)),
      finalize(() => (this.pending = null)),
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    return this.pending;
  }

  /** Drops the cached token and loads a fresh one. */
  refresh(): Observable<string> {
    this.token = null;
    this.pending = null;
    return this.getToken();
  }
}
