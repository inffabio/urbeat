import {
  SystemParameter,
  SystemParameterDisplay,
} from '../../shared/models/system-parameter.model';
import { formatSaoPauloDateTime } from './sao-paulo-date.helper';

export type SystemParameterControl =
  | 'text'
  | 'textarea'
  | 'number'
  | 'decimal'
  | 'url'
  | 'email'
  | 'checkbox'
  | 'readonly';

export interface SystemParameterGroup {
  group: string;
  label: string;
  parameters: SystemParameter[];
}

const GROUP_LABELS: Record<string, string> = {
  Business: 'Regras de negócio',
  Cookie: 'Cookies',
  Cors: 'CORS',
  Email: 'E-mail',
  Hangfire: 'Jobs e rotinas',
  Http: 'HTTP e resiliência',
  Payment: 'Pagamentos',
  Security: 'Segurança',
  Subscription: 'Assinaturas',
  Upload: 'Uploads',
  Url: 'URLs e integrações',
};

export function groupSystemParameters(parameters: SystemParameter[]): SystemParameterGroup[] {
  const groups = new Map<string, SystemParameter[]>();

  for (const parameter of parameters) {
    const group = parameter.group?.trim() || 'Outros';
    const current = groups.get(group);
    if (current) current.push(parameter);
    else groups.set(group, [parameter]);
  }

  return Array.from(groups.entries()).map(([group, items]) => ({
    group,
    label: GROUP_LABELS[group] ?? group,
    parameters: items,
  }));
}

export function parameterControl(param: SystemParameter): SystemParameterControl {
  if (param.isReadOnly) return 'readonly';

  switch (param.display) {
    case 'boolean':
      return 'checkbox';
    case 'integer':
      return 'number';
    case 'decimal':
    case 'currency':
    case 'percentage':
      return 'decimal';
    case 'multiline':
    case 'json':
      return 'textarea';
    case 'url':
      return 'url';
    case 'email':
      return 'email';
    case 'text':
      return param.maxLength > 200 ? 'textarea' : 'text';
    default:
      return 'text';
  }
}

export function parameterInputStep(param: SystemParameter): string | null {
  if (param.display === 'integer') return '1';
  if (
    param.display === 'decimal' ||
    param.display === 'currency' ||
    param.display === 'percentage'
  ) {
    return '0.01';
  }
  return null;
}

export function parameterControlId(key: string): string {
  return `parameter-${key.replace(/[^a-zA-Z0-9_-]/g, '-')}`;
}

/** Formats a canonical decimal string as pt-BR, or null when it is not numeric. */
export function formatPtBrDecimal(value: string): string | null {
  const parsed = Number(value);
  if (!value.trim() || !Number.isFinite(parsed)) return null;

  return parsed.toLocaleString('pt-BR', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
}

/** Human-readable hint shown below the canonical value; never sent to the API. */
export function formatParameterHint(
  param: SystemParameter,
  value: string = param.value,
): string | null {
  const trimmed = value.trim();
  if (!trimmed) return null;

  switch (param.display) {
    case 'currency': {
      const formatted = formatPtBrDecimal(trimmed);
      return formatted ? `R$ ${formatted}` : null;
    }
    case 'percentage': {
      const formatted = formatPtBrDecimal(trimmed);
      return formatted ? `${formatted}%` : null;
    }
    case 'decimal': {
      return formatPtBrDecimal(trimmed);
    }
    case 'integer': {
      const parsed = Number(trimmed);
      if (!Number.isFinite(parsed)) return null;
      return param.unit ? `${parsed} ${param.unit}` : `${parsed}`;
    }
    case 'cron':
      return 'Expressão cron (5 ou 6 campos)';
    case 'boolean':
    case 'secret':
      return null;
    default:
      return null;
  }
}

export function parameterDisplayLabel(param: SystemParameter): string {
  const labels: Record<SystemParameterDisplay, string> = {
    text: 'texto',
    multiline: 'texto longo',
    integer: 'inteiro',
    decimal: 'decimal',
    currency: 'moeda',
    percentage: 'percentual',
    url: 'URL',
    email: 'e-mail',
    cron: 'cron',
    boolean: 'booleano',
    json: 'lista',
    secret: 'segredo',
  };
  return labels[param.display] ?? param.type;
}

export function formatParameterUpdatedAt(param: SystemParameter): string {
  if (!param.updatedAtUtc) return 'Nunca alterado';
  const formatted = formatSaoPauloDateTime(param.updatedAtUtc);
  return formatted ? `Atualizado em ${formatted}` : 'Nunca alterado';
}

export function parameterRangeHint(param: SystemParameter): string | null {
  const parts: string[] = [];
  if (param.minValue !== null) parts.push(`mín. ${formatBound(param.minValue)}`);
  if (param.maxValue !== null) parts.push(`máx. ${formatBound(param.maxValue)}`);
  if (param.maxLength > 0 && (param.type === 'String' || param.type === 'Json')) {
    parts.push(`até ${param.maxLength} caracteres`);
  }
  return parts.length ? parts.join(' · ') : null;
}

function formatBound(value: number): string {
  return value.toLocaleString('pt-BR', { maximumFractionDigits: 2 });
}
