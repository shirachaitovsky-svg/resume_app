import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { Subscription } from 'rxjs';
import { ApiClient } from '../../shared/api/api-client';
import { AppError, withRetryHint } from '../../shared/api/app-error';
import { IntroAnswer, PublicProfileDetail } from '../../shared/api/profile.models';
import { STRINGS } from '../../shared/strings.he';
import { GUID_RE, safeHttpUrl } from '../../shared/url';
import { AvatarImage } from '../../shared/ui/avatar/avatar';
import { ContactRequest } from './contact-request';
import { ErrorState } from '../../shared/ui/error-state/error-state';
import { GlassCard } from '../../shared/ui/glass-card/glass-card';
import { InfoPage } from '../../shared/ui/info-page/info-page';
import { LoadingSkeleton } from '../../shared/ui/loading-skeleton/loading-skeleton';
import { SafeMarkdown } from '../../shared/ui/safe-markdown/safe-markdown';

type State =
  | { readonly status: 'loading' }
  | { readonly status: 'ready'; readonly profile: PublicProfileDetail }
  | { readonly status: 'unavailable' }
  | { readonly status: 'error'; readonly error: AppError };

@Component({
  selector: 'app-profile-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AvatarImage, ContactRequest, ErrorState, GlassCard, InfoPage, LoadingSkeleton, SafeMarkdown],
  templateUrl: './profile.html',
  styleUrl: './profile.css',
})
export class ProfilePage {
  private readonly api = inject(ApiClient);
  private readonly title = inject(Title);

  protected readonly strings = STRINGS;
  /** Route parameter `:id` (bound through withComponentInputBinding). */
  readonly id = input<string | undefined>(undefined);

  protected readonly state = signal<State>({ status: 'loading' });
  private sub: Subscription | null = null;

  protected readonly profile = computed(() => {
    const s = this.state();
    return s.status === 'ready' ? s.profile : null;
  });
  protected readonly productHref = computed(() => safeHttpUrl(this.profile()?.productUrl));
  protected readonly openProductLabel = computed(() =>
    STRINGS.profile.openProduct.replace('{name}', this.profile()?.productName ?? ''),
  );
  protected readonly subtitleLine = computed(() => {
    const p = this.profile();
    return [p?.role, p?.workplace].filter((v): v is string => !!v && v.trim() !== '').join(' · ');
  });
  protected readonly answers = computed<readonly (IntroAnswer & { question: string })[]>(() =>
    (this.profile()?.introAnswers ?? [])
      .filter((a) => a.text?.trim() && STRINGS.profile.questions[a.number])
      .map((a) => ({ ...a, question: STRINGS.profile.questions[a.number] })),
  );
  protected readonly displayError = computed(() => {
    const s = this.state();
    return s.status === 'error' ? withRetryHint(s.error) : null;
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => this.sub?.unsubscribe());
    effect(() => {
      const id = this.id();
      untracked(() => this.load(id));
    });
  }

  protected retry(): void {
    this.load(this.id());
  }

  private load(id: string | undefined): void {
    this.sub?.unsubscribe();
    if (!id || !GUID_RE.test(id)) {
      this.setUnavailable();
      return;
    }
    this.state.set({ status: 'loading' });
    this.sub = this.api.get<PublicProfileDetail>(`/profiles/${id}`).subscribe({
      next: (profile) => {
        this.state.set({ status: 'ready', profile });
        this.title.setTitle(`${profile.name} | ${STRINGS.appTitle}`);
      },
      error: (err: AppError) => {
        if (err.kind === 'notFound') this.setUnavailable();
        else this.state.set({ status: 'error', error: err });
      },
    });
  }

  private setUnavailable(): void {
    this.state.set({ status: 'unavailable' });
    this.title.setTitle(`${STRINGS.profile.unavailableTitle} | ${STRINGS.appTitle}`);
  }
}
