import { readFileSync } from 'node:fs';
import { join } from 'node:path';

const css = readFileSync(join(import.meta.dirname, 'glass-card.css'), 'utf8');

describe('GlassCard CSS', () => {

  it('falls back to an opaque white surface when backdrop-filter is unsupported', () => {
    const match = css.match(/@supports not[^{]*\{[^{]*\{([^}]*)\}/);
    expect(match).not.toBeNull();
    expect(match![1]).toContain('var(--color-surface)');
  });

  it('uses the glass tokens when supported', () => {
    expect(css).toContain('backdrop-filter: var(--glass-blur)');
    expect(css).toContain('var(--radius-xl)');
  });
});

describe('GlassCard', () => {
  it('sets the compact host class from the compact input', async () => {
    const { TestBed } = await import('@angular/core/testing');
    const { GlassCard } = await import('./glass-card');
    const fixture = TestBed.createComponent(GlassCard);
    const host = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();
    expect(host.classList.contains('compact')).toBe(false);
    fixture.componentRef.setInput('compact', true);
    await fixture.whenStable();
    expect(host.classList.contains('compact')).toBe(true);
  });
});
