import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { BrowserPrintAdapter } from './adapters/browser-print.adapter';
import { EscPosBluetoothAdapter } from './adapters/escpos-bluetooth.adapter';
import { MockPrinterAdapter } from './adapters/mock-printer.adapter';
import { PrinterAdapterRegistry } from './printer-adapter-registry';
import { PrintingConfig } from './seller-printing.models';

describe('PrinterAdapterRegistry', () => {
  let registry: PrinterAdapterRegistry;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient()] });
    registry = TestBed.inject(PrinterAdapterRegistry);
  });

  it('registers all four default adapters', () => {
    expect(registry.get('escpos-bluetooth')).toBeInstanceOf(EscPosBluetoothAdapter);
    expect(registry.get('browser-print')).toBeInstanceOf(BrowserPrintAdapter);
    expect(registry.get('mock')).toBeInstanceOf(MockPrinterAdapter);
    expect(registry.get('wifi-escpos')).toBeTruthy();
  });

  it('returns undefined for unknown adapter', () => {
    expect(registry.get('nonexistent')).toBeUndefined();
  });

  it('all adapters have required metadata', () => {
    for (const adapter of registry.all()) {
      expect(adapter.id).toBeTruthy();
      expect(adapter.name).toBeTruthy();
      expect(adapter.manufacturer).toBeTruthy();
      expect(typeof adapter.isAvailable()).toBe('boolean');
      expect(['android', 'ios', 'web']).toContain(adapter.platform());
    }
  });

  it('mock adapter is always available', () => {
    const mock = registry.get('mock');
    expect(mock!.isAvailable()).toBe(true);
  });

  it('browser adapter is always available', () => {
    const browser = registry.get('browser-print');
    expect(browser!.isAvailable()).toBe(true);
  });
});

describe('MockPrinterAdapter', () => {
  let adapter: MockPrinterAdapter;
  const defaultConfig: PrintingConfig = {
    presetId: '', printerName: 'Test', connectionType: 'mock',
    paperWidth: '58mm', copies: 1, autoCut: false, autoPrint: false,
    printKitchenCopy: true, printCounterCopy: false, printCustomerReceipt: false,
    printLogo: false, highlightOrderNumber: true, footerText: 'Obrigado',
    savedMacAddress: '', adapterId: 'mock',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({});
    adapter = TestBed.inject(MockPrinterAdapter);
  });

  it('logs kitchen copy when enabled', async () => {
    const spy = jest.spyOn(console, 'log').mockImplementation();
    await adapter.printOrder({
      code: 'ABC123', total: 50, createdAtUtc: new Date().toISOString(),
      customerName: 'Joao',
    }, { ...defaultConfig, printKitchenCopy: true, printCounterCopy: false, printCustomerReceipt: false });
    expect(spy).toHaveBeenCalledWith(expect.stringContaining('COZINHA'));
    spy.mockRestore();
  });

  it('logs all three roles when all copies enabled', async () => {
    const spy = jest.spyOn(console, 'log').mockImplementation();
    await adapter.printOrder({
      code: 'ABC123', total: 50, createdAtUtc: new Date().toISOString(),
    }, { ...defaultConfig, printKitchenCopy: true, printCounterCopy: true, printCustomerReceipt: true });
    expect(spy).toHaveBeenCalledWith(expect.stringContaining('COZINHA/BALCAO/CLIENTE'));
    spy.mockRestore();
  });

  it('logs multiple copies', async () => {
    const spy = jest.spyOn(console, 'log').mockImplementation();
    await adapter.printOrder({
      code: 'ABC123', total: 50, createdAtUtc: new Date().toISOString(),
    }, { ...defaultConfig, copies: 3 });
    expect(spy).toHaveBeenCalledWith(expect.stringContaining('3 via(s)'));
    spy.mockRestore();
  });
});
