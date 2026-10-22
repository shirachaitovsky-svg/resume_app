import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  OnDestroy,
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
import { SafeMarkdown } from '../../shared/ui/safe-markdown/safe-markdown';
import { Textarea } from '../../shared/ui/textarea/textarea';
import { AvatarPicker } from './avatar-picker';
import {
  ALL_FORM_FIELDS,
  FormField,
  JOIN_FIELDS,
  JOIN_MAX_LENGTH,
  JoinField,
  OPTIONAL_FIELDS,
  OPTIONAL_MAX_LENGTH,
  OptionalField,
  validateJoinField,
  validateOptionalField,
} from './join-validation';
import { IMAGE_DECODER, PhotoError, resizeImage } from './photo-resize';
import { ProfilePreviewCard } from './profile-preview';

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
  imports: [
    AvatarPicker,
    Button,
    ErrorState,
    Field,
    GlassCard,
    InfoPage,
    LoadingSkeleton,
    ProfilePreviewCard,
    RouterLink,
    SafeMarkdown,
    Textarea,
  ],
  templateUrl: './join-form.html',
  styleUrl: './join-form.css',
})
export class JoinFormPage implements OnDestroy {
  private readonly api = inject(ApiClient);
  private readonly title = inject(Title);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly decoder = inject(IMAGE_DECODER);

  protected readonly strings = STRINGS;
  protected readonly joinFields = JOIN_FIELDS;
  protected readonly maxLength = JOIN_MAX_LENGTH;
  protected readonly optionalMax = OPTIONAL_MAX_LENGTH;
  protected readonly optionalStrings = STRINGS.joinForm.optional;
  protected readonly photoStrings = STRINGS.joinForm.photo;
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
  protected readonly optional: Record<OptionalField, WritableSignal<string>> = {
    description: signal(''),
    role: signal(''),
    workplace: signal(''),
    introText: signal(''),
    introAnswer1: signal(''),
    introAnswer2: signal(''),
    introAnswer3: signal(''),
    cv: signal(''),
  };
  protected readonly avatarId = signal('');
  /** The resized photo that will be uploaded, and the local object URL used only for the preview. */
  protected readonly photoBlob = signal<Blob | null>(null);
  protected readonly photoUrl = signal<string | null>(null);
  protected readonly photoBusy = signal(false);
  private photoSeq = 0;
  /** Single source for both the per-field errors and the summary. */
  protected readonly errors = signal<Partial<Record<FormField, string>>>({});
  protected readonly extraErrors = signal<readonly string[]>([]);
  protected readonly submitError = signal<AppError | null>(null);

  protected readonly summary = computed(() => {
    const errors = this.errors();
    return ALL_FORM_FIELDS.filter((f) => errors[f]).map((f) => ({ field: f, message: errors[f] as string }));
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

  protected optionalLabel(field: FormField): string {
    return STRINGS.joinForm.optional.fields[field as keyof typeof STRINGS.joinForm.optional.fields];
  }

  protected question(n: 1 | 2 | 3): string {
    return STRINGS.profile.questions[n];
  }

  protected setValue(field: JoinField, value: string): void {
    this.values[field].set(value);
    this.clearError(field);
  }

  protected setOptional(field: OptionalField, value: string): void {
    this.optional[field].set(value);
    this.clearError(field);
  }

  protected setAvatar(value: string): void {
    this.avatarId.set(value);
    this.clearError('avatarId');
  }

  private clearError(field: FormField): void {
    if (this.errors()[field]) {
      const { [field]: _removed, ...rest } = this.errors();
      this.errors.set(rest);
    }
  }

  private setError(field: FormField, message: string): void {
    this.errors.set({ ...this.errors(), [field]: message });
  }

  protected onPhotoChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) {
      this.removePhoto();
      return;
    }
    const seq = ++this.photoSeq;
    this.clearError('photo');
    this.clearPhoto();
    this.photoBusy.set(true);
    resizeImage(file, this.decoder).then(
      (blob) => {
        if (seq !== this.photoSeq) return;
        this.photoBusy.set(false);
        this.photoBlob.set(blob);
        this.photoUrl.set(URL.createObjectURL(blob));
      },
      (err: unknown) => {
        if (seq !== this.photoSeq) return;
        this.photoBusy.set(false);
        input.value = '';
        const tooLarge = err instanceof PhotoError && err.kind === 'tooLarge';
        this.setError('photo', tooLarge ? STRINGS.joinForm.photo.tooLarge : STRINGS.joinForm.photo.unsupported);
      },
    );
  }

