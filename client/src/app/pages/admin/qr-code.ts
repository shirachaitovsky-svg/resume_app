import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import qrcode from 'qrcode-generator';

const QUIET_ZONE = 4;

export interface QrMatrix {
  readonly size: number;
  /** One SVG path: a horizontal run of dark modules per segment. */
  readonly path: string;
}

/** Builds the module matrix for `value`; null when it cannot be encoded. */
export function buildQr(value: string): QrMatrix | null {
  if (!value) return null;
  try {
    const qr = qrcode(0, 'M');
    qr.addData(value);
    qr.make();
    const count = qr.getModuleCount();
    let path = '';
    for (let row = 0; row < count; row++) {
      let col = 0;
      while (col < count) {
        if (!qr.isDark(row, col)) {
          col++;
          continue;
        }
        const start = col;
        while (col < count && qr.isDark(row, col)) col++;
        path += `M${start + QUIET_ZONE} ${row + QUIET_ZONE}h${col - start}v1h-${col - start}z`;
      }
    }
    return { size: count + QUIET_ZONE * 2, path };
  } catch {
    return null;
  }
}

/** QR code drawn as SVG elements by Angular (no innerHTML, no data URL). Dark on white for scanners. */
@Component({
  selector: 'app-qr-code',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (matrix(); as m) {
      <svg
        role="img"
        [attr.aria-label]="label()"
        [attr.viewBox]="'0 0 ' + m.size + ' ' + m.size"
        shape-rendering="crispEdges"
        xmlns="http://www.w3.org/2000/svg"
      >
        <rect width="100%" height="100%" fill="#ffffff" />
        <path [attr.d]="m.path" fill="#000000" />
      </svg>
    }
  `,
  styles: `
    :host {
      display: block;
      inline-size: 220px;
      max-inline-size: 100%;
    }
    svg {
      display: block;
      inline-size: 100%;
      block-size: auto;
      border-radius: var(--radius-md);
    }
  `,
})
export class QrCode {
  readonly value = input.required<string>();
  readonly label = input.required<string>();
  protected readonly matrix = computed(() => buildQr(this.value()));
}
