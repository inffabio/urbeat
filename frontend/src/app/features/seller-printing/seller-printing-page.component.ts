import { CommonModule } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonIcon } from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import { bluetoothOutline, desktopOutline, linkOutline, phonePortraitOutline, printOutline, searchOutline, serverOutline } from 'ionicons/icons';
import { ConfigSubnavComponent } from '../seller-shell/config-subnav.component';
import { LocalAgentStatus, PrinterConnectionType, PrinterPaperWidth, PrintingConfig, PrintResult, PrinterPresetResponse } from './seller-printing.models';
import { SellerPrintingService } from './seller-printing.service';

interface ConnectionOption {
  value: PrinterConnectionType;
  label: string;
  hint: string;
}

interface PlatformRecommendation {
  title: string;
  recommendation: string;
  detail: string;
  tone?: 'recommended' | 'warning';
}

addIcons({
  'bluetooth-outline': bluetoothOutline,
  'desktop-outline': desktopOutline,
  'link-outline': linkOutline,
  'phone-portrait-outline': phonePortraitOutline,
  'print-outline': printOutline,
  'search-outline': searchOutline,
  'server-outline': serverOutline,
});

@Component({
  selector: 'app-seller-printing-page',
  standalone: true,
  imports: [CommonModule, FormsModule, IonIcon, ConfigSubnavComponent],
  templateUrl: './seller-printing-page.component.html',
  styleUrls: ['./seller-printing-page.component.scss'],
})
export class SellerPrintingPageComponent implements OnInit {
  readonly printing = inject(SellerPrintingService);
  readonly form = signal<PrintingConfig>({ ...this.printing.config() });
  readonly testing = signal(false);
  readonly lastResult = signal<PrintResult | null>(this.printing.lastResult());
  readonly headerStatus = computed(() => {
    if (this.testing()) return 'Teste em andamento';
    if (this.lastResult()?.ok) return 'Ultimo teste concluido';
    if (this.lastResult()) return 'Ultimo teste com alerta';
    return 'Configuracao da loja atual';
  });
  readonly connectionOptions: ConnectionOption[] = [
    {
      value: 'android-bluetooth',
      label: 'Bluetooth para Android',
      hint: 'Opcao principal para app Android com impressoras termicas como a POS-58.',
    },
    {
      value: 'wifi',
      label: 'Wi-Fi para iOS, Windows, Linux e macOS',
      hint: 'Opcao preferencial fora do Android quando a impressora estiver na mesma rede local.',
    },
    {
      value: 'local-agent',
      label: 'Local agent para desktop automatico',
      hint: 'Modo desktop automatico preferencial quando o agente local estiver instalado em Windows, Linux ou macOS.',
    },
    {
      value: 'browser-print',
      label: 'Impressao pelo navegador (fallback/manual)',
      hint: 'Sem kiosk ou silent print, o navegador abre o fluxo manual/interativo de impressao.',
    },
    {
      value: 'mock',
      label: 'Simulador',
      hint: 'Usado apenas para validar layout e fluxo sem hardware.',
    },
  ];
  readonly platformRecommendations: PlatformRecommendation[] = [
    {
      title: 'Android',
      recommendation: 'Bluetooth preferencial',
      detail: 'Melhor caminho para POS-58 e similares no app Android nativo. Salve a impressora atual da loja e conecte antes do teste.',
      tone: 'recommended',
    },
    {
      title: 'iOS, Windows, Linux e macOS',
      recommendation: 'Wi-Fi preferencial',
      detail: 'Use impressora ESC/POS na mesma rede local para impressao direta sem escolha manual dentro do app.',
      tone: 'recommended',
    },
    {
      title: 'Windows, Linux e macOS com agente instalado',
      recommendation: 'local-agent como modo desktop automatico preferencial',
      detail: 'Quando o agente local estiver ativo no desktop da loja, ele vira o caminho automatico preferencial para POS-58 e outras termicas instaladas na maquina.',
      tone: 'recommended',
    },
    {
      title: 'Desktop pelo navegador',
      recommendation: 'Fallback manual/interativo',
      detail: 'So fica realmente automatico em kiosk ou silent print. Fora disso, o navegador pode abrir dialogo e pedir confirmacao.',
      tone: 'warning',
    },
  ];

  async ngOnInit(): Promise<void> {
    await Promise.all([this.printing.loadPresets(), this.printing.loadStoreConfig()]);
    await this.printing.refreshLocalAgent();
    this.form.set({ ...this.printing.config() });
  }

  get presets(): PrinterPresetResponse[] {
    return [...this.printing.presets()].sort((a, b) => this.presetPriority(b) - this.presetPriority(a));
  }

  get btState() {
    return this.printing.bluetoothState().status;
  }

  get btDevices() {
    return this.printing.bluetoothState().devices;
  }

  get isCapacitorAndroid() {
    return this.printing.isCapacitorAndroid;
  }

  get selectedConnectionOption(): ConnectionOption {
    return this.connectionOptions.find((option) => option.value === this.form().connectionType) ?? this.connectionOptions[0];
  }

  get agentHealth(): LocalAgentStatus {
    return this.printing.agentHealth();
  }

