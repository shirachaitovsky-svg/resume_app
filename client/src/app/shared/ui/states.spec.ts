import { TestBed } from '@angular/core/testing';
import { AppError } from '../api/app-error';
import { STRINGS } from '../strings.he';
import { EmptyState } from './empty-state/empty-state';
import { ErrorState } from './error-state/error-state';
import { LoadingSkeleton } from './loading-skeleton/loading-skeleton';

describe('LoadingSkeleton', () => {
  it('is busy, labelled, and renders the configured number of lines and cards', async () => {
    const fixture = TestBed.createComponent(LoadingSkeleton);
    fixture.componentRef.setInput('rows', 4);
    fixture.componentRef.setInput('cards', 2);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.getAttribute('aria-busy')).toBe('true');
    expect(el.getAttribute('aria-label')).toBe(STRINGS.states.loading);
    expect(el.querySelectorAll('.card').length).toBe(2);
    expect(el.querySelectorAll('.line').length).toBe(8);
  });

  it('defaults to one card of three lines', async () => {
    const fixture = TestBed.createComponent(LoadingSkeleton);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('.card').length).toBe(1);
    expect(el.querySelectorAll('.line').length).toBe(3);
  });
});

describe('EmptyState', () => {
  it('shows title and text', async () => {
    const fixture = TestBed.createComponent(EmptyState);
    fixture.componentRef.setInput('title', 'ריק');
    fixture.componentRef.setInput('text', 'אין כלום');
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('h2')?.textContent).toBe('ריק');
    expect(el.querySelector('p')?.textContent).toBe('אין כלום');
  });

  it('falls back to the default title and omits empty text', async () => {
    const fixture = TestBed.createComponent(EmptyState);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('h2')?.textContent).toBe(STRINGS.states.emptyTitle);
    expect(el.querySelector('p')).toBeNull();
  });
});

describe('ErrorState', () => {
  it('is an alert showing the AppError message and emits retry on click', async () => {
    const fixture = TestBed.createComponent(ErrorState);
    fixture.componentRef.setInput('error', new AppError('network', 0, STRINGS.errors.network));
    let retries = 0;
    fixture.componentInstance.retry.subscribe(() => retries++);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.getAttribute('role')).toBe('alert');
    expect(el.textContent).toContain(STRINGS.errors.network);
    const button = el.querySelector('button') as HTMLButtonElement;
    expect(button.textContent).toContain(STRINGS.states.retry);
    button.click();
    expect(retries).toBe(1);
  });

  it('shows a generic message when no error is given', async () => {
    const fixture = TestBed.createComponent(ErrorState);
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain(STRINGS.errors.server);
  });
});
