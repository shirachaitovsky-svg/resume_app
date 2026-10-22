import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  WritableSignal,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiClient } from '../../shared/api/api-client';
import { AppError, withRetryHint } from '../../shared/api/app-error';
import { STRINGS } from '../../shared/strings.he';
import { Button } from '../../shared/ui/button/button';
import { ErrorState } from '../../shared/ui/error-state/error-state';
import { Field } from '../../shared/ui/field/field';
import { GlassCard } from '../../shared/ui/glass-card/glass-card';
import { InfoPage } from '../../shared/ui/info-page/info-page';
import { LoadingSkeleton } from '../../shared/ui/loading-skeleton/loading-skeleton';
import { JOIN_FIELDS, JOIN_MAX_LENGTH, JoinField, validateJoinField } from './join-validation';

type State =
  | { readonly status: 'loading' }
  | { readonly status: 'ready' }
  | { readonly status: 'invalid' }
  | { readonly status: 'done' }
  | { readonly status: 'error'; readonly error: AppError };

const TOKEN_RE = /^[A-Za-z0-9_-]{1,200}$/;

@Component({
  selector: 'app-join-form-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Button, ErrorState, Field, GlassCard, InfoPage, LoadingSkeleton, RouterLink],
  templateUrl: './join-form.html',
  styleUrl: './join-form.css',
})
export class JoinFormPage {
  private readonly api = inject(ApiClient);
  private readonly title = inject(Title);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  protected readonly strings = STRINGS;
  protected readonly joinFields = JOIN_FIELDS;
  protected readonly maxLength = JOIN_MAX_LENGTH;
  /** Route parameter `:token` (bound through withComponentInputBinding). */
  readonly token = input<string | undefined>(undefined);

  protected readonly state = signal<State>({ status: 'loading' });
  protected readonly cohortName = signal('');
  protected readonly submitting = signal(false);
  protected readonly values: Record<JoinField, WritableSignal<string>> = {
    name: signal(''),
    productName: signal(''),
    productUrl: signal(''),
    email: signal(''),
    phone: signal(''),
  };
  /** Single source for both the per-field errors and the summary. */
  protected readonly errors = signal<Partial<Record<JoinField, string>>>({});
  protected readonly extraErrors = signal<readonly string[]>([]);
  protected readonly submitError = signal<AppError | null>(null);

  protected readonly summary = computed(() => {
    const errors = this.errors();
    return JOIN_FIELDS.filter((f) => errors[f]).map((f) => ({ field: f, message: errors[f] as string }));
  });
  protected readonly hasSummary = computed(
    () => this.summary().length > 0 || this.extraErrors().length > 0 || this.submitError() !== null,
  );
  protected readonly displayError = computed(() => {
    const s = this.state();
    return s.status === 'error' ? withRetryHint(s.error) : null;
  });
  protected readonly submitErrorMessage = computed(() => {
    const e = this.submitError();
    if (!e) return null;
    return e.kind === 'tooMany' ? withRetryHint(e).message : STRINGS.joinForm.submitFailed;
  });

