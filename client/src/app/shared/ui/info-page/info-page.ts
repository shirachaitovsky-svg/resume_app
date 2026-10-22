import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { STRINGS } from '../../strings.he';
import { GlassCard } from '../glass-card/glass-card';

/** Simple static page: heading, one sentence and a link back to the gallery. */
@Component({
  selector: 'app-info-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [GlassCard, RouterLink],
  template: `
    <app-glass-card>
      <h1>{{ heading() }}</h1>
      <p>{{ body() }}</p>
      @if (showBackLink()) {
        <a class="back" routerLink="/">{{ strings.backToGallery }}</a>
      }
    </app-glass-card>
  `,
  styleUrl: './info-page.css',
})
export class InfoPage {
  protected readonly strings = STRINGS;
  readonly heading = input.required<string>();
  readonly body = input.required<string>();
  readonly showBackLink = input(true);
}
