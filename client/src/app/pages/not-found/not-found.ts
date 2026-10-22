import { ChangeDetectionStrategy, Component } from '@angular/core';
import { STRINGS } from '../../shared/strings.he';
import { InfoPage } from '../../shared/ui/info-page/info-page';

@Component({
  selector: 'app-not-found-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [InfoPage],
  template: '<app-info-page [heading]="strings.notFoundPage.heading" [body]="strings.notFoundPage.body" [showBackLink]="true" />',
})
export class NotFoundPage {
  protected readonly strings = STRINGS;
}
