import { expectNoSeriousViolations } from './a11y';

describe('expectNoSeriousViolations', () => {
  it('passes for an accessible fragment and fails for an unlabeled input', async () => {
    const ok = document.createElement('div');
    ok.innerHTML = '<label for="a">שם</label><input id="a" type="text" />';
    document.body.append(ok);
    await expectNoSeriousViolations(ok);
    const bad = document.createElement('div');
    bad.innerHTML = '<input type="text" /><img src="x.png">';
    document.body.append(bad);
    await expect(expectNoSeriousViolations(bad)).rejects.toThrow();
    ok.remove();
    bad.remove();
  });
});
