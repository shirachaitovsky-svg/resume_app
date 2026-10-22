import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { AppError } from '../../api/app-error';
import { STRINGS } from '../../strings.he';
import { Button } from '../button/button';

@Component({
  selector: 'app-error-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Button],
  host: { role: 'alert' },
  template: `
    @if (asPage()) {
      <h1>{{ strings.states.errorTitle }}</h1>
    } @else {
      <h2>{{ strings.states.errorTitle }}</h2>
    }
    <p>{{ error()?.message ?? strings.errors.server }}</p>
    <app-button variant="secondary" (click)="retry.emit()">{{ strings.states.retry }}</app-button>
  `,
  styleUrl: './error-state.css',
})
export class ErrorState {
  protected readonly strings = STRINGS;
  readonly error = input<AppError | null>(null);
  /** True when this error replaces the whole page, so its title is the page's h1 (otherwise an h2 under the page's own h1). */
  readonly asPage = input(false);
  /** Emits when the user asks to try again. */
  readonly retry = output<void>();
}
