import {
  SystemParameter,
  SystemParameterDisplay,
} from '../../shared/models/system-parameter.model';
import {
  formatParameterHint,
  formatPtBrDecimal,
  groupSystemParameters,
  parameterControl,
  parameterControlId,
  parameterDisplayLabel,
  parameterInputStep,
  parameterRangeHint,
} from './system-parameter-format';

function parameter(overrides: Partial<SystemParameter> = {}): SystemParameter {
  return {
    id: 'id',
    key: 'Store.DefaultDeliveryFee',
    value: '19.90',
    type: 'Decimal',
    group: 'Business',
    description: null,
    createdAtUtc: '2026-01-01T00:00:00Z',
    updatedAtUtc: null,
    maxLength: 18,
    display: 'currency',
    unit: null,
    minValue: 0,
    maxValue: 1000000,
    isReadOnly: false,
    requiresConfirmation: false,
    isSecret: false,
    ...overrides,
  };
}

describe('system parameter formatting', () => {
  it('formats canonical decimals as pt-BR without changing the stored value', () => {
    expect(formatPtBrDecimal('19.90')).toBe('19,90');
    expect(formatPtBrDecimal('1000')).toBe('1.000,00');
    expect(formatPtBrDecimal('R$ 19,90')).toBeNull();
    expect(formatPtBrDecimal('19,90')).toBeNull();
    expect(formatPtBrDecimal('')).toBeNull();
  });

  it('builds pt-BR hints for currency, percentage and duration', () => {
    expect(formatParameterHint(parameter())).toBe('R$ 19,90');
    expect(
      formatParameterHint(
        parameter({ display: 'percentage', value: '12.5', maxLength: 18 }),
      ),
    ).toBe('12,50%');
    expect(
      formatParameterHint(
        parameter({
          display: 'integer',
          type: 'Int32',
          value: '15',
          unit: 'min',
          maxLength: 11,
        }),
      ),
    ).toBe('15 min');
  });

  it('maps each parameter type/display to the correct control', () => {
    expect(parameterControl(parameter({ display: 'text', type: 'String' }))).toBe('text');
    expect(
      parameterControl(parameter({ display: 'multiline', type: 'String', maxLength: 500 })),
    ).toBe('textarea');
    expect(
      parameterControl(parameter({ display: 'text', type: 'String', maxLength: 500 })),
    ).toBe('textarea');
    expect(parameterControl(parameter({ display: 'integer', type: 'Int32' }))).toBe('number');
    expect(parameterControl(parameter({ display: 'decimal', type: 'Decimal' }))).toBe(
      'decimal',
    );
    expect(parameterControl(parameter({ display: 'boolean', type: 'Boolean' }))).toBe(
      'checkbox',
    );
    expect(parameterControl(parameter({ display: 'json', type: 'Json' }))).toBe('textarea');
    expect(parameterControl(parameter({ display: 'email', type: 'String' }))).toBe('email');
    expect(parameterControl(parameter({ display: 'url', type: 'String' }))).toBe('url');
    expect(
      parameterControl(parameter({ display: 'secret', type: 'String', isReadOnly: true })),
    ).toBe('readonly');
  });

  it('uses the correct step for numeric inputs', () => {
    expect(parameterInputStep(parameter({ display: 'integer' }))).toBe('1');
    expect(parameterInputStep(parameter({ display: 'decimal' }))).toBe('0.01');
    expect(parameterInputStep(parameter({ display: 'currency' }))).toBe('0.01');
    expect(parameterInputStep(parameter({ display: 'text' }))).toBeNull();
  });

  it('groups parameters preserving backend order and translating known groups', () => {
    const groups = groupSystemParameters([
      parameter({ key: 'A', group: 'Security' }),
      parameter({ key: 'B', group: 'Business' }),
      parameter({ key: 'C', group: 'Business' }),
      parameter({ key: 'D', group: null }),
    ]);

    expect(groups.map((group) => group.group)).toEqual([
      'Security',
      'Business',
      'Outros',
    ]);
    expect(groups[0].label).toBe('Segurança');
    expect(groups[1].parameters.map((item) => item.key)).toEqual(['B', 'C']);
  });

  it('describes ranges and display labels for the admin UI', () => {
    expect(parameterRangeHint(parameter())).toBe('mín. 0 · máx. 1.000.000');
    expect(parameterDisplayLabel(parameter())).toBe('moeda');
    expect(parameterDisplayLabel(parameter({ display: 'boolean', type: 'Boolean' }))).toBe(
      'booleano',
    );
    expect(parameterControlId('Password.RequireDigit')).toBe(
      'parameter-Password-RequireDigit',
    );
  });

  it('keeps every supported display mapped to a label', () => {
    const displays: SystemParameterDisplay[] = [
      'text',
      'multiline',
      'integer',
      'decimal',
      'currency',
      'percentage',
      'url',
      'email',
      'cron',
      'boolean',
      'json',
      'secret',
    ];

    for (const display of displays) {
      expect(parameterDisplayLabel(parameter({ display }))).toBeTruthy();
    }
  });
});
