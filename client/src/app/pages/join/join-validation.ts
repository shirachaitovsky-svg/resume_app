export type JoinField = 'name' | 'productName' | 'productUrl' | 'email' | 'phone';
export type JoinErrorKind = 'required' | 'tooLong' | 'invalid';

export const JOIN_FIELDS: readonly JoinField[] = ['name', 'productName', 'productUrl', 'email', 'phone'];

/** Client-side limits; the server remains authoritative. */
export const JOIN_MAX_LENGTH: Readonly<Record<JoinField, number>> = {
  name: 100,
  productName: 100,
  productUrl: 2048,
  email: 254,
  phone: 40,
};

function isValidUrl(value: string): boolean {
  if (/\s/.test(value) || !/^https?:\/\//i.test(value)) return false;
  try {
    const url = new URL(value);
    return (
      (url.protocol === 'http:' || url.protocol === 'https:') && url.hostname !== '' && !url.username && !url.password
    );
  } catch {
    return false;
  }
}

function isValidEmail(value: string): boolean {
  if (/\s/.test(value)) return false;
  const parts = value.split('@');
  if (parts.length !== 2) return false;
  const [local, domain] = parts;
  return (
    local.length > 0 &&
    domain.includes('.') &&
    !domain.startsWith('.') &&
    !domain.endsWith('.') &&
    !domain.includes('..')
  );
}

function isValidPhone(value: string): boolean {
  if (!/^\+?[\d\s-]+$/.test(value)) return false;
  const digits = value.replace(/\D/g, '').length;
  return digits >= 9 && digits <= 15;
}

/** Validates one already-trimmed value. Returns the error kind, or null when valid. */
export function validateJoinField(field: JoinField, value: string): JoinErrorKind | null {
  if (value === '') return 'required';
  if (value.length > JOIN_MAX_LENGTH[field]) return 'tooLong';
  switch (field) {
    case 'productUrl':
      return isValidUrl(value) ? null : 'invalid';
    case 'email':
      return isValidEmail(value) ? null : 'invalid';
    case 'phone':
      return isValidPhone(value) ? null : 'invalid';
    default:
      return null;
  }
}
