import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { STRINGS } from '../../strings.he';
import { Field } from './field';

@Component({
  imports: [Field],
  template: `<app-field label="שם" [required]="required" [error]="error" />`,
})
class Host {
  required = false;
  error: string | null = null;
}

async function render(props: Partial<Host>): Promise<HTMLElement> {
  const fixture = TestBed.createComponent(Host);
  Object.assign(fixture.componentInstance, props);
  await fixture.whenStable();
  return fixture.nativeElement as HTMLElement;
}

describe('Field', () => {
  it('links the error message to the input via aria-describedby and sets aria-invalid', async () => {
    const el = await render({ error: 'שגיאה' });
    const input = el.querySelector('input')!;
    const error = el.querySelector('.error')!;
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(error.id).toBeTruthy();
    expect(input.getAttribute('aria-describedby')).toBe(error.id);
    expect(error.textContent).toContain('שגיאה');
  });

  it('shows no error and no ARIA error attributes when error is not set', async () => {
    const el = await render({});
    const input = el.querySelector('input')!;
    expect(el.querySelector('.error')).toBeNull();
    expect(input.hasAttribute('aria-invalid')).toBe(false);
    expect(input.hasAttribute('aria-describedby')).toBe(false);
  });

  it('shows the required text in the label when required', async () => {
    const el = await render({ required: true });
    expect(el.querySelector('label')!.textContent).toContain(STRINGS.required);
  });

  it('does not show the required text when optional', async () => {
    const el = await render({});
    expect(el.querySelector('label')!.textContent).not.toContain(STRINGS.required);
  });
});

describe('Field error announcement', () => {
  it('marks the error message as an alert', async () => {
    const el = await render({ error: 'שגיאה' });
    expect(el.querySelector('.error')!.getAttribute('role')).toBe('alert');
  });
});
