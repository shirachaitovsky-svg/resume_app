import { ChangeDetectionStrategy, Component, computed, input, model } from '@angular/core';
import { STRINGS } from '../../strings.he';

let nextId = 0;

/** Multi-line twin of `app-field`: same label, hint, error and counter behaviour and look. */
@Component({
  selector: 'app-textarea',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <label class="label" [attr.for]="inputId">
      {{ label() }}
      @if (required()) {
        <span class="required">{{ strings.required }}</span>
      }
    </label>
    @if (hint()) {
      <p class="hint" [id]="hintId">{{ hint() }}</p>
    }
    <textarea
      class="input"
      [id]="inputId"
      [rows]="rows()"
      [value]="value()"
      [required]="required()"
      [attr.aria-invalid]="hasError() ? 'true' : null"
      [attr.aria-describedby]="describedBy()"
      [attr.maxlength]="maxlength()"
      (input)="onInput($event)"
    ></textarea>
    @if (counterText(); as text) {
      <p class="counter" aria-hidden="true">{{ text }}</p>
    }
    @if (hasError()) {
      <p class="error" role="alert" [id]="errorId">{{ error() }}</p>
    }
  `,
  styleUrl: './textarea.css',
})
export class Textarea {
  protected readonly strings = STRINGS;
  private readonly uid = `app-textarea-${nextId++}`;

  readonly label = input.required<string>();
  readonly required = input(false);
  readonly error = input<string | null>(null);
  readonly maxlength = input<number | null>(null);
  readonly hint = input<string | null>(null);
  readonly rows = input(4);
  readonly counter = input(false);
  readonly value = model('');

  protected readonly inputId = this.uid;
  protected readonly errorId = `${this.uid}-error`;
  protected readonly hintId = `${this.uid}-hint`;
  protected readonly hasError = computed(() => !!this.error());
  protected readonly describedBy = computed(() => {
    const ids = [this.hint() ? this.hintId : null, this.error() ? this.errorId : null].filter(Boolean);
    return ids.length > 0 ? ids.join(' ') : null;
  });
  protected readonly counterText = computed(() => {
    const max = this.maxlength();
    if (!this.counter() || max === null) return null;
    return STRINGS.joinForm.optional.counter.replace('{count}', String(this.value().length)).replace('{max}', String(max));
  });

  protected onInput(event: Event): void {
    this.value.set((event.target as HTMLTextAreaElement).value);
  }
}
