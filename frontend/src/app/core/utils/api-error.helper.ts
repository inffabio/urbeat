/**
 * Extracts the usable message from the API's ProblemDetails payload.
 * Falls back to a caller-supplied message instead of leaking raw objects.
 */
export function extractApiErrorMessage(error: unknown, fallback: string): string {
  const payload = (error as { error?: unknown } | null)?.error;
  if (typeof payload === 'string' && payload.trim()) {
    return payload.trim();
  }

  if (payload && typeof payload === 'object') {
    const record = payload as Record<string, unknown>;
    for (const key of ['detail', 'error', 'message', 'title']) {
      const value = record[key];
      if (typeof value === 'string' && value.trim()) return value.trim();
    }

    const errors = record['errors'];
    if (errors && typeof errors === 'object') {
      const messages = Object.values(errors as Record<string, unknown>)
        .flatMap((value) => (Array.isArray(value) ? value : [value]))
        .filter((value): value is string => typeof value === 'string' && !!value.trim());
      if (messages.length) return messages.join(' ');
    }
  }

  const message = (error as { message?: unknown } | null)?.message;
  if (typeof message === 'string' && message.trim()) return message.trim();

  return fallback;
}
