import { apiBase } from '../config';

let accessToken: string | null = null;

/** Stores the Discord access token used to authorize activity API requests. */
export function setAccessToken(token: string | null): void {
  accessToken = token;
}

/** An API failure with the HTTP status and the ProblemDetails detail (when the body carried one). */
export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

/**
 * Authorized JSON request against the activity API. Attaches the bearer token (when set), sets the
 * JSON content-type for bodies, and parses ProblemDetails error bodies into {@link ApiError} so
 * callers can surface the backend's human-readable `detail`/`title` and branch on `status`.
 */
export async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  if (accessToken) {
    headers.set('Authorization', `Bearer ${accessToken}`);
  }
  if (init.body !== undefined && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }

  const response = await fetch(`${apiBase}${path}`, { ...init, headers });

  if (!response.ok) {
    let message = `Request failed (${response.status}).`;
    try {
      const problem = (await response.json()) as { detail?: string; title?: string };
      message = problem.detail ?? problem.title ?? message;
    } catch {
      // Not a ProblemDetails body — keep the generic message.
    }
    throw new ApiError(message, response.status);
  }

  if (response.status === 204) {
    return undefined as T;
  }
  return (await response.json()) as T;
}

/** Narrows an unknown thrown value to a display message, falling back to `fallback`. */
export function toErrorMessage(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}
