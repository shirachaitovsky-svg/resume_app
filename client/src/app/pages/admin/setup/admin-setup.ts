import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  OnDestroy,
  afterNextRender,
  computed,
  inject,
  signal,
} from '@angular/core';
import { Title } from '@angular/platform-browser';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { AppError, FieldErrors, withRetryHint } from '../../../shared/api/app-error';
import { STRINGS } from '../../../shared/strings.he';
import { Button } from '../../../shared/ui/button/button';
import { ErrorState } from '../../../shared/ui/error-state/error-state';
import { Field } from '../../../shared/ui/field/field';
import { GlassCard } from '../../../shared/ui/glass-card/glass-card';
import { LoadingSkeleton } from '../../../shared/ui/loading-skeleton/loading-skeleton';
import { ADMIN_HOME, ADMIN_LOGIN, AdminSession, TotpSetup } from '../admin-session';
import { QrCode } from '../qr-code';

export type SetupStep = 'password' | 'totp' | 'recovery';
type PasswordField = 'currentPassword' | 'newPassword' | 'confirmPassword';

const CODE_RE = /^\d{6}$/;

/** Splits the TOTP key into groups of 4 for manual entry. */
export function groupKey(key: string): string {
  return key.replace(/\s+/g, '').match(/.{1,4}/g)?.join(' ') ?? '';
}

/** First server message for a field (case-insensitive key), or null when the server did not flag it. */
function serverMessage(errors: FieldErrors, field: string): string | null {
  const entry = Object.entries(errors).find(([key]) => key.toLowerCase() === field.toLowerCase());
  return entry ? (entry[1][0] ?? '') : null;
}

/** Password change, TOTP enrollment and one-time recovery codes: a small state machine. */
@Component({
  selector: 'app-admin-setup-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Button, ErrorState, Field, GlassCard, LoadingSkeleton, QrCode],
  templateUrl: './admin-setup.html',
  styleUrls: ['../admin-shared.css', './admin-setup.css'],
})
export class AdminSetupPage implements OnDestroy {
  private readonly session = inject(AdminSession);
  private readonly router = inject(Router);
  private readonly title = inject(Title);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  protected readonly strings = STRINGS;
  protected readonly text = STRINGS.admin.setup;

  protected readonly step = signal<SetupStep>('password');
  protected readonly busy = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly passwordValues: Record<PasswordField, ReturnType<typeof signal<string>>> = {
    currentPassword: signal(''),
    newPassword: signal(''),
    confirmPassword: signal(''),
  };
  protected readonly passwordErrors = signal<Partial<Record<PasswordField, string>>>({});

  protected readonly totp = signal<TotpSetup | null>(null);
  protected readonly totpLoadError = signal<AppError | null>(null);
  protected readonly totpLoading = signal(false);
  protected readonly code = signal('');
  protected readonly codeError = signal<string | null>(null);
  protected readonly groupedKey = computed(() => groupKey(this.totp()?.key ?? ''));

  /** Held by the session, so leaving and returning to this screen does not lose the one-time codes. */
  protected readonly recoveryCodes = this.session.recoveryCodes;
  protected readonly confirmed = signal(false);
  protected readonly copyMessage = signal<string | null>(null);

  protected readonly loadErrorDisplay = computed(() => {
    const e = this.totpLoadError();
    return e ? withRetryHint(e) : null;
  });

  /** The password changed but re-reading the session failed; retryable. */
  protected readonly refreshError = signal<AppError | null>(null);

