import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { STRINGS } from '../../strings.he';

export interface NavItem {
  readonly id: string;
  readonly label: string;
  readonly href: string;
  /** When set, the item navigates through the router instead of a plain href. */
  readonly link?: string;
}

@Component({
  selector: 'app-nav-pill',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  templateUrl: './nav-pill.html',
  styleUrl: './nav-pill.css',
})
export class NavPill {
  protected readonly strings = STRINGS;
  readonly items = input.required<readonly NavItem[]>();
  readonly activeId = input<string | null>(null);
}
