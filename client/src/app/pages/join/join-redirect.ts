import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { Router, RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiClient } from '../../shared/api/api-client';
import { AppError, withRetryHint } from '../../shared/api/app-error';
import { STRINGS } from '../../shared/strings.he';
import { ErrorState } from '../../shared/ui/error-state/error-state';
import { GlassCard } from '../../shared/ui/glass-card/glass-card';
import { LoadingSkeleton } from '../../shared/ui/loading-skeleton/loading-skeleton';

type State =
  | { readonly status: 'loading' }
  | { readonly status: 'invalid' }
  | { readonly status: 'error'; readonly error: AppError };

const TOKEN_RE = /^[A-Za-z0-9_-]{1,200}$/;

interface PublicInvite {
  readonly cohortId: string;
  readonly cohortName: string;
}

/** Old invite links keep working: the token only pre-selects the cohort on "My profile". */
@Component({
  selector: 'app-join-redirect-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ErrorState, GlassCard, LoadingSkeleton, RouterLink],
  template: `
    @switch (state().status) {
      @case ('loading') {
        <app-loading-skeleton [rows]="3" />
      }
      @case ('invalid') {
        <app-glass-card>
          <h1>{{ text.invalidTitle }}</h1>
          <p>{{ text.invalidBody }}</p>
          <a class="link-button link-button--primary" routerLink="/me">{{ text.continueToProfile }}</a>
        </app-glass-card>
      }
      @case ('error') {
        <app-error-state [asPage]="true" [error]="displayError()" (retry)="retry()" />
      }
    }
  `,
  styles: `
    :host {
      display: block;
    }
  `,
})
export class JoinRedirectPage {
  private readonly api = inject(ApiClient);
  private readonly router = inject(Router);
  private readonly title = inject(Title);
  private sub: Subscription | null = null;

  protected readonly text = STRINGS.joinForm;
  /** Route parameter `:token` (bound through withComponentInputBinding). */
  readonly token = input<string | undefined>(undefined);
  protected readonly state = signal<State>({ status: 'loading' });

  constructor() {
    inject(DestroyRef).onDestroy(() => this.sub?.unsubscribe());
    effect(() => {
      const token = this.token();
      untracked(() => this.load(token));
    });
  }

  protected displayError(): AppError | null {
    const s = this.state();
    return s.status === 'error' ? withRetryHint(s.error) : null;
  }

  protected retry(): void {
    this.load(this.token());
  }

  private load(token: string | undefined): void {
    this.sub?.unsubscribe();
    if (!token || !TOKEN_RE.test(token)) {
      this.setInvalid();
      return;
    }
    this.state.set({ status: 'loading' });
    this.sub = this.api.get<PublicInvite>(`/invites/${encodeURIComponent(token)}`).subscribe({
      next: (invite) => {
        void this.router.navigate(['/me'], { queryParams: { cohort: invite.cohortId }, replaceUrl: true });
      },
      error: (err: AppError) => {
        if (err.kind === 'notFound') this.setInvalid();
        else this.state.set({ status: 'error', error: err });
      },
    });
  }

  private setInvalid(): void {
    this.state.set({ status: 'invalid' });
    this.title.setTitle(`${STRINGS.joinForm.invalidTitle} | ${STRINGS.appTitle}`);
  }
}