  private sub: Subscription | null = null;
  private logoutSub: Subscription | null = null;
  private destroyed = false;
  private readonly unloadHandler = (event: BeforeUnloadEvent): void => {
    if (this.session.recoveryPending() && !this.confirmed()) {
      event.preventDefault();
      event.returnValue = '';
    }
  };

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.sub?.unsubscribe();
      this.logoutSub?.unsubscribe();
    });
    const state = this.session.state();
    const mustChange = state.status === 'authenticated' && state.mustChangePassword;
    this.goTo(this.session.recoveryPending() ? 'recovery' : mustChange ? 'password' : 'totp', false);
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    window.removeEventListener('beforeunload', this.unloadHandler);
    // Pending recovery codes stay in the session until confirmed; everything else is wiped.
    this.wipeSecrets();
  }

  private goTo(step: SetupStep, focus = true): void {
    this.step.set(step);
    this.formError.set(null);
    this.title.setTitle(`${STRINGS.admin.setup[step].heading} | ${STRINGS.appTitle}`);
    if (step === 'totp') this.loadTotp();
    if (step === 'recovery') window.addEventListener('beforeunload', this.unloadHandler);
    if (focus) this.focusHeading();
  }

  private focusHeading(): void {
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>('h1')?.focus(), {
      injector: this.injector,
    });
  }

  private focusError(): void {
    afterNextRender(
      () => {
        const root = this.host.nativeElement;
        (
          root.querySelector<HTMLElement>('input[aria-invalid="true"]') ?? root.querySelector<HTMLElement>('.alert')
        )?.focus();
      },
      { injector: this.injector },
    );
  }

  /** A 401 in the middle of the session: back to the login screen with a notice. */
  private onUnauthorized(err: AppError): boolean {
    if (!this.session.handleUnauthorized(err)) return false;
    this.wipeSecrets();
    void this.router.navigateByUrl(ADMIN_LOGIN);
    return true;
  }

  protected retryRefresh(): void {
    void this.afterPasswordChange();
  }

  private async afterPasswordChange(): Promise<void> {
    const failure = await this.session.refresh();
    if (this.destroyed) return;
    this.busy.set(false);
    this.refreshError.set(failure);
    if (failure) return;
    const state = this.session.state();
    if (state.status !== 'authenticated') {
      // 401 while re-reading: the session ended.
      this.session.expired.set(true);
      void this.router.navigateByUrl(ADMIN_LOGIN);
    } else if (state.totpSetupRequired) this.goTo('totp');
    else void this.router.navigateByUrl(this.session.destination());
  }

  // --- password step ---

  protected setPassword(field: PasswordField, value: string): void {
    this.passwordValues[field].set(value);
    const { [field]: _removed, ...rest } = this.passwordErrors();
    this.passwordErrors.set(rest);
  }

  protected submitPassword(event: Event): void {
    event.preventDefault();
    if (this.busy()) return;
    const p = this.text.password;
    const current = this.passwordValues.currentPassword();
    const next = this.passwordValues.newPassword();
    const errors: Partial<Record<PasswordField, string>> = {};
    if (current === '') errors.currentPassword = p.currentRequired;
    if (next === '') errors.newPassword = p.nextRequired;
    else if (this.passwordValues.confirmPassword() !== next) errors.confirmPassword = p.mismatch;
    this.passwordErrors.set(errors);
    this.formError.set(null);
    if (Object.keys(errors).length > 0) {
      this.focusError();
      return;
    }

    this.busy.set(true);
    this.sub = this.session.changePassword(current, next).subscribe({
      next: () => {
        this.clearPasswords();
        void this.afterPasswordChange();
      },
      error: (err: AppError) => {
        this.busy.set(false);
        if (this.onUnauthorized(err)) return;
        const mapped: Partial<Record<PasswordField, string>> = {};
        if (err.kind === 'validation') {
          const currentMessage = serverMessage(err.fieldErrors, 'currentPassword');
          const nextMessage = serverMessage(err.fieldErrors, 'newPassword');
          if (currentMessage !== null) mapped.currentPassword = currentMessage || p.currentInvalid;
          if (nextMessage !== null) mapped.newPassword = nextMessage || p.nextInvalid;
        }
        if (Object.keys(mapped).length > 0) this.passwordErrors.set(mapped);
        else this.formError.set(err.kind === 'tooMany' ? withRetryHint(err).message : err.message);
        this.focusError();
      },
    });
  }

  private clearPasswords(): void {
    for (const value of Object.values(this.passwordValues)) value.set('');
  }

  // --- TOTP step ---

  protected loadTotp(): void {
    this.sub?.unsubscribe();
    this.totpLoading.set(true);
    this.totpLoadError.set(null);
    this.totp.set(null);
    this.sub = this.session.startTotp().subscribe({
      next: (setup) => {
        this.totpLoading.set(false);
        this.totp.set(setup);
      },
      error: (err: AppError) => {
        this.totpLoading.set(false);
        if (err.kind === 'conflict') {
          // Already enabled: nothing left to enroll.
          if (this.session.recoveryPending()) {
            this.goTo('recovery');
            return;
          }
          void this.session.refresh().then((failure) => {
            if (this.destroyed) return;
            if (failure) this.totpLoadError.set(failure);
            else void this.router.navigateByUrl(this.session.destination());
          });
          return;
        }
        if (this.onUnauthorized(err)) return;
        this.totpLoadError.set(err);
      },
    });
  }

  protected setCode(value: string): void {
    this.code.set(value);
    this.codeError.set(null);
  }

  protected submitCode(event: Event): void {
    event.preventDefault();
    if (this.busy() || this.totp() === null) return;
    const code = this.code().replace(/[\s-]/g, '');
    this.formError.set(null);
    if (!CODE_RE.test(code)) {
      this.codeError.set(this.text.totp.codeRequired);
      this.focusError();
      return;
    }
    this.busy.set(true);
    this.sub = this.session.enableTotp(code).subscribe({
      next: (codes) => {
        this.busy.set(false);
        this.code.set('');
        this.totp.set(null);
        this.session.recoveryCodes.set(codes);
        this.confirmed.set(false);
        this.goTo('recovery');
      },
      error: (err: AppError) => {
        this.busy.set(false);
        this.code.set('');
        this.session.clearRecovery();
        if (this.onUnauthorized(err)) return;
        if (err.kind === 'validation') {
          this.codeError.set(serverMessage(err.fieldErrors, 'code') || this.text.totp.codeInvalid);
        } else {
          this.formError.set(err.kind === 'tooMany' ? withRetryHint(err).message : err.message);
        }
        this.focusError();
      },
    });
  }

  // --- recovery codes step ---

  protected async copyCodes(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.recoveryCodes().join('\n'));
      this.copyMessage.set(this.text.recovery.copied);
    } catch {
      this.selectCodes();
      this.copyMessage.set(this.text.recovery.copyFailed);
    }
  }

  private selectCodes(): void {
    const list = this.host.nativeElement.querySelector<HTMLElement>('.codes');
    const selection = window.getSelection();
    if (!list || !selection) return;
    const range = document.createRange();
    range.selectNodeContents(list);
    selection.removeAllRanges();
    selection.addRange(range);
  }

  protected downloadCodes(): void {
    const blob = new Blob([this.recoveryCodes().join('\n') + '\n'], { type: 'text/plain' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = this.text.recovery.fileName;
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 0);
  }

  protected setConfirmed(event: Event): void {
    this.confirmed.set((event.target as HTMLInputElement).checked);
  }

  protected finish(): void {
    if (!this.confirmed()) return;
    window.removeEventListener('beforeunload', this.unloadHandler);
    this.wipeSecrets();
    this.session.markSetupComplete();
    void this.router.navigateByUrl(ADMIN_HOME);
  }

  // --- leaving ---

  protected logout(): void {
    if (this.busy() || (this.step() === 'recovery' && !this.confirmed())) return;
    this.busy.set(true);
    // A late response from an in-flight request must not repopulate the secrets we are about to wipe.
    this.sub?.unsubscribe();
    window.removeEventListener('beforeunload', this.unloadHandler);
    this.wipeSecrets();
    this.session.clearRecovery();
    this.logoutSub = this.session.logout().subscribe(() => {
      if (!this.destroyed) void this.router.navigateByUrl(ADMIN_LOGIN);
    });
  }

  /** Nothing sensitive outlives the screen. */
  private wipeSecrets(): void {
    this.confirmed.set(false);
    this.totp.set(null);
    this.code.set('');
    this.copyMessage.set(null);
    this.clearPasswords();
  }
}
