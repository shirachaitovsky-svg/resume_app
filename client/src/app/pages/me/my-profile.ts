import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, Injector, afterNextRender, effect, inject, input, output, signal, untracked } from '@angular/core';
import { Observable, Subscription, switchMap, throwError } from 'rxjs';
import { AppError, withRetryHint } from '../../shared/api/app-error';
import { CohortRef, OwnProfile } from '../../shared/api/profile.models';
import { STRINGS } from '../../shared/strings.he';
import { Button } from '../../shared/ui/button/button';
import { ErrorState } from '../../shared/ui/error-state/error-state';
import { LoadingSkeleton } from '../../shared/ui/loading-skeleton/loading-skeleton';
import { MeSessionService } from './me-session';
import { ProfileApi, ProfilePayload } from './profile-api';
import { ProfileForm } from './profile-form/profile-form';

type State =
  | { readonly status: 'loading' }
  | { readonly status: 'create'; readonly cohorts: readonly CohortRef[] }
  | { readonly status: 'edit'; readonly own: OwnProfile }
  | { readonly status: 'error'; readonly error: AppError };

/** Loads what the shared form needs (cohorts to create, the stored profile to edit) and reacts to its outcomes. */
@Component({
  selector: 'app-my-profile',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Button, ErrorState, LoadingSkeleton, ProfileForm],
  template: `
    @switch (state().status) {
      @case ('loading') {
        <app-loading-skeleton [rows]="6" />
      }
      @case ('error') {
        <app-error-state [error]="displayError()" (retry)="load()" />
      }
      @case ('create') {
        @if (cohortList().length === 0) {
          <p class="empty" role="status">{{ text.noCohorts }}</p>
        } @else {
          <app-profile-form
            mode="create"
            [email]="email()"
            [cohorts]="cohortList()"
            [initialCohortId]="cohortParam()"
            [submitFn]="createFn"
            (saved)="onCreated()"
            (profileExists)="reloadAfterCreate(false)"
            (unauthorized)="signedOut.emit()"
          />
        }
      }
      @case ('edit') {
        @if (created()) {
          <div class="created" role="status">
            <h2 tabindex="-1">{{ joinText.confirmTitle }}</h2>
            <p>{{ joinText.confirmBody }}</p>
          </div>
        }
        @if (savedNote()) {
          <p class="saved" role="status">{{ text.form.savedEdit }}</p>
        }
        @if (refreshFailed()) {
          <div class="saved" role="alert">
            <p>{{ text.form.savedRefreshFailed }}</p>
            <app-button variant="secondary" (click)="onEdited()">{{ text.form.retryRefresh }}</app-button>
          </div>
        }
        <app-profile-form
          mode="edit"
          [email]="email()"
          [own]="own()"
          [submitFn]="updateFn"
          (saved)="onEdited()"
          (unauthorized)="signedOut.emit()"
        />
      }
    }
  `,
  styles: `
    :host {
      display: block;
    }
    .created {
      margin-block-end: 24px;
      padding: 16px;
      border: 1px solid var(--color-outline-strong);
      border-radius: var(--radius-md);
      background: var(--color-surface-muted);
    }
    .created h2 {
      margin-block-end: 8px;
    }
    .saved {
      margin-block: 0 16px;
      font-weight: 600;
    }
    .created p,
    .empty {
      margin: 0;
    }
  `,
})
export class MyProfile {
  private readonly api = inject(ProfileApi);
  private readonly session = inject(MeSessionService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private sub: Subscription | null = null;

  protected readonly text = STRINGS.myProfile;
  protected readonly joinText = STRINGS.joinForm;

  readonly email = input('');
  readonly hasProfile = input.required<boolean>();
  /** Cohort id from `?cohort=`, selected up front when it is an active cohort. */
  readonly cohortParam = input<string | null>(null);
  readonly signedOut = output<void>();

  protected readonly state = signal<State>({ status: 'loading' });
  protected readonly created = signal(false);
  protected readonly savedNote = signal(false);
  protected readonly refreshFailed = signal(false);

  protected readonly createFn = (payload: ProfilePayload): Observable<unknown> => this.api.create(payload);
  protected readonly updateFn = (payload: ProfilePayload): Observable<unknown> => this.api.update(payload);

  constructor() {
    inject(DestroyRef).onDestroy(() => this.sub?.unsubscribe());
    effect(() => {
      this.hasProfile();
      untracked(() => this.load());
    });
  }

  protected cohortList(): readonly CohortRef[] {
    const s = this.state();
    return s.status === 'create' ? s.cohorts : [];
  }

  protected own(): OwnProfile | null {
    const s = this.state();
    return s.status === 'edit' ? s.own : null;
  }

  protected displayError(): AppError | null {
    const s = this.state();
    return s.status === 'error' ? withRetryHint(s.error) : null;
  }

  protected load(): void {
    this.sub?.unsubscribe();
    this.savedNote.set(false);
    this.refreshFailed.set(false);
    this.state.set({ status: 'loading' });
    if (this.hasProfile()) {
      this.sub = this.api.own().subscribe({
        next: (own) => this.state.set({ status: 'edit', own }),
        error: (err: AppError) => this.fail(err),
      });
    } else {
      this.sub = this.api.activeCohorts().subscribe({
        next: (list) => this.state.set({ status: 'create', cohorts: list.items }),
        error: (err: AppError) => this.fail(err),
      });
    }
  }

  protected onCreated(): void {
    this.reloadAfterCreate(true);
  }

  /** The new profile changes the session: read `me` again, then the stored profile, and switch to editing. */
  protected reloadAfterCreate(showConfirmation: boolean): void {
    this.sub?.unsubscribe();
    this.state.set({ status: 'loading' });
    this.sub = this.session
      .me()
      .pipe(
        switchMap((me) =>
          me.hasProfile ? this.api.own() : throwError(() => new AppError('server', 500, STRINGS.errors.server)),
        ),
      )
      .subscribe({
        next: (own) => {
          this.created.set(showConfirmation);
          this.state.set({ status: 'edit', own });
          if (showConfirmation) {
            afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>('.created h2')?.focus(), {
              injector: this.injector,
            });
          }
        },
        error: (err: AppError) => this.fail(err),
      });
  }

  /** The save worked; the reload only refreshes the view, so a failed reload keeps the form and says so. */
  protected onEdited(): void {
    this.created.set(false);
    this.savedNote.set(true);
    this.refreshFailed.set(false);
    this.sub?.unsubscribe();
    this.sub = this.api.own().subscribe({
      next: (own) => this.state.set({ status: 'edit', own }),
      error: (err: AppError) => {
        if (err.status === 401) this.signedOut.emit();
        else this.refreshFailed.set(true);
      },
    });
  }

  private fail(err: AppError): void {
    if (err.status === 401) this.signedOut.emit();
    else this.state.set({ status: 'error', error: err });
  }
}
