import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'app-glass-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.compact]': 'compact()' },
  template: '<ng-content />',
  styleUrl: './glass-card.css',
})
export class GlassCard {
  readonly compact = input(false);
}
