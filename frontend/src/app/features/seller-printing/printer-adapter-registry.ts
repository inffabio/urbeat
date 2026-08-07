import { Injectable, inject } from '@angular/core';
import { BrowserPrintAdapter } from './adapters/browser-print.adapter';
import { EscPosBluetoothAdapter } from './adapters/escpos-bluetooth.adapter';
import { MockPrinterAdapter } from './adapters/mock-printer.adapter';
import { PrinterAdapter } from './adapters/printer-adapter.abstract';
import { WifiEscPosAdapter } from './adapters/wifi-escpos.adapter';

@Injectable({ providedIn: 'root' })
export class PrinterAdapterRegistry {
  private readonly map = new Map<string, PrinterAdapter>();

  constructor() {
    this.register(inject(EscPosBluetoothAdapter));
    this.register(inject(BrowserPrintAdapter));
    this.register(inject(MockPrinterAdapter));
    this.register(inject(WifiEscPosAdapter));
  }

  get(adapterId: string): PrinterAdapter | undefined {
    return this.map.get(adapterId);
  }

  all(): IterableIterator<PrinterAdapter> {
    return this.map.values();
  }

  /** Registra um novo adapter. Chame no construtor de adapters customizados futuros. */
  register(adapter: PrinterAdapter): void {
    this.map.set(adapter.id, adapter);
  }
}
