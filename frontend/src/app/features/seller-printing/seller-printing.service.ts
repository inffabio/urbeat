import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { OrderService } from '../../core/services/order.service';
import { OrderDetails } from '../../shared/models/order.model';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';
import { BluetoothPrinterAdapter } from './bluetooth-printer.adapter';
import { PrinterAdapterRegistry } from './printer-adapter-registry';
import { LocalAgentStatus, PrintableOrder, PrintableOrderItem, PrintingConfig, PrintResult, PrinterConnectionType, PrinterPaperWidth, PrinterPresetResponse } from './seller-printing.models';

const STORAGE_KEY = 'urbeat:seller-printing-config';
const LOCAL_AGENT_BASE_URL = 'http://127.0.0.1:43111';

interface StorePrintingConfigResponse {
  printerPresetId?: string;
  printerName?: string;
  macAddress?: string | null;
  copies?: number;
  autoPrint?: boolean;
  autoCut?: boolean;
  printKitchenCopy?: boolean;
  printCounterCopy?: boolean;
  printCustomerReceipt?: boolean;
  printLogo?: boolean;
  highlightOrderNumber?: boolean;
  footerText?: string | null;
}

interface LocalAgentHealthResponse {
  status?: string;
  mode?: string;
}

interface LocalAgentPrinterCatalogResponse {
  installedPrinters?: string[];
}

const DEFAULT_CONFIG: PrintingConfig = {
  presetId: '',
  printerName: '',
  connectionType: 'android-bluetooth',
  paperWidth: '58mm',
  copies: 1,
  autoCut: false,
  autoPrint: false,
  printKitchenCopy: true,
  printCounterCopy: true,
  printCustomerReceipt: false,
  printLogo: false,
  highlightOrderNumber: true,
  footerText: 'Obrigado pela preferencia.',
  savedMacAddress: '',
  adapterId: 'escpos-bluetooth',
  logoUrl: '',
};

@Injectable({ providedIn: 'root' })
export class SellerPrintingService {
  private readonly api = inject(ApiService);
  private readonly http = inject(HttpClient);
  private readonly orderService = inject(OrderService);
  private readonly bt = inject(BluetoothPrinterAdapter);
  private readonly registry = inject(PrinterAdapterRegistry);

  readonly presets = signal<PrinterPresetResponse[]>([]);
  readonly config = signal<PrintingConfig>(this.loadLocalFallback());
  readonly lastResult = signal<PrintResult | null>(null);
  readonly loadingPresets = signal(false);
  readonly agentHealth = signal<LocalAgentStatus>({
    available: false,
    mode: 'local-agent',
    printers: [],
    message: 'Agente local indisponivel neste desktop.',
  });

  get bluetoothState() {
    return this.bt.state;
  }

  get isCapacitorAndroid(): boolean {
    return this.bt.isAvailable();
  }

  async loadPresets(): Promise<void> {
    this.loadingPresets.set(true);
    try {
      const list = await firstValueFrom(this.api.get<PrinterPresetResponse[]>('/api/printer-config/presets'));
      this.presets.set(list);
    } catch {
      this.presets.set([]);
    } finally {
      this.loadingPresets.set(false);
    }
  }

  async loadStoreConfig(): Promise<void> {
    try {
      const remote = await firstValueFrom(this.api.get<StorePrintingConfigResponse>('/api/printer-config/store'));
      const merged = this.normalizeConfig({
        ...this.loadLocalFallback(),
        ...this.mapRemoteConfig(remote),
        logoUrl: this.config().logoUrl,
      });
      this.config.set(merged);
      this.saveLocal(merged);
    } catch {
      const local = this.loadLocalFallback();
      this.config.set(local);
    }
  }

  setLogoUrl(url: string): void {
    this.config.update((c) => ({ ...c, logoUrl: url }));
  }

  async saveConfig(config: PrintingConfig): Promise<void> {
    const normalized = this.normalizeConfig(config);
    this.config.set(normalized);
    this.saveLocal(normalized);
    try {
      await firstValueFrom(this.api.put('/api/printer-config/store', this.toApiPayload(normalized)));
    } catch {
      // localStorage é o fallback
    }
  }

