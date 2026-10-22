import { ChangeDetectionStrategy, Component, ElementRef, inject, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { STRINGS } from './shared/strings.he';
import { NavItem, NavPill } from './shared/ui/nav-pill/nav-pill';

const NAV_ITEMS: readonly NavItem[] = [
  { id: 'gallery', label: STRINGS.nav.gallery, href: '/', link: '/' },
  { id: 'about', label: STRINGS.nav.about, href: '/about', link: '/about' },
  { id: 'join', label: STRINGS.nav.createProfile, href: '/join', link: '/join' },
  { id: 'edit', label: STRINGS.nav.editProfile, href: '/edit', link: '/edit' },
];

const ACTIVE_BY_PATH: Readonly<Record<string, string>> = {
  '/': 'gallery',
  '/about': 'about',
  '/join': 'join',
  '/edit': 'edit',
};

export function activeNavId(url: string): string | null {
  const path = url.split(/[?#]/)[0].replace(/(.)\/+$/, '$1') || '/';
  return ACTIVE_BY_PATH[path] ?? null;
}

@Component({
  imports: [RouterOutlet, NavPill],
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {
  private readonly router = inject(Router);
  private readonly main = viewChild.required<ElementRef<HTMLElement>>('main');

  protected readonly strings = STRINGS;
  protected readonly navItems = NAV_ITEMS;
  protected readonly activeId = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => activeNavId(event.urlAfterRedirects)),
    ),
    { initialValue: activeNavId(this.router.url) },
  );

  /** A plain `#id` link would resolve against <base href>, so focus the content directly. */
  protected skipToContent(event: Event): void {
    event.preventDefault();
    this.main().nativeElement.focus();
  }
}
