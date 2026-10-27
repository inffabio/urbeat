import {
  formatBrazilianPhone,
  isValidBrazilianMobile,
  normalizeBrazilianMobile,
  onlyPhoneDigits,
  phoneCaretPosition,
} from './brazilian-phone.utils';

describe('brazilian-phone.utils', () => {
  it('formats an 11-digit mobile with the mask (##) #####-####', () => {
    expect(formatBrazilianPhone('11999998888')).toBe('(11) 99999-8888');
    expect(formatBrazilianPhone('(11) 99999-8888')).toBe('(11) 99999-8888');
  });

  it('keeps legacy 10-digit phones in the landline layout', () => {
    expect(formatBrazilianPhone('1133334444')).toBe('(11) 3333-4444');
    expect(formatBrazilianPhone('(11) 3333-4444')).toBe('(11) 3333-4444');
  });

  it('formats legacy records that still carry the +55 country code', () => {
    expect(formatBrazilianPhone('+55 (11) 99999-8888')).toBe('(11) 99999-8888');
    expect(formatBrazilianPhone('551133334444')).toBe('(11) 3333-4444');
  });

  it('formats partial input while the user types', () => {
    expect(formatBrazilianPhone('')).toBe('');
    expect(formatBrazilianPhone('11')).toBe('(11');
    expect(formatBrazilianPhone('119')).toBe('(11) 9');
    expect(formatBrazilianPhone('1199999')).toBe('(11) 99999-');
    expect(formatBrazilianPhone('119999988')).toBe('(11) 99999-88');
  });

  it('normalizes masked values to digits for the payload', () => {
    expect(onlyPhoneDigits('(11) 99999-8888')).toBe('11999998888');
    expect(normalizeBrazilianMobile('(11) 99999-8888')).toBe('11999998888');
  });

  it('validates DDD plus 9 mobile digits', () => {
    expect(isValidBrazilianMobile('(11) 99999-8888')).toBe(true);
    expect(isValidBrazilianMobile('11999998888')).toBe(true);

    expect(isValidBrazilianMobile('')).toBe(false);
    expect(isValidBrazilianMobile('(11) 3333-4444')).toBe(false);
    expect(isValidBrazilianMobile('(11) 88888-7777')).toBe(false);
    expect(isValidBrazilianMobile('(00) 99999-8888')).toBe(false);
    expect(isValidBrazilianMobile('(10) 99999-8888')).toBe(false);
    expect(isValidBrazilianMobile('(11) 99999-8888x')).toBe(false);
  });

  it('keeps the caret after the typed digits', () => {
    expect(phoneCaretPosition('(11) 99999-8888', 4)).toBe(7);
    expect(phoneCaretPosition('(11) 99999-8888', 11)).toBe(15);
    expect(phoneCaretPosition('', 0)).toBe(0);
  });
});
