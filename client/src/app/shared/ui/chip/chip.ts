import { ChangeDetectionStrategy, Component, input, model } from '@angular/core';

@Component({
  selector: 'app-chip',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.selected]': 'selected()' },
  template: `<button type="button" [attr.aria-pressed]="selected()" (click)="toggle()">
    <ng-content />
  </button>`,
  styleUrl: './chip.css',
})
export class Chip {
  readonly selected = model(false);
  /** Single-choice mode: a click selects and never deselects (the parent owns the selection). */
  readonly sticky = input(false);

  protected toggle(): void {
    this.selected.set(this.sticky() ? true : !this.selected());
  }
}
