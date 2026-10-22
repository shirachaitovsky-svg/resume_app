import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiClient } from '../../../shared/api/api-client';
import { AppError, withRetryHint } from '../../../shared/api/app-error';
import { STRINGS } from '../../../shared/strings.he';
import { ErrorState } from '../../../shared/ui/error-state/error-state';
import { InfoPage } from '../../../shared/ui/info-page/info-page';
import { LoadingSkeleton } from '../../../shared/ui/loading-skeleton/loading-skeleton';
import { takeTokenFromFragment } from '../token-fragment';

type State =
  | { readonly status: 'verifying' }
  | { readonly status: 'done' }
  | { readonly status: 'invalid' }
  | { readonly status: 'error'; readonly error: AppError };

/** 401, 400, 409 and 410 (by status or code) mean the link can never work; a retry would not help. */
function isLinkProblem(err: AppError): boolean {
  return [400, 401, 409, 410].includes(err.status) || err.code === 'link-expired';
}

/**
 * Target of the link in the verification mail. This is the one page that posts on load: the visitor already
 * clicked the link on purpose, and the server only consumes the token on this POST (mail scanners only GET).
 */
@Component({
  selector: 'app-contact-verify-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ErrorState, InfoPage, LoadingSkeleton],
  template: `
    @switch (state().status) {
      @case ('verifying') {
        <app-loading-skeleton [rows]="2" />
      }
      @case ('done') {
        <app-info-page [heading]="text.successTitle" [body]="text.successBody" />
      }
      @case ('invalid') {
        <app-info-page [heading]="text.invalidTitle" [body]="text.invalidBody" />
      }
      @case ('error') {
        <app-error-state [asPage]="true" [error]="displayError()" (retry)="verify()" />
      }
    }
  `,
})
export class ContactVerifyPage {
  private readonly api = inject(ApiClient);
  private readonly token: string | null;
  private sub: Subscription | null = null;

  protected readonly text = STRINGS.contactVerify;
  protected readonly state = signal<State>({ status: 'verifying' });

  constructor() {
    const fragment = inject(ActivatedRoute).snapshot.fragment;
    this.token = takeTokenFromFragment(fragment, inject(DOCUMENT));
    inject(DestroyRef).onDestroy(() => this.sub?.unsubscribe());
    if (this.token === null) this.state.set({ status: 'invalid' });
    else this.verify();
  }

  protected displayError(): AppError | null {
    const s = this.state();
    return s.status === 'error' ? withRetryHint(s.error) : null;
  }

  protected verify(): void {
    if (this.token === null) return;
    this.sub?.unsubscribe();
    this.state.set({ status: 'verifying' });
    this.sub = this.api.post<unknown>('/contact-requests/verify', { token: this.token }).subscribe({
      next: () => this.state.set({ status: 'done' }),
      error: (err: AppError) =>
        this.state.set(isLinkProblem(err) ? { status: 'invalid' } : { status: 'error', error: err }),
    });
  }
}
