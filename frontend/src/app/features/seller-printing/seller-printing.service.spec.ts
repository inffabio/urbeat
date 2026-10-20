import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { throwError, of } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { OrderService } from '../../core/services/order.service';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';
import { SellerPrintingService } from './seller-printing.service';

describe('SellerPrintingService', () => {
  let apiServiceMock: { get: jest.Mock; put: jest.Mock };
  let orderServiceMock: { getStoreOrder: jest.Mock };
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    localStorage.setItem('urbeat:local-agent-token', 'test-local-token');
    apiServiceMock = { get: jest.fn().mockReturnValue(of([])), put: jest.fn().mockReturnValue(of({})) };
    orderServiceMock = { getStoreOrder: jest.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ApiService, useValue: apiServiceMock },
        { provide: OrderService, useValue: orderServiceMock },
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpTesting.verify();
  });

  const flushMicrotasks = async (): Promise<void> => {
    for (let i = 0; i < 6; i += 1) await Promise.resolve();
  };

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

  it('pairs with the local agent before calling protected endpoints', async () => {
    localStorage.removeItem('urbeat:local-agent-token');
    const service = TestBed.inject(SellerPrintingService);
    service.platform.set('windows');

    const refresh = service.refreshLocalAgent();
    httpTesting.expectOne('http://127.0.0.1:43111/health').flush({ status: 'ok', mode: 'local-agent' });
    const pair = httpTesting.expectOne('http://127.0.0.1:43111/pair');
    expect(pair.request.headers.get('X-Urbeat-Pair')).toBe('1');
    pair.flush({ token: 'paired-token' });
    await flushMicrotasks();
    const printers = httpTesting.expectOne('http://127.0.0.1:43111/printers');
    expect(printers.request.headers.get('X-Urbeat-Local-Token')).toBe('paired-token');
    printers.flush({ recommendedProfiles: [], installedPrinters: ['POS-58 USB'] });
    await refresh;

    expect(localStorage.getItem('urbeat:local-agent-token')).toBe('paired-token');
    expect(service.installedPrinters()).toEqual(['POS-58 USB']);
  });

  it('loads installed printers and exposes a platform-filtered catalog from the local agent', async () => {
    const service = TestBed.inject(SellerPrintingService);
    service.platform.set('windows');

    const refresh = service.refreshLocalAgent();
    httpTesting.expectOne('http://127.0.0.1:43111/health').flush({ status: 'ok', mode: 'local-agent' });
    await flushMicrotasks();
    const printers = httpTesting.expectOne('http://127.0.0.1:43111/printers');
    expect(printers.request.headers.get('X-Urbeat-Local-Token')).toBe('test-local-token');
    printers.flush({
      recommendedProfiles: [
        { profileId: 'pos-58', displayName: 'POS-58', paperWidth: '58mm', supportsAutoCut: false, preferredConnection: 'android-bluetooth|wifi' },
      ],
      installedPrinters: ['POS-58 USB'],
    });
    await refresh;

    expect(service.installedPrinters()).toEqual(['POS-58 USB']);
    expect(service.catalogEntries().some((entry) => entry.source === 'installed' && entry.name === 'POS-58 USB')).toBe(true);
  });

  it('excludes installed printers and generic Bluetooth from the catalog on Android', async () => {
    const service = TestBed.inject(SellerPrintingService);
    service.platform.set('android');

    const refresh = service.refreshLocalAgent();
    httpTesting.expectOne('http://127.0.0.1:43111/health').flush({ status: 'ok', mode: 'local-agent' });
    await flushMicrotasks();
    const printers = httpTesting.expectOne('http://127.0.0.1:43111/printers');
    expect(printers.request.headers.get('X-Urbeat-Local-Token')).toBe('test-local-token');
    printers.flush({ recommendedProfiles: [], installedPrinters: ['POS-58 USB'] });
    await refresh;

    expect(service.installedPrinters()).toEqual(['POS-58 USB']);
    expect(service.catalogEntries().some((entry) => entry.source === 'installed')).toBe(false);
    expect(service.catalogEntries().some((entry) => entry.transport === 'android-bluetooth')).toBe(true);
  });

  it('forwards the idempotency operation key to the local agent', async () => {
    const service = TestBed.inject(SellerPrintingService);
    service.saveConfig({
      ...service.config(),
      connectionType: 'local-agent',
      adapterId: 'local-agent',
      printerName: 'POS-58 USB',
    });

    const print = service.printOrderFromDetails({
      id: 'order-1',
      code: 'URB-1',
      storeId: 'store-1',
      fulfillmentType: FulfillmentType.PickUp,
      status: OrderStatus.Preparing,
      paymentMethod: PaymentMethod.PixOnline,
      subtotal: 10,
      deliveryFee: 0,
      total: 10,
      createdAtUtc: '2026-08-20T00:00:00.000Z',
      items: [],
      history: [],
    }, 'order:order-1:accept:2026-10-19T12:00:00Z:seq:2:printer:POS-58-USB:profile:pos-58');
    await flushMicrotasks();

    const request = httpTesting.expectOne('http://127.0.0.1:43111/print/order');
    expect(request.request.headers.get('X-Urbeat-Local-Token')).toBe('test-local-token');
    expect(request.request.body.operationKey).toBe(
      'order:order-1:accept:2026-10-19T12:00:00Z:seq:2:printer:POS-58-USB:profile:pos-58',
    );
    request.flush({ status: 'sent', message: 'ok' });

    await expect(print).resolves.toEqual(expect.objectContaining({ ok: true }));
  });

  it('reports a failed local-agent job as a failed print result', async () => {
    const service = TestBed.inject(SellerPrintingService);
    service.saveConfig({
      ...service.config(),
      connectionType: 'local-agent',
      adapterId: 'local-agent',
      printerName: 'POS-58 USB',
    });

    const print = service.printReceipt({
      code: 'URB-TEST123',
      total: 10,
      createdAtUtc: '2026-08-20T00:00:00.000Z',
    });
    await flushMicrotasks();

    const request = httpTesting.expectOne('http://127.0.0.1:43111/print/order');
    expect(request.request.headers.get('X-Urbeat-Local-Token')).toBe('test-local-token');
    request.flush({ status: 'failed', message: 'Impressora sem papel' });

    await expect(print).resolves.toEqual(expect.objectContaining({
      ok: false,
      message: 'Impressora sem papel',
    }));
  });
});
