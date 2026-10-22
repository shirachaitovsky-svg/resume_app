import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiClient } from '../../../shared/api/api-client';
import { PAGE_LIMIT, Page } from '../review/review.service';

/** One verified contact request in the admin queue. Carries the visitor's data only, never a participant's contact details. */
export interface ContactRequestItem {
  readonly requestId: string;
  readonly visitorName: string;
  readonly visitorEmail: string;
  readonly reason: string;
  readonly profileId: string;
  readonly profileName: string;
  readonly productName: string;
  readonly cohortName: string;
  readonly profileVisibility: string;
  readonly createdAt: string;
  readonly verifiedAt: string;
  readonly status: string;
  readonly participantDecision: string | null;
  readonly participantDecidedAt: string | null;
  readonly managerApprovedAt: string | null;
  readonly notificationStatus: string;
  readonly deliveryStatus: string;
  readonly deliveryErrorCategory: string | null;
}

/** Data access for the contact-request queue; all calls go through `ApiClient`. */
@Injectable({ providedIn: 'root' })
export class ContactRequestsService {
  private readonly api = inject(ApiClient);

  list(): Observable<Page<ContactRequestItem>> {
    return this.api
      .get<{ items?: readonly ContactRequestItem[]; total?: number }>(`/admin/contact-requests?status=all&limit=${PAGE_LIMIT}`)
      .pipe(
        map((r) => {
          const items = r?.items ?? [];
          return { items, total: Math.max(r?.total ?? items.length, items.length) };
        }),
      );
  }

  sendToParticipant(requestId: string): Observable<unknown> {
    return this.api.post<unknown>(`/admin/contact-requests/${encodeURIComponent(requestId)}/send-to-participant`);
  }

  approveDelivery(requestId: string): Observable<unknown> {
    return this.api.post<unknown>(`/admin/contact-requests/${encodeURIComponent(requestId)}/approve-delivery`);
  }
}