  async refreshLocalAgent(): Promise<void> {
    try {
      const [health, printers] = await Promise.all([
        firstValueFrom(this.http.get<LocalAgentHealthResponse>(`${LOCAL_AGENT_BASE_URL}/health`)),
        this.loadLocalAgentPrinters(),
      ]);

      this.agentHealth.set({
        available: true,
        mode: 'local-agent',
        printers,
        message: health?.status === 'ok'
          ? 'Agente local detectado e pronto para impressao automatica no desktop.'
          : 'Agente local respondeu, mas requer verificacao.',
      });
    } catch {
      this.agentHealth.set({
        available: false,
        mode: 'local-agent',
        printers: [],
        message: 'Agente local indisponivel. No desktop, prefira local-agent quando estiver instalado; se nao, use Wi-Fi ou browser print manual.',
      });
    }
  }

  async loadLocalAgentPrinters(): Promise<string[]> {
    try {
      const printers = await firstValueFrom(this.http.get<string[] | LocalAgentPrinterCatalogResponse>(`${LOCAL_AGENT_BASE_URL}/printers`));
      if (Array.isArray(printers)) {
        return printers;
      }
      return Array.isArray(printers?.installedPrinters) ? printers.installedPrinters : [];
    } catch {
      return [];
    }
  }

  async scanDevices(): Promise<void> {
    if (!this.bt.isAvailable()) return;
    try {
      await this.bt.ensureEnabled();
      const paired = await this.bt.listDevices();
      this.bt.state.set({ ...this.bt.state(), devices: paired });
      await this.bt.scanUnpaired();
    } catch (error) {
      this.bt.state.set({ ...this.bt.state(), status: 'error', lastError: (error as Error).message });
    }
  }

  async connectToPrinter(macAddress: string): Promise<void> {
    await this.bt.connect(macAddress);
    this.saveConfig({ ...this.config(), savedMacAddress: macAddress });
  }

  async disconnectPrinter(): Promise<void> {
    await this.bt.disconnect();
  }

  async openBluetoothSettings(): Promise<void> {
    await this.bt.openBluetoothSettings();
  }

  async autoPrintOrder(orderId: string): Promise<void> {
    if (!this.config().autoPrint) return;
    await this.printAcceptedOrder(orderId);
  }

  async printAcceptedOrder(orderId: string): Promise<void> {
    try {
      const details = await firstValueFrom(this.orderService.getStoreOrder(orderId));
      await this.printOrderFromDetails(details);
    } catch {
      // silent: print is best-effort
    }
  }

  printOrderFromDetails(details: OrderDetails): Promise<PrintResult> {
    const items: PrintableOrderItem[] = (details.items ?? []).map((item) => ({
      name: item.productName,
      quantity: item.quantity,
      unitPrice: item.unitPrice,
    }));

    const addressParts = [
      details.addressStreet, details.addressNumber,
      details.addressNeighborhood, details.addressCity,
    ].filter(Boolean).join(', ');

    const order: PrintableOrder = {
      code: details.code,
      customerName: details.customerName,
      customerPhoneNumber: details.customerPhoneNumber,
      fulfillmentType: details.fulfillmentType,
      paymentMethod: details.paymentMethod,
      status: details.status,
      itemsSummary: details.items?.map((item) => `${item.quantity}x ${item.productName}`).join(', '),
      items,
      total: details.total,
      createdAtUtc: details.createdAtUtc,
      addressSummary: addressParts || undefined,
      notes: details.notes,
    };

    return this.printReceipt(order);
  }

  async printTestReceipt(): Promise<PrintResult> {
    const cfg = this.config();

    if (cfg.connectionType === 'local-agent') {
      return this.printTestThroughLocalAgent(cfg);
    }

    const adapter = this.registry.get(cfg.adapterId);

    if (adapter?.isAvailable()) {
      try {
        if (adapter.connect && cfg.savedMacAddress && this.bt.state().status !== 'connected') {
          await adapter.connect(cfg.savedMacAddress);
        }
        await adapter.printTestPage(cfg);
        const result: PrintResult = {
          ok: true,
          mode: cfg.connectionType,
          message: `Teste enviado para ${cfg.printerName}.`,
          printedAtUtc: new Date().toISOString(),
        };
        this.lastResult.set(result);
        return result;
      } catch (error) {
        const result: PrintResult = {
          ok: false,
          mode: cfg.connectionType,
          message: (error as Error).message,
          printedAtUtc: new Date().toISOString(),
        };
        this.lastResult.set(result);
        return result;
      }
    }

    const fallbackOrder: PrintableOrder = {
      code: 'TESTE',
      customerName: 'Cliente exemplo',
      customerPhoneNumber: '11999999999',
      paymentMethod: PaymentMethod.PixOnline,
      items: [
        { name: 'X-Burger', quantity: 1, unitPrice: 25.0 },
        { name: 'Coca-Cola 350ml', quantity: 1, unitPrice: 7.9 },
      ],
      total: 32.9,
      createdAtUtc: new Date().toISOString(),
    };
    const result = await this.printReceipt(fallbackOrder);
    this.lastResult.set(result);
    return result;
  }

