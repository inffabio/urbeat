import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { formatSaoPauloDateTime } from '../../../core/utils/sao-paulo-date.helper';
import { PrintableOrder, PrintingConfig } from '../seller-printing.models';
import { AdapterPlatform, PrinterAdapter } from './printer-adapter.abstract';

@Injectable({ providedIn: 'root' })
export class WifiEscPosAdapter extends PrinterAdapter {
  override readonly id = 'wifi-escpos';
  override readonly name = 'ESC/POS Wi-Fi';
  override readonly manufacturer = 'Generica';
  override readonly description = 'Impressora termica ESC/POS via Wi-Fi. Envia comandos pela rede local. Funciona em qualquer dispositivo.';

  private readonly api = inject(ApiService);

  override isAvailable(): boolean {
    return true;
  }

  override platform(): AdapterPlatform {
    return 'web';
  }

  override async printTestPage(config: PrintingConfig): Promise<void> {
    const ip = config.savedMacAddress || '192.168.0.100';
    for (let via = 0; via < config.copies; via++) {
      const lines = this.buildTestLines(config, via);
      const data = this.concatLines(lines);
      await this.sendViaBackend(ip, data);
    }
  }

  override async printOrder(order: PrintableOrder, config: PrintingConfig): Promise<void> {
    const ip = config.savedMacAddress || '192.168.0.100';
    const labels: string[] = [];
    if (config.printKitchenCopy) labels.push('*** COZINHA ***');
    if (config.printCounterCopy) labels.push('*** BALCAO ***');
    if (config.printCustomerReceipt) labels.push('*** CLIENTE ***');
    const roles = labels.length > 0 ? labels : [''];

    for (let via = 0; via < config.copies; via++) {
      for (const role of roles) {
        const lines = this.buildOrderLines(order, config, role);
        const data = this.concatLines(lines);
        await this.sendViaBackend(ip, data);
      }
    }
  }

  private async sendViaBackend(ip: string, data: Uint8Array): Promise<void> {
    const base64 = this.toBase64(data);
    const resp = await firstValueFrom(this.api.post<{ ok: boolean; message: string }>(
      '/api/printer-config/wifi-print',
      { ipAddress: ip, port: 9100, base64Data: base64 },
    ));
    if (!resp.ok) throw new Error(resp.message);
  }

  private buildTestLines(config: PrintingConfig, via: number): Uint8Array[] {
    const lines: Uint8Array[] = [];
    lines.push(this.init());
    lines.push(...this.logoHeader(config));
    lines.push(this.align('center'));
    lines.push(this.bold(true));
    lines.push(this.text('URBEAT - TESTE'));
    lines.push(this.bold(false));
    lines.push(this.align('left'));
    lines.push(this.br());
    lines.push(this.text('Impressora configurada!'));
    lines.push(this.br());
    lines.push(this.text(`Wi-Fi: ${config.savedMacAddress || '-'}`));
    lines.push(this.br());
    lines.push(this.text(formatSaoPauloDateTime(new Date())));
    if (config.copies > 1) {
      lines.push(this.br());
      lines.push(this.text(`Via ${via + 1} de ${config.copies}`));
    }
    lines.push(this.br());
    lines.push(this.br());
    lines.push(this.text(config.footerText));
    lines.push(...this.endOfPrint(config.autoCut));
    return lines;
  }