  protected removePhoto(): void {
    this.photoSeq++;
    this.photoBusy.set(false);
    this.clearPhoto();
    this.clearError('photo');
    const input = this.host.nativeElement.querySelector<HTMLInputElement>('[data-field="photo"] input[type="file"]');
    if (input) input.value = '';
  }

  /** Drops the photo and releases its object URL. */
  private clearPhoto(): void {
    const url = this.photoUrl();
    if (url) URL.revokeObjectURL(url);
    this.photoUrl.set(null);
    this.photoBlob.set(null);
  }

  ngOnDestroy(): void {
    this.photoSeq++;
    this.clearPhoto();
  }

  protected retry(): void {
    this.load(this.token());
  }

  protected focusField(event: Event, field: FormField): void {
    event.preventDefault();
    this.host.nativeElement.querySelector<HTMLElement>(`[data-field="${field}"] :is(input, textarea)`)?.focus();
  }

  protected submit(event: Event): void {
    event.preventDefault();
    if (this.submitting() || this.state().status !== 'ready') return;
    if (this.photoBusy()) {
      this.extraErrors.set([STRINGS.joinForm.photo.processing]);
      this.focusSummary();
      return;
    }

    const trimmed = {} as Record<JoinField, string>;
    const errors: Partial<Record<FormField, string>> = {};
    for (const f of JOIN_FIELDS) {
      trimmed[f] = this.values[f]().trim();
      const kind = validateJoinField(f, trimmed[f]);
      if (kind) errors[f] = STRINGS.joinForm.errors[f][kind];
    }
    const fields: Record<string, string> = { ...trimmed };
    for (const f of OPTIONAL_FIELDS) {
      const value = this.optional[f]().trim();
      if (validateOptionalField(f, value)) {
        errors[f] = STRINGS.joinForm.optional.tooLong
          .replace('{field}', this.optionalLabel(f))
          .replace('{max}', String(OPTIONAL_MAX_LENGTH[f]));
      } else if (value !== '') {
        fields[f] = value;
      }
    }
    if (this.avatarId() !== '') fields['avatarId'] = this.avatarId();
    this.submitError.set(null);
    this.extraErrors.set([]);
    this.errors.set(errors);
    if (Object.keys(errors).length > 0) {
      this.focusSummary();
      return;
    }

    this.submitting.set(true);
    const path = `/invites/${encodeURIComponent(this.token() ?? '')}/profiles`;
    const photo = this.photoBlob();
    let request;
    if (photo) {
      const form = new FormData();
      for (const [key, value] of Object.entries(fields)) form.append(key, value);
      form.append('photo', new File([photo], 'photo.jpg', { type: 'image/jpeg' }));
      request = this.api.postMultipart<{ status: string }>(path, form, { turnstile: true });
    } else {
      request = this.api.post<{ status: string }>(path, fields, { turnstile: true });
    }
    this.submitSub = request
      .subscribe({
        next: () => {
          this.submitting.set(false);
          this.resetValues();
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
      const errors: Partial<Record<FormField, string>> = {};
      let unknown = false;
      for (const key of Object.keys(err.fieldErrors)) {
        const field = ALL_FORM_FIELDS.find((f) => f.toLowerCase() === key.toLowerCase());
        if (!field) unknown = true;
        else if ((JOIN_FIELDS as readonly string[]).includes(field)) {
          errors[field] = STRINGS.joinForm.errors[field as JoinField].invalid;
        } else {
          errors[field] = STRINGS.joinForm.optional.invalid.replace('{field}', this.optionalLabel(field));
        }
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

  private resetValues(): void {
    for (const f of JOIN_FIELDS) this.values[f].set('');
    for (const f of OPTIONAL_FIELDS) this.optional[f].set('');
    this.avatarId.set('');
    this.photoSeq++;
    this.photoBusy.set(false);
    this.clearPhoto();
  }

  private load(token: string | undefined): void {
    this.sub?.unsubscribe();
    this.submitSub?.unsubscribe();
    this.submitting.set(false);
    this.resetValues();
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
