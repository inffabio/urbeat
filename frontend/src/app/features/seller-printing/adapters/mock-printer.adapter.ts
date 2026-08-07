import { Injectable } from '@angular/core';
import { PrintableOrder, PrintingConfig } from '../seller-printing.models';
import { AdapterPlatform, PrinterAdapter } from './printer-adapter.abstract';

@Injectable({ providedIn: 'root' })
export class MockPrinterAdapter extends PrinterAdapter {
  override readonly id = 'mock';
  override readonly name = 'Simulador';
  override readonly manufacturer = 'Sistema';
  override readonly description = 'Simula impressao sem hardware. Usado para validar layout e fluxo.';

  override isAvailable(): boolean {
    return true;
  }

  override platform(): AdapterPlatform {
    return 'web';
  }

  override async printTestPage(config: PrintingConfig): Promise<void> {
    console.log(`[mock] Teste: ${config.printerName} (${config.paperWidth}, ${config.copies} via(s), autoCut=${config.autoCut})`);
  }

  override async printOrder(order: PrintableOrder, config: PrintingConfig): Promise<void> {
    const roles: string[] = [];
    if (config.printKitchenCopy) roles.push('COZINHA');
    if (config.printCounterCopy) roles.push('BALCAO');
    if (config.printCustomerReceipt) roles.push('CLIENTE');
    const roleStr = roles.length > 0 ? roles.join('/') : 'PADRAO';
    console.log(`[mock] Pedido #${order.code} → ${roleStr} (${config.copies} via(s), ${config.paperWidth}, cliente=${order.customerName || '-'}, total=${order.total})`);
  }
}
