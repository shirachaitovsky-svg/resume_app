import { Injectable, inject, signal } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiClient } from '../../../shared/api/api-client';

export type SubmissionKind = 'new' | 'update';

export interface SubmissionItem {
  readonly revisionId: string;
  readonly name: string;
  readonly productName: string;
  readonly cohortName: string;
  readonly kind: SubmissionKind;
  readonly submittedAt: string;
  readonly hasContactChange: boolean;
  readonly profileRemoved: boolean;
}

export interface PublishedSnapshot {
  readonly name: string;
  readonly productName: string;
  readonly productUrl: string;
  readonly description: string | null;
  readonly role: string | null;
  readonly workplace: string | null;
  readonly introText: string | null;
  readonly introAnswer1: string | null;
  readonly introAnswer2: string | null;
  readonly introAnswer3: string | null;
  readonly avatarId: string;
  readonly hasPhoto: boolean;
  readonly cv: string | null;
  readonly cvInvalid: boolean;
}

export interface SubmissionDetailData {
  readonly revisionId: string;
  readonly kind: SubmissionKind;
  readonly name: string;
  readonly productName: string;
  readonly productUrl: string;
  readonly cohortName: string;
  readonly submittedAt: string;
  readonly email: string;
  readonly phone: string;
  readonly description: string | null;
  readonly role: string | null;
  readonly workplace: string | null;
  readonly introText: string | null;
  readonly introAnswer1: string | null;
  readonly introAnswer2: string | null;
  readonly introAnswer3: string | null;
  readonly avatarId: string;
  readonly hasPhoto: boolean;
  readonly cv: string | null;
  readonly cvInvalid: boolean;
  readonly published: PublishedSnapshot | null;
  readonly changes: readonly string[];
  readonly contactChange: { readonly pendingEmail: string | null; readonly pendingPhone: string | null } | null;
  readonly profileRemoved: boolean;
}

export interface AdminProfile {
  readonly profileId: string;
  readonly name: string;
  readonly productName: string;
  readonly cohortName: string;
  readonly visibility: 'active' | 'removed';
  readonly hasApproved: boolean;
  readonly createdAt: string;
}

/** The server clamps `limit` to this value. */
export const PAGE_LIMIT = 100;

export interface Page<T> {
  readonly items: readonly T[];
  readonly total: number;
}

function toPage<T>(r: { items?: readonly T[]; total?: number } | null): Page<T> {
  const items = r?.items ?? [];
  return { items, total: Math.max(r?.total ?? items.length, items.length) };
}

/** Data access for the review queue, submission details and published profiles; all calls go through `ApiClient`. */
@Injectable({ providedIn: 'root' })
export class ReviewService {
  private readonly api = inject(ApiClient);

  /** One-shot message handed from the detail screen to the queue after a decision. */
  readonly flash = signal<string | null>(null);

  list(): Observable<Page<SubmissionItem>> {
    return this.api
      .get<{ items?: readonly SubmissionItem[]; total?: number }>(`/admin/submissions?limit=${PAGE_LIMIT}`)
      .pipe(map(toPage));
  }

  get(revisionId: string): Observable<SubmissionDetailData> {
    return this.api.get<SubmissionDetailData>(`/admin/submissions/${encodeURIComponent(revisionId)}`);
  }

  /** Same-origin URL of the pending photo; the browser sends the admin session cookie. */
  photoUrl(revisionId: string): string {
    return `/api/admin/submissions/${encodeURIComponent(revisionId)}/photo`;
  }

  approve(revisionId: string): Observable<void> {
    return this.api.post<void>(`/admin/submissions/${encodeURIComponent(revisionId)}/approve`);
  }

  returnForCorrection(revisionId: string, note: string): Observable<void> {
    return this.api.post<void>(`/admin/submissions/${encodeURIComponent(revisionId)}/return`, { note });
  }

  listProfiles(): Observable<Page<AdminProfile>> {
    return this.api
      .get<{ items?: readonly AdminProfile[]; total?: number }>(`/admin/profiles?limit=${PAGE_LIMIT}`)
      .pipe(map(toPage));
  }

  removeProfile(profileId: string): Observable<void> {
    return this.api.post<void>(`/admin/profiles/${encodeURIComponent(profileId)}/remove`);
  }
}
