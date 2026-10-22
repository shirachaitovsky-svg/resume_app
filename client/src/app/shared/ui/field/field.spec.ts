import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { STRINGS } from '../../strings.he';
import { Field } from './field';

@Component({
  imports: [Field],
  template: `<app-field label="שם" [required]="required" [error]="error" [hint]="hint" [autocomplete]="autocomplete" [inputmode]="inputmode" [maxlength]="maxlength" />`,
})
class Host {
  required = false;
  error: string | null = null;
  hint: string | null = null;
  autocomplete: string | null = null;
  inputmode: string | null = null;
  maxlength: number | null = null;
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

describe('Field hint and input attributes', () => {
  it('describes the input by the hint alone, then by hint and error', async () => {
    let el = await render({ hint: 'רמז' });
    const hintId = el.querySelector('.hint')!.id;
    expect(el.querySelector('input')!.getAttribute('aria-describedby')).toBe(hintId);
    el = await render({ hint: 'רמז', error: 'שגיאה' });
    const ids = [el.querySelector('.hint')!.id, el.querySelector('.error')!.id];
    expect(el.querySelector('input')!.getAttribute('aria-describedby')).toBe(ids.join(' '));
  });

  it('sets autocomplete, inputmode and maxlength only when provided', async () => {
    let input = (await render({ autocomplete: 'email', inputmode: 'email', maxlength: 5 })).querySelector('input')!;
    expect(input.getAttribute('autocomplete')).toBe('email');
    expect(input.getAttribute('inputmode')).toBe('email');
    expect(input.getAttribute('maxlength')).toBe('5');
    input = (await render({})).querySelector('input')!;
    expect(input.hasAttribute('autocomplete')).toBe(false);
    expect(input.hasAttribute('inputmode')).toBe(false);
    expect(input.hasAttribute('maxlength')).toBe(false);
  });
});
