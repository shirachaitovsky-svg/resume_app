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
  output,
  signal,
  untracked,
} from '@angular/core';
import { Observable, Subscription } from 'rxjs';
import { AppError, withRetryHint } from '../../../shared/api/app-error';
import { CohortRef, OwnProfile } from '../../../shared/api/profile.models';
import { STRINGS } from '../../../shared/strings.he';
import { Button } from '../../../shared/ui/button/button';
import { Field } from '../../../shared/ui/field/field';
import { SafeMarkdown } from '../../../shared/ui/safe-markdown/safe-markdown';
import { Textarea } from '../../../shared/ui/textarea/textarea';
import { ProfilePayload } from '../profile-api';
import { AvatarPicker } from './avatar-picker';
import {
  ALL_FORM_FIELDS,
  FormField,
  JOIN_MAX_LENGTH,
  JoinField,
  OPTIONAL_FIELDS,
  OPTIONAL_MAX_LENGTH,
  OptionalField,
  TYPED_FIELDS,
  TypedField,
  validateJoinField,
  validateOptionalField,
} from './join-validation';
import { IMAGE_DECODER, PhotoError, resizeImage } from './photo-resize';
import { ProfilePreviewCard } from './profile-preview';

export type ProfileFormMode = 'create' | 'edit';

/**
 * One form for creating and editing a profile. It only presents and validates: the parent loads the data and
 * supplies `submitFn`, and reacts to the outputs (success, an existing profile, a lost session).
 */
@Component({
  selector: 'app-profile-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AvatarPicker, Button, Field, ProfilePreviewCard, SafeMarkdown, Textarea],
  templateUrl: './profile-form.html',
  styleUrl: './profile-form.css',
})
export class ProfileForm implements OnDestroy {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly decoder = inject(IMAGE_DECODER);

  readonly mode = input.required<ProfileFormMode>();
  /** The account's email, shown read-only and never typed in. */
  readonly email = input('');
  /** Create mode: the cohorts to pick from. */
  readonly cohorts = input<readonly CohortRef[]>([]);
  /** Create mode: the cohort to select up front, ignored unless it is in `cohorts`. */
  readonly initialCohortId = input<string | null>(null);
  /** Edit mode: the stored profile the form is filled with. */
  readonly own = input<OwnProfile | null>(null);
  readonly submitFn = input.required<(payload: ProfilePayload) => Observable<unknown>>();

  readonly saved = output<void>();
  readonly profileExists = output<void>();
  readonly unauthorized = output<void>();

  protected readonly strings = STRINGS;
  protected readonly text = STRINGS.myProfile.form;
  protected readonly typedFields = TYPED_FIELDS;
  protected readonly maxLength = JOIN_MAX_LENGTH;
  protected readonly optionalMax = OPTIONAL_MAX_LENGTH;
  protected readonly optionalStrings = STRINGS.joinForm.optional;
  protected readonly photoStrings = STRINGS.joinForm.photo;