  async printReceipt(order: PrintableOrder): Promise<PrintResult> {
    const cfg = this.config();

    if (cfg.connectionType === 'local-agent') {
      return this.printOrderThroughLocalAgent(order, cfg);
    }

    const adapter = this.registry.get(cfg.adapterId);

    if (!adapter) {
      const result: PrintResult = { ok: false, mode: 'mock', message: 'Adapter nao encontrado.', printedAtUtc: new Date().toISOString() };
      this.lastResult.set(result);
      return result;
    }

    try {
      await adapter.printOrder(order, cfg);
      const result: PrintResult = {
        ok: true,
        mode: cfg.connectionType,
        message: `Pedido #${order.code} enviado para ${cfg.printerName}.`,
        printedAtUtc: new Date().toISOString(),
      };
      this.lastResult.set(result);
      return result;
    } catch (error) {
      const result: PrintResult = {
        ok: false,
        mode: cfg.connectionType,
        message: (error as Error).message,
        printedAtUtc: new Date().toISOString(),
      };
      this.lastResult.set(result);
      return result;
    }
  }

  private normalizeConfig(config: PrintingConfig): PrintingConfig {
    const normalized: PrintingConfig = {
      ...DEFAULT_CONFIG,
      ...config,
      connectionType: this.normalizeConnectionType(config.connectionType),
      paperWidth: this.normalizePaperWidth(config.paperWidth),
      adapterId: this.adapterIdForConnection(this.normalizeConnectionType(config.connectionType), config.adapterId),
      copies: Math.min(5, Math.max(1, config.copies || 1)),
      footerText: (config.footerText ?? '').slice(0, 120),
      savedMacAddress: config.savedMacAddress ?? '',
    };

    if (this.shouldKeepAutoCutDisabled(normalized.paperWidth, normalized.printerName)) {
      normalized.autoCut = false;
    }

    return normalized;
  }