  private sub: Subscription | null = null;
  private submitSub: Subscription | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.sub?.unsubscribe();
      this.submitSub?.unsubscribe();
    });
    effect(() => {
      const token = this.token();
      untracked(() => this.load(token));
    });
  }

  protected label(field: JoinField): string {
    return STRINGS.joinForm.fields[field];
  }

  protected hint(field: JoinField): string | null {
    return field === 'productUrl' ? STRINGS.joinForm.hints.productUrl : null;
  }

  protected inputType(field: JoinField): string {
    return field === 'productUrl' ? 'url' : field === 'email' ? 'email' : field === 'phone' ? 'tel' : 'text';
  }

  protected autocomplete(field: JoinField): string {
    return { name: 'name', productName: 'off', productUrl: 'url', email: 'email', phone: 'tel' }[field];
  }

  protected inputmode(field: JoinField): string | null {
    return field === 'productUrl' ? 'url' : field === 'email' ? 'email' : field === 'phone' ? 'tel' : null;
  }

  protected setValue(field: JoinField, value: string): void {
    this.values[field].set(value);
    if (this.errors()[field]) {
      const { [field]: _removed, ...rest } = this.errors();
      this.errors.set(rest);
    }
  }

  protected retry(): void {
    this.load(this.token());
  }

  protected focusField(event: Event, field: JoinField): void {
    event.preventDefault();
    this.host.nativeElement.querySelector<HTMLInputElement>(`[data-field="${field}"] input`)?.focus();
  }

  protected submit(event: Event): void {
    event.preventDefault();
    if (this.submitting() || this.state().status !== 'ready') return;

    const trimmed = {} as Record<JoinField, string>;
    const errors: Partial<Record<JoinField, string>> = {};
    for (const f of JOIN_FIELDS) {
      trimmed[f] = this.values[f]().trim();
      const kind = validateJoinField(f, trimmed[f]);
      if (kind) errors[f] = STRINGS.joinForm.errors[f][kind];
    }
    this.submitError.set(null);
    this.extraErrors.set([]);
    this.errors.set(errors);
    if (Object.keys(errors).length > 0) {
      this.focusSummary();
      return;
    }

    this.submitting.set(true);
    this.submitSub = this.api
      .post<{ status: string }>(`/invites/${encodeURIComponent(this.token() ?? '')}/profiles`, trimmed, {
        turnstile: true,
      })
      .subscribe({
        next: () => {
          this.submitting.set(false);
          for (const f of JOIN_FIELDS) this.values[f].set('');
          this.state.set({ status: 'done' });
          this.title.setTitle(`${STRINGS.joinForm.confirmTitle} | ${STRINGS.appTitle}`);
          this.focusHeading();
        },
        error: (err: AppError) => {
          this.submitting.set(false);
          this.onSubmitError(err);
        },
      });
  }

  private onSubmitError(err: AppError): void {
    if (err.kind === 'notFound') {
      this.setInvalid();
      return;
    }
    if (err.kind === 'validation' && Object.keys(err.fieldErrors).length > 0) {
      const errors: Partial<Record<JoinField, string>> = {};
      let unknown = false;
      for (const key of Object.keys(err.fieldErrors)) {
        const field = JOIN_FIELDS.find((f) => f.toLowerCase() === key.toLowerCase());
        if (field) errors[field] = STRINGS.joinForm.errors[field].invalid;
        else unknown = true;
      }
      this.errors.set(errors);
      this.extraErrors.set(unknown ? [STRINGS.joinForm.unknownFieldError] : []);
    } else {
      this.submitError.set(err);
    }
    this.focusSummary();
  }

  private focusHeading(): void {
    afterNextRender(
      () => {
        const h1 = this.host.nativeElement.querySelector<HTMLElement>('h1');
        h1?.setAttribute('tabindex', '-1');
        h1?.focus();
      },
      { injector: this.injector },
    );
  }

  private focusSummary(): void {
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>('.summary')?.focus(), {
      injector: this.injector,
    });
  }

  private load(token: string | undefined): void {
    this.sub?.unsubscribe();
    this.submitSub?.unsubscribe();
    this.submitting.set(false);
    for (const f of JOIN_FIELDS) this.values[f].set('');
    this.errors.set({});
    this.extraErrors.set([]);
    this.submitError.set(null);
    if (!token || !TOKEN_RE.test(token)) {
      this.setInvalid();
      return;
    }
    this.state.set({ status: 'loading' });
    this.title.setTitle(`${STRINGS.joinForm.title} | ${STRINGS.appTitle}`);
    this.sub = this.api.get<{ name: string }>(`/invites/${encodeURIComponent(token)}`).subscribe({
      next: (invite) => {
        this.cohortName.set(invite.name);
        this.state.set({ status: 'ready' });
      },
      error: (err: AppError) => {
        if (err.kind === 'notFound') this.setInvalid();
        else this.state.set({ status: 'error', error: err });
      },
    });
  }

  private setInvalid(): void {
    this.state.set({ status: 'invalid' });
    this.title.setTitle(`${STRINGS.joinForm.invalidTitle} | ${STRINGS.appTitle}`);
    this.focusHeading();
  }
}
