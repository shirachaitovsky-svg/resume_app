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
import { AdminProfile, ReviewService } from '../review/review.service';

@Component({
  selector: 'app-admin-profiles-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Button, ConfirmDialog, EmptyState, ErrorState, GlassCard, LoadingSkeleton, RouterLink],
  templateUrl: './admin-profiles.html',
  styleUrls: ['../admin-shared.css', './admin-profiles.css'],
})
export class AdminProfilesPage {
  private readonly review = inject(ReviewService);
  private readonly session = inject(AdminSession);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  protected readonly strings = STRINGS;
  protected readonly text = STRINGS.admin.profiles;

  protected readonly items = signal<readonly AdminProfile[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<AppError | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  protected readonly pendingRemove = signal<AdminProfile | null>(null);
  protected readonly removing = signal(false);
  protected readonly loggingOut = signal(false);

  protected readonly removeMessage = computed(() =>
    this.text.removeConfirmMessage.replace('{name}', () => this.pendingRemove()?.name ?? ''),
  );

  private loadSub: Subscription | null = null;
  private removeSub: Subscription | null = null;
  private logoutSub: Subscription | null = null;
  private loadSeq = 0;

  constructor() {
    this.load();
    inject(DestroyRef).onDestroy(() => {
      this.loadSub?.unsubscribe();
      this.removeSub?.unsubscribe();
      this.logoutSub?.unsubscribe();
    });
  }

  /** Loads the list. `quiet` keeps the rows on screen (used after a 409 or 404). */
  protected load(quiet = false): void {
    this.loadSub?.unsubscribe();
    const seq = ++this.loadSeq;
    if (!quiet) {
      this.loading.set(true);
      this.loadError.set(null);
    }
    this.loadSub = this.review.listProfiles().subscribe({
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

  protected statusText(profile: AdminProfile): string {
    return profile.visibility === 'removed' ? this.text.statusRemoved : this.text.statusActive;
  }

  protected removeAria(profile: AdminProfile): string {
    return this.text.removeAria.replace('{name}', () => profile.name);
  }

  protected askRemove(profile: AdminProfile): void {
    this.actionError.set(null);
    this.notice.set(null);
    this.pendingRemove.set(profile);
  }

  protected cancelRemove(): void {
    if (this.removing()) return;
    this.pendingRemove.set(null);
  }

  protected confirmRemove(): void {
    const profile = this.pendingRemove();
    if (!profile || this.removing()) return;
    this.removing.set(true);
    this.removeSub?.unsubscribe();
    this.removeSub = this.review.removeProfile(profile.profileId).subscribe({
      next: () => {
        this.removing.set(false);
        this.pendingRemove.set(null);
        // A quiet reload still in flight must not overwrite the row just marked removed.
        this.loadSub?.unsubscribe();
        this.loadSeq++;
        this.notice.set(this.text.removed);
        this.items.update((list) =>
          list.map((p) => (p.profileId === profile.profileId ? { ...p, visibility: 'removed' as const } : p)),
        );
        this.focusSelector('.list-notice');
      },
      error: (err: AppError) => {
        this.removing.set(false);
        this.pendingRemove.set(null);
        if (this.onUnauthorized(err)) return;
        if (err.kind === 'conflict' || err.kind === 'notFound') {
          this.notice.set(err.kind === 'conflict' ? this.text.alreadyRemoved : this.text.notFound);
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
