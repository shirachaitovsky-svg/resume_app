import { ChangeDetectionStrategy, Component } from '@angular/core';
import { STRINGS } from '../../shared/strings.he';
import { InfoPage } from '../../shared/ui/info-page/info-page';

@Component({
  selector: 'app-edit-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [InfoPage],
  template: '<app-info-page [heading]="strings.edit.heading" [body]="strings.edit.body" [showBackLink]="true" />',
})
export class EditPage {
  protected readonly strings = STRINGS;
}
