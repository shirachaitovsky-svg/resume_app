/** Returns a normalized absolute http(s) URL, or null for anything else (relative, javascript:, data:, ...). */
export function safeHttpUrl(value: string | null | undefined): string | null {
  if (!value) return null;
  const trimmed = value.trim();
  if (!/^https?:\/\//i.test(trimmed)) return null;
  try {
    const url = new URL(trimmed);
    return url.protocol === 'http:' || url.protocol === 'https:' ? url.href : null;
  } catch {
    return null;
  }
}

/** Maps a relative server photo path (`/profiles/{id}/photo`) to the `/api` proxy path; anything else is rejected. */
export function photoSrc(photoUrl: string | null | undefined): string | null {
  if (!photoUrl) return null;
  // local preview of a photo the participant picked (object URL created by this page)
  if (/^blob:/i.test(photoUrl)) return photoUrl;
  if (/^\/api\/profiles\/[0-9a-f-]+\/photo$/i.test(photoUrl)) return photoUrl;
  return /^\/profiles\/[0-9a-f-]+\/photo$/i.test(photoUrl) ? `/api${photoUrl}` : null;
}

export const GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
