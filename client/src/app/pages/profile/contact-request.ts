import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { Subscription } from 'rxjs';
import { ApiClient } from '../../shared/api/api-client';
import { AppError, withRetryHint } from '../../shared/api/app-error';
import { STRINGS } from '../../shared/strings.he';
import { Button } from '../../shared/ui/button/button';
import { Field } from '../../shared/ui/field/field';
import { Textarea } from '../../shared/ui/textarea/textarea';
import { validateJoinField } from '../me/profile-form/join-validation';

type ContactField = 'name' | 'email' | 'reason';
const FIELDS: readonly ContactField[] = ['name', 'email', 'reason'];
const MAX: Readonly<Record<ContactField, number>> = { name: 100, email: 254, reason: 1000 };

/** The visitor's contact-request form on the public profile. Never shows any participant contact details. */
@Component({
  selector: 'app-contact-request',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Button, Field, Textarea],
  template: `
    @switch (mode()) {
      @case ('closed') {
        <app-button class="open" variant="accent" (click)="openForm()">{{ text.open }}</app-button>
      }
      @case ('sent') {
        <div class="sent" role="status">
          <h2 tabindex="-1">{{ text.sentTitle }}</h2>
          <p>{{ text.sentBody }}</p>
        </div>
      }
      @default {
        <form class="contact-form" novalidate aria-labelledby="contact-form-heading" (submit)="submit($event)">
          <h2 id="contact-form-heading" tabindex="-1">{{ text.heading }}</h2>
          <p class="intro">{{ text.intro }}</p>
          @if (summary().length > 0 || submitErrorMessage()) {
            <div class="summary" role="alert" tabindex="-1">
              @if (summary().length > 0) {
                <h3>{{ text.summaryHeading }}</h3>
                <ul>
                  @for (item of summary(); track item.field) {
                    <li>
                      <a href="#" (click)="focusField($event, item.field)">{{ item.message }}</a>
                    </li>
                  }
                </ul>
              }
              @if (submitErrorMessage(); as message) {
                <p>{{ message }}</p>
              }
            </div>
          }
          <app-field
            data-field="name"
            [label]="text.fields.name"
            [required]="true"
            [error]="errors()['name'] ?? null"
            autocomplete="name"
            [maxlength]="max.name"
            [value]="values.name()"
            (valueChange)="setValue('name', $event)"
          />
          <app-field
            data-field="email"
            type="email"
            inputmode="email"
            [label]="text.fields.email"
            [required]="true"
            [error]="errors()['email'] ?? null"
            autocomplete="email"
            [maxlength]="max.email"
            [value]="values.email()"
            (valueChange)="setValue('email', $event)"
          />
          <app-textarea
            data-field="reason"
            [label]="text.fields.reason"
            [required]="true"
            [error]="errors()['reason'] ?? null"
            [maxlength]="max.reason"
            [counter]="true"
            [value]="values.reason()"
            (valueChange)="setValue('reason', $event)"
          />
          <div class="actions">
            <app-button type="submit" variant="accent" [disabled]="busy()">{{ busy() ? text.submitting : text.submit }}</app-button>
            <app-button variant="secondary" [disabled]="busy()" (click)="close()">{{ text.cancel }}</app-button>
          </div>
        </form>
      }
    }
  `,
  styles: `
    :host {
      display: block;
      inline-size: 100%;
      max-inline-size: 640px;
    }
    .contact-form,
    .sent {
      display: grid;
      gap: 16px;
      padding: 24px;
      border-radius: var(--radius-md);
      background: var(--color-surface);
      border: 1px solid var(--color-outline);
    }
    h2,
    h3,
    p {
      margin: 0;
    }
    h2:focus-visible {
      outline: 3px solid var(--color-focus-ring);
      outline-offset: 4px;
    }
    .intro {
      color: var(--color-on-surface-muted);
    }
    .summary {
      padding: 16px;
      border: 2px solid var(--color-error);
      border-radius: var(--radius-md);
      color: var(--color-error);
    }
    .summary:focus-visible {
      outline: 3px solid var(--color-focus-ring);
      outline-offset: 2px;
    }
    .summary ul,
    .summary p {
      margin: 8px 0 0;
    }
    .summary a {
      color: inherit;
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
    }
  `,
})
export class ContactRequest {
  private readonly api = inject(ApiClient);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  readonly profileId = input.required<string>();

