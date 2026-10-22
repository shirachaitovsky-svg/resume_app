import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { STRINGS } from '../../shared/strings.he';
import { AvatarImage } from '../../shared/ui/avatar/avatar';
import { GlassCard } from '../../shared/ui/glass-card/glass-card';

/**
 * Live, non-interactive look-alike of the gallery card. The content is aria-hidden so it is not announced on every
 * keystroke; there are no links or buttons in it (the product link is never opened from here).
 */
@Component({
  selector: 'app-profile-preview',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AvatarImage, GlassCard],
  template: `
    <section [attr.aria-label]="strings.heading">
      <h2 class="heading">{{ strings.heading }}</h2>
      <app-glass-card [compact]="true" class="card" aria-hidden="true">
        <app-avatar [avatarId]="avatarId()" [photoUrl]="photoUrl()" [name]="displayName()" [size]="96" />
        <p class="name" [class.placeholder]="!name().trim()">{{ displayName() }}</p>
        <p class="product" [class.placeholder]="!productName().trim()">{{ displayProduct() }}</p>
        @if (description().trim()) {
          <p class="description">{{ description().trim() }}</p>
        }
        @if (cohort()) {
          <span class="cohort">{{ cohort() }}</span>
        }
      </app-glass-card>
    </section>
  `,
  styleUrl: './profile-preview.css',
})
export class ProfilePreviewCard {
  protected readonly strings = STRINGS.joinForm.preview;
  readonly name = input('');
  readonly productName = input('');
  readonly description = input('');
  readonly cohort = input('');
  readonly avatarId = input('');
  readonly photoUrl = input<string | null>(null);

  protected readonly displayName = computed(() => this.name().trim() || this.strings.namePlaceholder);
  protected readonly displayProduct = computed(() => this.productName().trim() || this.strings.productPlaceholder);
}
