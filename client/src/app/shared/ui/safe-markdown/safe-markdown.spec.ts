import { TestBed } from '@angular/core/testing';
import { parseMarkdown } from './markdown-parser';
import { SafeMarkdown } from './safe-markdown';

async function render(source: string | null): Promise<HTMLElement> {
  const fixture = TestBed.createComponent(SafeMarkdown);
  fixture.componentRef.setInput('source', source);
  await fixture.whenStable();
  return fixture.nativeElement as HTMLElement;
}

describe('SafeMarkdown', () => {
  it('starts at the first level under the section heading even when the text begins with ##', async () => {
    const el = await render('## ניסיון\n\n### פרט');
    expect(el.querySelector('h3')?.textContent?.trim()).toBe('ניסיון');
    expect(el.querySelector('h4')?.textContent?.trim()).toBe('פרט');
    expect(el.querySelector('h5')).toBeNull();
  });

  it('shifts a text whose shallowest heading is ### (the deepest level the parser knows) to the first level, and keeps gaps between # and ###', async () => {
    const only = await render('### א');
    expect(only.querySelector('h3')?.textContent?.trim()).toBe('א');
    const gap = await render('# א\n\n### ב');
    expect(gap.querySelector('h3')?.textContent?.trim()).toBe('א');
    expect(gap.querySelector('h5')?.textContent?.trim()).toBe('ב');
  });

  it('renders headings, lists, emphasis and https links', async () => {
    const el = await render(
      '# כותרת\n\nטקסט **מודגש** ו*נטוי* עם [קישור](https://example.com/x).\n\n- א\n- ב\n\n1. אחד\n2. שניים\n\n## משנה',
    );
    expect(el.querySelector('h3')?.textContent?.trim()).toBe('כותרת');
    expect(el.querySelector('h4')?.textContent?.trim()).toBe('משנה');
    expect(el.querySelector('strong')?.textContent?.trim()).toBe('מודגש');
    expect(el.querySelector('em')?.textContent?.trim()).toBe('נטוי');
    expect(el.querySelectorAll('ul li').length).toBe(2);
    expect(el.querySelectorAll('ol li').length).toBe(2);
    const a = el.querySelector('a')!;
    expect(a.getAttribute('href')).toBe('https://example.com/x');
    expect(a.getAttribute('target')).toBe('_blank');
    expect(a.getAttribute('rel')).toBe('noopener noreferrer nofollow');
  });

  it('renders line breaks inside a paragraph', async () => {
    const el = await render('שורה א\nשורה ב');
    expect(el.querySelectorAll('p').length).toBe(1);
    expect(el.querySelectorAll('p br').length).toBe(1);
  });

  it('renders nothing for empty input', async () => {
    for (const empty of ['', '   \n ', null]) {
      const el = await render(empty);
      expect(el.querySelectorAll('p, h3, ul, ol, a').length).toBe(0);
    }
  });

  it('shows <script> as text and creates no script element', async () => {
    const el = await render('<script>alert(1)</script>');
    expect(el.querySelector('script')).toBeNull();
    expect(el.textContent).toContain('<script>alert(1)</script>');
  });

  it('shows <img onerror> as text and creates no img element', async () => {
    const el = await render('<img src=x onerror=alert(1)>');
    expect(el.querySelector('img')).toBeNull();
    expect(el.textContent).toContain('<img src=x onerror=alert(1)>');
    expect(el.innerHTML).not.toContain('<img');
  });

  it.each([
    ['javascript:', '[x](javascript:alert(1))'],
    ['data:', '[x](data:text/html;base64,AAAA)'],
    ['relative', '[x](/admin/secret)'],
    ['protocol-relative', '[x](//evil.example/x)'],
    ['mixed case javascript', '[x](JaVaScRiPt:alert(1))'],
  ])('renders a %s link as plain text without an anchor', async (_name, md) => {
    const el = await render(md);
    expect(el.querySelector('a')).toBeNull();
    expect(el.textContent).toContain('x');
    expect(el.innerHTML).not.toMatch(/javascript:|href=/i);
  });

  it('never produces an element other than the allowed set', async () => {
    const el = await render('# <b>x</b>\n\n- <iframe src=//e></iframe>\n\n**<svg onload=1>**\n\n[<img src=x>](https://ok.example)');
    const allowed = new Set(['H3', 'P', 'UL', 'OL', 'LI', 'STRONG', 'EM', 'A', 'BR']);
    for (const node of Array.from(el.querySelectorAll('*'))) expect(allowed.has(node.tagName)).toBe(true);
  });

  it('parser: tree has no raw html and unsafe links have null href', () => {
    const blocks = parseMarkdown('[a](javascript:x) [b](https://ok.example)');
    const runs = blocks[0].type === 'p' ? blocks[0].runs : [];
    expect(runs.find((r) => r.text === 'a')?.href).toBeNull();
    expect(runs.find((r) => r.text === 'b')?.href).toBe('https://ok.example/');
  });
});
