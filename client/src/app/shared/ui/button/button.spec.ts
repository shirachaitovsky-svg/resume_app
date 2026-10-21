import { TestBed } from '@angular/core/testing';
import { Button } from './button';

describe('Button', () => {
  it('sets the variant class on the host', async () => {
    const fixture = TestBed.createComponent(Button);
    fixture.componentRef.setInput('variant', 'danger');
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).classList.contains('variant-danger')).toBe(true);
  });

  it('passes disabled to the inner button', async () => {
    const fixture = TestBed.createComponent(Button);
    fixture.componentRef.setInput('disabled', true);
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).querySelector('button')!.disabled).toBe(true);
  });

  it('defaults type to button', async () => {
    const fixture = TestBed.createComponent(Button);
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).querySelector('button')!.type).toBe('button');
  });
});
