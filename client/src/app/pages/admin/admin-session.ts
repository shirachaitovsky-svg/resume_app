import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, catchError, firstValueFrom, map, of, switchMap, tap } from 'rxjs';
import { ApiClient } from '../../shared/api/api-client';
import { AppError, toAppError } from '../../shared/api/app-error';
import { STRINGS } from '../../shared/strings.he';
import { CsrfService } from '../../shared/api/csrf.service';

export type SessionState =
  | { readonly status: 'unknown' }
  | { readonly status: 'anonymous' }
  | {
      readonly status: 'authenticated';
      readonly username: string;
      readonly mustChangePassword: boolean;
      readonly totpSetupRequired: boolean;
    };

export interface LoginRequest {
  readonly username: string;
  readonly password: string;
  readonly code?: string;
  readonly recoveryCode?: string;
}

interface SessionResponse {
  readonly username: string;
  readonly mustChangePassword: boolean;
  readonly totpSetupRequired: boolean;
}

export interface TotpSetup {
  readonly key: string;
  readonly uri: string;
}

export const ADMIN_LOGIN = '/admin/login';
export const ADMIN_SETUP = '/admin/setup';
export const ADMIN_HOME = '/admin';

/** In-memory admin session. Nothing (password, code, key) is ever written to browser storage. */
@Injectable({ providedIn: 'root' })
export class AdminSession {
  private readonly api = inject(ApiClient);
  private readonly csrf = inject(CsrfService);
  private loading: Promise<void> | null = null;

  readonly state = signal<SessionState>({ status: 'unknown' });
  /** True after a mid-session 401, so the login screen can say why. */
  readonly expired = signal(false);
  /** Set when the initial `/auth/me` failed for a reason other than 401; the login screen offers a retry. */
  readonly loadError = signal<AppError | null>(null);
  /**
   * One-time recovery codes held until the user confirms they were saved. Memory only, never storage.
   * Kept here (not in the setup component) so leaving and returning to the screen does not lose them.
   */
  readonly recoveryCodes = signal<readonly string[]>([]);
  readonly recoveryPending = computed(() => this.recoveryCodes().length > 0);
  readonly username = computed(() => {
    const s = this.state();
    return s.status === 'authenticated' ? s.username : '';
  });

  /** Where the current state belongs: login, the setup steps or the landing page. */
  destination(state: SessionState = this.state()): string {
    if (state.status !== 'authenticated') return ADMIN_LOGIN;
    return state.mustChangePassword || state.totpSetupRequired || this.recoveryPending() ? ADMIN_SETUP : ADMIN_HOME;
  }

  /** Loads the session from `/auth/me` once; a 401 simply means signed out. */
  ensureLoaded(): Promise<void> {
    // After a failed load the error screen offers `reload()`; redirects must not loop on the failing request.
    if (this.state().status !== 'unknown' || this.loadError() !== null) return Promise.resolve();
    this.loading ??= this.loadInitial().finally(() => (this.loading = null));
    return this.loading;
  }

  /** Retries the initial load after a failure. */
  reload(): Promise<void> {
    this.loadError.set(null);
    return this.ensureLoaded();
  }

  private async loadInitial(): Promise<void> {
    // On a non-401 failure the state stays `unknown` and the error is kept for a retry.
    this.loadError.set(await this.fetchMe());
  }

  /**
   * Re-reads the session after a server-side change (for example the password change).
   * Resolves with the failure for anything but 401 (which means signed out); the previous state is kept then.
   */
  refresh(): Promise<AppError | null> {
    return this.fetchMe();
  }

  private async fetchMe(): Promise<AppError | null> {
    try {
      const me = await firstValueFrom(this.api.get<SessionResponse>('/auth/me'));
      this.csrf.setScope('admin');
      this.setAuthenticated(me);
      return null;
    } catch (error) {
      const appError = toAppError(error);
      if (appError.kind === 'unauthorized') {
        this.clear();
        return null;
      }
      return appError;
    }
  }

  login(request: LoginRequest): Observable<void> {
    return this.api.post<SessionResponse>('/auth/login', request).pipe(
      switchMap((me) => {
        // The anti-forgery token is bound to the principal: fetch a fresh one before any further POST.
        // If that fails the login still succeeded; the next POST fetches the token lazily.
        this.csrf.setScope('admin');
        return this.csrf.refresh().pipe(
          map(() => me),
          catchError(() => of(me)),
        );
      }),
      tap((me) => {
        this.expired.set(false);
        this.loadError.set(null);
        this.setAuthenticated(me);
      }),
      map(() => undefined),
    );
  }

  changePassword(currentPassword: string, newPassword: string): Observable<void> {
    return this.api.post<void>('/auth/change-password', { currentPassword, newPassword });
  }

  startTotp(): Observable<TotpSetup> {
    return this.api.post<TotpSetup>('/auth/totp/setup');
  }

  enableTotp(code: string): Observable<readonly string[]> {
    return this.api
      .post<{ recoveryCodes: readonly string[] }>('/auth/totp/enable', { code })
      .pipe(
        map((response) => {
          const codes = response?.recoveryCodes;
          if (!Array.isArray(codes) || codes.length === 0 || !codes.every((c) => typeof c === 'string')) {
            throw new AppError('server', 500, STRINGS.errors.server);
          }
          return codes;
        }),
      );
  }

  /** Called when the whole setup is done (codes confirmed), so the landing page becomes reachable. */
  markSetupComplete(): void {
    this.clearRecovery();
    const s = this.state();
    if (s.status === 'authenticated') {
      this.state.set({ ...s, mustChangePassword: false, totpSetupRequired: false });
    }
  }

  clearRecovery(): void {
    this.recoveryCodes.set([]);
  }

  /** Always clears the local session and token, even when the server call fails. */
  logout(): Observable<void> {
    return this.api.post<void>('/auth/logout').pipe(
      catchError(() => of(undefined)),
      tap(() => this.clear()),
      map(() => undefined),
    );
  }

  /** Handles a 401 in the middle of a session. Returns true when it was one. */
  handleUnauthorized(error: AppError): boolean {
    if (error.kind !== 'unauthorized') return false;
    this.clear();
    this.expired.set(true);
    return true;
  }

  private clear(): void {
    this.clearRecovery();
    this.csrf.setScope('public');
    this.state.set({ status: 'anonymous' });
  }

  private setAuthenticated(me: SessionResponse): void {
    this.state.set({
      status: 'authenticated',
      username: me.username,
      mustChangePassword: me.mustChangePassword,
      totpSetupRequired: me.totpSetupRequired,
    });
  }
}
