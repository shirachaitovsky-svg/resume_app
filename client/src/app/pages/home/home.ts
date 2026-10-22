import { ChangeDetectionStrategy, Component } from '@angular/core';
import { STRINGS } from '../../shared/strings.he';
import { InfoPage } from '../../shared/ui/info-page/info-page';

@Component({
  selector: 'app-home-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [InfoPage],
  template: '<app-info-page [heading]="strings.home.heading" [body]="strings.home.body" [showBackLink]="false" />',
})
export class HomePage {
  protected readonly strings = STRINGS;
}
