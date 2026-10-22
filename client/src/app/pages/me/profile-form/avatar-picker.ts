import { ChangeDetectionStrategy, Component, computed, model, signal } from '@angular/core';
import { STRINGS } from '../../../shared/strings.he';
import { AvatarImage } from '../../../shared/ui/avatar/avatar';

type Gender = 'girl' | 'boy';
let nextId = 0;

/** Two steps: girl or boy, then one of three figures of that kind. Selected id is `girl-1..3` or `boy-1..3`, or ''. */
@Component({
  selector: 'app-avatar-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AvatarImage],
  template: `
    <fieldset class="group">
      <legend>{{ strings.legend }}</legend>
      <div class="genders" role="radiogroup" [attr.aria-label]="strings.genderLegend">
        @for (g of genders; track g.id) {
          <label class="gender" [class.checked]="gender() === g.id">
            <input
              type="radio"
              [name]="uid + '-gender'"
              [value]="g.id"
              [checked]="gender() === g.id"
              [attr.aria-checked]="gender() === g.id"
              (change)="selectGender(g.id)"
            />
            <span>{{ g.label }}</span>
          </label>
        }
      </div>
      @if (gender(); as current) {
        <div class="figures" role="radiogroup" [attr.aria-label]="strings.figureLegend">
          @for (n of numbers; track n) {
            <label class="figure" [class.checked]="value() === current + '-' + n">
              <input
                type="radio"
                [name]="uid + '-figure'"
                [value]="current + '-' + n"
                [checked]="value() === current + '-' + n"
                [attr.aria-checked]="value() === current + '-' + n"
                [attr.aria-label]="figureLabel(current, n)"
                (change)="value.set(current + '-' + n)"
              />
              <app-avatar [avatarId]="current + '-' + n" [name]="figureLabel(current, n)" [size]="56" />
            </label>
          }
        </div>
      }
    </fieldset>
  `,
  styleUrl: './avatar-picker.css',
})
export class AvatarPicker {
  protected readonly strings = STRINGS.joinForm.avatarPicker;
  protected readonly uid = `app-avatar-picker-${nextId++}`;
  protected readonly numbers = [1, 2, 3] as const;
  protected readonly genders: readonly { id: Gender; label: string }[] = [
    { id: 'girl', label: STRINGS.joinForm.avatarPicker.girl },
    { id: 'boy', label: STRINGS.joinForm.avatarPicker.boy },
  ];

  /** Selected avatar id, or '' when none. */
  readonly value = model('');

  private readonly chosenGender = signal<Gender | ''>('');
  /** The value decides when set (also after an external reset); otherwise the last chosen gender. */
  protected readonly gender = computed<Gender | ''>(() => {
    const v = this.value();
    return v.startsWith('girl-') ? 'girl' : v.startsWith('boy-') ? 'boy' : v === '' ? this.chosenGender() : '';
  });

  protected figureLabel(gender: Gender, n: number): string {
    const label = gender === 'girl' ? this.strings.girl : this.strings.boy;
    return this.strings.figure.replace('{n}', String(n)).replace('{gender}', label);
  }

  protected selectGender(gender: Gender): void {
    this.chosenGender.set(gender);
    if (!this.value().startsWith(`${gender}-`)) this.value.set('');
  }
}
