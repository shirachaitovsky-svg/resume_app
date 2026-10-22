import { HttpErrorResponse } from '@angular/common/http';
import { STRINGS } from '../strings.he';

export type AppErrorKind =
  | 'validation'
  | 'unauthorized'
  | 'forbidden'
  | 'notFound'
  | 'conflict'
  | 'tooMany'
  | 'server'
  | 'network';

export type FieldErrors = Readonly<Record<string, readonly string[]>>;

/** Normalized API failure. Never carries the raw response body. */
export class AppError {
  constructor(
    readonly kind: AppErrorKind,
    readonly status: number,
    readonly message: string,
    readonly code: string | null = null,
    readonly fieldErrors: FieldErrors = {},
    readonly retryAfterSeconds: number | null = null,
  ) {}
}

const MESSAGES: Record<AppErrorKind, string> = {
  validation: STRINGS.errors.validation,
  unauthorized: STRINGS.errors.unauthorized,
  forbidden: STRINGS.errors.forbidden,
  notFound: STRINGS.errors.notFound,
  conflict: STRINGS.errors.conflict,
  tooMany: STRINGS.errors.tooMany,
  server: STRINGS.errors.server,
  network: STRINGS.errors.network,
};

function kindFor(status: number): AppErrorKind {
  switch (status) {
    case 0:
      return 'network';
    case 400:
      return 'validation';
    case 401:
      return 'unauthorized';
    case 403:
      return 'forbidden';
    case 404:
      return 'notFound';
    case 409:
      return 'conflict';
    case 429:
      return 'tooMany';
    default:
      return 'server';
  }
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

function parseFieldErrors(body: Record<string, unknown> | null): FieldErrors {
  const errors = asRecord(body?.['errors']);
  if (!errors) return {};
  const result: Record<string, readonly string[]> = {};
  for (const [field, value] of Object.entries(errors)) {
    const list = (Array.isArray(value) ? value : [value]).filter(
      (item): item is string => typeof item === 'string',
    );
    if (list.length > 0) result[field] = list;
  }
  return result;
}

function parseCode(body: Record<string, unknown> | null): string | null {
  const code = asRecord(body?.['extensions'])?.['code'] ?? body?.['code'];
  return typeof code === 'string' ? code : null;
}

function parseRetryAfter(error: HttpErrorResponse): number | null {
  const raw = error.headers?.get('Retry-After');
  if (raw === null || raw === undefined || raw.trim() === '') return null;
  const seconds = Number(raw);
  return Number.isFinite(seconds) && seconds >= 0 ? seconds : null;
}

/** True for the problem+json shape of a validation failure (has field errors). */
export function hasFieldErrors(error: unknown): boolean {
  return error instanceof HttpErrorResponse && Object.keys(parseFieldErrors(asRecord(error.error))).length > 0;
}

/** Maps anything thrown by the HTTP stack (including timeouts) to an AppError. */
export function toAppError(error: unknown): AppError {
  if (error instanceof AppError) return error;
  if (error instanceof HttpErrorResponse) {
    const kind = kindFor(error.status);
    const body = asRecord(error.error);
    return new AppError(
      kind,
      error.status,
      MESSAGES[kind],
      parseCode(body),
      kind === 'validation' ? parseFieldErrors(body) : {},
      kind === 'tooMany' ? parseRetryAfter(error) : null,
    );
  }
  return new AppError('network', 0, MESSAGES.network);
}
