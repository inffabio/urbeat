import { formatSaoPauloDate, formatSaoPauloDateTime, formatSaoPauloTime, saoPauloPeriodRange } from './sao-paulo-date.helper';

describe('sao-paulo-date.helper', () => {
  it('formats UTC time using the Sao Paulo timezone', () => {
    expect(formatSaoPauloTime('2026-07-29T03:30:00.000Z')).toBe('00:30');
  });

  it('formats UTC date using the Sao Paulo timezone', () => {
    expect(formatSaoPauloDate('2026-07-29T02:30:00.000Z')).toBe('28/07/2026');
  });

  it('formats UTC date time using the Sao Paulo timezone', () => {
    expect(formatSaoPauloDateTime('2026-07-29T03:30:00.000Z')).toContain('29/07/2026');
    expect(formatSaoPauloDateTime('2026-07-29T03:30:00.000Z')).toContain('00:30');
  });

  it('returns Sao Paulo day boundaries converted to UTC', () => {
    const range = saoPauloPeriodRange('today', new Date('2026-07-29T12:00:00.000Z'));

    expect(range).toEqual({
      startDateUtc: '2026-07-29T03:00:00.000Z',
      endDateUtc: '2026-07-29T12:00:00.000Z',
    });
  });

  it('returns an empty string for invalid date input', () => {
    expect(formatSaoPauloTime('invalid')).toBe('');
    expect(formatSaoPauloDate('not-a-date')).toBe('');
    expect(formatSaoPauloDateTime('')).toBe('');
  });

  it('returns an empty range for an invalid now date', () => {
    expect(saoPauloPeriodRange('today', new Date('invalid'))).toEqual({});
  });
});
