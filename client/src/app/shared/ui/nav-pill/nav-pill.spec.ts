import { TestBed } from '@angular/core/testing';
import { NavPill } from './nav-pill';

const items = [
  { id: 'a', label: 'א', href: '#a' },
  { id: 'b', label: 'ב', href: '#b' },
];

async function render(activeId: string | null): Promise<HTMLElement> {
  const fixture = TestBed.createComponent(NavPill);
  fixture.componentRef.setInput('items', items);
  fixture.componentRef.setInput('activeId', activeId);
  await fixture.whenStable();
  return fixture.nativeElement as HTMLElement;
}

describe('NavPill', () => {
  it('marks exactly the matching link with aria-current', async () => {
    const el = await render('b');
    const current = el.querySelectorAll('a[aria-current="page"]');
    expect(current.length).toBe(1);
    expect(current[0].getAttribute('href')).toBe('#b');
  });

  it('marks no link when activeId is null', async () => {
    const el = await render(null);
    expect(el.querySelectorAll('[aria-current]').length).toBe(0);
  });

  it('renders the hrefs', async () => {
    const el = await render(null);
    const hrefs = Array.from(el.querySelectorAll('a')).map((a) => a.getAttribute('href'));
    expect(hrefs).toEqual(['#a', '#b']);
  });
});

describe('NavPill router links', () => {
  it('renders items with a link through routerLink', async () => {
    const { provideRouter } = await import('@angular/router');
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(NavPill);
    fixture.componentRef.setInput('items', [{ id: 'a', label: 'א', href: '/a', link: '/a' }]);
    fixture.componentRef.setInput('activeId', 'a');
    await fixture.whenStable();
    const a = (fixture.nativeElement as HTMLElement).querySelector('a') as HTMLAnchorElement;
    expect(a.getAttribute('href')).toBe('/a');
    expect(a.getAttribute('aria-current')).toBe('page');
  });
});
