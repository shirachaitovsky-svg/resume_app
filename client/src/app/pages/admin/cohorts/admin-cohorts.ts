import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { AppError, withRetryHint } from '../../../shared/api/app-error';
import { STRINGS } from '../../../shared/strings.he';
import { Button } from '../../../shared/ui/button/button';
import { ConfirmDialog } from '../../../shared/ui/confirm-dialog/confirm-dialog';
import { EmptyState } from '../../../shared/ui/empty-state/empty-state';
import { ErrorState } from '../../../shared/ui/error-state/error-state';
import { Field } from '../../../shared/ui/field/field';
import { GlassCard } from '../../../shared/ui/glass-card/glass-card';
import { LoadingSkeleton } from '../../../shared/ui/loading-skeleton/loading-skeleton';
import { ADMIN_LOGIN, AdminSession } from '../admin-session';
import { AdminCohort, CohortsService } from './cohorts.service';

export const COHORT_NAME_MAX = 100;
const FEEDBACK_MS = 4000;

const dateFormat = new Intl.DateTimeFormat('he-IL', { dateStyle: 'short' });

function formatDate(value: string | null): string {
  const date = value ? new Date(value) : null;
  return date && !Number.isNaN(date.getTime()) ? dateFormat.format(date) : '';
}

@Component({
  selector: 'app-admin-cohorts-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Button, ConfirmDialog, EmptyState, ErrorState, Field, GlassCard, LoadingSkeleton, RouterLink],
  templateUrl: './admin-cohorts.html',
  styleUrls: ['../admin-shared.css', './admin-cohorts.css'],
})
export class AdminCohortsPage {
  private readonly cohorts = inject(CohortsService);
  private readonly session = inject(AdminSession);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  protected readonly strings = STRINGS;
  protected readonly text = STRINGS.admin.cohorts;

