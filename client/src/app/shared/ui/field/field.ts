import { ChangeDetectionStrategy, Component, computed, input, model } from '@angular/core';
import { STRINGS } from '../../strings.he';

let nextId = 0;

@Component({
  selector: 'app-field',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './field.html',
  styleUrl: './field.css',
})
export class Field {
  protected readonly strings = STRINGS;
  private readonly uid = `app-field-${nextId++}`;

  readonly label = input.required<string>();
  readonly type = input('text');
  readonly required = input(false);
  readonly error = input<string | null>(null);
  readonly autocomplete = input<string | null>(null);
  readonly inputmode = input<string | null>(null);
  readonly maxlength = input<number | null>(null);
  readonly hint = input<string | null>(null);
  readonly value = model('');

  protected readonly inputId = this.uid;
  protected readonly errorId = `${this.uid}-error`;
  protected readonly hintId = `${this.uid}-hint`;
  protected readonly describedBy = computed(() => {
    const ids = [this.hint() ? this.hintId : null, this.error() ? this.errorId : null].filter(Boolean);
    return ids.length > 0 ? ids.join(' ') : null;
  });
  protected readonly hasError = computed(() => !!this.error());

  protected onInput(event: Event): void {
    this.value.set((event.target as HTMLInputElement).value);
  }
}
