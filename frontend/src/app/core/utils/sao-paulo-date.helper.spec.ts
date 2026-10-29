import { formatSaoPauloDate, formatSaoPauloDateTime, formatSaoPauloTime, saoPauloPeriodRange } from './sao-paulo-date.helper';
import {
  analyticsDaysInMonth,
  analyticsMaxWeekBlock,
  analyticsWeekBlockEndIso,
  analyticsWeekBlockForDay,
  analyticsWeekBlockLabel,
  analyticsWeekBlockStartIso,
  formatSaoPauloIsoDate,
  saoPauloTodayIso,
} from './sao-paulo-date.helper';

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

  it('returns the local Sao Paulo ISO date', () => {
    expect(saoPauloTodayIso(new Date('2026-10-29T02:30:00.000Z'))).toBe('2026-10-28');
    expect(saoPauloTodayIso(new Date('2026-10-29T12:00:00.000Z'))).toBe('2026-10-29');
  });

  it('uses the current instant when no date is provided', () => {
    jest.useFakeTimers().setSystemTime(new Date('2026-10-29T02:30:00.000Z'));

    expect(saoPauloTodayIso()).toBe('2026-10-28');

    jest.useRealTimers();
  });

  it('formats a civil ISO date without shifting the day', () => {
    expect(formatSaoPauloIsoDate('2026-10-29')).toBe('29/10/2026');
    expect(formatSaoPauloIsoDate('invalid')).toBe('');
  });

  it('calculates days in month and maximum week block', () => {
    expect(analyticsDaysInMonth(2026, 2)).toBe(28);
    expect(analyticsDaysInMonth(2028, 2)).toBe(29);
    expect(analyticsMaxWeekBlock(2026, 2)).toBe(4);
    expect(analyticsMaxWeekBlock(2028, 2)).toBe(5);
    expect(analyticsMaxWeekBlock(2026, 10)).toBe(5);
  });

  it('calculates the week block that contains a day', () => {
    expect(analyticsWeekBlockForDay(2026, 10, 29)).toBe(5);
    expect(analyticsWeekBlockForDay(2026, 10, 7)).toBe(1);
    expect(analyticsWeekBlockForDay(2026, 2, 28)).toBe(4);
  });

  it('calculates week block start and end ISO dates', () => {
    expect(analyticsWeekBlockStartIso('2026-10-29', 5)).toBe('2026-10-29');
    expect(analyticsWeekBlockEndIso('2026-10-29', 5)).toBe('2026-10-31');
    expect(analyticsWeekBlockStartIso('2026-10-29', 1)).toBe('2026-10-01');
    expect(analyticsWeekBlockEndIso('2026-10-29', 1)).toBe('2026-10-07');
  });

  it('labels a week block with its day range and month', () => {
    expect(analyticsWeekBlockLabel(5, 2026, 10)).toContain('29');
    expect(analyticsWeekBlockLabel(5, 2026, 10)).toContain('31');
    expect(analyticsWeekBlockLabel(5, 2026, 10)).toContain('outubro');
  });
});
