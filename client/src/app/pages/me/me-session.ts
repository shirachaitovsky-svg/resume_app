import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../../shared/api/api-client';

export interface MeSession {
  readonly email: string;
  readonly hasProfile: boolean;
  readonly profileId?: string;
}

/** Participant session calls (Google sign-in). Nothing is persisted in the browser; the cookie lives on the server. */
@Injectable({ providedIn: 'root' })
export class MeSessionService {
  private readonly api = inject(ApiClient);

  /** 200 is signed in; a 401 surfaces as an AppError with status 401. */
  me(): Observable<MeSession> {
    return this.api.get<MeSession>('/auth/google/me');
  }

  logout(): Observable<unknown> {
    return this.api.post<unknown>('/auth/google/logout');
  }
}
