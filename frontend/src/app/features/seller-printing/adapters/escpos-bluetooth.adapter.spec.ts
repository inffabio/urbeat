import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { BluetoothPrinterAdapter } from '../bluetooth-printer.adapter';
import { PrintingConfig } from '../seller-printing.models';
import { EscPosBluetoothAdapter } from './escpos-bluetooth.adapter';

describe('EscPosBluetoothAdapter', () => {
  let adapter: EscPosBluetoothAdapter;
  let sendEscPos: jest.Mock;

  const config: PrintingConfig = {
    presetId: '', printerName: 'POS-58 BT', connectionType: 'android-bluetooth',
    paperWidth: '58mm', copies: 1, autoCut: false, autoPrint: false,
    printKitchenCopy: false, printCounterCopy: false, printCustomerReceipt: false,
    printLogo: false, highlightOrderNumber: true, footerText: '',
    savedMacAddress: '', adapterId: 'escpos-bluetooth',
  };

  const sentText = (): string => {
    const bytes = sendEscPos.mock.calls[0][0] as Uint8Array;
    return Buffer.from(bytes).toString('latin1');
  };

  beforeEach(() => {
    sendEscPos = jest.fn().mockResolvedValue(undefined);
    TestBed.configureTestingModule({
      providers: [{
        provide: BluetoothPrinterAdapter,
        useValue: {
          isAvailable: () => true,
          platform: 'android',
          state: signal({ status: 'disconnected', devices: [] }),
          sendEscPos,
        },
      }],
    });
    adapter = TestBed.inject(EscPosBluetoothAdapter);
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
