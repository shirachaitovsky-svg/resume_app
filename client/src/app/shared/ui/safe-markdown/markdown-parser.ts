import { safeHttpUrl } from '../../url';

/** One inline run. Text is always rendered through interpolation, never as HTML. */
export interface MdRun {
  readonly kind: 'text' | 'br';
  readonly text: string;
  readonly strong: boolean;
  readonly em: boolean;
  /** Validated absolute http(s) URL, or null. */
  readonly href: string | null;
}

export type MdBlock =
  | { readonly type: 'p'; readonly runs: readonly MdRun[] }
  | { readonly type: 'h'; readonly level: 1 | 2 | 3; readonly runs: readonly MdRun[] }
  | { readonly type: 'ul' | 'ol'; readonly items: readonly (readonly MdRun[])[] };

const INLINE_RE = /\[([^\]\n]+)\]\(([^)\s]+)\)|\*\*(.+?)\*\*|\*(?!\s)([^*\n]+?)\*/g;

function parseRuns(text: string, strong = false, em = false, href: string | null = null, allowLink = true): MdRun[] {
  const runs: MdRun[] = [];
  const push = (t: string): void => {
    if (t) runs.push({ kind: 'text', text: t, strong, em, href });
  };
  let last = 0;
  INLINE_RE.lastIndex = 0;
  const re = new RegExp(INLINE_RE.source, 'g');
  let m: RegExpExecArray | null;
  while ((m = re.exec(text)) !== null) {
    push(text.slice(last, m.index));
    last = m.index + m[0].length;
    if (m[1] !== undefined) {
      if (!allowLink) {
        push(m[0]);
      } else {
        // unsafe or relative targets become plain text without a link
        runs.push(...parseRuns(m[1], strong, em, safeHttpUrl(m[2]), false));
      }
    } else if (m[3] !== undefined) {
      runs.push(...parseRuns(m[3], true, em, href, allowLink));
    } else {
      runs.push(...parseRuns(m[4], strong, true, href, allowLink));
    }
  }
  push(text.slice(last));
  return runs;
}

function parseLines(lines: readonly string[]): MdRun[] {
  const runs: MdRun[] = [];
  lines.forEach((line, i) => {
    if (i > 0) runs.push({ kind: 'br', text: '', strong: false, em: false, href: null });
    runs.push(...parseRuns(line.trim()));
  });
  return runs;
}

/** Parses a restricted Markdown subset into a node tree: paragraphs, #..### headings, lists, bold, italic, links. */
export function parseMarkdown(source: string | null | undefined): MdBlock[] {
  if (!source || !source.trim()) return [];
  const blocks: MdBlock[] = [];
  let para: string[] = [];
  let list: { type: 'ul' | 'ol'; items: MdRun[][] } | null = null;

  const flushPara = (): void => {
    if (para.length > 0) blocks.push({ type: 'p', runs: parseLines(para) });
    para = [];
  };
  const flushList = (): void => {
    if (list) blocks.push({ type: list.type, items: list.items });
    list = null;
  };

  for (const raw of source.replace(/\r\n?/g, '\n').split('\n')) {
    const line = raw.trimEnd();
    if (line.trim() === '') {
      flushPara();
      flushList();
      continue;
    }
    const heading = /^\s{0,3}(#{1,3})\s+(.*)$/.exec(line);
    if (heading) {
      flushPara();
      flushList();
      blocks.push({ type: 'h', level: heading[1].length as 1 | 2 | 3, runs: parseRuns(heading[2].trim()) });
      continue;
    }
    const bullet = /^\s*[-*+]\s+(.*)$/.exec(line);
    const ordered = /^\s*\d+[.)]\s+(.*)$/.exec(line);
    const item = bullet ?? ordered;
    if (item) {
      flushPara();
      const type = bullet ? 'ul' : 'ol';
      if (list && list.type !== type) flushList();
      list ??= { type, items: [] };
      list.items.push(parseRuns(item[1].trim()));
      continue;
    }
    flushList();
    para.push(line);
  }
  flushPara();
  flushList();
  return blocks;
}
