import { ChangeDetectionStrategy, Component, computed, input, linkedSignal } from '@angular/core';
import { STRINGS } from '../../strings.he';
import { photoSrc } from '../../url';

export const AVATAR_IDS = ['girl-1', 'girl-2', 'girl-3', 'boy-1', 'boy-2', 'boy-3'] as const;
export type AvatarFigure = (typeof AVATAR_IDS)[number] | 'default';

export function avatarFigure(id: string | null | undefined): AvatarFigure {
  return (AVATAR_IDS as readonly string[]).includes(id ?? '') ? (id as AvatarFigure) : 'default';
}

/**
 * Profile picture: the uploaded photo (through /api) or one of six flat illustrations.
 * Skin and hair tones are the only colours outside the design tokens; background and clothes use the blue tokens.
 */
@Component({
  selector: 'app-avatar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[style.--avatar-size.px]': 'size()' },
  template: `
    @if (src(); as photo) {
      <img class="photo" [src]="photo" [alt]="label()" (error)="failed.set(true)" />
    } @else {
      <svg viewBox="0 0 100 100" role="img" [attr.aria-label]="label()" [attr.data-figure]="figure()">
        <circle class="bg" cx="50" cy="50" r="50" />
        @switch (figure()) {
          @case ('girl-1') {
            <path d="M30 46C28 20 72 20 70 46L74 82L26 82Z" fill="#6B4226" />
            <path class="cloth" d="M16 100C16 78 32 70 50 70C68 70 84 78 84 100Z" />
            <rect x="44" y="58" width="12" height="14" fill="#F5D0B5" />
            <circle cx="50" cy="44" r="18" fill="#F5D0B5" />
            <path d="M31 42C33 24 67 24 69 42C58 35 44 33 31 42Z" fill="#6B4226" />
            <circle cx="43" cy="46" r="1.8" fill="#2B2B2B" />
            <circle cx="57" cy="46" r="1.8" fill="#2B2B2B" />
            <path d="M44 53Q50 58 56 53" stroke="#B5654A" stroke-width="1.6" fill="none" stroke-linecap="round" />
          }
          @case ('girl-2') {
            <circle cx="32" cy="38" r="10" fill="#2B1B12" />
            <circle cx="42" cy="27" r="10" fill="#2B1B12" />
            <circle cx="58" cy="27" r="10" fill="#2B1B12" />
            <circle cx="68" cy="38" r="10" fill="#2B1B12" />
            <circle cx="30" cy="52" r="8" fill="#2B1B12" />
            <circle cx="70" cy="52" r="8" fill="#2B1B12" />
            <path class="cloth" d="M16 100C16 78 32 70 50 70C68 70 84 78 84 100Z" />
            <rect x="44" y="58" width="12" height="14" fill="#8D5524" />
            <circle cx="50" cy="45" r="17" fill="#8D5524" />
            <path d="M33 40C36 26 64 26 67 40C58 34 42 34 33 40Z" fill="#2B1B12" />
            <circle cx="43" cy="47" r="1.8" fill="#1A1A1A" />
            <circle cx="57" cy="47" r="1.8" fill="#1A1A1A" />
            <path d="M44 54Q50 59 56 54" stroke="#4A2A12" stroke-width="1.6" fill="none" stroke-linecap="round" />
          }
          @case ('girl-3') {
            <path d="M29 56C27 20 73 20 71 56Z" fill="#A0522D" />
            <path class="cloth" d="M16 100C16 78 32 70 50 70C68 70 84 78 84 100Z" />
            <rect x="44" y="58" width="12" height="14" fill="#D9A066" />
            <circle cx="50" cy="45" r="17" fill="#D9A066" />
            <path d="M32 44C32 24 68 24 68 44C62 36 56 32 50 32C44 32 38 36 32 44Z" fill="#A0522D" />
            <circle cx="43" cy="47" r="1.8" fill="#2B2B2B" />
            <circle cx="57" cy="47" r="1.8" fill="#2B2B2B" />
            <path d="M44 54Q50 59 56 54" stroke="#9A4F36" stroke-width="1.6" fill="none" stroke-linecap="round" />
          }
          @case ('boy-1') {
            <path class="cloth" d="M16 100C16 78 32 70 50 70C68 70 84 78 84 100Z" />
            <rect x="44" y="58" width="12" height="14" fill="#F1C9A5" />
            <circle cx="50" cy="45" r="18" fill="#F1C9A5" />
            <path d="M32 42C32 22 68 22 68 42C60 34 40 34 32 42Z" fill="#7A5A32" />
            <circle cx="43" cy="47" r="1.8" fill="#2B2B2B" />
            <circle cx="57" cy="47" r="1.8" fill="#2B2B2B" />
            <path d="M44 54Q50 58 56 54" stroke="#B5654A" stroke-width="1.6" fill="none" stroke-linecap="round" />
          }
          @case ('boy-2') {
            <path class="cloth" d="M16 100C16 78 32 70 50 70C68 70 84 78 84 100Z" />
            <rect x="44" y="58" width="12" height="14" fill="#6B4226" />
            <circle cx="50" cy="45" r="18" fill="#6B4226" />
            <path d="M32 40C32 22 68 22 68 40C62 32 38 32 32 40Z" fill="#1A1A1A" />
            <circle cx="43" cy="47" r="1.8" fill="#111111" />
            <circle cx="57" cy="47" r="1.8" fill="#111111" />
            <path d="M44 54Q50 58 56 54" stroke="#3A2210" stroke-width="1.6" fill="none" stroke-linecap="round" />
          }
          @case ('boy-3') {
            <path class="cloth" d="M16 100C16 78 32 70 50 70C68 70 84 78 84 100Z" />
            <rect x="44" y="58" width="12" height="14" fill="#C68E5B" />
            <circle cx="50" cy="45" r="18" fill="#C68E5B" />
            <circle cx="36" cy="33" r="7" fill="#4A3020" />
            <circle cx="46" cy="27" r="7" fill="#4A3020" />
            <circle cx="56" cy="27" r="7" fill="#4A3020" />
            <circle cx="65" cy="33" r="7" fill="#4A3020" />
            <circle cx="43" cy="47" r="1.8" fill="#2B2B2B" />
            <circle cx="57" cy="47" r="1.8" fill="#2B2B2B" />
            <path d="M44 54Q50 58 56 54" stroke="#8A4F2E" stroke-width="1.6" fill="none" stroke-linecap="round" />
          }
          @default {
            <path class="cloth" d="M16 100C16 78 32 70 50 70C68 70 84 78 84 100Z" />
            <circle class="neutral" cx="50" cy="44" r="18" />
          }
        }
      </svg>
    }
  `,
  styleUrl: './avatar.css',
})
export class AvatarImage {
  readonly avatarId = input<string | null>(null);
  readonly photoUrl = input<string | null>(null);
  readonly name = input<string>('');
  /** Diameter in px. */
  readonly size = input(96);

  protected readonly figure = computed(() => avatarFigure(this.avatarId()));
  protected readonly label = computed(() => this.name() || STRINGS.avatar.fallbackLabel);
  /** A failed photo load falls back to the illustration; a new photo URL resets that. */
  protected readonly failed = linkedSignal<string | null, boolean>({ source: this.photoUrl, computation: () => false });
  protected readonly src = computed(() => (this.failed() ? null : photoSrc(this.photoUrl())));
}
