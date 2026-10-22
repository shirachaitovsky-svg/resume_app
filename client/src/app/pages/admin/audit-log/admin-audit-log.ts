import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
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
import { AUDIT_PAGE_SIZE, AuditEntry, AuditLogService } from './audit-log.service';

const timeFormat = new Intl.DateTimeFormat('he-IL', { dateStyle: 'short', timeStyle: 'short' });

/** Admin screen: the audit log, read-only. Nothing here can change an entry. */
@Component({
  selector: 'app-admin-audit-log-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Button, EmptyState, ErrorState, GlassCard, LoadingSkeleton, RouterLink],
  templateUrl: './admin-audit-log.html',
  styleUrls: ['../admin-shared.css', './admin-audit-log.css'],
})
export class AdminAuditLogPage {
  private readonly audit = inject(AuditLogService);
  private readonly session = inject(AdminSession);
  private readonly router = inject(Router);

  protected readonly strings = STRINGS;
  protected readonly text = STRINGS.admin.auditLog;
  protected readonly paging = STRINGS.admin.paging;

  protected readonly items = signal<readonly AuditEntry[]>([]);
  protected readonly total = signal(0);
  protected readonly offset = signal(0);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<AppError | null>(null);
  protected readonly loggingOut = signal(false);

  protected readonly hasPrevious = computed(() => this.offset() > 0);
  protected readonly hasNext = computed(() => this.offset() + AUDIT_PAGE_SIZE < this.total());
  protected readonly needsPaging = computed(() => this.hasPrevious() || this.hasNext());
  protected readonly rangeText = computed(() =>
    this.paging.range
      .replace('{from}', String(this.items().length === 0 ? 0 : this.offset() + 1))
      .replace('{to}', String(this.offset() + this.items().length))
      .replace('{total}', String(this.total())),
  );

  private loadSub: Subscription | null = null;
  private logoutSub: Subscription | null = null;
  private loadSeq = 0;

  constructor() {
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
    this.loadSub = this.audit.list(this.offset()).subscribe({
      next: (page) => {
        if (seq !== this.loadSeq) return;
        this.items.set(page.items);
        this.total.set(page.total);
        this.loading.set(false);
      },
      error: (err: AppError) => {
        if (seq !== this.loadSeq || this.onUnauthorized(err)) return;
        this.loading.set(false);
        this.loadError.set(withRetryHint(err));
      },
    });
  }

  protected goPrevious(): void {
    this.offset.set(Math.max(0, this.offset() - AUDIT_PAGE_SIZE));
    this.load();
  }

  protected goNext(): void {
    this.offset.set(this.offset() + AUDIT_PAGE_SIZE);
    this.load();
  }

  protected timeText(entry: AuditEntry): string {
    const date = new Date(entry.at);
    return Number.isNaN(date.getTime()) ? '' : timeFormat.format(date);
  }

  protected actorText(entry: AuditEntry): string {
    return entry.actorName ?? this.text.actorUnknown;
  }

  protected actionText(entry: AuditEntry): string {
    return this.text.actions[entry.action] ?? entry.action;
  }

  protected entityTypeText(entry: AuditEntry): string {
    return this.text.entities[entry.entityType] ?? entry.entityType;
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
}
