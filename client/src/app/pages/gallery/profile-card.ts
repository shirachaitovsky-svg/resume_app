import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PublicProfileCard } from '../../shared/api/profile.models';
import { STRINGS } from '../../shared/strings.he';
import { safeHttpUrl } from '../../shared/url';
import { AvatarImage } from '../../shared/ui/avatar/avatar';
import { GlassCard } from '../../shared/ui/glass-card/glass-card';

/**
 * Gallery card. The product link covers the whole card through a stretched pseudo-element;
 * "to profile" is a separate link above it, so there is no interactive element inside another.
 */
@Component({
  selector: 'app-profile-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AvatarImage, GlassCard, RouterLink],
  template: `
    <app-glass-card [compact]="true" class="card">
      <app-avatar [avatarId]="profile().avatarId" [photoUrl]="profile().photoUrl" [name]="profile().name" [size]="96" />
      <h2>
        @if (productHref(); as href) {
          <a class="product-link" [href]="href" target="_blank" rel="noopener noreferrer" [attr.aria-label]="productLabel()">{{ profile().name }}</a>
        } @else {
          {{ profile().name }}
        }
      </h2>
      <p class="product">{{ profile().productName }}</p>
      @if (profile().description) {
        <p class="description">{{ profile().description }}</p>
      }
      <span class="cohort">{{ profile().cohort.name }}</span>
      <a class="link-button link-button--secondary to-profile" [routerLink]="['/profile', profile().id]" [attr.aria-label]="profileLabel()">{{ strings.gallery.toProfile }}</a>
    </app-glass-card>
  `,
  styleUrl: './profile-card.css',
})
export class ProfileCard {
  protected readonly strings = STRINGS;
  readonly profile = input.required<PublicProfileCard>();

  protected readonly productHref = computed(() => safeHttpUrl(this.profile().productUrl));
  protected readonly productLabel = computed(() =>
    STRINGS.gallery.openProductOf.replace('{name}', this.profile().name).replace('{product}', this.profile().productName),
  );
  protected readonly profileLabel = computed(() => STRINGS.gallery.toProfileOf.replace('{name}', this.profile().name));
}
