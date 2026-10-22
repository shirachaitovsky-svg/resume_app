import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ADMIN_CSRF_URL, CSRF_URL, CsrfService } from './csrf.service';

describe('CsrfService scope', () => {
  it('does not cache a token that arrives after the scope was switched', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const csrf = TestBed.inject(CsrfService);
    const http = TestBed.inject(HttpTestingController);

    csrf.getToken().subscribe();
    csrf.setScope('admin');
    http.expectOne(CSRF_URL).flush({ token: 'public-late' });

    const seen: string[] = [];
    csrf.getToken().subscribe((t) => seen.push(t));
    http.expectOne(ADMIN_CSRF_URL).flush({ token: 'admin-token' });
    expect(seen).toEqual(['admin-token']);

    csrf.getToken().subscribe((t) => seen.push(t));
    http.verify();
    expect(seen).toEqual(['admin-token', 'admin-token']);
  });
});
