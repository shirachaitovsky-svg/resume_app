import { TestBed } from '@angular/core/testing';
import { AVATAR_IDS, AvatarImage } from './avatar';

async function make(inputs: Record<string, unknown>) {
  const fixture = TestBed.createComponent(AvatarImage);
  for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
  await fixture.whenStable();
  return { fixture, el: fixture.nativeElement as HTMLElement };
}

describe('AvatarImage', () => {
  it.each([...AVATAR_IDS])('renders the %s illustration with role img and a label', async (id) => {
    const { el } = await make({ avatarId: id, name: 'נועה' });
    const svg = el.querySelector('svg')!;
    expect(svg.getAttribute('role')).toBe('img');
    expect(svg.getAttribute('aria-label')).toBe('נועה');
    expect(svg.getAttribute('data-figure')).toBe(id);
  });

  it.each(['default', 'nope', '', null])('falls back to the default figure for %s', async (id) => {
    const { el } = await make({ avatarId: id, name: 'נועה' });
    expect(el.querySelector('svg')?.getAttribute('data-figure')).toBe('default');
  });

  it('shows the photo through /api with alt text', async () => {
    const { el } = await make({ photoUrl: '/profiles/5eed0001-0000-4000-8000-000000000001/photo', name: 'נועה', avatarId: 'girl-1' });
    const img = el.querySelector('img')!;
    expect(img.getAttribute('src')).toBe('/api/profiles/5eed0001-0000-4000-8000-000000000001/photo');
    expect(img.getAttribute('alt')).toBe('נועה');
    expect(el.querySelector('svg')).toBeNull();
  });

  it('falls back to the illustration when the photo fails to load', async () => {
    const { fixture, el } = await make({ photoUrl: '/profiles/5eed0001-0000-4000-8000-000000000001/photo', name: 'נועה', avatarId: 'boy-2' });
    el.querySelector('img')!.dispatchEvent(new Event('error'));
    await fixture.whenStable();
    expect(el.querySelector('img')).toBeNull();
    expect(el.querySelector('svg')?.getAttribute('data-figure')).toBe('boy-2');
  });

  it.each(['https://evil.example/p.png', '//evil.example/p.png', 'javascript:alert(1)'])(
    'ignores a photo URL outside /api (%s)',
    async (photoUrl) => {
      const { el } = await make({ photoUrl, name: 'x' });
      expect(el.querySelector('img')).toBeNull();
      expect(el.querySelector('svg')).not.toBeNull();
    },
  );
});
