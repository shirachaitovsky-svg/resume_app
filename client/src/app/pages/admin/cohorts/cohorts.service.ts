import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiClient } from '../../../shared/api/api-client';

/** A cohort as the admin API returns it. Carries no participant contact details. */
export interface AdminCohort {
  readonly id: string;
  readonly name: string;
  readonly inviteToken: string;
  readonly inviteUrl: string;
  readonly createdAt: string;
  readonly revoked: boolean;
  readonly revokedAt: string | null;
}

/** Data access for the cohorts screen; every call goes through the shared `ApiClient`. */
@Injectable({ providedIn: 'root' })
export class CohortsService {
  private readonly api = inject(ApiClient);

  list(): Observable<readonly AdminCohort[]> {
    return this.api.get<{ items: readonly AdminCohort[] }>('/admin/cohorts').pipe(map((r) => r?.items ?? []));
  }

  create(name: string): Observable<AdminCohort> {
    return this.api
      .post<Omit<AdminCohort, 'revoked' | 'revokedAt'>>('/admin/cohorts', { name })
      .pipe(map((c) => ({ ...c, revoked: false, revokedAt: null })));
  }

  revoke(id: string): Observable<void> {
    return this.api.post<void>(`/admin/cohorts/${encodeURIComponent(id)}/revoke`);
  }
}
