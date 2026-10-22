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
    <h2>{{ strings.states.errorTitle }}</h2>
    <p>{{ error()?.message ?? strings.errors.server }}</p>
    <app-button variant="secondary" (click)="retry.emit()">{{ strings.states.retry }}</app-button>
  `,
  styleUrl: './error-state.css',
})
export class ErrorState {
  protected readonly strings = STRINGS;
  readonly error = input<AppError | null>(null);
  /** Emits when the user asks to try again. */
  readonly retry = output<void>();
}
