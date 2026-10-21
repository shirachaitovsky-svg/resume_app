import { ChangeDetectionStrategy, Component, model } from '@angular/core';

@Component({
  selector: 'app-chip',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.selected]': 'selected()' },
  template: `<button type="button" [attr.aria-pressed]="selected()" (click)="selected.set(!selected())">
    <ng-content />
  </button>`,
  styleUrl: './chip.css',
})
export class Chip {
  readonly selected = model(false);
}
