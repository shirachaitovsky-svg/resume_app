import { InjectionToken } from '@angular/core';

export interface TurnstileService {
  getToken(): Promise<string>;
}

/** Cloudflare test key accepted by the server in development. Replaced by the real widget in stories 5.4 and 5.9. */
export const DEV_TURNSTILE_TOKEN = 'dev-turnstile-token';

export class DevTurnstileService implements TurnstileService {
  getToken(): Promise<string> {
    return Promise.resolve(DEV_TURNSTILE_TOKEN);
  }
}

/** Replaceable provider: override with `{ provide: TURNSTILE, useClass: ... }`. */
export const TURNSTILE = new InjectionToken<TurnstileService>('TurnstileService', {
  providedIn: 'root',
  factory: () => new DevTurnstileService(),
});
