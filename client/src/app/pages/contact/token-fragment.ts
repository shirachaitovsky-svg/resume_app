const TOKEN_SHAPE = /^[^\s]{1,512}$/;

/**
 * Reads `token` from a URL fragment such as `token=abc` and removes the whole fragment from the address bar,
 * so the secret never stays in the history entry, a bookmark or a copied link. Returns null when absent or malformed.
 */
export function takeTokenFromFragment(fragment: string | null | undefined, doc: Document): string | null {
  clearFragment(doc);
  if (!fragment) return null;
  const token = new URLSearchParams(fragment.replace(/^#/, '')).get('token');
  return token !== null && TOKEN_SHAPE.test(token) ? token : null;
}

function clearFragment(doc: Document): void {
  const win = doc.defaultView;
  if (!win) return;
  try {
    win.history.replaceState(win.history.state, '', win.location.pathname + win.location.search);
  } catch {
    // The address bar stays as it is; the token is already in memory only.
  }
}
