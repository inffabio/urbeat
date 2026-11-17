import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { PrintingConfig } from '../seller-printing.models';
import { WifiEscPosAdapter } from './wifi-escpos.adapter';

describe('WifiEscPosAdapter', () => {
  let adapter: WifiEscPosAdapter;
  let apiMock: { post: jest.Mock };

  const config: PrintingConfig = {
    presetId: '', printerName: 'POS-58 Wi-Fi', connectionType: 'wifi',
    paperWidth: '58mm', copies: 1, autoCut: false, autoPrint: false,
    printKitchenCopy: false, printCounterCopy: false, printCustomerReceipt: false,
    printLogo: false, highlightOrderNumber: true, footerText: '',
    savedMacAddress: '192.168.0.100', adapterId: 'wifi-escpos',
  };

  const sentText = (): string => {
    const body = apiMock.post.mock.calls[0][1] as { base64Data: string };
    return Buffer.from(body.base64Data, 'base64').toString('latin1');
  };

  beforeEach(() => {
    apiMock = { post: jest.fn().mockReturnValue(of({ ok: true, message: 'ok' })) };
    TestBed.configureTestingModule({
      providers: [{ provide: ApiService, useValue: apiMock }],
    });
    adapter = TestBed.inject(WifiEscPosAdapter);
  });

  it('prints the assigned motoboy name when informed', async () => {
    await adapter.printOrder({
      code: 'URB-1',
      customerName: 'Joao',
      deliveryDriverName: 'Ana Motoboy',
      total: 30,
      createdAtUtc: '2026-08-20T00:00:00.000Z',
    }, config);

    expect(sentText()).toContain('Entregador: Ana Motoboy');
  });

  it('keeps the legacy receipt when no driver is assigned', async () => {
    await adapter.printOrder({
      code: 'URB-2',
      customerName: 'Joao',
      total: 30,
      createdAtUtc: '2026-08-20T00:00:00.000Z',
    }, config);

    expect(sentText()).toContain('Cliente: Joao');
    expect(sentText()).not.toContain('Entregador');
  });
});
