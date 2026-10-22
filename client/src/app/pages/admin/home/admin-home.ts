import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { STRINGS } from '../../../shared/strings.he';
import { Button } from '../../../shared/ui/button/button';
import { GlassCard } from '../../../shared/ui/glass-card/glass-card';
import { AdminSession, ADMIN_LOGIN } from '../admin-session';

/** Admin landing page: navigation to the admin screens. The review queue link (5.6) is added next to cohorts. */
@Component({
  selector: 'app-admin-home-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Button, GlassCard, RouterLink],
  template: `
    <app-glass-card>
      <h1>{{ strings.admin.heading }}</h1>
      <p class="intro">{{ greeting() }}</p>
      <nav class="nav-cards" [attr.aria-label]="strings.admin.navLabel">
        <a class="nav-card" routerLink="/admin/cohorts">
          <span class="nav-title">{{ strings.admin.cohorts.navLink }}</span>
          <span class="nav-hint">{{ strings.admin.cohorts.navHint }}</span>
        </a>
        <!-- Story 5.6 adds the review queue link here. -->
      </nav>
      <div class="logout">
        <app-button variant="secondary" [disabled]="busy()" (click)="logout()">{{ strings.admin.logout }}</app-button>
      </div>
    </app-glass-card>
  `,
  styleUrl: '../admin-shared.css',
})
export class AdminHomePage {
  protected readonly session = inject(AdminSession);
  private readonly router = inject(Router);
  protected readonly strings = STRINGS;
  protected readonly busy = signal(false);
  protected readonly greeting = computed(() =>
    // A replacer function: `$&` and friends in a username must stay literal.
    STRINGS.admin.greeting.replace('{username}', () => this.session.username()),
  );

  protected logout(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.session.logout().subscribe(() => void this.router.navigateByUrl(ADMIN_LOGIN));
  }
}
