import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { STRINGS } from '../../strings.he';

@Component({
  selector: 'app-empty-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2>{{ title() }}</h2>
    @if (text()) {
      <p>{{ text() }}</p>
    }
    <div class="action"><ng-content /></div>
  `,
  styleUrl: './empty-state.css',
})
export class EmptyState {
  readonly title = input<string>(STRINGS.states.emptyTitle);
  readonly text = input<string>('');
}
