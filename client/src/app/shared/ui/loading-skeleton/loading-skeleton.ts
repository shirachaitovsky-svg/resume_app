import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { STRINGS } from '../../strings.he';

@Component({
  selector: 'app-loading-skeleton',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { role: 'status', 'aria-busy': 'true', '[attr.aria-label]': 'strings.states.loading' },
  template: `
    @for (card of cardList(); track card) {
      <div class="card" aria-hidden="true">
        @for (line of lineList(); track line) {
          <span class="line"></span>
        }
      </div>
    }
  `,
  styleUrl: './loading-skeleton.css',
})
export class LoadingSkeleton {
  protected readonly strings = STRINGS;
  /** Number of placeholder lines inside each card. */
  readonly rows = input(3);
  /** Number of placeholder cards. */
  readonly cards = input(1);

  protected readonly lineList = computed(() => range(this.rows()));
  protected readonly cardList = computed(() => range(this.cards()));
}

function range(count: number): number[] {
  return Array.from({ length: Math.max(0, Math.floor(count)) }, (_, i) => i);
}
