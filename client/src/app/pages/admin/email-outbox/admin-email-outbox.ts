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
import { GlassCard } from '../../../shared/ui/glass-card/glass-card';
import { LoadingSkeleton } from '../../../shared/ui/loading-skeleton/loading-skeleton';
import { ADMIN_LOGIN, AdminSession } from '../admin-session';
import { EmailOutboxService, OUTBOX_PAGE_SIZE, StuckEmail } from './email-outbox.service';

/** Admin screen: emails that failed for good or got stuck, with a manual retry. */
@Component({
  selector: 'app-admin-email-outbox-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Button, ConfirmDialog, EmptyState, ErrorState, GlassCard, LoadingSkeleton, RouterLink],
  templateUrl: './admin-email-outbox.html',
  styleUrls: ['../admin-shared.css', './admin-email-outbox.css'],
})
export class AdminEmailOutboxPage {
  private readonly outbox = inject(EmailOutboxService);
  private readonly session = inject(AdminSession);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  protected readonly strings = STRINGS;
  protected readonly text = STRINGS.admin.emailOutbox;
  protected readonly paging = STRINGS.admin.paging;

  protected readonly items = signal<readonly StuckEmail[]>([]);
  protected readonly total = signal(0);
  protected readonly offset = signal(0);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<AppError | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  protected readonly pendingRetry = signal<StuckEmail | null>(null);
  protected readonly retrying = signal(false);
  protected readonly loggingOut = signal(false);

  protected readonly hasPrevious = computed(() => this.offset() > 0);
  protected readonly hasNext = computed(() => this.offset() + OUTBOX_PAGE_SIZE < this.total());
  protected readonly needsPaging = computed(() => this.hasPrevious() || this.hasNext());
  protected readonly rangeText = computed(() =>
    this.paging.range
      .replace('{from}', String(this.items().length === 0 ? 0 : this.offset() + 1))
      .replace('{to}', String(this.offset() + this.items().length))
      .replace('{total}', String(this.total())),
  );
  protected readonly retryMessage = computed(() =>
    this.text.retryConfirmMessage.replace('{type}', () => this.typeText(this.pendingRetry()?.templateKey ?? '')),
  );

  private loadSub: Subscription | null = null;
  private retrySub: Subscription | null = null;
  private logoutSub: Subscription | null = null;
  private loadSeq = 0;

  constructor() {
    this.load();
    inject(DestroyRef).onDestroy(() => {
      this.loadSub?.unsubscribe();
      this.retrySub?.unsubscribe();
      this.logoutSub?.unsubscribe();
    });
  }

  /** Loads the current page. `quiet` keeps the rows on screen (used after an action). */
  protected load(quiet = false): void {
    this.loadSub?.unsubscribe();
    const seq = ++this.loadSeq;
    if (!quiet) {
      this.loading.set(true);
      this.loadError.set(null);
    }
    this.loadSub = this.outbox.listStuck(this.offset()).subscribe({
      next: (page) => {
        if (seq !== this.loadSeq) return;
        // The list shrank (rows were retried): step back to the last page that still exists.
        if (page.items.length === 0 && page.total > 0 && this.offset() > 0) {
          this.offset.set(Math.max(0, Math.floor((page.total - 1) / OUTBOX_PAGE_SIZE) * OUTBOX_PAGE_SIZE));
          this.load(quiet);
          return;
        }
        this.items.set(page.items);
        this.total.set(page.total);
        this.loadError.set(null);
        this.loading.set(false);
      },
      error: (err: AppError) => {
        if (seq !== this.loadSeq || this.onUnauthorized(err)) return;
        this.loading.set(false);
        if (quiet) {
          // No success notice next to the reload failure over a stale list.
          this.notice.set(null);
          this.actionError.set(withRetryHint(err).message);
        } else {
          this.loadError.set(withRetryHint(err));
        }
      },
    });
  }

  protected goPrevious(): void {
    this.go(Math.max(0, this.offset() - OUTBOX_PAGE_SIZE));
  }

  protected goNext(): void {
    this.go(this.offset() + OUTBOX_PAGE_SIZE);
  }

  private go(offset: number): void {
    this.notice.set(null);
    this.actionError.set(null);
    this.offset.set(offset);
    this.load();
  }

  protected typeText(key: string): string {
    return this.text.templates[key] ?? key;
  }

  protected entityText(item: StuckEmail): string {
    if (item.requestId) return this.text.entityRequest.replace('{id}', item.requestId.slice(0, 8));
    if (item.profileId) return this.text.entityProfile.replace('{id}', item.profileId.slice(0, 8));
    return this.text.entityNone;
  }

  protected statusText(item: StuckEmail): string {
    return item.status === 'dead' ? this.text.statusDead : this.text.statusPending;
  }

  protected ageText(item: StuckEmail): string {
    if (!Number.isFinite(item.ageSeconds)) return '';
    const minutes = Math.floor(item.ageSeconds / 60);
    if (minutes < 60) return this.text.ageMinutes.replace('{n}', String(minutes));
    const hours = Math.floor(minutes / 60);
    if (hours < 48) return this.text.ageHours.replace('{n}', String(hours));
    return this.text.ageDays.replace('{n}', String(Math.floor(hours / 24)));
  }

  protected retryAria(item: StuckEmail): string {
    return this.text.retryAria.replace('{type}', () => this.typeText(item.templateKey));
  }

  protected askRetry(item: StuckEmail): void {
    this.actionError.set(null);
    this.notice.set(null);
    this.pendingRetry.set(item);
  }

  protected cancelRetry(): void {
    if (this.retrying()) return;
    this.pendingRetry.set(null);
  }

  protected confirmRetry(): void {
    const item = this.pendingRetry();
    if (!item || this.retrying()) return;
    this.retrying.set(true);
    this.retrySub?.unsubscribe();
    this.retrySub = this.outbox.retry(item.id).subscribe({
      next: () => {
        this.retrying.set(false);
        this.pendingRetry.set(null);
        this.notice.set(this.text.retried);
        this.load(true);
        this.focusSelector('.list-notice');
      },
      error: (err: AppError) => {
        this.retrying.set(false);
        this.pendingRetry.set(null);
        if (this.onUnauthorized(err)) return;
        if (err.kind === 'conflict' || err.kind === 'notFound') {
          this.notice.set(err.kind === 'conflict' ? this.text.notDead : this.text.notFound);
          this.load(true);
          this.focusSelector('.list-notice');
        } else {
          this.actionError.set(withRetryHint(err).message || this.text.actionError);
          this.focusSelector('.alert');
        }
      },
    });
  }

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
