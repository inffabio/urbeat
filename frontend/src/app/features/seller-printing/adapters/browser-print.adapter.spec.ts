import { TestBed } from '@angular/core/testing';
import { BrowserPrintAdapter } from './browser-print.adapter';
import { PrintingConfig } from '../seller-printing.models';

describe('BrowserPrintAdapter', () => {
  let adapter: BrowserPrintAdapter;
  let popupSpy: jest.Mock;

  const defaultConfig: PrintingConfig = {
    presetId: '', printerName: 'Test', connectionType: 'browser-print',
    paperWidth: '58mm', copies: 1, autoCut: false, autoPrint: false,
    printKitchenCopy: true, printCounterCopy: false, printCustomerReceipt: false,
    printLogo: false, highlightOrderNumber: true, footerText: 'Obrigado',
    savedMacAddress: '', adapterId: 'browser-print',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({});
    adapter = TestBed.inject(BrowserPrintAdapter);

    popupSpy = jest.fn().mockReturnValue({
      document: {
        write: jest.fn(),
        close: jest.fn(),
      },
      focus: jest.fn(),
      print: jest.fn(),
    });
    window.open = popupSpy;
  });

  it('is always available', () => {
    expect(adapter.isAvailable()).toBe(true);
  });

  it('returns web platform', () => {
    expect(adapter.platform()).toBe('web');
  });

  it('opens popup with order code in the receipt', async () => {
    await adapter.printOrder({
      code: 'ABC123', customerName: 'Joao', customerPhoneNumber: '11999999999',
      itemsSummary: '1x X-Burger',
      total: 32.9, createdAtUtc: '2026-07-30T14:00:00Z',
    }, defaultConfig);

    expect(popupSpy).toHaveBeenCalled();
    const html = popupSpy.mock.results[0].value.document.write.mock.calls[0][0] as string;
    expect(html).toContain('ABC123');
    expect(html).toContain('Joao');
    expect(html).toContain('X-Burger');
  });

  it('prints multiple copies', async () => {
    await adapter.printOrder({
      code: 'ABC123', total: 30, createdAtUtc: new Date().toISOString(),
    }, { ...defaultConfig, copies: 2 });

    expect(popupSpy).toHaveBeenCalledTimes(2);
  });

  it('adds kitchen label when kitchen copy enabled', async () => {
    await adapter.printOrder({
      code: 'ABC123', total: 30, createdAtUtc: new Date().toISOString(),
    }, defaultConfig);

    const html = popupSpy.mock.results[0].value.document.write.mock.calls[0][0] as string;
    expect(html).toContain('COZINHA');
  });

  it('does not show kitchen label when disabled', async () => {
    await adapter.printOrder({
      code: 'ABC123', total: 30, createdAtUtc: new Date().toISOString(),
    }, { ...defaultConfig, printKitchenCopy: false });

    const html = popupSpy.mock.results[0].value.document.write.mock.calls[0][0] as string;
    expect(html).not.toContain('COZINHA');
  });

  it('applies highlight to order code when enabled', async () => {
    await adapter.printOrder({
      code: 'ABC123', total: 30, createdAtUtc: new Date().toISOString(),
    }, { ...defaultConfig, highlightOrderNumber: true });

    const html = popupSpy.mock.results[0].value.document.write.mock.calls[0][0] as string;
    expect(html).toContain('font-weight:900');
  });

  it('uses smaller code without highlight when disabled', async () => {
    await adapter.printOrder({
      code: 'ABC123', total: 30, createdAtUtc: new Date().toISOString(),
    }, { ...defaultConfig, highlightOrderNumber: false });

    const html = popupSpy.mock.results[0].value.document.write.mock.calls[0][0] as string;
    expect(html).not.toContain('font-weight:900');
  });

  it('includes footer text in receipt', async () => {
    await adapter.printOrder({
      code: 'ABC123', total: 30, createdAtUtc: new Date().toISOString(),
    }, { ...defaultConfig, footerText: 'Volte sempre!' });

    const html = popupSpy.mock.results[0].value.document.write.mock.calls[0][0] as string;
    expect(html).toContain('Volte sempre!');
  });

  it('shows fulfillment and payment in tags', async () => {
    await adapter.printOrder({
      code: 'ABC123', total: 30, createdAtUtc: new Date().toISOString(),
      fulfillmentType: 1, paymentMethod: 0, addressSummary: 'Rua A, 123',
    }, defaultConfig);

    const html = popupSpy.mock.results[0].value.document.write.mock.calls[0][0] as string;
    expect(html).toContain('Entrega');
    expect(html).toContain('Pix');
    expect(html).toContain('Rua A, 123');
  });

  it('uses wider paper for 80mm config', async () => {
    await adapter.printOrder({
      code: 'ABC123', total: 30, createdAtUtc: new Date().toISOString(),
    }, { ...defaultConfig, paperWidth: '80mm' });

    const html = popupSpy.mock.results[0].value.document.write.mock.calls[0][0] as string;
    expect(html).toContain('80mm');
  });

  it('uses narrow paper for 58mm config', async () => {
    await adapter.printOrder({
      code: 'ABC123', total: 30, createdAtUtc: new Date().toISOString(),
    }, { ...defaultConfig, paperWidth: '58mm' });

    const html = popupSpy.mock.results[0].value.document.write.mock.calls[0][0] as string;
    expect(html).toContain('58mm');
  });

  it('resolves silently when popup is blocked', async () => {
    window.open = jest.fn().mockReturnValue(null);

    await expect(adapter.printOrder({
      code: 'ABC123', total: 30, createdAtUtc: new Date().toISOString(),
    }, defaultConfig)).resolves.toBeUndefined();
  });
});
