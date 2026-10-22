import { ChangeDetectionStrategy, Component } from '@angular/core';
import { STRINGS } from '../../shared/strings.he';
import { InfoPage } from '../../shared/ui/info-page/info-page';

@Component({
  selector: 'app-about-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [InfoPage],
  template: '<app-info-page [heading]="strings.about.heading" [body]="strings.about.body" [showBackLink]="true" />',
})
export class AboutPage {
  protected readonly strings = STRINGS;
}
