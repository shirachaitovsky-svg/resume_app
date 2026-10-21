import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { STRINGS } from '../../strings.he';

export interface NavItem {
  readonly id: string;
  readonly label: string;
  readonly href: string;
}

@Component({
  selector: 'app-nav-pill',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './nav-pill.html',
  styleUrl: './nav-pill.css',
})
export class NavPill {
  protected readonly strings = STRINGS;
  readonly items = input.required<readonly NavItem[]>();
  readonly activeId = input<string | null>(null);
}
