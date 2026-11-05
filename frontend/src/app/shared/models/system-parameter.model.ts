export type SystemParameterType = 'String' | 'Int32' | 'Decimal' | 'Boolean' | 'Json';

export type SystemParameterDisplay =
  | 'text'
  | 'multiline'
  | 'integer'
  | 'decimal'
  | 'currency'
  | 'percentage'
  | 'url'
  | 'email'
  | 'cron'
  | 'boolean'
  | 'json'
  | 'secret';

export interface SystemParameter {
  id: string;
  key: string;
  value: string;
  type: SystemParameterType;
  group: string | null;
  description: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  maxLength: number;
  display: SystemParameterDisplay;
  unit: string | null;
  minValue: number | null;
  maxValue: number | null;
  isReadOnly: boolean;
  requiresConfirmation: boolean;
  isSecret: boolean;
}

export interface UpdateSystemParameterRequest {
  value: string;
  confirmRestrictedChange: boolean;
}
