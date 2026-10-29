export type DashboardPeriod = 'all' | 'today' | 'week' | 'month';

const SAO_PAULO_TIME_ZONE = 'America/Sao_Paulo';

export function formatSaoPauloTime(value: string | Date): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  return date.toLocaleTimeString('pt-BR', {
    hour: '2-digit',
    minute: '2-digit',
    timeZone: SAO_PAULO_TIME_ZONE,
  });
}

export function formatSaoPauloDate(value: string | Date): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  return date.toLocaleDateString('pt-BR', {
    timeZone: SAO_PAULO_TIME_ZONE,
  });
}

export function formatSaoPauloDateTime(value: string | Date): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  return date.toLocaleString('pt-BR', {
    timeZone: SAO_PAULO_TIME_ZONE,
  });
}

export function saoPauloPeriodRange(period: DashboardPeriod, now = new Date()): { startDateUtc?: string; endDateUtc?: string } {
  if (period === 'all') return {};
  if (Number.isNaN(now.getTime())) return {};

  const today = saoPauloDateParts(now);
  const start = new Date(Date.UTC(today.year, today.month - 1, today.day, 3, 0, 0, 0));
  if (period === 'week') start.setUTCDate(start.getUTCDate() - 6);
  if (period === 'month') start.setUTCDate(start.getUTCDate() - 29);

  return { startDateUtc: start.toISOString(), endDateUtc: now.toISOString() };
}

function saoPauloDateParts(value: Date): { year: number; month: number; day: number } {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: SAO_PAULO_TIME_ZONE,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(value);

  return {
    year: Number(parts.find((part) => part.type === 'year')?.value),
    month: Number(parts.find((part) => part.type === 'month')?.value),
    day: Number(parts.find((part) => part.type === 'day')?.value),
  };
}

/** Local civil date (yyyy-MM-dd) in America/Sao_Paulo, without recalculating per request. */
export function saoPauloTodayIso(now = new Date()): string {
  if (Number.isNaN(now.getTime())) return '';
  const parts = saoPauloDateParts(now);
  return `${parts.year}-${pad2(parts.month)}-${pad2(parts.day)}`;
}

/** Formats a civil yyyy-MM-dd date as dd/MM/yyyy without timezone conversion. */
export function formatSaoPauloIsoDate(isoDate: string): string {
  const parts = parseIsoDate(isoDate);
  if (!parts) return '';
  return `${pad2(parts.day)}/${pad2(parts.month)}/${parts.year}`;
}

export function analyticsDaysInMonth(year: number, month: number): number {
  return new Date(Date.UTC(year, month, 0)).getUTCDate();
}

export function analyticsMaxWeekBlock(year: number, month: number): number {
  return Math.ceil(analyticsDaysInMonth(year, month) / 7);
}

export function analyticsWeekBlockForDay(year: number, month: number, day: number): number {
  const block = Math.floor((day - 1) / 7) + 1;
  return Math.min(Math.max(block, 1), analyticsMaxWeekBlock(year, month));
}

export function analyticsWeekBlockStartIso(anchorIso: string, block: number): string {
  const parts = parseIsoDate(anchorIso);
  if (!parts) return '';
  const startDay = (Math.min(Math.max(block, 1), analyticsMaxWeekBlock(parts.year, parts.month)) - 1) * 7 + 1;
  return `${parts.year}-${pad2(parts.month)}-${pad2(startDay)}`;
}

export function analyticsWeekBlockEndIso(anchorIso: string, block: number): string {
  const parts = parseIsoDate(anchorIso);
  if (!parts) return '';
  const maxBlock = analyticsMaxWeekBlock(parts.year, parts.month);
  const safeBlock = Math.min(Math.max(block, 1), maxBlock);
  const startDay = (safeBlock - 1) * 7 + 1;
  const endDay = Math.min(startDay + 6, analyticsDaysInMonth(parts.year, parts.month));
  return `${parts.year}-${pad2(parts.month)}-${pad2(endDay)}`;
}

export function analyticsWeekBlockLabel(block: number, year: number, month: number): string {
  const maxBlock = analyticsMaxWeekBlock(year, month);
  const safeBlock = Math.min(Math.max(block, 1), maxBlock);
  const startDay = (safeBlock - 1) * 7 + 1;
  const endDay = Math.min(startDay + 6, analyticsDaysInMonth(year, month));
  const monthName = MONTH_NAMES_PT_BR[month - 1] ?? '';
  return `Dias ${startDay} a ${endDay} de ${monthName} de ${year}`;
}

const MONTH_NAMES_PT_BR = [
  'janeiro',
  'fevereiro',
  'março',
  'abril',
  'maio',
  'junho',
  'julho',
  'agosto',
  'setembro',
  'outubro',
  'novembro',
  'dezembro',
];

function parseIsoDate(value: string): { year: number; month: number; day: number } | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) return null;

  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  if (month < 1 || month > 12 || day < 1 || day > analyticsDaysInMonth(year, month)) {
    return null;
  }

  return { year, month, day };
}

function pad2(value: number): string {
  return String(value).padStart(2, '0');
}