  private saveLocal(config: PrintingConfig): void {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(config));
  }

  private loadLocalFallback(): PrintingConfig {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (raw) return this.normalizeConfig({ ...DEFAULT_CONFIG, ...JSON.parse(raw) });
    } catch { /* vazio */ }
    return this.normalizeConfig({ ...DEFAULT_CONFIG });
  }

  private mapRemoteConfig(remote: StorePrintingConfigResponse | null | undefined): Partial<PrintingConfig> {
    if (!remote) {
      return {};
    }

    const preset = this.presets().find((item) => item.id === remote.printerPresetId);

    return {
      presetId: remote.printerPresetId ?? '',
      printerName: remote.printerName ?? preset?.name ?? '',
      connectionType: this.normalizeConnectionType((preset?.connectionType as PrinterConnectionType | undefined) ?? this.config().connectionType),
      paperWidth: this.normalizePaperWidth((preset?.paperWidth as PrinterPaperWidth | undefined) ?? this.config().paperWidth),
      copies: remote.copies ?? DEFAULT_CONFIG.copies,
      autoCut: remote.autoCut ?? DEFAULT_CONFIG.autoCut,
      autoPrint: remote.autoPrint ?? DEFAULT_CONFIG.autoPrint,
      printKitchenCopy: remote.printKitchenCopy ?? DEFAULT_CONFIG.printKitchenCopy,
      printCounterCopy: remote.printCounterCopy ?? DEFAULT_CONFIG.printCounterCopy,
      printCustomerReceipt: remote.printCustomerReceipt ?? DEFAULT_CONFIG.printCustomerReceipt,
      printLogo: remote.printLogo ?? DEFAULT_CONFIG.printLogo,
      highlightOrderNumber: remote.highlightOrderNumber ?? DEFAULT_CONFIG.highlightOrderNumber,
      footerText: remote.footerText ?? DEFAULT_CONFIG.footerText,
      savedMacAddress: remote.macAddress ?? '',
      adapterId: preset?.adapterId ?? this.adapterIdForConnection(this.config().connectionType),
    };
  }

  private normalizeConnectionType(connectionType: PrinterConnectionType | string | undefined): PrinterConnectionType {
    switch (connectionType) {
      case 'android-bluetooth':
      case 'browser-print':
      case 'mock':
      case 'wifi':
      case 'local-agent':
        return connectionType;
      default:
        return DEFAULT_CONFIG.connectionType;
    }
  }

  private normalizePaperWidth(paperWidth: PrinterPaperWidth | string | undefined): PrinterPaperWidth {
    return paperWidth === '80mm' ? '80mm' : '58mm';
  }

  private adapterIdForConnection(connectionType: PrinterConnectionType, currentAdapterId?: string): string {
    if (connectionType === 'local-agent') return 'local-agent';
    if (connectionType === 'wifi') return 'wifi-escpos';
    if (connectionType === 'browser-print') return 'browser-print';
    if (connectionType === 'mock') return 'mock';
    return currentAdapterId || 'escpos-bluetooth';
  }

  private shouldKeepAutoCutDisabled(paperWidth: PrinterPaperWidth, printerName: string): boolean {
    return paperWidth === '58mm' || printerName.toLowerCase().includes('pos-58');
  }

  private async printTestThroughLocalAgent(config: PrintingConfig): Promise<PrintResult> {
    try {
      await firstValueFrom(this.http.post(`${LOCAL_AGENT_BASE_URL}/print/test`, {
        printerName: config.printerName,
        printerProfile: config.paperWidth === '58mm' ? 'pos-58' : 'thermal-80',
        paperWidth: config.paperWidth,
        autoCut: config.autoCut,
        copies: config.copies,
        footerText: config.footerText,
      }));

      const result: PrintResult = {
        ok: true,
        mode: 'local-agent',
        message: `Teste enviado para ${config.printerName || 'a impressora do agente local'}.`,
        printedAtUtc: new Date().toISOString(),
      };
      this.lastResult.set(result);
      return result;
    } catch (error) {
      const result: PrintResult = {
        ok: false,
        mode: 'local-agent',
        message: (error as Error).message,
        printedAtUtc: new Date().toISOString(),
      };
      this.lastResult.set(result);
      return result;
    }
  }

  private async printOrderThroughLocalAgent(order: PrintableOrder, config: PrintingConfig): Promise<PrintResult> {
    try {
      await firstValueFrom(this.http.post(`${LOCAL_AGENT_BASE_URL}/print/order`, {
        printerName: config.printerName,
        printerProfile: config.paperWidth === '58mm' ? 'pos-58' : 'thermal-80',
        paperWidth: config.paperWidth,
        autoCut: config.autoCut,
        copies: config.copies,
        printKitchenCopy: config.printKitchenCopy,
        printCounterCopy: config.printCounterCopy,
        printCustomerReceipt: config.printCustomerReceipt,
        footerText: config.footerText,
        order,
      }));

      const result: PrintResult = {
        ok: true,
        mode: 'local-agent',
        message: `Pedido #${order.code} enviado para ${config.printerName || 'o agente local'}.`,
        printedAtUtc: new Date().toISOString(),
      };
      this.lastResult.set(result);
      return result;
    } catch (error) {
      const result: PrintResult = {
        ok: false,
        mode: 'local-agent',
        message: (error as Error).message,
        printedAtUtc: new Date().toISOString(),
      };
      this.lastResult.set(result);
      return result;
    }
  }

  private toApiPayload(config: PrintingConfig): Record<string, unknown> {
    return {
      printerPresetId: config.presetId || undefined,
      printerName: config.printerName,
      macAddress: config.savedMacAddress || null,
      copies: config.copies,
      autoPrint: config.autoPrint,
      autoCut: config.autoCut,
      printKitchenCopy: config.printKitchenCopy,
      printCounterCopy: config.printCounterCopy,
      printCustomerReceipt: config.printCustomerReceipt,
      printLogo: config.printLogo,
      highlightOrderNumber: config.highlightOrderNumber,
      footerText: config.footerText || null,
    };
  }
}
