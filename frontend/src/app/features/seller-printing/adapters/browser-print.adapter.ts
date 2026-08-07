import { Injectable } from '@angular/core';
import { formatSaoPauloDateTime, formatSaoPauloTime } from '../../../core/utils/sao-paulo-date.helper';
import { FulfillmentType } from '../../../shared/enums/fulfillment-type.enum';
import { OrderStatus } from '../../../shared/enums/order-status.enum';
import { PaymentMethod } from '../../../shared/enums/payment-method.enum';
import { PrintableOrder, PrintingConfig } from '../seller-printing.models';
import { AdapterPlatform, PrinterAdapter } from './printer-adapter.abstract';

@Injectable({ providedIn: 'root' })
export class BrowserPrintAdapter extends PrinterAdapter {
  override readonly id = 'browser-print';
  override readonly name = 'Impressora do navegador';
  override readonly manufacturer = 'Sistema';
  override readonly description = 'Imprime via navegador. No Windows com Chrome --kiosk-printing imprime direto sem dialogo.';

  override isAvailable(): boolean {
    return true;
  }

  override platform(): AdapterPlatform {
    return 'web';
  }

  override async printTestPage(config: PrintingConfig): Promise<void> {
    const w = config.paperWidth === '58mm' ? '58mm' : '80mm';
    const html = this.wrapHtml(w, [
      this.tag('h1', { align: 'center', size: '18px' }, 'URBEAT - TESTE'),
      this.tag('div', { cls: 'line' }),
      this.tag('p', {}, `Impressora: ${this.esc(config.printerName)}`),
      this.tag('p', {}, formatSaoPauloDateTime(new Date())),
      this.tag('div', { cls: 'line' }),
      this.tag('p', {}, this.esc(config.footerText)),
      ...this.feedLines(3),
    ]);

    for (let i = 0; i < config.copies; i++) {
      await this.silentPrint(html);
    }
  }

  override async printOrder(order: PrintableOrder, config: PrintingConfig): Promise<void> {
    const w = config.paperWidth === '58mm' ? '58mm' : '80mm';
    const labels: string[] = [];
    if (config.printKitchenCopy) labels.push('*** COZINHA ***');
    if (config.printCounterCopy) labels.push('*** BALCAO ***');
    if (config.printCustomerReceipt) labels.push('*** CLIENTE ***');
    const roles = labels.length > 0 ? labels : [''];

    for (let via = 0; via < config.copies; via++) {
      for (const role of roles) {
        const codeSize = config.highlightOrderNumber ? '22px' : '16px';
        const codeWeight = config.highlightOrderNumber ? '900' : '700';
        const meta = this.buildMetaLine(order);
        const tags = this.buildTagsLine(order);

        const parts: string[] = [];

        if (config.printLogo && config.printerName) {
          parts.push(this.tag('h2', { align: 'center', double: true }, this.esc(config.printerName)));
          parts.push(this.tag('div', { cls: 'line' }));
        }

        if (role) {
          parts.push(this.tag('p', { align: 'center', small: true }, this.esc(role)));
        }

        parts.push(this.tag('h1', { align: 'center', size: codeSize, weight: codeWeight }, `Pedido #${this.esc(order.code)}`));
        parts.push(this.tag('p', { align: 'center', small: true }, meta));

        parts.push(this.tag('div', { cls: 'line' }));

        if (order.customerName || order.customerPhoneNumber) {
          parts.push(this.tag('p', {}, this.esc([order.customerName, order.customerPhoneNumber].filter(Boolean).join(' · '))));
        }

        if (order.items && order.items.length > 0) {
          parts.push('<table>');
          for (const item of order.items) {
            const lineTotal = item.quantity * item.unitPrice;
            parts.push(`<tr><td class="name">${this.esc(item.name)}</td><td class="qty">${item.quantity}x</td><td class="linetotal">${this.formatBRL(lineTotal)}</td></tr>`);
          }
          parts.push('</table>');
        } else if (order.itemsSummary) {
          parts.push(this.tag('p', {}, this.esc(order.itemsSummary)));
        }

        if (tags) {
          parts.push(this.tag('p', { small: true }, this.esc(tags)));
        }

        parts.push(this.tag('div', { cls: 'line' }));
        parts.push(this.tag('p', { align: 'right', size: '17px', weight: '700' }, `Total ${this.formatBRL(order.total)}`));

        if (config.footerText) {
          parts.push(this.tag('p', { align: 'center', small: true }, this.esc(config.footerText)));
        }
        if (config.copies > 1) {
          parts.push(this.tag('p', { align: 'center', small: true, muted: true }, `Via ${via + 1} de ${config.copies}`));
        }

        parts.push(...this.feedLines(3));

        const html = this.wrapHtml(w, parts, this.posStyles());
        await this.silentPrint(html);
      }
    }
  }

