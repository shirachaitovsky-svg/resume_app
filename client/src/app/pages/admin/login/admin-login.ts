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
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { AppError, withRetryHint } from '../../../shared/api/app-error';
import { STRINGS } from '../../../shared/strings.he';
import { Button } from '../../../shared/ui/button/button';
import { Field } from '../../../shared/ui/field/field';
import { GlassCard } from '../../../shared/ui/glass-card/glass-card';
import { AdminSession } from '../admin-session';

@Component({
  selector: 'app-admin-login-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Button, Field, GlassCard],
  templateUrl: './admin-login.html',
  styleUrls: ['../admin-shared.css', './admin-login.css'],
})
export class AdminLoginPage {
  private readonly session = inject(AdminSession);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  protected readonly strings = STRINGS;
  protected readonly text = STRINGS.admin.login;
  protected readonly username = signal('');
  protected readonly password = signal('');
  protected readonly code = signal('');
  protected readonly recoveryCode = signal('');
  protected readonly useRecovery = signal(false);
  protected readonly submitting = signal(false);
  protected readonly error = signal<AppError | null>(null);
  /** Shown once if the previous session ended with a 401. */
  protected readonly expired = signal(false);

  /** One message for every 401, so nothing reveals what was wrong. */
  protected readonly errorMessage = computed(() => {
    const e = this.error();
    if (!e) return null;
    if (e.kind === 'unauthorized') return STRINGS.admin.login.invalid;
    return e.kind === 'tooMany' ? withRetryHint(e).message : e.message;
  });

  private sub: Subscription | null = null;

  constructor() {
    this.expired.set(this.session.expired());
    this.session.expired.set(false);
    inject(DestroyRef).onDestroy(() => this.sub?.unsubscribe());
  }

  protected toggleRecovery(): void {
    this.useRecovery.update((v) => !v);
    this.code.set('');
    this.recoveryCode.set('');
  }

  protected submit(event: Event): void {
    event.preventDefault();
    if (this.submitting()) return;
    this.expired.set(false);

    const username = this.username().trim();
    if (username === '' || this.password() === '') {
      // Same message as a server rejection; no request for an obviously empty form.
      this.error.set(new AppError('unauthorized', 401, STRINGS.admin.login.invalid));
      this.focusAlert();
      return;
    }
    this.submitting.set(true);
    this.error.set(null);
    const request = {
      username,
      password: this.password(),
      ...(this.useRecovery()
        ? this.recoveryCode().trim() !== ''
          ? { recoveryCode: this.recoveryCode().trim() }
          : {}
        : this.code().trim() !== ''
          ? { code: this.code().trim() }
          : {}),
    };
    this.sub = this.session.login(request).subscribe({
      next: () => {
        this.submitting.set(false);
        this.password.set('');
        this.code.set('');
        this.recoveryCode.set('');
        void this.router.navigateByUrl(this.session.destination());
      },
      error: (err: AppError) => {
        this.submitting.set(false);
        this.password.set('');
        this.code.set('');
        this.recoveryCode.set('');
        this.error.set(err);
        this.focusAlert();
      },
    });
  }

  private focusAlert(): void {
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>('.alert')?.focus(), {
      injector: this.injector,
    });
  }

  /** The initial `/auth/me` failed (not a 401): offer a retry instead of a silent login screen. */
  protected readonly loadError = this.session.loadError;

  protected retryLoad(): void {
    void this.session.reload().then(() => {
      if (this.session.state().status === 'authenticated') void this.router.navigateByUrl(this.session.destination());
    });
  }
}