  applyPreset(preset: PrinterPresetResponse): void {
    const config: PrintingConfig = {
      ...this.form(),
      presetId: preset.id,
      printerName: preset.name,
      connectionType: preset.connectionType as PrinterConnectionType,
      paperWidth: preset.paperWidth as PrinterPaperWidth,
      adapterId: preset.adapterId,
      autoCut: this.shouldKeepAutoCutDisabled(preset.paperWidth as PrinterPaperWidth, preset.name) ? false : this.form().autoCut,
    };
    this.form.set(config);
    this.persist(config);
  }

  updateConnectionType(value: PrinterConnectionType): void {
    const adapterMap: Record<PrinterConnectionType, string> = {
      'android-bluetooth': 'escpos-bluetooth',
      'browser-print': 'browser-print',
      'local-agent': 'local-agent',
      'mock': 'mock',
      'wifi': 'wifi-escpos',
    };
    this.form.update((c) => ({ ...c, connectionType: value, adapterId: adapterMap[value] }));
    this.persist(this.form());
    if (value === 'local-agent') {
      void this.printing.refreshLocalAgent();
    }
  }

  updatePaperWidth(value: PrinterPaperWidth): void {
    this.form.update((c) => ({
      ...c,
      paperWidth: value,
      autoCut: this.shouldKeepAutoCutDisabled(value, c.printerName) ? false : c.autoCut,
    }));
    this.persist(this.form());
  }

  updatePrinterName(value: string): void {
    this.form.update((config) => ({ ...config, printerName: value }));
    this.persist(this.form());
  }

  updateMacAddress(value: string): void {
    this.form.update((config) => ({ ...config, savedMacAddress: value }));
    this.persist(this.form());
  }

  updateCopies(value: number): void {
    this.form.update((c) => ({ ...c, copies: value >= 1 && value <= 5 ? value : 1 }));
    this.persist(this.form());
  }

  updateFlag(key: keyof Pick<PrintingConfig, 'autoCut' | 'autoPrint' | 'printKitchenCopy' | 'printCounterCopy' | 'printCustomerReceipt' | 'printLogo' | 'highlightOrderNumber'>, value: boolean): void {
    this.form.update((c) => ({
      ...c,
      [key]: key === 'autoCut' && this.shouldKeepAutoCutDisabled(c.paperWidth, c.printerName) ? false : value,
    }));
    this.persist(this.form());
  }

  updateFooter(value: string): void {
    this.form.update((c) => ({ ...c, footerText: value }));
    this.persist(this.form());
  }

  async scanDevices(): Promise<void> {
    await this.printing.scanDevices();
  }

  async connectDevice(mac: string): Promise<void> {
    await this.printing.connectToPrinter(mac);
    this.lastResult.set({
      ok: true,
      mode: 'android-bluetooth',
      message: 'Conectado a impressora.',
      printedAtUtc: new Date().toISOString(),
    });
  }

  async disconnectDevice(): Promise<void> {
    await this.printing.disconnectPrinter();
  }

  async openBluetoothSettings(): Promise<void> {
    try {
      await this.printing.openBluetoothSettings();
    } catch (error) {
      this.lastResult.set({
        ok: false,
        mode: 'android-bluetooth',
        message: (error as Error).message,
        printedAtUtc: new Date().toISOString(),
      });
    }
  }

  async testPrint(): Promise<void> {
    if (this.testing()) return;
    this.persist(this.form());
    this.testing.set(true);
    try {
      const result = await this.printing.printTestReceipt();
      this.lastResult.set(result);
    } finally {
      this.testing.set(false);
    }
  }

  async refreshLocalAgent(): Promise<void> {
    await this.printing.refreshLocalAgent();
  }

  selectLocalAgentPrinter(printerName: string): void {
    this.form.update((config) => ({ ...config, printerName }));
    this.persist(this.form());
  }

  private persist(config: PrintingConfig): void {
    this.printing.saveConfig(config);
  }

  isRecommendedPreset(preset: PrinterPresetResponse): boolean {
    return this.presetPriority(preset) >= 100;
  }

  presetBadges(preset: PrinterPresetResponse): string[] {
    const badges = [preset.paperWidth, this.connectionLabel(preset.connectionType as PrinterConnectionType)];
    if (this.isRecommendedPreset(preset)) badges.unshift('Recomendado');
    return badges;
  }

  isAutoCutLocked(): boolean {
    const config = this.form();
    return this.shouldKeepAutoCutDisabled(config.paperWidth, config.printerName);
  }

  private presetPriority(preset: PrinterPresetResponse): number {
    const text = `${preset.name} ${preset.description} ${preset.manufacturer}`.toLowerCase();
    let score = 0;

    if (text.includes('pos-58')) score += 100;
    if (preset.paperWidth === '58mm') score += 10;
    if (preset.connectionType === 'local-agent') score += 8;
    if (preset.connectionType === 'android-bluetooth') score += 5;

    return score;
  }

  private shouldKeepAutoCutDisabled(paperWidth: PrinterPaperWidth, printerName: string): boolean {
    return paperWidth === '58mm' || printerName.toLowerCase().includes('pos-58');
  }

  private connectionLabel(connectionType: PrinterConnectionType): string {
    switch (connectionType) {
      case 'android-bluetooth':
        return 'Bluetooth';
      case 'wifi':
        return 'Wi-Fi';
      case 'local-agent':
        return 'Local agent';
      case 'browser-print':
        return 'Browser print';
      default:
        return 'Simulador';
    }
  }
}
