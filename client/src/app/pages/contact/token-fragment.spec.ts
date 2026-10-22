import { takeTokenFromFragment } from './token-fragment';

describe('takeTokenFromFragment', () => {
  let replaceState: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    replaceState = vi.spyOn(window.history, 'replaceState');
  });
  afterEach(() => replaceState.mockRestore());

  const stripped = () =>
    replaceState.mock.calls.some((c: unknown[]) => typeof c[2] === 'string' && !(c[2] as string).includes('#'));

  it.each([
    ['token=abc_-9', 'abc_-9'],
    ['#token=abc_-9', 'abc_-9'],
    ['x=1&token=abc', 'abc'],
    ['?token=abc', 'abc'],
    ['#?token=abc', 'abc'],
  ])('returns the token for %s and strips the fragment', (fragment, expected) => {
    expect(takeTokenFromFragment(fragment, document)).toBe(expected);
    expect(stripped()).toBe(true);
  });

  it.each([[null], [undefined], [''], ['token='], ['x=1'], ['token=a b']])(
    'returns null for %s and still strips the fragment',
    (fragment) => {
      expect(takeTokenFromFragment(fragment as string | null | undefined, document)).toBeNull();
      expect(stripped()).toBe(true);
    },
  );
});
