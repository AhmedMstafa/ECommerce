import { clearToken, getToken } from './auth';

const configuredBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5246';
export const API_BASE_URL = configuredBaseUrl.replace(/\/+$/, '');

export class ApiError extends Error {
  readonly status: number;
  readonly code?: string;
  readonly retryAfterSeconds?: number;

  constructor(
    status: number,
    message: string,
    code?: string,
    retryAfterSeconds?: number,
  ) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.retryAfterSeconds = retryAfterSeconds;
  }
}

interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE';
  body?: unknown;
  headers?: Record<string, string>;
  /** Set false only for anonymous endpoints such as login. */
  auth?: boolean;
  signal?: AbortSignal;
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value !== null && typeof value === 'object' ? (value as Record<string, unknown>) : null;
}

function errorMessage(value: unknown, fallback: string): string {
  const record = asRecord(value);
  if (!record) return fallback;
  const detail = record.detail ?? record.message ?? record.title;
  return typeof detail === 'string' && detail.length > 0 ? detail : fallback;
}

/**
 * Typed fetch wrapper for the real ApiResponse<T> envelope.
 * Throws ApiError with the HTTP status; ProblemDetails from the API are preserved.
 */
export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const headers: Record<string, string> = {
    Accept: 'application/json',
    ...options.headers,
  };

  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }

  if (options.auth !== false) {
    const token = getToken();
    if (token) {
      headers.Authorization = `Bearer ${token}`;
    }
  }

  let response: Response;
  try {
    response = await fetch(`${API_BASE_URL}${path}`, {
      method: options.method ?? 'GET',
      headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      credentials: 'omit',
      signal: options.signal,
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') throw error;
    throw new ApiError(0, 'Cannot reach the API. Check VITE_API_BASE_URL and CORS.');
  }

  const text = await response.text();
  let payload: unknown;
  if (text.length > 0) {
    try {
      payload = JSON.parse(text) as unknown;
    } catch {
      payload = undefined;
    }
  }

  if (!response.ok) {
    if (response.status === 401) clearToken();

    const retryAfterHeader = response.headers.get('Retry-After');
    const parsedRetryAfter = retryAfterHeader === null ? NaN : Number(retryAfterHeader);
    const record = asRecord(payload);
    const code = record && typeof record.code === 'string' ? record.code : undefined;

    throw new ApiError(
      response.status,
      errorMessage(payload, response.statusText || 'Request failed.'),
      code,
      Number.isFinite(parsedRetryAfter) ? parsedRetryAfter : undefined,
    );
  }

  const envelope = asRecord(payload);
  if (envelope && 'success' in envelope) {
    if (envelope.success !== true) {
      throw new ApiError(response.status, errorMessage(payload, 'API request failed.'));
    }
    return envelope.data as T;
  }

  return payload as T;
}

export function describeError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 401) return 'Your session has expired. Please sign in again.';
    if (error.status === 429) {
      return error.retryAfterSeconds
        ? `Too many requests. Try again in ${error.retryAfterSeconds} seconds.`
        : 'Too many requests. Please wait before trying again.';
    }
    if (error.status === 402) return `Payment was rejected: ${error.message}`;
    if (error.status === 0) return error.message;
    return error.message;
  }
  if (error instanceof Error) return error.message;
  return 'Something went wrong. Please try again.';
}

export function isUnauthorized(error: unknown): boolean {
  return error instanceof ApiError && error.status === 401;
}
