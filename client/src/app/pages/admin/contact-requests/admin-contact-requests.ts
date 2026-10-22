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
import { SafeMarkdown } from '../../../shared/ui/safe-markdown/safe-markdown';
import { ADMIN_LOGIN, AdminSession } from '../admin-session';
import { formatDate } from '../review/admin-review';
import { ContactRequestItem, ContactRequestsService } from './contact-requests.service';

type ActionKind = 'send' | 'approve';
interface Pending {
  readonly kind: ActionKind;
  readonly item: ContactRequestItem;
}

@Component({
  selector: 'app-admin-contact-requests-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Button, ConfirmDialog, EmptyState, ErrorState, GlassCard, LoadingSkeleton, RouterLink, SafeMarkdown],
  templateUrl: './admin-contact-requests.html',
  styleUrls: ['../admin-shared.css', './admin-contact-requests.css'],
})
export class AdminContactRequestsPage {
  private readonly service = inject(ContactRequestsService);
  private readonly session = inject(AdminSession);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  protected readonly strings = STRINGS;
  protected readonly text = STRINGS.admin.contactRequests;

  protected readonly items = signal<readonly ContactRequestItem[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<AppError | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly pending = signal<Pending | null>(null);
  protected readonly acting = signal(false);
  protected readonly loggingOut = signal(false);

  protected readonly dialogHeading = computed(() =>
    this.pending()?.kind === 'approve' ? this.text.approveConfirmHeading : this.text.sendConfirmHeading,
  );
  protected readonly dialogConfirm = computed(() =>
    this.pending()?.kind === 'approve' ? this.text.approveConfirm : this.text.sendConfirm,
  );
  protected readonly dialogCancel = computed(() =>
    this.pending()?.kind === 'approve' ? this.text.approveCancel : this.text.sendCancel,
  );
  protected readonly dialogMessage = computed(() => {
    const p = this.pending();
    if (!p) return '';
    const template = p.kind === 'approve' ? this.text.approveConfirmMessage : this.text.sendConfirmMessage;
    return template.replace('{visitor}', () => p.item.visitorName).replace('{profile}', () => p.item.profileName);
  });

  private loadSub: Subscription | null = null;
  private actionSub: Subscription | null = null;
  private logoutSub: Subscription | null = null;
  private loadSeq = 0;

  constructor() {
    this.load();
    inject(DestroyRef).onDestroy(() => {
      this.loadSub?.unsubscribe();
      this.actionSub?.unsubscribe();
      this.logoutSub?.unsubscribe();
    });
  }

  /** Loads the queue. `quiet` keeps the rows on screen (used after an action or a 409). */
  protected load(quiet = false): void {
    this.loadSub?.unsubscribe();
    const seq = ++this.loadSeq;
    if (!quiet) {
      this.loading.set(true);
      this.loadError.set(null);
    }
    this.loadSub = this.service.list().subscribe({
      next: (page) => {
        if (seq !== this.loadSeq) return;
        this.items.set(page.items);
        this.total.set(page.total);
        this.loadError.set(null);
        this.loading.set(false);
      },
      error: (err: AppError) => {
        if (seq !== this.loadSeq || this.onUnauthorized(err)) return;
        this.loading.set(false);
        if (quiet) this.actionError.set(withRetryHint(err).message);
        else this.loadError.set(withRetryHint(err));
      },
    });
  }

  protected shownText(): string {
    return this.strings.admin.shownOfTotal
      .replace('{shown}', String(this.items().length))
      .replace('{total}', String(this.total()));
  }

  protected dateText(item: ContactRequestItem): string {
    return formatDate(item.verifiedAt);
  }

  protected statusText(item: ContactRequestItem): string {
    return this.text.statuses[item.status] ?? item.status;
  }

  protected participantText(item: ContactRequestItem): string {
    const d = item.participantDecision;
    return d === 'approved'
      ? this.text.participantApproval.approved
      : d === 'declined'
        ? this.text.participantApproval.declined
        : this.text.participantApproval.pending;
  }

  protected managerText(item: ContactRequestItem): string {
    return item.managerApprovedAt ? this.text.managerApproval.approved : this.text.managerApproval.pending;
  }

  protected deliveryText(item: ContactRequestItem): string {
    return this.text.delivery[item.deliveryStatus] ?? item.deliveryStatus;
  }

  protected errorCategoryText(item: ContactRequestItem): string {
    return this.text.errorCategory.replace('{category}', () => item.deliveryErrorCategory ?? '');
  }

  /** The delivery approval is open only once the participant approved, and again after a dead delivery (manual retry). */
  protected canApprove(item: ContactRequestItem): boolean {
    return (
      item.status === 'participant_approved' && item.deliveryStatus !== 'pending' && item.deliveryStatus !== 'sent'
    );
  }

  protected approveLabel(item: ContactRequestItem): string {
    return item.deliveryStatus === 'dead' ? this.text.retryDelivery : this.text.approveDelivery;
  }

  protected sendAria(item: ContactRequestItem): string {
    return this.text.sendToParticipantAria
      .replace('{visitor}', () => item.visitorName)
      .replace('{profile}', () => item.profileName);
  }

  protected approveAria(item: ContactRequestItem): string {
    return this.text.approveDeliveryAria
      .replace('{visitor}', () => item.visitorName)
      .replace('{profile}', () => item.profileName);
  }

  /** Why "approve and send" is locked for this request; null when it is available or there is nothing to explain. */
  protected lockedReason(item: ContactRequestItem): string | null {
    if (this.canApprove(item)) return null;
    switch (item.status) {
      case 'submitted':
        return this.text.lockedSubmitted;
      case 'sent_to_participant':
        return this.text.lockedSent;
      case 'participant_declined':
        return this.text.lockedDeclined;
      case 'delivered':
        return this.text.lockedDelivered;
      case 'expired':
        return this.text.lockedExpired;
      case 'participant_approved':
        return this.text.lockedPending;
      default:
        return null;
    }
  }

  protected ask(kind: ActionKind, item: ContactRequestItem): void {
    if (this.acting()) return;
    this.actionError.set(null);
    this.notice.set(null);
    this.pending.set({ kind, item });
  }

  protected cancel(): void {
    if (this.acting()) return;
    this.pending.set(null);
  }

  protected confirm(): void {
    const p = this.pending();
    if (!p || this.acting()) return;
    this.acting.set(true);
    this.actionSub?.unsubscribe();
    const call =
      p.kind === 'send'
        ? this.service.sendToParticipant(p.item.requestId)
        : this.service.approveDelivery(p.item.requestId);
    this.actionSub = call.subscribe({
      next: () => {
        this.acting.set(false);
        this.pending.set(null);
        this.notice.set(p.kind === 'send' ? this.text.sentNotice : this.text.approvedNotice);
        this.load(true);
        this.focusSelector('.list-notice');
      },
      error: (err: AppError) => {
        this.acting.set(false);
        this.pending.set(null);
        if (this.onUnauthorized(err)) return;
        if (err.kind === 'conflict' || err.kind === 'notFound') {
          this.notice.set(err.kind === 'conflict' ? this.text.staleNotice : this.text.notFoundNotice);
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
