import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { AppError, withRetryHint } from '../../../shared/api/app-error';
import { STRINGS } from '../../../shared/strings.he';
import { Button } from '../../../shared/ui/button/button';
import { EmptyState } from '../../../shared/ui/empty-state/empty-state';
import { ErrorState } from '../../../shared/ui/error-state/error-state';
import { GlassCard } from '../../../shared/ui/glass-card/glass-card';
import { LoadingSkeleton } from '../../../shared/ui/loading-skeleton/loading-skeleton';
import { ADMIN_LOGIN, AdminSession } from '../admin-session';
import { ReviewService, SubmissionItem } from './review.service';

const dateFormat = new Intl.DateTimeFormat('he-IL', { dateStyle: 'short' });

export function formatDate(value: string | null): string {
  const date = value ? new Date(value) : null;
  return date && !Number.isNaN(date.getTime()) ? dateFormat.format(date) : '';
}

@Component({
  selector: 'app-admin-review-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Button, EmptyState, ErrorState, GlassCard, LoadingSkeleton, RouterLink],
  templateUrl: './admin-review.html',
  styleUrls: ['../admin-shared.css', './admin-review.css'],
})
export class AdminReviewPage {
  private readonly review = inject(ReviewService);
  private readonly session = inject(AdminSession);
  private readonly router = inject(Router);

  protected readonly strings = STRINGS;
  protected readonly text = STRINGS.admin.review;

  protected readonly items = signal<readonly SubmissionItem[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<AppError | null>(null);
  protected readonly notice = signal<string | null>(this.review.flash());
  protected readonly loggingOut = signal(false);

  private loadSub: Subscription | null = null;
  private logoutSub: Subscription | null = null;
  private loadSeq = 0;

  constructor() {
    this.review.flash.set(null);
    this.load();
    inject(DestroyRef).onDestroy(() => {
      this.loadSub?.unsubscribe();
      this.logoutSub?.unsubscribe();
    });
  }

  protected load(): void {
    this.loadSub?.unsubscribe();
    const seq = ++this.loadSeq;
    this.loading.set(true);
    this.loadError.set(null);
    this.loadSub = this.review.list().subscribe({
      next: (page) => {
        if (seq !== this.loadSeq) return;
        this.items.set(page.items);
        this.total.set(page.total);
        this.loading.set(false);
      },
      error: (err: AppError) => {
        if (seq !== this.loadSeq) return;
        if (this.session.handleUnauthorized(err)) {
          void this.router.navigateByUrl(ADMIN_LOGIN);
          return;
        }
        this.loading.set(false);
        this.loadError.set(withRetryHint(err));
      },
    });
  }

  protected shownText(): string {
    return this.strings.admin.shownOfTotal
      .replace('{shown}', String(this.items().length))
      .replace('{total}', String(this.total()));
  }

  protected kindText(item: SubmissionItem): string {
    return item.kind === 'update' ? this.text.kindUpdate : this.text.kindNew;
  }

  protected dateText(item: SubmissionItem): string {
    return formatDate(item.submittedAt);
  }

  protected openAria(item: SubmissionItem): string {
    return this.text.openAria.replace('{name}', () => item.name).replace('{product}', () => item.productName);
  }

  protected logout(): void {
    if (this.loggingOut()) return;
    this.loggingOut.set(true);
    this.logoutSub = this.session.logout().subscribe(() => void this.router.navigateByUrl(ADMIN_LOGIN));
  }
}
