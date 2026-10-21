import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { STRINGS } from '../shared/strings.he';
import { Button } from '../shared/ui/button/button';
import { Chip } from '../shared/ui/chip/chip';
import { Field } from '../shared/ui/field/field';
import { GlassCard } from '../shared/ui/glass-card/glass-card';
import { NavItem, NavPill } from '../shared/ui/nav-pill/nav-pill';

@Component({
  selector: 'app-design-system',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Button, Chip, Field, GlassCard, NavPill],
  templateUrl: './design-system.html',
  styleUrl: './design-system.css',
})
export class DesignSystem {
  protected readonly strings = STRINGS;
  protected readonly name = signal('');
  protected readonly navItems: readonly NavItem[] = [
    { id: 'gallery', label: STRINGS.nav.gallery, href: '#gallery' },
    { id: 'about', label: STRINGS.nav.about, href: '#about' },
    { id: 'create', label: STRINGS.nav.createProfile, href: '#create' },
    { id: 'edit', label: STRINGS.nav.editProfile, href: '#edit' },
  ];
}
