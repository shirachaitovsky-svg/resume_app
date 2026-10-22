import { NgTemplateOutlet } from '@angular/common';
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
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { AppError, withRetryHint } from '../../../shared/api/app-error';
import { STRINGS } from '../../../shared/strings.he';
import { safeHttpUrl } from '../../../shared/url';
import { AvatarImage } from '../../../shared/ui/avatar/avatar';
import { Button } from '../../../shared/ui/button/button';
import { ConfirmDialog } from '../../../shared/ui/confirm-dialog/confirm-dialog';
import { ErrorState } from '../../../shared/ui/error-state/error-state';
import { GlassCard } from '../../../shared/ui/glass-card/glass-card';
import { LoadingSkeleton } from '../../../shared/ui/loading-skeleton/loading-skeleton';
import { SafeMarkdown } from '../../../shared/ui/safe-markdown/safe-markdown';
import { Textarea } from '../../../shared/ui/textarea/textarea';
import { ADMIN_LOGIN, AdminSession } from '../admin-session';
import { formatDate } from './admin-review';
import { ReviewService, SubmissionDetailData } from './review.service';

export const RETURN_NOTE_MAX = 1000;
const ADMIN_REVIEW = '/admin/review';

type RowKind = 'text' | 'link' | 'markdown' | 'avatar' | 'photo';

interface CompareRow {
  readonly key: string;
  readonly label: string;
  readonly kind: RowKind;
  readonly proposed: string | null;
  readonly published: string | null;
  readonly changed: boolean;
}

type Decision = 'approve' | 'return' | null;

@Component({
  selector: 'app-admin-submission-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    AvatarImage,
    Button,
    ConfirmDialog,
    ErrorState,
    GlassCard,
    LoadingSkeleton,
    NgTemplateOutlet,
    RouterLink,
    SafeMarkdown,
    Textarea,
  ],
  templateUrl: './submission-detail.html',
  styleUrls: ['../admin-shared.css', './submission-detail.css'],
})
export class AdminSubmissionDetailPage {
  private readonly review = inject(ReviewService);
  private readonly session = inject(AdminSession);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly revisionId = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';

  protected readonly strings = STRINGS;
  protected readonly text = STRINGS.admin.review.detail;
  protected readonly queueLink = ADMIN_REVIEW;

  protected readonly detail = signal<SubmissionDetailData | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<AppError | null>(null);
  protected readonly notFound = signal(false);
  protected readonly actionError = signal<string | null>(null);

  protected readonly note = signal('');
  protected readonly noteError = signal<string | null>(null);
  protected readonly decision = signal<Decision>(null);
  protected readonly busy = signal(false);

  protected readonly photoSrc = computed(() => this.review.photoUrl(this.revisionId));
  protected readonly isUpdate = computed(() => this.detail()?.published != null);
  protected readonly submittedText = computed(() =>
    this.text.submittedAt.replace('{date}', () => formatDate(this.detail()?.submittedAt ?? null)),
  );
  protected readonly approveMessage = computed(() =>
    this.text.approveConfirmMessage.replace('{name}', () => this.detail()?.name ?? ''),
  );
  protected readonly returnMessage = computed(() =>
    this.text.returnConfirmMessage.replace('{name}', () => this.detail()?.name ?? ''),
  );

  protected readonly rows = computed<readonly CompareRow[]>(() => {
    const d = this.detail();
    if (!d) return [];
    const p = d.published;
    const f = this.text.fields;
    const changed = new Set(d.changes);
    const row = (
      key: string,
      label: string,
      kind: RowKind,
      proposed: string | null,
      published: string | null,
    ): CompareRow => ({ key, label, kind, proposed, published, changed: changed.has(key) });
    return [
      row('name', f.name, 'text', d.name, p?.name ?? null),
      row('productName', f.productName, 'text', d.productName, p?.productName ?? null),
      row('productUrl', f.productUrl, 'link', d.productUrl, p?.productUrl ?? null),
      row('description', f.description, 'text', d.description, p?.description ?? null),
      row('role', f.role, 'text', d.role, p?.role ?? null),
      row('workplace', f.workplace, 'text', d.workplace, p?.workplace ?? null),
      row('introText', f.introText, 'markdown', d.introText, p?.introText ?? null),
      row('introAnswer1', f.introAnswer1, 'markdown', d.introAnswer1, p?.introAnswer1 ?? null),
      row('introAnswer2', f.introAnswer2, 'markdown', d.introAnswer2, p?.introAnswer2 ?? null),
      row('introAnswer3', f.introAnswer3, 'markdown', d.introAnswer3, p?.introAnswer3 ?? null),
      row('avatarId', f.avatarId, 'avatar', d.avatarId, p?.avatarId ?? null),
      row('photo', f.photo, 'photo', d.hasPhoto ? 'yes' : null, p?.hasPhoto ? 'yes' : null),
      row('cv', f.cv, 'markdown', d.cv, p?.cv ?? null),
    ];
  });

