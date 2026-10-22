import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ConfirmDialog } from './confirm-dialog';

@Component({
  imports: [ConfirmDialog],
  template: `
    <button id="opener" type="button" (click)="open.set(true)">open</button>
    <app-confirm-dialog
      [open]="open()"
      heading="כותרת"
      message="גוף ההודעה"
      confirmLabel="אישור"
      cancelLabel="ביטול"
      [busy]="busy()"
      (confirmed)="confirmed = confirmed + 1"
      (cancelled)="cancelled = cancelled + 1; open.set(false)"
    />
  `,
})
class Host {
  readonly open = signal(false);
  readonly busy = signal(false);
  confirmed = 0;
  cancelled = 0;
}

describe('ConfirmDialog', () => {
  afterEach(() => document.querySelectorAll('[data-test-host]').forEach((n) => n.remove()));

  async function setup() {
    const fixture = TestBed.createComponent(Host);
    document.body.appendChild(fixture.nativeElement);
    fixture.nativeElement.setAttribute('data-test-host', '');
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const opener = el.querySelector<HTMLButtonElement>('#opener')!;
    opener.focus();
    return { fixture, host: fixture.componentInstance, el, opener };
  }
  async function openIt(s: Awaited<ReturnType<typeof setup>>) {
    s.opener.click();
    s.fixture.detectChanges();
    await s.fixture.whenStable();
  }
  const dialog = (el: HTMLElement) => el.querySelector('dialog')!;
  const btn = (el: HTMLElement, cls: string) => el.querySelector<HTMLButtonElement>(`.${cls} button`)!;


  it('is closed until opened, then exposes alertdialog semantics with the consequence text', async () => {
    const s = await setup();
    expect(dialog(s.el).hasAttribute('open')).toBe(false);
    await openIt(s);
    const d = dialog(s.el);
    expect(d.hasAttribute('open') || d.open).toBe(true);
    expect(d.getAttribute('role')).toBe('alertdialog');
    expect(d.getAttribute('aria-modal')).toBe('true');
    const labelled = s.el.querySelector(`#${d.getAttribute('aria-labelledby')}`)!;
    const described = s.el.querySelector(`#${d.getAttribute('aria-describedby')}`)!;
    expect(labelled.textContent).toContain('כותרת');
    expect(described.textContent).toContain('גוף ההודעה');
  });

  it('puts focus on the cancel button and restores it to the opener on close', async () => {
    const s = await setup();
    await openIt(s);
    expect(document.activeElement).toBe(btn(s.el, 'cancel'));
    s.host.open.set(false);
    s.fixture.detectChanges();
    await s.fixture.whenStable();
    expect(document.activeElement).toBe(s.opener);
  });

  it('closes as cancelled on Escape and on a backdrop click, without confirming', async () => {
    const s = await setup();
    await openIt(s);
    dialog(s.el).dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    s.fixture.detectChanges();
    expect(s.host.cancelled).toBe(1);
    await s.fixture.whenStable();
    await openIt(s);
    dialog(s.el).dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    dialog(s.el).click();
    expect(s.host.cancelled).toBe(2);
    expect(s.host.confirmed).toBe(0);
  });

  it('does not dismiss when a drag starts in the panel and ends on the backdrop', async () => {
    const s = await setup();
    await openIt(s);
    s.el.querySelector<HTMLElement>('.panel')!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    dialog(s.el).click();
    expect(s.host.cancelled).toBe(0);
  });

  it('reports one Escape once even when keydown and the native cancel event both arrive', async () => {
    const s = await setup();
    await openIt(s);
    dialog(s.el).dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    dialog(s.el).dispatchEvent(new Event('cancel', { cancelable: true }));
    expect(s.host.cancelled).toBe(1);
  });

  it('reopens the native dialog if the browser closed it while still open', async () => {
    const s = await setup();
    await openIt(s);
    dialog(s.el).removeAttribute('open');
    dialog(s.el).dispatchEvent(new Event('close'));
    expect(dialog(s.el).hasAttribute('open') || dialog(s.el).open).toBe(true);
  });

  it('does not treat a click inside the panel as the backdrop', async () => {
    const s = await setup();
    await openIt(s);
    s.el.querySelector<HTMLElement>('.panel')!.click();
    expect(s.host.cancelled).toBe(0);
  });

  it('emits confirmed from the confirm button and cancelled from the cancel button', async () => {
    const s = await setup();
    await openIt(s);
    btn(s.el, 'confirm').click();
    expect(s.host.confirmed).toBe(1);
    btn(s.el, 'cancel').click();
    expect(s.host.cancelled).toBe(1);
  });

  it('disables both buttons and ignores Escape while busy', async () => {
    const s = await setup();
    await openIt(s);
    s.host.busy.set(true);
    s.fixture.detectChanges();
    await s.fixture.whenStable();
    expect(btn(s.el, 'confirm').disabled).toBe(true);
    expect(btn(s.el, 'cancel').disabled).toBe(true);
    dialog(s.el).dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    expect(s.host.cancelled).toBe(0);
  });

  it('keeps Tab inside the dialog', async () => {
    const s = await setup();
    await openIt(s);
    btn(s.el, 'confirm').focus();
    dialog(s.el).dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(btn(s.el, 'cancel'));
    dialog(s.el).dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true, cancelable: true }),
    );
    expect(document.activeElement).toBe(btn(s.el, 'confirm'));
  });
});
