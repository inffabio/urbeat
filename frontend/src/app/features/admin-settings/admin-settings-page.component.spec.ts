import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { SystemParameterService } from '../../core/services/system-parameter.service';
import { SystemParameter } from '../../shared/models/system-parameter.model';
import { AdminSettingsPageComponent } from './admin-settings-page.component';

function parameter(overrides: Partial<SystemParameter> = {}): SystemParameter {
  return {
    id: 'id',
    key: 'Store.DefaultDeliveryFee',
    value: '19.90',
    type: 'Decimal',
    group: 'Business',
    description: 'Taxa de entrega padrão',
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

/** Business-only payload, mirroring the server's business catalog. */
const PARAMETERS: SystemParameter[] = [
  parameter(),
  parameter({
    key: 'Order.CodePrefix',
    value: 'URB-',
    type: 'String',
    display: 'text',
    maxLength: 20,
    minValue: null,
    maxValue: null,
    description: 'Prefixo do código do pedido',
  }),
  parameter({
    key: 'Customer.MaxAddresses',
    value: '3',
    type: 'Int32',
    display: 'integer',
    unit: 'endereços',
    maxLength: 11,
    minValue: 1,
    maxValue: 50,
    description: 'Máximo de endereços por cliente',
  }),
  parameter({
    key: 'Subscription.DueSoonDays',
    value: '3',
    type: 'Int32',
    display: 'integer',
    unit: 'dias',
    maxLength: 11,
    minValue: 0,
    maxValue: 365,
    description: 'Dias antes do vencimento para notificar',
  }),
  parameter({
    key: 'Subscription.Description',
    value: 'Assinatura urbeat',
    type: 'String',
    display: 'text',
    maxLength: 200,
    minValue: null,
    maxValue: null,
    description: 'Descrição da assinatura',
  }),
];

describe('AdminSettingsPageComponent', () => {
  let fixture: ComponentFixture<AdminSettingsPageComponent>;
  let serviceMock: {
    list: jest.Mock;
    update: jest.Mock;
  };

  beforeEach(async () => {
    serviceMock = {
      list: jest.fn(() => of(PARAMETERS)),
      update: jest.fn((key: string, value: string) =>
        of({ ...PARAMETERS.find((item) => item.key === key)!, value }),
      ),
    };

    await TestBed.configureTestingModule({
      imports: [AdminSettingsPageComponent],
      providers: [{ provide: SystemParameterService, useValue: serviceMock }],
    }).compileComponents();

    fixture = TestBed.createComponent(AdminSettingsPageComponent);
    fixture.detectChanges();
  });

  function rowFor(key: string): HTMLElement {
    const rows = Array.from(
      fixture.nativeElement.querySelectorAll('.admin-param'),
    ) as HTMLElement[];
    const row = rows.find((item) => item.textContent?.includes(key));
    if (!row) throw new Error(`Row not found for ${key}`);
    return row;
  }

  async function changeInput(key: string, value: string): Promise<void> {
    const input = rowFor(key).querySelector('input') as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    await fixture.whenStable();
  }

  async function clickSave(key: string): Promise<void> {
    (rowFor(key).querySelector('button.admin-primary') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('renders only the business parameter group and a control per type', () => {
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Regras de negócio');
    expect(text).not.toContain('Segurança');
    expect(text).not.toContain('E-mail');
    expect(text).not.toContain('Cookies');
    expect(text).not.toContain('Uploads');
    expect(text).not.toContain('URLs e integrações');

    const currency = rowFor('Store.DefaultDeliveryFee').querySelector(
      'input',
    ) as HTMLInputElement;
    expect(currency.type).toBe('number');
    expect(currency.step).toBe('0.01');
    expect(currency.value).toBe('19.90');

    const integer = rowFor('Customer.MaxAddresses').querySelector(
      'input',
    ) as HTMLInputElement;
    expect(integer.type).toBe('number');
    expect(integer.step).toBe('1');

    const stringInput = rowFor('Order.CodePrefix').querySelector(
      'input',
    ) as HTMLInputElement;
    expect(stringInput.type).toBe('text');
    expect(stringInput.getAttribute('maxlength')).toBe('20');
  });

  it('shows the pt-BR hint while keeping the canonical value in the input', () => {
    const row = rowFor('Store.DefaultDeliveryFee');

    expect((row.querySelector('input') as HTMLInputElement).value).toBe('19.90');
    expect(row.textContent).toContain('R$ 19,90');
  });

  it('saves the canonical value and shows success feedback', async () => {
    await changeInput('Store.DefaultDeliveryFee', '25.50');
    await clickSave('Store.DefaultDeliveryFee');

    expect(serviceMock.update).toHaveBeenCalledWith(
      'Store.DefaultDeliveryFee',
      '25.50',
      false,
    );
    expect(fixture.nativeElement.textContent).toContain(
      'Store.DefaultDeliveryFee atualizado.',
    );
    expect(
      (rowFor('Store.DefaultDeliveryFee').querySelector('input') as HTMLInputElement).value,
    ).toBe('25.50');
  });

  it('surfaces the server validation message', async () => {
    serviceMock.update.mockReturnValue(
      throwError(() => ({
        error: {
          detail: 'Customer.MaxAddresses: O valor deve estar entre 1 e 50.',
        },
      })),
    );

    await changeInput('Customer.MaxAddresses', '0');
    const parameter = fixture.componentInstance
      .parameters()
      .find((item) => item.key === 'Customer.MaxAddresses')!;
    await fixture.componentInstance.save(parameter);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(
      'O valor deve estar entre 1 e 50.',
    );
  });

  it('shows the load error with a retry action', async () => {
    serviceMock.list.mockReturnValue(
      throwError(() => ({ error: { detail: 'Falha de conexão.' } })),
    );

    fixture.componentInstance.load();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(
      'Não foi possível carregar os parâmetros.',
    );
    expect(fixture.nativeElement.textContent).toContain('Falha de conexão.');
  });
});
