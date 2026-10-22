import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiClient } from '../../shared/api/api-client';
import { AppError, withRetryHint } from '../../shared/api/app-error';
import { CohortList, CohortRef, ProfileList, PublicProfileCard } from '../../shared/api/profile.models';
import { STRINGS } from '../../shared/strings.he';
import { Chip } from '../../shared/ui/chip/chip';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { ErrorState } from '../../shared/ui/error-state/error-state';
import { LoadingSkeleton } from '../../shared/ui/loading-skeleton/loading-skeleton';
import { ProfileCard } from './profile-card';

type CohortsStatus = 'loading' | 'ready' | 'failed';
type ProfilesState =
  | { readonly status: 'loading' }
  | { readonly status: 'ready'; readonly items: readonly PublicProfileCard[] }
  | { readonly status: 'error'; readonly error: AppError };

@Component({
  selector: 'app-gallery-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Chip, EmptyState, ErrorState, LoadingSkeleton, ProfileCard],
  templateUrl: './gallery.html',
  styleUrl: './gallery.css',
})
export class GalleryPage {
  private readonly api = inject(ApiClient);
  private readonly router = inject(Router);

  protected readonly strings = STRINGS;
  /** `?cohort=` query parameter (bound through withComponentInputBinding). */
  readonly cohort = input<string | string[] | undefined>(undefined);

  protected readonly cohorts = signal<readonly CohortRef[]>([]);
  protected readonly cohortsStatus = signal<CohortsStatus>('loading');
  protected readonly state = signal<ProfilesState>({ status: 'loading' });

  /** The cohort actually applied: an id unknown to the server's list falls back to "all". */
  protected readonly selected = computed<string | null>(() => {
    const raw = this.cohort();
    const id = typeof raw === 'string' && raw.trim() !== '' ? raw : null;
    if (id === null) return null;
    if (this.cohortsStatus() === 'ready' && !this.cohorts().some((c) => c.id === id)) return null;
    return id;
  });
  protected readonly readyItems = computed<readonly PublicProfileCard[]>(() => {
    const s = this.state();
    return s.status === 'ready' ? s.items : [];
  });
  protected readonly displayError = computed(() => {
    const s = this.state();
    return s.status === 'error' ? withRetryHint(s.error) : null;
  });

  private cohortsSub: Subscription | null = null;
  private profilesSub: Subscription | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.cohortsSub?.unsubscribe();
      this.profilesSub?.unsubscribe();
    });
    this.loadCohorts();
    effect(() => {
      const status = this.cohortsStatus();
      const id = this.selected();
      if (status === 'loading') return;
      untracked(() => this.loadProfiles(id));
    });
  }

  protected select(id: string | null): void {
    void this.router.navigate([], { queryParams: { cohort: id }, queryParamsHandling: 'merge' });
  }

  protected retry(): void {
    if (this.cohortsStatus() === 'failed') {
      this.cohortsStatus.set('loading');
      this.loadCohorts();
    } else {
      this.loadProfiles(this.selected());
    }
  }

  private loadCohorts(): void {
    this.cohortsSub?.unsubscribe();
    this.cohortsSub = this.api.get<CohortList>('/cohorts').subscribe({
      next: (res) => {
        this.cohorts.set(res.items);
        this.cohortsStatus.set('ready');
      },
      error: () => this.cohortsStatus.set('failed'),
    });
  }

  private loadProfiles(cohort: string | null): void {
    this.profilesSub?.unsubscribe(); // a late answer for the previous selection is dropped
    this.state.set({ status: 'loading' });
    const path = cohort ? `/profiles?cohort=${encodeURIComponent(cohort)}` : '/profiles';
    this.profilesSub = this.api.get<ProfileList>(path).subscribe({
      next: (res) => this.state.set({ status: 'ready', items: res.items }),
      error: (err: AppError) =>
        // a malformed cohort id answers 400: show it as an empty list instead of an error
        this.state.set(
          cohort && (err.kind === 'validation' || err.kind === 'notFound')
            ? { status: 'ready', items: [] }
            : { status: 'error', error: err },
        ),
    });
  }
}
