import axe from 'axe-core';

/**
 * Rules switched off, each with a reason:
 * - color-contrast: jsdom does no layout and computes no colors; contrast is checked by reading the tokens.
 * - region: component tests render a single route outside the app shell (which owns the `main` landmark).
 * - page-has-heading-one / landmark-one-main: same reason, the shell provides `main`; the h1 is asserted by hand.
 */
const DISABLED_RULES = ['color-contrast', 'region', 'page-has-heading-one', 'landmark-one-main'];

/**
 * Fails when axe finds ANY violation (critical, serious, moderate or minor: labels, names, roles, heading order, lang/dir)
 * in `root`, for every rule except the disabled ones above (color-contrast, region, page-has-heading-one, landmark-one-main).
 */
export async function expectNoSeriousViolations(root: Element): Promise<void> {
  // axe probes canvas support and jsdom logs "not implemented" noise; null is the documented "no canvas" answer. Restored afterwards.
  const original = HTMLCanvasElement.prototype.getContext;
  HTMLCanvasElement.prototype.getContext = (() => null) as typeof original;
  let results: axe.AxeResults;
  try {
    results = await axe.run(root, {
      rules: Object.fromEntries(DISABLED_RULES.map((id) => [id, { enabled: false }])),
      resultTypes: ['violations'],
    });
  } finally {
    HTMLCanvasElement.prototype.getContext = original;
  }
  const blocking = results.violations.filter((v) => v.impact === 'critical' || v.impact === 'serious');
  const report = blocking
    .map((v) => `${v.id} (${v.impact}): ${v.help}\n${v.nodes.map((n) => '  ' + n.html).join('\n')}`)
    .join('\n');
  expect(report).toBe('');
  // Moderate and minor findings (e.g. heading order) are also worth knowing; they fail too, so they get fixed.
  const other = results.violations.filter((v) => !blocking.includes(v));
  expect(other.map((v) => `${v.id}: ${v.nodes.map((n) => n.html).join(' | ')}`)).toEqual([]);
}

/** Manual check (axe's own page-level rules are off in component tests): exactly one h1 in the rendered screen. */
export function expectSingleH1(root: Element): void {
  expect(root.querySelectorAll('h1').length).toBe(1);
}
