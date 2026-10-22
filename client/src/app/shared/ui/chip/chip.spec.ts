import { TestBed } from '@angular/core/testing';
import { Chip } from './chip';

// The check mark and bold weight are CSS pseudo-content/styles (chip.css), which jsdom does not render: verified manually in the browser.
// What is asserted here is the state that drives them (the selected class) and the screen-reader state (aria-pressed).
describe('Chip', () => {
  it('toggles the selected state and aria-pressed on click', async () => {
    const fixture = TestBed.createComponent(Chip);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    const button = host.querySelector('button')!;
    expect(button.getAttribute('aria-pressed')).toBe('false');
    expect(host.classList.contains('selected')).toBe(false);

    button.click();
    await fixture.whenStable();
    expect(button.getAttribute('aria-pressed')).toBe('true');
    expect(host.classList.contains('selected')).toBe(true);
  });

  it('renders as selected when the input is set', async () => {
    const fixture = TestBed.createComponent(Chip);
    fixture.componentRef.setInput('selected', true);
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).classList.contains('selected')).toBe(true);
  });
});
