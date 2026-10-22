import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { STRINGS } from '../../shared/strings.he';
import { GlassCard } from '../../shared/ui/glass-card/glass-card';

/** "About the course": real content in sections, one h1, and a link back to the gallery. */
@Component({
  selector: 'app-about-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [GlassCard, RouterLink],
  template: `
    <app-glass-card>
      <h1>{{ strings.about.heading }}</h1>
      <p class="lead">{{ strings.about.body }}</p>
      @for (section of strings.about.sections; track section.heading) {
        <section>
          <h2>{{ section.heading }}</h2>
          @for (paragraph of section.paragraphs; track paragraph) {
            <p>{{ paragraph }}</p>
          }
        </section>
      }
      <a class="back" routerLink="/">{{ strings.backToGallery }}</a>
    </app-glass-card>
  `,
  styleUrl: '../../shared/ui/info-page/info-page.css',
  styles: `
    section {
      display: grid;
      gap: 8px;
    }
    h2 {
      font-size: var(--type-title-size);
    }
  `,
})
export class AboutPage {
  protected readonly strings = STRINGS;
}