  private loadSub: Subscription | null = null;
  private actionSub: Subscription | null = null;
  private logoutSub: Subscription | null = null;

  protected readonly loggingOut = signal(false);

  constructor() {
    this.load();
    inject(DestroyRef).onDestroy(() => {
      this.loadSub?.unsubscribe();
      this.actionSub?.unsubscribe();
      this.logoutSub?.unsubscribe();
    });
  }

  protected load(): void {
    this.loadSub?.unsubscribe();
    this.loading.set(true);
    this.loadError.set(null);
    this.notFound.set(false);
    this.loadSub = this.review.get(this.revisionId).subscribe({
      next: (detail) => {
        this.detail.set(detail);
        this.loading.set(false);
      },
      error: (err: AppError) => {
        if (this.onUnauthorized(err)) return;
        this.loading.set(false);
        if (err.kind === 'notFound') this.notFound.set(true);
        else this.loadError.set(withRetryHint(err));
      },
    });
  }

  protected safeUrl(value: string | null): string | null {
    return safeHttpUrl(value);
  }

  // --- decisions ---

  protected askApprove(): void {
    if (this.busy()) return;
    this.actionError.set(null);
    this.decision.set('approve');
  }

  /** The note is checked here first, so an empty or over-long note never reaches the confirmation window. */
  protected askReturn(): void {
    if (this.busy()) return;
    this.actionError.set(null);
    const problem = this.noteProblem(this.note().trim());
    if (problem) {
      this.noteError.set(problem);
      this.focusSelector('textarea');
      return;
    }
    this.decision.set('return');
  }

  protected cancelDecision(): void {
    if (this.busy()) return;
    this.decision.set(null);
  }

  protected onNoteInput(value: string): void {
    this.note.set(value);
    this.noteError.set(null);
  }

  protected confirmDecision(): void {
    const which = this.decision();
    if (!which || this.busy()) return;
    this.busy.set(true);
    if (which === 'approve') {
      this.actionSub = this.review.approve(this.revisionId).subscribe({
        next: () => this.finish(STRINGS.admin.review.approvedNotice),
        error: (err: AppError) => this.failed(err),
      });
    } else {
      this.actionSub = this.review.returnForCorrection(this.revisionId, this.note().trim()).subscribe({
        next: () => this.finish(STRINGS.admin.review.returnedNotice),
        error: (err: AppError) => this.failed(err),
      });
    }
  }

  private finish(message: string): void {
    this.busy.set(false);
    this.decision.set(null);
    this.review.flash.set(message);
    void this.router.navigateByUrl(ADMIN_REVIEW);
  }

  private failed(err: AppError): void {
    this.busy.set(false);
    const was = this.decision();
    this.decision.set(null);
    if (this.onUnauthorized(err)) return;
    if (err.kind === 'conflict' || err.kind === 'notFound') {
      // Already handled elsewhere (409), or gone (404): the queue reloads and says so.
      this.finish(STRINGS.admin.review.alreadyHandled);
      return;
    }
    if (err.kind === 'validation' && was === 'return') {
      const key = Object.keys(err.fieldErrors).find((k) => k.toLowerCase() === 'note');
      this.noteError.set(key ? (err.fieldErrors[key][0] ?? this.text.noteInvalid) : this.text.noteInvalid);
      this.focusSelector('textarea');
      return;
    }
    this.actionError.set(withRetryHint(err).message || this.text.actionError);
    this.focusSelector('.action-error');
  }

  private noteProblem(note: string): string | null {
    if (note === '') return this.text.noteRequired;
    if (note.length > RETURN_NOTE_MAX) return this.text.noteTooLong;
    return null;
  }

  // --- session ---

  protected logout(): void {
    if (this.loggingOut()) return;
    this.loggingOut.set(true);
    this.logoutSub = this.session.logout().subscribe(() => void this.router.navigateByUrl(ADMIN_LOGIN));
  }

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