  protected readonly items = signal<readonly AdminCohort[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<AppError | null>(null);
  /** Failure of the revoke action, shown at the top of the list. */
  protected readonly actionError = signal<string | null>(null);
  /** Short non-error message (for example after a 404 on revoke). */
  protected readonly notice = signal<string | null>(null);

  protected readonly name = signal('');
  protected readonly nameError = signal<string | null>(null);
  protected readonly summary = signal<string | null>(null);
  protected readonly submitting = signal(false);
  protected readonly created = signal<AdminCohort | null>(null);

  protected readonly feedback = signal<string | null>(null);
  protected readonly pendingRevoke = signal<AdminCohort | null>(null);
  protected readonly revoking = signal(false);
  protected readonly loggingOut = signal(false);

  protected readonly createdMessage = computed(() =>
    this.text.created.replace('{name}', () => this.created()?.name ?? ''),
  );
  protected readonly revokeMessage = computed(() =>
    this.text.revokeConfirmMessage.replace('{name}', () => this.pendingRevoke()?.name ?? ''),
  );

  private loadSub: Subscription | null = null;
  private createSub: Subscription | null = null;
  private revokeSub: Subscription | null = null;
  private logoutSub: Subscription | null = null;
  private feedbackTimer: ReturnType<typeof setTimeout> | null = null;
  private loadSeq = 0;

  constructor() {
    this.load();
    inject(DestroyRef).onDestroy(() => {
      this.loadSub?.unsubscribe();
      this.createSub?.unsubscribe();
      this.revokeSub?.unsubscribe();
      this.logoutSub?.unsubscribe();
      this.clearFeedbackTimer();
    });
  }

  // --- list ---

  /** Loads the list. `quiet` keeps the current rows on screen (used after a 409 or 404). */
  protected load(quiet = false, onLoaded?: () => void): void {
    this.loadSub?.unsubscribe();
    const seq = ++this.loadSeq;
    if (!quiet) {
      this.loading.set(true);
      this.loadError.set(null);
    }
    this.loadSub = this.cohorts.list().subscribe({
      next: (items) => {
        if (seq !== this.loadSeq) return;
        this.items.set(items);
        this.loadError.set(null);
        this.loading.set(false);
        onLoaded?.();
      },
      error: (err: AppError) => {
        if (seq !== this.loadSeq || this.onUnauthorized(err)) return;
        this.loading.set(false);
        if (quiet) this.actionError.set(withRetryHint(err).message);
        else this.loadError.set(withRetryHint(err));
      },
    });
  }

  protected statusText(cohort: AdminCohort): string {
    return cohort.revoked
      ? this.text.statusRevoked.replace('{date}', formatDate(cohort.revokedAt))
      : this.text.statusActive;
  }

  protected createdText(cohort: AdminCohort): string {
    return this.text.createdOn.replace('{date}', formatDate(cohort.createdAt));
  }

  protected copyAria(cohort: AdminCohort): string {
    return this.text.copyAria.replace('{name}', () => cohort.name);
  }

  protected revokeAria(cohort: AdminCohort): string {
    return this.text.revokeAria.replace('{name}', () => cohort.name);
  }

  // --- create ---

  protected create(event: Event): void {
    event.preventDefault();
    if (this.submitting()) return;
    const name = this.name().trim();
    const problem = name === '' ? this.text.nameRequired : name.length > COHORT_NAME_MAX ? this.text.nameTooLong : null;
    if (problem) {
      this.showCreateError(problem, problem);
      return;
    }
    // The list may still be loading (or have failed): the new cohort is shown at once and the list refetched after it.
    const listNotReady = this.loading() || this.loadError() !== null;
    this.submitting.set(true);
    this.nameError.set(null);
    this.summary.set(null);
    this.createSub = this.cohorts.create(name).subscribe({
      next: (cohort) => {
        this.submitting.set(false);
        this.items.update((list) => [cohort, ...list.filter((c) => c.id !== cohort.id)]);
        this.created.set(cohort);
        this.name.set('');
        if (listNotReady) {
          this.loading.set(false);
          this.loadError.set(null);
          // Supersedes any pending load; this request starts after the create, so it includes the new cohort.
          this.load(true);
        }
        this.focusSelector('[data-field="name"] input');
      },
      error: (err: AppError) => {
        this.submitting.set(false);
        if (this.onUnauthorized(err)) return;
        if (err.kind === 'validation') {
          const own = this.fieldMessage(err);
          this.showCreateError(own ?? this.text.nameInvalid, this.text.errorSummary);
        } else {
          this.summary.set(withRetryHint(err).message);
          this.focusSelector('.create .alert');
        }
      },
    });
  }

  private fieldMessage(err: AppError): string | null {
    const key = Object.keys(err.fieldErrors).find((k) => k.toLowerCase() === 'name');
    return key ? (err.fieldErrors[key][0] ?? null) : null;
  }

  /** Field error beside the input plus a summary on top; the typed value is kept. */
  private showCreateError(fieldText: string, summaryText: string): void {
    this.nameError.set(fieldText);
    this.summary.set(summaryText);
    this.focusSelector('[data-field="name"] input');
  }

  // --- copy ---

  /** Copies the invite link; without the clipboard API the text is selected for Ctrl+C. */
  protected async copy(cohort: AdminCohort, linkId: string): Promise<void> {
    let ok = false;
    try {
      await navigator.clipboard.writeText(cohort.inviteUrl);
      ok = true;
    } catch {
      // No API or permission: fall through to the manual path.
    }
    if (!ok) this.selectLink(linkId);
    this.showFeedback(ok ? this.text.copied : this.text.copyManual);
  }

  private selectLink(linkId: string): void {
    const el = this.host.nativeElement.querySelector<HTMLElement>(`[data-link="${CSS.escape(linkId)}"]`);
    const selection = window.getSelection();
    if (!el || !selection) return;
    const range = document.createRange();
    range.selectNodeContents(el);
    selection.removeAllRanges();
    selection.addRange(range);
  }

  private showFeedback(message: string): void {
    this.clearFeedbackTimer();
    this.feedback.set(message);
    this.feedbackTimer = setTimeout(() => this.feedback.set(null), FEEDBACK_MS);
  }

  private clearFeedbackTimer(): void {
    if (this.feedbackTimer !== null) clearTimeout(this.feedbackTimer);
    this.feedbackTimer = null;
  }

  // --- revoke ---

  protected askRevoke(cohort: AdminCohort): void {
    this.actionError.set(null);
    this.notice.set(null);
    this.pendingRevoke.set(cohort);
  }

  protected cancelRevoke(): void {
    if (this.revoking()) return;
    this.pendingRevoke.set(null);
  }

  protected confirmRevoke(): void {
    const cohort = this.pendingRevoke();
    if (!cohort || this.revoking()) return;
    this.revoking.set(true);
    this.revokeSub = this.cohorts.revoke(cohort.id).subscribe({
      next: () => {
        this.revoking.set(false);
        this.pendingRevoke.set(null);
        this.clearCreated(cohort.id);
        this.items.update((list) =>
          list.map((c) => (c.id === cohort.id ? { ...c, revoked: true, revokedAt: new Date().toISOString() } : c)),
        );
        // The revoke button is gone now; keep keyboard focus on the row it belonged to.
        this.focusRow(cohort.id);
      },
      error: (err: AppError) => {
        this.revoking.set(false);
        this.pendingRevoke.set(null);
        if (this.onUnauthorized(err)) return;
        if (err.kind === 'conflict') {
          this.clearCreated(cohort.id);
          // Focus only once the reload has re-rendered the rows.
          this.load(true, () => this.focusRow(cohort.id));
        } else if (err.kind === 'notFound') {
          this.clearCreated(cohort.id);
          this.notice.set(this.text.revokeNotFound);
          this.load(true, () => this.focusSelector('.list-notice'));
        } else {
          this.actionError.set(withRetryHint(err).message || this.text.actionError);
          this.focusSelector('.list-heading + .alert');
        }
      },
    });
  }

  private clearCreated(id: string): void {
    if (this.created()?.id === id) this.created.set(null);
  }

  /** Typing a correction clears the stale error and summary. */
  protected onNameInput(value: string): void {
    this.name.set(value);
    this.nameError.set(null);
    this.summary.set(null);
  }

  private focusRow(id: string): void {
    this.focusSelector(`#${CSS.escape('cohort-' + id)}`);
  }

  // --- session ---

  protected logout(): void {
    if (this.loggingOut()) return;
    this.loggingOut.set(true);
    this.logoutSub = this.session.logout().subscribe(() => void this.router.navigateByUrl(ADMIN_LOGIN));
  }

  /** A 401 in the middle of the session: back to the login screen with a notice. */
  private onUnauthorized(err: AppError): boolean {
    if (!this.session.handleUnauthorized(err)) return false;
    void this.router.navigateByUrl(ADMIN_LOGIN);
    return true;
  }

  private focusSelector(selector: string): void {
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>(selector)?.focus(), {
      injector: this.injector,
    });
  }
}
