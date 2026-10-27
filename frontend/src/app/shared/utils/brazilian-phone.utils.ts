const MOBILE_DIGITS = 11;

/** Keeps only the digits typed by the user, regardless of the visual mask. */
export function onlyPhoneDigits(value: string | null | undefined): string {
  return (value ?? '').replace(/\D/g, '');
}

/**
 * Formats a Brazilian phone for display. Mobile numbers (11 digits) use `(##) #####-####`;
 * legacy 10-digit records keep the landline layout `(##) ####-####` instead of being
 * reinterpreted or truncated.
 */
export function formatBrazilianPhone(value: string | null | undefined): string {
  const rawDigits = onlyPhoneDigits(value);
  // Legacy records may contain the country code; formatting the local part avoids showing
  // the wrong DDD without changing what was persisted.
  const localDigits = rawDigits.length > MOBILE_DIGITS && rawDigits.startsWith('55')
    ? rawDigits.slice(2)
    : rawDigits;
  const digits = localDigits.slice(0, MOBILE_DIGITS);
  if (!digits) return '';
  if (digits.length <= 2) return `(${digits}`;

  const prefix = `(${digits.slice(0, 2)}) `;
  const subscriber = digits.slice(2);

  if (digits.length <= 6) return `${prefix}${subscriber}`;
  if (digits.length === 10) {
    return `${prefix}${subscriber.slice(0, 4)}-${subscriber.slice(4)}`;
  }

  return `${prefix}${subscriber.slice(0, 5)}-${subscriber.slice(5)}`;
}

/** Digits-only payload stored by the backend. */
export function normalizeBrazilianMobile(value: string | null | undefined): string {
  return onlyPhoneDigits(value);
}

/** A Brazilian mobile number has a valid DDD and 9 subscriber digits starting with 9. */
export function isValidBrazilianMobile(value: string | null | undefined): boolean {
  if (!value || !/^[0-9\s()+-]+$/.test(value)) return false;

  const digits = onlyPhoneDigits(value);
  if (digits.length !== MOBILE_DIGITS) return false;

  const ddd = Number(digits.slice(0, 2));
  return ddd >= 11 && ddd <= 99 && digits[2] === '9';
}

/** Places the caret after the same number of digits the user had typed. */
export function phoneCaretPosition(formatted: string, digitCount: number): number {
  if (digitCount <= 0) return 0;

  let seen = 0;
  for (let index = 0; index < formatted.length; index += 1) {
    if (/\d/.test(formatted[index])) {
      seen += 1;
      if (seen === digitCount) return index + 1;
    }
  }

  return formatted.length;
}
