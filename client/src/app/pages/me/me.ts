import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { AppError, withRetryHint } from '../../shared/api/app-error';
import { STRINGS } from '../../shared/strings.he';
import { Button } from '../../shared/ui/button/button';
import { ErrorState } from '../../shared/ui/error-state/error-state';
import { GlassCard } from '../../shared/ui/glass-card/glass-card';
import { LoadingSkeleton } from '../../shared/ui/loading-skeleton/loading-skeleton';
import { MeSessionService } from './me-session';
import { MyProfile } from './my-profile';

const GOOGLE_START = '/api/auth/google/start';
const GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export const GOOGLE_START_URL = `${GOOGLE_START}?returnUrl=${encodeURIComponent('/me')}`;

/** The sign-in link; a valid `?cohort=` is carried through the sign-in so the form still opens with it selected. */
export function googleStartUrl(cohortId: string | null): string {
  return cohortId ? `${GOOGLE_START}?returnUrl=${encodeURIComponent(`/me?cohort=${cohortId}`)}` : GOOGLE_START_URL;
}

type State =
  | { readonly status: 'loading' }
  | { readonly status: 'signedOut' }
  | { readonly status: 'signedIn'; readonly email: string; readonly hasProfile: boolean }
  | { readonly status: 'error'; readonly error: AppError };

const SIGNIN_MESSAGES: Readonly<Record<string, string>> = {
  cancelled: STRINGS.myProfile.signinCancelled,
  failed: STRINGS.myProfile.signinFailed,
  unverified: STRINGS.myProfile.signinUnverified,
};

@Component({
  selector: 'app-me-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Button, ErrorState, GlassCard, LoadingSkeleton, MyProfile],
  template: `
    @switch (state().status) {
      @case ('loading') {
        <app-loading-skeleton [rows]="3" />
      }
      @case ('error') {
        <app-error-state [error]="displayError()" (retry)="retry()" />
      }
      @case ('signedOut') {
        <app-glass-card>
          <h1>{{ text.heading }}</h1>
          <p>{{ text.signedOutBody }}</p>
          @if (signinMessage(); as message) {
            <p class="signin-error" role="alert">{{ message }}</p>
          }
          <a class="google-signin" [href]="startUrl">{{ text.signIn }}</a>
        </app-glass-card>
      }
      @case ('signedIn') {
        <app-glass-card>
          <h1>{{ text.heading }}</h1>
          <p class="account">
            {{ text.signedInAs }} <bdi class="email">{{ email() }}</bdi>
          </p>
          @if (logoutError()) {
            <p class="signin-error" role="alert">{{ text.logoutFailed }}</p>
          }
          <app-my-profile
            [email]="email()"
            [hasProfile]="hasProfile()"
            [cohortParam]="cohortParam"
            (signedOut)="onSignedOut()"
          />
          <app-button variant="secondary" [disabled]="loggingOut()" (click)="logout()">{{ text.signOut }}</app-button>
        </app-glass-card>
      }
    }
  `,
  styles: `
    :host {
      display: block;
    }
    .signin-error {
      color: var(--color-error);
      font-weight: 600;
    }
    .google-signin {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      box-sizing: border-box;
      min-block-size: var(--size-button);
      min-inline-size: var(--size-touch-min);
      padding-inline: 28px;
      border-radius: var(--radius-full);
      background: var(--color-surface-inverse);
      color: var(--color-on-inverse);
      font-size: var(--type-label-size);
      font-weight: 600;
      text-decoration: none;
    }
    .google-signin:focus-visible {
      outline: 3px solid var(--color-accent);
      outline-offset: 3px;
    }
  `,
})
export class MePage {
  private readonly session = inject(MeSessionService);
  private readonly router = inject(Router);
  private sub: Subscription | null = null;
  private logoutSub: Subscription | null = null;
  private paramPending: boolean;

  protected readonly text = STRINGS.myProfile;
  protected readonly cohortParam: string | null;
  protected readonly startUrl: string;
  protected readonly state = signal<State>({ status: 'loading' });
  protected readonly signinMessage = signal<string | null>(null);
  protected readonly loggingOut = signal(false);
  protected readonly logoutError = signal(false);

  constructor() {
    const params = inject(ActivatedRoute).snapshot.queryParamMap;
    const cohort = params.get('cohort');
    this.cohortParam = cohort !== null && GUID_RE.test(cohort) ? cohort.toLowerCase() : null;
    this.startUrl = googleStartUrl(this.cohortParam);
    const signin = params.get('signin');
    this.paramPending = signin !== null;
    if (signin !== null && Object.hasOwn(SIGNIN_MESSAGES, signin)) this.signinMessage.set(SIGNIN_MESSAGES[signin]);
    inject(DestroyRef).onDestroy(() => {
      this.sub?.unsubscribe();
      this.logoutSub?.unsubscribe();
    });
    this.load();
  }

  protected email(): string {
    const s = this.state();
    return s.status === 'signedIn' ? s.email : '';
  }

  protected hasProfile(): boolean {
    const s = this.state();
    return s.status === 'signedIn' && s.hasProfile;
  }

  protected onSignedOut(): void {
    this.signinMessage.set(null);
    this.state.set({ status: 'signedOut' });
  }

  protected displayError(): AppError | null {
    const s = this.state();
    return s.status === 'error' ? withRetryHint(s.error) : null;
  }

  protected retry(): void {
    this.signinMessage.set(null);
    this.load();
  }

  protected load(): void {
    this.sub?.unsubscribe();
    this.state.set({ status: 'loading' });
    this.sub = this.session.me().subscribe({
      next: (me) => {
        this.signinMessage.set(null);
        this.state.set({ status: 'signedIn', email: me.email, hasProfile: me.hasProfile });
        this.clearSigninParam();
      },
      error: (err: AppError) => {
        this.state.set(err.status === 401 ? { status: 'signedOut' } : { status: 'error', error: err });
        this.clearSigninParam();
      },
    });
  }

  protected logout(): void {
    if (this.loggingOut()) return;
    this.loggingOut.set(true);
    this.logoutError.set(false);
    this.logoutSub = this.session.logout().subscribe({
      next: () => {
        this.loggingOut.set(false);
        this.signinMessage.set(null);
        this.state.set({ status: 'signedOut' });
      },
      error: (err: AppError) => {
        this.loggingOut.set(false);
        if (err.status === 401) {
          this.signinMessage.set(null);
          this.state.set({ status: 'signedOut' });
        } else this.logoutError.set(true);
      },
    });
  }

  private clearSigninParam(): void {
    if (!this.paramPending) return;
    this.paramPending = false;
    void this.router.navigate([], {
      queryParams: { signin: null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }
}
