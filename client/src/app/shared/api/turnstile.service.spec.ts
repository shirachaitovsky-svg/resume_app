import { TestBed } from '@angular/core/testing';
import { DEV_TURNSTILE_TOKEN, TURNSTILE } from './turnstile.service';

describe('TurnstileService', () => {
  it('defaults to the dev token', async () => {
    expect(await TestBed.inject(TURNSTILE).getToken()).toBe(DEV_TURNSTILE_TOKEN);
    expect(DEV_TURNSTILE_TOKEN).toBe('dev-turnstile-token');
  });

  it('can be replaced through the injection token', async () => {
    TestBed.configureTestingModule({
      providers: [{ provide: TURNSTILE, useValue: { getToken: () => Promise.resolve('real') } }],
    });
    expect(await TestBed.inject(TURNSTILE).getToken()).toBe('real');
  });
});
