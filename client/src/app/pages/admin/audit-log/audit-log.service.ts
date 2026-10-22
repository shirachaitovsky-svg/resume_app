import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiClient } from '../../../shared/api/api-client';

export const AUDIT_PAGE_SIZE = 50;

/** One audit entry: who did what to which entity, by identifiers only. */
export interface AuditEntry {
  readonly id: string;
  readonly at: string;
  readonly actorId: string;
  readonly actorName: string | null;
  readonly action: string;
  readonly entityType: string;
  readonly entityId: string;
}

export interface AuditPage {
  readonly items: readonly AuditEntry[];
  readonly total: number;
}

/** Read-only data access for the audit log. There is deliberately no write method. */
@Injectable({ providedIn: 'root' })
export class AuditLogService {
  private readonly api = inject(ApiClient);

  list(offset: number): Observable<AuditPage> {
    const query = offset > 0 ? `limit=${AUDIT_PAGE_SIZE}&offset=${offset}` : `limit=${AUDIT_PAGE_SIZE}`;
    return this.api.get<{ items?: readonly AuditEntry[]; total?: number }>(`/admin/audit-log?${query}`).pipe(
      map((r) => {
        const items = r?.items ?? [];
        return { items, total: Math.max(r?.total ?? items.length, items.length) };
      }),
    );
  }
}
