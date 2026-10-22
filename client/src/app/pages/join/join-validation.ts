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

export type OptionalField =
  | 'description'
  | 'role'
  | 'workplace'
  | 'introText'
  | 'introAnswer1'
  | 'introAnswer2'
  | 'introAnswer3'
  | 'cv';

/** Every field the form can show an error for (the server may also report `avatarId` and `photo`). */
export type FormField = JoinField | OptionalField | 'avatarId' | 'photo';

export const OPTIONAL_FIELDS: readonly OptionalField[] = [
  'description',
  'role',
  'workplace',
  'introText',
  'introAnswer1',
  'introAnswer2',
  'introAnswer3',
  'cv',
];

/** Error-summary order: required fields first, then the optional section top to bottom. */
export const ALL_FORM_FIELDS: readonly FormField[] = [
  ...JOIN_FIELDS,
  'description',
  'role',
  'workplace',
  'avatarId',
  'photo',
  'introText',
  'introAnswer1',
  'introAnswer2',
  'introAnswer3',
  'cv',
];

export const OPTIONAL_MAX_LENGTH: Readonly<Record<OptionalField, number>> = {
  description: 120,
  role: 100,
  workplace: 100,
  introText: 1000,
  introAnswer1: 1000,
  introAnswer2: 1000,
  introAnswer3: 1000,
  cv: 20000,
};

/** Optional fields: empty (after trim) is fine and is not sent; only the length is checked. */
export function validateOptionalField(field: OptionalField, value: string): 'tooLong' | null {
  return value.length > OPTIONAL_MAX_LENGTH[field] ? 'tooLong' : null;
}
