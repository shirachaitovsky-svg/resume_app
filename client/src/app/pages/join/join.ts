import { ChangeDetectionStrategy, Component } from '@angular/core';
import { STRINGS } from '../../shared/strings.he';
import { InfoPage } from '../../shared/ui/info-page/info-page';

@Component({
  selector: 'app-join-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [InfoPage],
  template: '<app-info-page [heading]="strings.join.heading" [body]="strings.join.body" [showBackLink]="true" />',
})
export class JoinPage {
  protected readonly strings = STRINGS;
}