  private posStyles(): string {
    return `
      @page { size: 58mm auto; margin: 0; }
      body { font-family: 'Courier New', monospace; margin: 0; padding: 6px 8px; font-size: 12px; color: #000; background: #fff; }
      h1 { margin: 2px 0; }
      h2 { margin: 2px 0 4px; }
      .line { border-top: 1px dashed #333; margin: 6px 0; }
      p { margin: 2px 0; line-height: 1.35; }
      table { width: 100%; border-collapse: collapse; margin: 4px 0; }
      td { padding: 2px 0; font-size: 12px; vertical-align: top; }
      td.name { font-weight: 600; }
      td.qty { text-align: center; white-space: nowrap; width: 30px; }
      td.linetotal { text-align: right; white-space: nowrap; font-weight: 600; }
      .feed { height: 12px; }
    `;
  }

  private wrapHtml(width: string, parts: string[], extraStyles?: string): string {
    return `<!doctype html><html><head><meta charset="utf-8"><style>${extraStyles ?? ''}</style></head><body style="width:${width}">${parts.join('')}</body></html>`;
  }

  private feedLines(count: number): string[] {
    return Array(count).fill('<div class="feed"></div>');
  }

  private tag(tag: string, opts: {
    align?: string;
    size?: string;
    weight?: string;
    cls?: string;
    small?: boolean;
    muted?: boolean;
    double?: boolean;
  }, content?: string): string {
    const styles: string[] = [];
    if (opts.align) styles.push(`text-align:${opts.align}`);
    if (opts.size) styles.push(`font-size:${opts.size}`);
    if (opts.weight) styles.push(`font-weight:${opts.weight}`);
    if (opts.small && !opts.size) styles.push('font-size:11px');
    if (opts.muted) styles.push('color:#888');
    if (opts.double) styles.push('font-size:20px;font-weight:800;letter-spacing:0.5px');
    const cls = opts.cls ? ` class="${opts.cls}"` : '';
    const style = styles.length > 0 ? ` style="${styles.join(';')}"` : '';
    return `<${tag}${cls}${style}>${content ?? ''}</${tag}>`;
  }

  private esc(value: string): string {
    return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  }

  private silentPrint(html: string): Promise<void> {
    return new Promise((resolve) => {
      const win = window.open('', '_blank', 'width=420,height=640');
      if (!win) {
        resolve();
        return;
      }

      let resolved = false;
      const done = () => {
        if (resolved) return;
        resolved = true;
        setTimeout(() => { try { win.close(); } catch { /* ja fechou */ } }, 300);
        resolve();
      };

      win.document.write(html);
      win.document.close();
      win.onafterprint = () => done();
      win.print();

      setTimeout(() => done(), 300);
      setTimeout(() => done(), 10_000);
    });
  }

  private buildMetaLine(order: PrintableOrder): string {
    const parts: string[] = [];
    if (order.status != null) parts.push(this.statusLabel(order.status));
    if (order.createdAtUtc) parts.push(this.formatTime(order.createdAtUtc));
    return parts.join(' · ');
  }

  private buildTagsLine(order: PrintableOrder): string {
    const parts: string[] = [];
    if (order.fulfillmentType != null) parts.push(this.fulfillmentLabel(order.fulfillmentType));
    if (order.paymentMethod != null && order.paymentMethod !== undefined) {
      parts.push(this.paymentLabel(order.paymentMethod));
    }
    if (order.addressSummary) parts.push(order.addressSummary);
    return parts.join('  |  ');
  }

  private statusLabel(s: OrderStatus): string {
    const map: Record<number, string> = { 0: 'Recebido', 1: 'Preparando', 2: 'Pronto', 3: 'Saiu p/ entrega', 4: 'Entregue', 5: 'Cancelado' };
    return map[s] ?? 'Recebido';
  }

  private fulfillmentLabel(f: FulfillmentType): string {
    const map: Record<number, string> = { 0: 'Retirada', 1: 'Entrega' };
    return map[f] ?? '';
  }

  private paymentLabel(m: PaymentMethod): string {
    const map: Record<number, string> = { 0: 'Pix', 1: 'Cartao', 2: 'Dinheiro', 3: 'Cartao' };
    return map[m] ?? '-';
  }

  private formatBRL(value: number): string {
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value);
  }

  private formatTime(utc: string): string {
    return formatSaoPauloTime(utc);
  }
}
