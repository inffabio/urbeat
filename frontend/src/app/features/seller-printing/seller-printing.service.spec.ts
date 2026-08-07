import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { throwError, of } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { OrderService } from '../../core/services/order.service';
import { SellerPrintingService } from './seller-printing.service';

describe('SellerPrintingService', () => {
  let apiServiceMock: { get: jest.Mock; put: jest.Mock };
  let orderServiceMock: { getStoreOrder: jest.Mock };

  beforeEach(() => {
    localStorage.clear();
    apiServiceMock = { get: jest.fn().mockReturnValue(of([])), put: jest.fn().mockReturnValue(of({})) };
    orderServiceMock = { getStoreOrder: jest.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        { provide: ApiService, useValue: apiServiceMock },
        { provide: OrderService, useValue: orderServiceMock },
      ],
    });
  });

  it('loads default config from localStorage fallback', () => {
    const service = TestBed.inject(SellerPrintingService);
    expect(service.config().adapterId).toBe('escpos-bluetooth');
    expect(service.config().copies).toBe(1);
    expect(service.config().footerText).toContain('Obrigado');
  });

  it('saves config to localStorage', () => {
    const service = TestBed.inject(SellerPrintingService);
    service.saveConfig({ ...service.config(), copies: 3, footerText: 'Volte sempre' });

    const raw = localStorage.getItem('urbeat:seller-printing-config');
    expect(raw).toBeTruthy();
    const parsed = JSON.parse(raw!);
    expect(parsed.copies).toBe(3);
    expect(parsed.footerText).toBe('Volte sempre');
  });

  it('normalizes copies to range 1-5', () => {
    const service = TestBed.inject(SellerPrintingService);
    service.saveConfig({ ...service.config(), copies: 10 });
    expect(service.config().copies).toBe(5);
    service.saveConfig({ ...service.config(), copies: 0 });
    expect(service.config().copies).toBe(1);
  });

  it('truncates footer text to 120 chars', () => {
    const service = TestBed.inject(SellerPrintingService);
    const long = 'x'.repeat(200);
    service.saveConfig({ ...service.config(), footerText: long });
    expect(service.config().footerText.length).toBeLessThanOrEqual(120);
  });

  it('registry provides mock adapter as fallback', () => {
    const service = TestBed.inject(SellerPrintingService);
    expect(service.config().adapterId).toBeTruthy();
  });

  it('prefers local-agent for desktop POS-58 setup and keeps autoCut disabled for 58mm', async () => {
    apiServiceMock.get.mockImplementation((url: string) => {
      if (url === '/api/printer-config/store') {
        return of({
          printerPresetId: 'preset-pos58',
          printerName: 'POS-58 Balcao',
          copies: 1,
          autoCut: true,
          autoPrint: true,
          printKitchenCopy: true,
          printCounterCopy: true,
          printCustomerReceipt: false,
          printLogo: false,
          highlightOrderNumber: true,
          footerText: 'Obrigado',
        });
      }

      if (url === '/api/printer-config/presets') {
        return of([
          {
            id: 'preset-pos58',
            name: 'POS-58 Balcao',
            manufacturer: 'Generica',
            connectionType: 'local-agent',
            paperWidth: '58mm',
            commandSet: 'esc-pos',
            adapterId: 'local-agent',
            description: 'Modo desktop automatico via agente local',
            isActive: true,
          },
        ]);
      }

      return of([]);
    });

    const service = TestBed.inject(SellerPrintingService);

    await service.loadPresets();
    await service.loadStoreConfig();

    expect(service.config().connectionType).toBe('local-agent');
    expect(service.config().paperWidth).toBe('58mm');
    expect(service.config().autoCut).toBe(false);
    expect(service.config().adapterId).toBe('local-agent');
  });

  it('falls back to local config when store config is unavailable', async () => {
    apiServiceMock.get.mockImplementation((url: string) => {
      if (url === '/api/printer-config/store') {
        return throwError(() => new Error('offline'));
      }

      return of([]);
    });

    localStorage.setItem('urbeat:seller-printing-config', JSON.stringify({
      connectionType: 'local-agent',
      paperWidth: '58mm',
      autoCut: true,
      printerName: 'POS-58 Loja',
    }));

    const service = TestBed.inject(SellerPrintingService);
    await service.loadStoreConfig();

    expect(service.config().connectionType).toBe('local-agent');
    expect(service.config().autoCut).toBe(false);
  });

  it('prints accepted orders using the current config even when auto print is off', async () => {
    orderServiceMock.getStoreOrder.mockReturnValue(of({
      id: 'order-1',
      code: '123',
      customerName: 'Maria',
      customerPhoneNumber: '11999999999',
      storeId: 'store-1',
      fulfillmentType: 0,
      status: 1,
      paymentMethod: 0,
      subtotal: 30,
      deliveryFee: 0,
      total: 30,
      createdAtUtc: '2026-08-04T03:30:00.000Z',
      items: [],
      history: [],
    }));

    const service = TestBed.inject(SellerPrintingService);
    const printReceiptSpy = jest.spyOn(service, 'printReceipt').mockResolvedValue({
      ok: true,
      mode: 'android-bluetooth',
      message: 'ok',
      printedAtUtc: '2026-08-04T03:35:00.000Z',
    });

    await service.printAcceptedOrder('order-1');

    expect(orderServiceMock.getStoreOrder).toHaveBeenCalledWith('order-1');
    expect(printReceiptSpy).toHaveBeenCalledWith(expect.objectContaining({
      code: '123',
      createdAtUtc: '2026-08-04T03:30:00.000Z',
    }));
  });
});
