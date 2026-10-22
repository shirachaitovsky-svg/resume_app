import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MdRun, parseMarkdown } from './markdown-parser';

/**
 * Renders restricted Markdown with Angular elements only: no innerHTML, no sanitizer bypass.
 * Any HTML in the input is shown as literal text.
 */
@Component({
  selector: 'app-safe-markdown',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet],
  template: `
    <ng-template #styled let-run>
      @if (run.strong && run.em) {
        <strong><em>{{ run.text }}</em></strong>
      } @else if (run.strong) {
        <strong>{{ run.text }}</strong>
      } @else if (run.em) {
        <em>{{ run.text }}</em>
      } @else {
        {{ run.text }}
      }
    </ng-template>
    <ng-template #runs let-list>
      @for (run of list; track $index) {
        @if (run.kind === 'br') {
          <br />
        } @else if (run.href) {
          <a [href]="run.href" target="_blank" rel="noopener noreferrer nofollow">
            <ng-container *ngTemplateOutlet="styled; context: { $implicit: run }" />
          </a>
        } @else {
          <ng-container *ngTemplateOutlet="styled; context: { $implicit: run }" />
        }
      }
    </ng-template>
    @for (block of blocks(); track $index) {
      @switch (block.type) {
        @case ('h') {
          @switch (headingTag(block.level)) {
            @case (3) {
              <h3><ng-container *ngTemplateOutlet="runs; context: { $implicit: block.runs }" /></h3>
            }
            @case (4) {
              <h4><ng-container *ngTemplateOutlet="runs; context: { $implicit: block.runs }" /></h4>
            }
            @default {
              <h5><ng-container *ngTemplateOutlet="runs; context: { $implicit: block.runs }" /></h5>
            }
          }
        }
        @case ('ul') {
          <ul>
            @for (item of block.items; track $index) {
              <li><ng-container *ngTemplateOutlet="runs; context: { $implicit: item }" /></li>
            }
          </ul>
        }
        @case ('ol') {
          <ol>
            @for (item of block.items; track $index) {
              <li><ng-container *ngTemplateOutlet="runs; context: { $implicit: item }" /></li>
            }
          </ol>
        }
        @default {
          <p><ng-container *ngTemplateOutlet="runs; context: { $implicit: block.runs }" /></p>
        }
      }
    }
  `,
  styleUrl: './safe-markdown.css',
})
export class SafeMarkdown {
  readonly source = input<string | null | undefined>('');
  /** Markdown `#` becomes this heading level (default h3, under the section's h2). */
  readonly headingOffset = input(2);

  protected readonly blocks = computed(() => parseMarkdown(this.source()));

  protected headingTag(level: number): number {
    return Math.min(5, level + this.headingOffset());
  }
}

export type { MdRun };