  protected readonly submitting = signal(false);
  protected readonly cohortId = signal('');
  protected readonly values: Record<TypedField, WritableSignal<string>> = {
    name: signal(''),
    productName: signal(''),
    productUrl: signal(''),
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
  /** Edit mode: the stored photo is to be removed on the next submit. */
  protected readonly removeStored = signal(false);
  private photoSeq = 0;
  /** Single source for both the per-field errors and the summary. */
  protected readonly errors = signal<Partial<Record<FormField, string>>>({});
  protected readonly extraErrors = signal<readonly string[]>([]);
  protected readonly submitError = signal<AppError | null>(null);
  protected readonly emailTaken = signal(false);

  protected readonly isCreate = computed(() => this.mode() === 'create');
  protected readonly hasStoredPhoto = computed(() => !this.isCreate() && (this.own()?.hasPhoto ?? false));
  protected readonly phonePending = computed(() => !!this.own()?.pendingPhone);
  protected readonly returnedInfo = computed(() => this.own()?.returned ?? null);
  protected readonly pendingChanges = computed(() => !this.returnedInfo() && !!this.own()?.pending);
  protected readonly cohortName = computed(() => {
    if (!this.isCreate()) return this.own()?.cohortName ?? '';
    return this.cohorts().find((c) => c.id === this.cohortId())?.name ?? '';
  });
  protected readonly submitLabel = computed(() => {
    if (this.submitting()) return STRINGS.joinForm.submitting;
    if (this.isCreate()) return this.text.submitCreate;
    return this.returnedInfo() ? this.text.submitResubmit : this.text.submitEdit;
  });
  protected readonly summary = computed(() => {
    const errors = this.errors();
    return ALL_FORM_FIELDS.filter((f) => errors[f]).map((f) => ({ field: f, message: errors[f] as string }));
  });
  protected readonly hasSummary = computed(
    () =>
      this.summary().length > 0 || this.extraErrors().length > 0 || this.submitError() !== null || this.emailTaken(),
  );
  protected readonly submitErrorMessage = computed(() => {
    const e = this.submitError();
    if (!e) return null;
    return e.kind === 'tooMany' ? withRetryHint(e).message : STRINGS.joinForm.submitFailed;
  });

  private submitSub: Subscription | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.submitSub?.unsubscribe());
    // Fill from the stored profile (edit) or the pre-selected cohort (create) whenever those inputs change.
    effect(() => {
      const own = this.own();
      const cohorts = this.cohorts();
      const initial = this.initialCohortId();
      const mode = this.mode();
      untracked(() => this.populate(mode, own, cohorts, initial));
    });
  }

  protected label(field: JoinField): string {
    return STRINGS.joinForm.fields[field];
  }

  protected hint(field: JoinField): string | null {
    return field === 'productUrl' ? STRINGS.joinForm.hints.productUrl : null;
  }

  protected inputType(field: JoinField): string {
    return field === 'productUrl' ? 'url' : field === 'phone' ? 'tel' : 'text';
  }

  protected autocomplete(field: JoinField): string {
    return { name: 'name', productName: 'off', productUrl: 'url', email: 'email', phone: 'tel' }[field];
  }

  protected inputmode(field: JoinField): string | null {
    return field === 'productUrl' ? 'url' : field === 'phone' ? 'tel' : null;
  }

  protected optionalLabel(field: FormField): string {
    return STRINGS.joinForm.optional.fields[field as keyof typeof STRINGS.joinForm.optional.fields];
  }

  protected question(n: 1 | 2 | 3): string {
    return STRINGS.profile.questions[n];
  }

  protected setValue(field: TypedField, value: string): void {
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

  protected setCohort(event: Event): void {
    this.cohortId.set((event.target as HTMLSelectElement).value);
    this.clearError('cohortId');
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
        this.removeStored.set(false);
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

  /** Drops the newly chosen photo. */
  protected removePhoto(): void {
    this.photoSeq++;
    this.photoBusy.set(false);
    this.clearPhoto();
    this.clearError('photo');
    const input = this.host.nativeElement.querySelector<HTMLInputElement>('[data-field="photo"] input[type="file"]');
    if (input) input.value = '';
  }

  protected removeStoredPhoto(): void {
    this.removeStored.set(true);
  }

  protected keepStoredPhoto(): void {
    this.removeStored.set(false);
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

  protected focusField(event: Event, field: FormField): void {
    event.preventDefault();
    const selector = field === 'cohortId' ? 'select' : ':is(input, textarea)';
    this.host.nativeElement.querySelector<HTMLElement>(`[data-field="${field}"] ${selector}`)?.focus();
  }

  protected submit(event: Event): void {
    event.preventDefault();
    if (this.submitting()) return;
    if (this.photoBusy()) {
      this.extraErrors.set([STRINGS.joinForm.photo.processing]);
      this.focusSummary();
      return;
    }

    const errors: Partial<Record<FormField, string>> = {};
    const fields: Record<string, string> = {};
    if (this.isCreate()) {
      const cohortId = this.cohortId();
      if (cohortId === '') errors.cohortId = this.text.cohortRequired;
      else fields['cohortId'] = cohortId;
    }
    for (const f of TYPED_FIELDS) {
      const value = this.values[f]().trim();
      const kind = validateJoinField(f, value);
      if (kind) errors[f] = STRINGS.joinForm.errors[f][kind];
      fields[f] = value;
    }
    // The server still reads the email on edit; sending the shown one keeps it unchanged. A create ignores it.
    if (!this.isCreate()) fields['email'] = this.own()?.email ?? this.email();
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
    this.emailTaken.set(false);
    this.extraErrors.set([]);
    this.errors.set(errors);
    if (Object.keys(errors).length > 0) {
      this.focusSummary();
      return;
    }

    this.submitting.set(true);
    this.submitSub = this.submitFn()({
      fields,
      photo: this.photoBlob(),
      removePhoto: this.removeStored(),
    }).subscribe({
      next: () => {
        this.submitting.set(false);
        if (this.isCreate()) this.resetValues();
        else {
          this.photoSeq++;
          this.clearPhoto();
          this.removeStored.set(false);
        }
        this.saved.emit();
      },
      error: (err: AppError) => {
        this.submitting.set(false);
        this.onSubmitError(err);
      },
    });
  }

  private onSubmitError(err: AppError): void {
    if (err.kind === 'unauthorized') {
      this.unauthorized.emit();
      return;
    }
    if (err.kind === 'conflict' && err.code === 'profile-exists') {
      this.profileExists.emit();
      return;
    }
    if (err.kind === 'conflict' && err.code === 'email-taken') {
      this.emailTaken.set(true);
    } else if (err.kind === 'validation' && Object.keys(err.fieldErrors).length > 0) {
      const errors: Partial<Record<FormField, string>> = {};
      let unknown = false;
      for (const key of Object.keys(err.fieldErrors)) {
        const field = ALL_FORM_FIELDS.find((f) => f.toLowerCase() === key.toLowerCase());
        if (!field || field === 'email') unknown = true;
        else if (field === 'cohortId') errors[field] = this.text.cohortInvalid;
        else if ((TYPED_FIELDS as readonly string[]).includes(field)) {
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

  private focusSummary(): void {
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>('.summary')?.focus(), {
      injector: this.injector,
    });
  }

  private resetValues(): void {
    for (const f of Object.values(this.values)) f.set('');
    for (const f of OPTIONAL_FIELDS) this.optional[f].set('');
    this.avatarId.set('');
    this.cohortId.set('');
    this.removeStored.set(false);
    this.photoSeq++;
    this.photoBusy.set(false);
    this.clearPhoto();
  }

  private populate(
    mode: ProfileFormMode,
    own: OwnProfile | null,
    cohorts: readonly CohortRef[],
    initial: string | null,
  ): void {
    this.submitSub?.unsubscribe();
    this.submitting.set(false);
    this.resetValues();
    this.errors.set({});
    this.extraErrors.set([]);
    this.submitError.set(null);
    this.emailTaken.set(false);
    if (mode === 'create') {
      if (initial && cohorts.some((c) => c.id === initial)) this.cohortId.set(initial);
      return;
    }
    if (!own) return;
    this.values.name.set(own.name);
    this.values.productName.set(own.productName);
    this.values.productUrl.set(own.productUrl);
    this.values.phone.set(own.phone);
    for (const f of OPTIONAL_FIELDS) this.optional[f].set(own[f] ?? '');
    this.avatarId.set(own.avatarId === 'default' ? '' : own.avatarId);
  }
}