  private buildOrderLines(order: PrintableOrder, config: PrintingConfig, role: string): Uint8Array[] {
    const lines: Uint8Array[] = [];
    const w = config.paperWidth === '80mm' ? 48 : 32;

    lines.push(this.init());
    lines.push(...this.logoHeader(config));

    if (role) {
      lines.push(this.align('center'));
      lines.push(this.bold(true));
      lines.push(this.text(role));
      lines.push(this.bold(false));
      lines.push(this.br());
    }

    lines.push(this.align('center'));
    lines.push(this.bold(config.highlightOrderNumber));
    lines.push(this.text(config.highlightOrderNumber ? `PEDIDO #${order.code}` : `Pedido #${order.code}`));
    lines.push(this.bold(false));
    lines.push(this.align('left'));
    lines.push(this.br());

    lines.push(this.text(`Cliente: ${order.customerName || 'Cliente'}`));
    lines.push(this.br());

    lines.push(this.text(this.divider(w)));
    lines.push(this.br());
    lines.push(this.text(order.itemsSummary || 'Itens do pedido'));
    lines.push(this.br());
    lines.push(this.text(this.divider(w)));
    lines.push(this.br());

    lines.push(this.align('right'));
    lines.push(this.bold(true));
    lines.push(this.text(`TOTAL ${this.formatBRL(order.total)}`));
    lines.push(this.bold(false));
    lines.push(this.align('left'));
    lines.push(this.br());

    if (config.footerText) {
      lines.push(this.align('center'));
      lines.push(this.text(config.footerText));
      lines.push(this.br());
    }
    lines.push(...this.endOfPrint(config.autoCut));
    return lines;
  }

  private text(value: string): Uint8Array {
    return this.encodeSafe(value);
  }

  private br(): Uint8Array {
    return new Uint8Array([0x0a]);
  }

  private divider(width: number): string {
    return '-'.repeat(width);
  }

  private init(): Uint8Array {
    return new Uint8Array([0x1b, 0x40]);
  }

  private align(dir: 'left' | 'center' | 'right'): Uint8Array {
    const v = dir === 'center' ? 1 : dir === 'right' ? 2 : 0;
    return new Uint8Array([0x1b, 0x61, v]);
  }

  private bold(on: boolean): Uint8Array {
    return new Uint8Array([0x1b, 0x45, on ? 1 : 0]);
  }

  private doubleSize(on: boolean): Uint8Array {
    return new Uint8Array([0x1d, 0x21, on ? 0x11 : 0x00]);
  }

  /** Encerra a impressao com feeds de linha. Sem ESC @ no final — reinicializar apos imprimir causa alimentacao continua na TC-163. */
  private endOfPrint(autoCut: boolean): Uint8Array[] {
    const commands: Uint8Array[] = [];
    commands.push(this.br());
    commands.push(this.br());
    commands.push(this.br());
    commands.push(this.br());
    if (autoCut) {
      commands.push(new Uint8Array([0x1b, 0x64, 0x03]));
    }
    commands.push(this.br());
    commands.push(this.br());
    return commands;
  }

  /** Cabecalho com nome da loja em tamanho ampliado quando printLogo esta ativo. */
  private logoHeader(config: PrintingConfig): Uint8Array[] {
    if (!config.printLogo || !config.printerName) return [];
    const lines: Uint8Array[] = [];
    lines.push(this.align('center'));
    lines.push(this.doubleSize(true));
    lines.push(this.text(config.printerName));
    lines.push(this.doubleSize(false));
    lines.push(this.align('left'));
    lines.push(this.br());
    lines.push(this.text(this.divider(config.paperWidth === '80mm' ? 48 : 32)));
    lines.push(this.br());
    return lines;
  }

  /** Converte string para Latin-1 (ISO-8859-1) para compatibilidade com impressoras termicas. */
  private encodeSafe(value: string): Uint8Array {
    const bytes = new Uint8Array(value.length);
    for (let i = 0; i < value.length; i++) {
      const cp = value.charCodeAt(i);
      bytes[i] = cp > 255 ? 0x3f : cp;
    }
    return bytes;
  }

  private concatLines(lines: Uint8Array[]): Uint8Array {
    const total = lines.reduce((s, a) => s + a.length, 0);
    const buf = new Uint8Array(total);
    let off = 0;
    for (const a of lines) {
      buf.set(a, off);
      off += a.length;
    }
    return buf;
  }

  private toBase64(data: Uint8Array): string {
    let binary = '';
    for (let i = 0; i < data.length; i++) {
      binary += String.fromCharCode(data[i]);
    }
    return btoa(binary);
  }

  private formatBRL(value: number): string {
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value);
  }
}
