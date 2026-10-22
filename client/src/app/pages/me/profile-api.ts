import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../../shared/api/api-client';
import { CohortList, OwnProfile } from '../../shared/api/profile.models';

/** What the form hands over: text fields, a resized photo to upload, and whether the stored photo should go. */
export interface ProfilePayload {
  readonly fields: Readonly<Record<string, string>>;
  readonly photo: Blob | null;
  readonly removePhoto: boolean;
}

@Injectable({ providedIn: 'root' })
export class ProfileApi {
  private readonly api = inject(ApiClient);

  own(): Observable<OwnProfile> {
    return this.api.get<OwnProfile>('/me/profile');
  }

  activeCohorts(): Observable<CohortList> {
    return this.api.get<CohortList>('/cohorts/active');
  }

  create(payload: ProfilePayload): Observable<unknown> {
    const { json, form } = this.body(payload);
    return form ? this.api.postMultipart('/me/profile', form) : this.api.post('/me/profile', json);
  }

  update(payload: ProfilePayload): Observable<unknown> {
    const { json, form } = this.body(payload);
    return form ? this.api.putMultipart('/me/profile', form) : this.api.put('/me/profile', json);
  }

  private body(payload: ProfilePayload): { json: Record<string, unknown>; form: FormData | null } {
    const removePhoto = payload.photo === null && payload.removePhoto;
    if (payload.photo) {
      const form = new FormData();
      for (const [key, value] of Object.entries(payload.fields)) form.append(key, value);
      form.append('photo', new File([payload.photo], 'photo.jpg', { type: 'image/jpeg' }));
      return { json: {}, form };
    }
    return { json: removePhoto ? { ...payload.fields, removePhoto: true } : { ...payload.fields }, form: null };
  }
}

