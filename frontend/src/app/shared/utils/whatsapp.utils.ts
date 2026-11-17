/**
 * Helpers for the seller → motoboy WhatsApp hand-off.
 *
 * Two browser rules drive this module:
 * 1. `window.open` only survives when it runs inside the user gesture. Opening it after an HTTP
 *    response loses the activation and the browser silently blocks the tab, so async flows must
 *    reserve the tab during the click and navigate it later.
 * 2. Passing the `noopener` feature makes `window.open` return `null` even when the tab actually
 *    opened, which is indistinguishable from a blocked popup. The feature is therefore never
 *    passed here; the opener reference is severed manually instead.
 */

const RESERVED_WINDOW_URL = 'about:blank';

/** Digits only, ignoring the visual mask. */
function onlyDigits(value: string | null | undefined): string {
  return (value ?? '').replace(/\D/g, '');
}

/**
 * WhatsApp dialable number for a Brazilian mobile. A value up to 11 digits is a local
 * DDD + subscriber number and receives the +55 prefix; a longer value already carries the
 * country code. A local DDD 55 (Rio Grande do Sul) number is never mistaken for the prefix.
 */
export function whatsAppNumber(value: string | null | undefined): string | null {
  const digits = onlyDigits(value);
  if (!digits) return null;
  if (digits.length <= 11) return `55${digits}`;
  return digits.startsWith('55') ? digits : `55${digits}`;
}

/** Absolute http(s) URL for a server-provided share link; unsafe/empty values are rejected. */
export function toAbsoluteHttpUrl(value: string | null | undefined): string | null {
  if (!value) return null;
  try {
    const url = new URL(value, document.baseURI);
    return url.protocol === 'http:' || url.protocol === 'https:' ? url.toString() : null;
  } catch {
    return null;
  }
}

/** Opens a URL in a new tab and severs `opener`, without the `noopener` return-value trap. */
export function openExternalWindow(url: string): boolean {
  if (typeof window === 'undefined') return false;
  const safeUrl = toAbsoluteHttpUrl(url);
  if (!safeUrl) return false;
  const opened = window.open(safeUrl, '_blank');
  if (!opened) return false;
  severOpener(opened);
  return true;
}

/** Reserves a tab while the click is being handled, so an async response can navigate it. */
export function reserveExternalWindow(): Window | null {
  if (typeof window === 'undefined') return null;
  const reserved = window.open(RESERVED_WINDOW_URL, '_blank');
  if (!reserved) return null;
  severOpener(reserved);
  return reserved;
}

/** Navigates a previously reserved tab; false when the browser discarded or blocked it. */
export function navigateExternalWindow(win: Window | null, url: string): boolean {
  if (!win || win.closed) return false;
  const safeUrl = toAbsoluteHttpUrl(url);
  if (!safeUrl) return false;
  try {
    win.location.href = safeUrl;
    return true;
  } catch {
    return false;
  }
}

/** Best-effort close of a tab that will not be used (blocked/failed flow). */
export function closeExternalWindow(win: Window | null): void {
  if (!win || win.closed) return;
  try {
    win.close();
  } catch {
    // A tab that already navigated away may refuse to close; nothing else to do.
  }
}

function severOpener(win: Window): void {
  try {
    win.opener = null;
  } catch {
    // Cross-origin windows cannot be inspected; they already lack a usable opener reference.
  }
}
