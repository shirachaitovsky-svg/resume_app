import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiClient } from '../../../shared/api/api-client';
import { AppError, withRetryHint } from '../../../shared/api/app-error';
import { STRINGS } from '../../../shared/strings.he';
import { Button } from '../../../shared/ui/button/button';
import { ErrorState } from '../../../shared/ui/error-state/error-state';
import { GlassCard } from '../../../shared/ui/glass-card/glass-card';
import { InfoPage } from '../../../shared/ui/info-page/info-page';
import { LoadingSkeleton } from '../../../shared/ui/loading-skeleton/loading-skeleton';
import { SafeMarkdown } from '../../../shared/ui/safe-markdown/safe-markdown';
import { takeTokenFromFragment } from '../token-fragment';

type Decision = 'approved' | 'declined';
type LinkProblem = 'invalid' | 'expired' | 'alreadyDecided';

type State =
  | { readonly status: 'loading' }
  | { readonly status: 'ready'; readonly visitorName: string; readonly reason: string }
  | { readonly status: 'done'; readonly decision: Decision }
  | { readonly status: 'problem'; readonly problem: LinkProblem }
  | { readonly status: 'error'; readonly error: AppError };

/** 401 invalid, 410 (by `code` or status, never by `kind`) expired, 409 already decided; anything else is not a link problem. */
function linkProblem(err: AppError): LinkProblem | null {
  if (err.status === 410 || err.code === 'link-expired') return 'expired';
  if (err.status === 409) return 'alreadyDecided';
  if (err.status === 401) return 'invalid';
  return null;
}

/**
 * The participant's decision page. Loading only previews (nothing is consumed); the token is used up by an
 * explicit click on one of the two buttons. The visitor's address is never shown here.
 */
@Component({
  selector: 'app-contact-decide-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Button, ErrorState, GlassCard, InfoPage, LoadingSkeleton, SafeMarkdown],
  template: `
    @switch (state().status) {
      @case ('loading') {
        <app-loading-skeleton [rows]="3" />
      }
      @case ('problem') {
        <app-info-page [heading]="problemText()" [body]="''" [showBackLink]="false" />
      }
      @case ('error') {
        <app-error-state [asPage]="true" [error]="displayError()" (retry)="loadPreview()" />
      }
      @case ('done') {
        <app-glass-card>
          <div role="status">
            <h1>{{ doneTitle() }}</h1>
            <p>{{ doneBody() }}</p>
          </div>
        </app-glass-card>
      }
      @case ('ready') {
        <app-glass-card>
          <h1>{{ text.heading }}</h1>
          <p>{{ introText() }}</p>
          <h2 class="reason-label">{{ text.reasonLabel }}</h2>
          <app-safe-markdown [source]="reason()" />
          <p class="note">{{ text.note }}</p>
          @if (decideError()) {
            <p class="error" role="alert">{{ text.decideFailed }}</p>
          }
          <div class="actions">
            <app-button variant="accent" [disabled]="busy()" (click)="decide('approved')">{{
              busy() ? text.sending : text.approve
            }}</app-button>
            <app-button variant="secondary" [disabled]="busy()" (click)="decide('declined')">{{ text.decline }}</app-button>
          </div>
        </app-glass-card>
      }
    }
  `,
  styles: `
    :host {
      display: block;
      max-inline-size: 640px;
      margin-inline: auto;
    }
    .reason-label {
      margin-block: 16px 8px;
      font-size: var(--type-title-size);
    }
    .note {
      color: var(--color-on-surface-muted);
    }
    .error {
      color: var(--color-error);
      font-weight: 600;
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      margin-block-start: 16px;
    }
  `,
})
export class ContactDecidePage {
  private readonly api = inject(ApiClient);
  private readonly token: string | null;
  private sub: Subscription | null = null;

  protected readonly text = STRINGS.contactDecide;
  protected readonly state = signal<State>({ status: 'loading' });
  protected readonly busy = signal(false);
  protected readonly decideError = signal(false);

  constructor() {
    this.token = takeTokenFromFragment(inject(ActivatedRoute).snapshot.fragment, inject(DOCUMENT));
    inject(DestroyRef).onDestroy(() => this.sub?.unsubscribe());
    if (this.token === null) this.state.set({ status: 'problem', problem: 'invalid' });
    else this.loadPreview();
  }

  protected reason(): string {
    const s = this.state();
    return s.status === 'ready' ? s.reason : '';
  }

  protected introText(): string {
    const s = this.state();
    return this.text.intro.replace('{name}', () => (s.status === 'ready' ? s.visitorName : ''));
  }

  protected problemText(): string {
    const s = this.state();
    if (s.status !== 'problem') return '';
    return s.problem === 'expired' ? this.text.expired : s.problem === 'alreadyDecided' ? this.text.alreadyDecided : this.text.invalid;
  }

  protected doneTitle(): string {
    const s = this.state();
    return s.status === 'done' && s.decision === 'declined' ? this.text.declinedTitle : this.text.approvedTitle;
  }

  protected doneBody(): string {
    const s = this.state();
    return s.status === 'done' && s.decision === 'declined' ? this.text.declinedBody : this.text.approvedBody;
  }

  protected displayError(): AppError | null {
    const s = this.state();
    return s.status === 'error' ? withRetryHint(s.error) : null;
  }

  protected loadPreview(): void {
    if (this.token === null) return;
    this.sub?.unsubscribe();
    this.state.set({ status: 'loading' });
    this.sub = this.api
      .post<{ visitorName: string; reason: string }>('/contact-decisions/preview', { token: this.token })
      .subscribe({
        next: (p) => this.state.set({ status: 'ready', visitorName: p.visitorName, reason: p.reason }),
        error: (err: AppError) => {
          const problem = linkProblem(err);
          this.state.set(problem ? { status: 'problem', problem } : { status: 'error', error: err });
        },
      });
  }

  protected decide(decision: Decision): void {
    if (this.token === null || this.busy() || this.state().status !== 'ready') return;
    this.busy.set(true);
    this.decideError.set(false);
    this.sub?.unsubscribe();
    this.sub = this.api.post<unknown>('/contact-decisions/decide', { token: this.token, decision }).subscribe({
      next: () => {
        this.busy.set(false);
        this.state.set({ status: 'done', decision });
      },
      error: (err: AppError) => {
        this.busy.set(false);
        const problem = linkProblem(err);
        if (problem) this.state.set({ status: 'problem', problem });
        else this.decideError.set(true);
      },
    });
  }
}
