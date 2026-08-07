export type DashboardPeriod = 'all' | 'today' | 'week' | 'month';

const SAO_PAULO_TIME_ZONE = 'America/Sao_Paulo';

export function formatSaoPauloTime(value: string | Date): string {
  return new Date(value).toLocaleTimeString('pt-BR', {
    hour: '2-digit',
    minute: '2-digit',
    timeZone: SAO_PAULO_TIME_ZONE,
  });
}

export function formatSaoPauloDate(value: string | Date): string {
  return new Date(value).toLocaleDateString('pt-BR', {
    timeZone: SAO_PAULO_TIME_ZONE,
  });
}

export function formatSaoPauloDateTime(value: string | Date): string {
  return new Date(value).toLocaleString('pt-BR', {
    timeZone: SAO_PAULO_TIME_ZONE,
  });
}

export function saoPauloPeriodRange(period: DashboardPeriod, now = new Date()): { startDateUtc?: string; endDateUtc?: string } {
  if (period === 'all') return {};

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