  protected readonly text = STRINGS.profile.contact;
  protected readonly max = MAX;
  protected readonly mode = signal<'closed' | 'form' | 'sent'>('closed');
  protected readonly busy = signal(false);
  protected readonly values = { name: signal(''), email: signal(''), reason: signal('') };
  protected readonly errors = signal<Partial<Record<ContactField, string>>>({});
  private readonly submitError = signal<AppError | null>(null);
  protected readonly summary = computed(() =>
    FIELDS.filter((f) => this.errors()[f]).map((f) => ({ field: f, message: this.errors()[f] as string })),
  );
  protected readonly submitErrorMessage = computed(() => {
    const e = this.submitError();
    if (!e) return null;
    return e.kind === 'tooMany' ? withRetryHint(e).message : this.text.submitFailed;
  });

  private sub: Subscription | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.sub?.unsubscribe());
  }

  protected openForm(): void {
    this.mode.set('form');
    this.focusAfterRender('#contact-form-heading');
  }

  protected close(): void {
    if (this.busy()) return;
    this.mode.set('closed');
  }

  protected setValue(field: ContactField, value: string): void {
    this.values[field].set(value);
    if (this.errors()[field]) {
      const { [field]: _removed, ...rest } = this.errors();
      this.errors.set(rest);
    }
  }

  protected focusField(event: Event, field: ContactField): void {
    event.preventDefault();
    this.host.nativeElement.querySelector<HTMLElement>(`[data-field="${field}"] :is(input, textarea)`)?.focus();
  }

  protected submit(event: Event): void {
    event.preventDefault();
    if (this.busy()) return;
    const trimmed = {
      name: this.values.name().trim(),
      email: this.values.email().trim(),
      reason: this.values.reason().trim(),
    };
    const errors: Partial<Record<ContactField, string>> = {};
    const name = validateJoinField('name', trimmed.name);
    if (name) errors.name = this.text.errors.name[name];
    const email = validateJoinField('email', trimmed.email);
    if (email) errors.email = this.text.errors.email[email];
    if (trimmed.reason === '') errors.reason = this.text.errors.reason.required;
    else if (trimmed.reason.length > MAX.reason) errors.reason = this.text.errors.reason.tooLong;
    this.submitError.set(null);
    this.errors.set(errors);
    if (Object.keys(errors).length > 0) {
      this.focusAfterRender('.summary');
      return;
    }

    this.busy.set(true);
    this.sub = this.api
      .post<unknown>(`/profiles/${encodeURIComponent(this.profileId())}/contact-requests`, trimmed, { turnstile: true })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.values.name.set('');
          this.values.email.set('');
          this.values.reason.set('');
          this.mode.set('sent');
          this.focusAfterRender('.sent h2');
        },
        error: (err: AppError) => {
          this.busy.set(false);
          if (err.kind === 'validation' && Object.keys(err.fieldErrors).length > 0) {
            const mapped: Partial<Record<ContactField, string>> = {};
            for (const key of Object.keys(err.fieldErrors)) {
              const field = FIELDS.find((f) => f === key.toLowerCase());
              if (field) mapped[field] = this.text.errors[field].invalid;
            }
            if (Object.keys(mapped).length > 0) {
              this.errors.set(mapped);
              this.focusAfterRender('.summary');
              return;
            }
          }
          this.submitError.set(err);
          this.focusAfterRender('.summary');
        },
      });
  }

  private focusAfterRender(selector: string): void {
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>(selector)?.focus(), {
      injector: this.injector,
    });
  }
}
