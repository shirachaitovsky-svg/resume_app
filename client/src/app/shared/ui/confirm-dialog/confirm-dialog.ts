import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  effect,
  inject,
  input,
  output,
  viewChild,
} from '@angular/core';
import { Button } from '../button/button';

let nextId = 0;

/**
 * Shared accessible confirmation (UX-DR20) for irreversible actions: a modal `<dialog>` that states the
 * consequence, focuses the cancel button on open, closes as "cancelled" on Escape or a backdrop click and
 * hands focus back to whatever opened it. The parent owns `open`: set it to false on `cancelled`, and when
 * the confirmed action finishes.
 */
@Component({
  selector: 'app-confirm-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Button],
  template: `
    <dialog
      #dialog
      class="dialog"
      role="alertdialog"
      aria-modal="true"
      [attr.aria-labelledby]="headingId"
      [attr.aria-describedby]="messageId"
      (cancel)="onNativeCancel($event)"
      (close)="onNativeClose()"
      (mousedown)="downTarget = $event.target"
      (click)="onBackdropClick($event)"
      (keydown)="onKeydown($event)"
    >
      <div class="panel">
        <h2 [id]="headingId">{{ heading() }}</h2>
        <p [id]="messageId">{{ message() }}</p>
        <div class="actions">
          <app-button class="cancel" variant="secondary" [disabled]="busy()" (click)="cancel()">{{
            cancelLabel()
          }}</app-button>
          <app-button class="confirm" variant="danger" [disabled]="busy()" (click)="confirm()">{{
            confirmLabel()
          }}</app-button>
        </div>
      </div>
    </dialog>
  `,
  styleUrl: './confirm-dialog.css',
})
export class ConfirmDialog {
  readonly open = input(false);
  readonly heading = input.required<string>();
  readonly message = input.required<string>();
  readonly confirmLabel = input.required<string>();
  readonly cancelLabel = input.required<string>();
  /** While true both buttons are disabled and Escape or the backdrop do nothing. */
  readonly busy = input(false);
  readonly confirmed = output<void>();
  readonly cancelled = output<void>();

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  protected readonly headingId = `app-confirm-heading-${nextId}`;
  protected readonly messageId = `app-confirm-message-${nextId++}`;
  private opener: HTMLElement | null = null;
  private escapeHandled = false;
  /** Where the last mouse press started; a drag from the panel to the backdrop must not dismiss. */
  protected downTarget: EventTarget | null = null;

  constructor() {
    effect(() => {
      const open = this.open();
      const el = this.dialog().nativeElement;
      if (open) this.show(el);
      else this.hide(el);
    });
  }

  private show(el: HTMLDialogElement): void {
    if (el.open) return;
    this.opener ??= document.activeElement instanceof HTMLElement ? document.activeElement : null;
    if (typeof el.showModal === 'function') el.showModal();
    else el.setAttribute('open', '');
    this.host.nativeElement.querySelector<HTMLElement>('.cancel button')?.focus();
  }

  private hide(el: HTMLDialogElement): void {
    if (!el.open && !el.hasAttribute('open')) return;
    if (typeof el.close === 'function') el.close();
    else el.removeAttribute('open');
    const opener = this.opener;
    this.opener = null;
    // The opener may be gone (a revoked cohort loses its button); the caller then places focus itself.
    if (opener?.isConnected) opener.focus();
  }

  protected confirm(): void {
    if (!this.busy()) this.confirmed.emit();
  }

  protected cancel(): void {
    if (!this.busy()) this.cancelled.emit();
  }

  /** Escape: stop the browser closing the dialog itself; the parent decides through `open`. */
  protected onNativeCancel(event: Event): void {
    event.preventDefault();
    // The keydown handler already reported this Escape.
    if (!this.escapeHandled) this.cancel();
  }

  /** The browser closed the dialog by itself (for example a second Escape) while the parent still wants it open. */
  protected onNativeClose(): void {
    const el = this.dialog().nativeElement;
    if (this.open() && !el.open && !el.hasAttribute('open')) this.show(el);
  }

  protected onBackdropClick(event: MouseEvent): void {
    // The panel fills the dialog, so a click that lands on the dialog element itself is the backdrop.
    const el = this.dialog().nativeElement;
    const down = this.downTarget;
    this.downTarget = null;
    if (event.target === el && down === el) this.cancel();
  }

  /** Keeps Tab inside the dialog (also where `showModal` is unavailable) and handles Escape there. */
  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      this.escapeHandled = true;
      setTimeout(() => (this.escapeHandled = false));
      this.cancel();
      return;
    }
    if (event.key !== 'Tab') return;
    const buttons = Array.from(this.host.nativeElement.querySelectorAll<HTMLButtonElement>('dialog button')).filter(
      (b) => !b.disabled,
    );
    if (buttons.length === 0) {
      event.preventDefault();
      return;
    }
    const first = buttons[0];
    const last = buttons[buttons.length - 1];
    const active = document.activeElement;
    if (event.shiftKey && active === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && active === last) {
      event.preventDefault();
      first.focus();
    }
  }
}
