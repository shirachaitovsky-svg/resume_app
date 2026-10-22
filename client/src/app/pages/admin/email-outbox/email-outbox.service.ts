import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiClient } from '../../../shared/api/api-client';

/** The server's page size for the stuck-email list. */
export const OUTBOX_PAGE_SIZE = 100;

/** A dead or stuck outbox row. Ids, state and counters only: never an address, text or token. */
export interface StuckEmail {
  readonly id: string;
  readonly templateKey: string;
  readonly profileId: string | null;
  readonly requestId: string | null;
  readonly status: 'dead' | 'pending';
  readonly createdAt: string;
  readonly ageSeconds: number;
  readonly attempts: number;
  readonly lastErrorCategory: string | null;
}

export interface StuckEmailPage {
  readonly items: readonly StuckEmail[];
  readonly total: number;
}

/** Data access for the stuck-email screen; all calls go through `ApiClient`. */
@Injectable({ providedIn: 'root' })
export class EmailOutboxService {
  private readonly api = inject(ApiClient);

  listStuck(offset: number): Observable<StuckEmailPage> {
    const query = offset > 0 ? `limit=${OUTBOX_PAGE_SIZE}&offset=${offset}` : `limit=${OUTBOX_PAGE_SIZE}`;
    return this.api.get<{ items?: readonly StuckEmail[]; total?: number }>(`/admin/email-outbox/stuck?${query}`).pipe(
      map((r) => {
        const items = r?.items ?? [];
        return { items, total: Math.max(r?.total ?? items.length, items.length) };
      }),
    );
  }

  retry(id: string): Observable<unknown> {
    return this.api.post<unknown>(`/admin/email-outbox/${encodeURIComponent(id)}/retry`);
  }
}
