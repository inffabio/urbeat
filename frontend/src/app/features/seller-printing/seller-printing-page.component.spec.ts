import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { SellerPrintingPageComponent } from './seller-printing-page.component';
import { SellerPrintingService } from './seller-printing.service';

describe('SellerPrintingPageComponent', () => {
  let printingServiceMock: SellerPrintingService;

  beforeEach(async () => {
    localStorage.clear();
    printingServiceMock = {
      presets: signal([]),
      config: signal({
        presetId: '',
        printerName: 'POS-58 Balcao',
        connectionType: 'local-agent',
        paperWidth: '58mm',
        copies: 1,
        autoCut: false,
        autoPrint: true,
        printKitchenCopy: true,
        printCounterCopy: true,
        printCustomerReceipt: false,
        printLogo: false,
        highlightOrderNumber: true,
        footerText: 'Obrigado pela preferencia.',
        savedMacAddress: '',
        adapterId: 'local-agent',
        logoUrl: '',
      }),
      lastResult: signal(null),
      loadingPresets: signal(false),
      agentHealth: signal({ available: true, mode: 'local-agent', printers: ['POS-58 Balcao'], message: 'Agente local detectado.' }),
      bluetoothState: signal({ status: 'disconnected', devices: [], connectedDevice: null, lastError: null }),
      isCapacitorAndroid: false,
      loadPresets: jest.fn().mockResolvedValue(undefined),
      loadStoreConfig: jest.fn().mockResolvedValue(undefined),
      refreshLocalAgent: jest.fn().mockResolvedValue(undefined),
      saveConfig: jest.fn(async (config) => printingServiceMock.config.set(config)),
      scanDevices: jest.fn().mockResolvedValue(undefined),
      connectToPrinter: jest.fn().mockResolvedValue(undefined),
      disconnectPrinter: jest.fn().mockResolvedValue(undefined),
      openBluetoothSettings: jest.fn().mockResolvedValue(undefined),
      printTestReceipt: jest.fn().mockResolvedValue({ ok: true, mode: 'local-agent', message: 'ok', printedAtUtc: '2026-08-04T03:35:00.000Z' }),
      printAcceptedOrder: jest.fn().mockResolvedValue(undefined),
      printOrderFromDetails: jest.fn(),
      printReceipt: jest.fn(),
      loadLocalAgentPrinters: jest.fn().mockReturnValue(of(['POS-58 Balcao'])),
    } as unknown as SellerPrintingService;

    await TestBed.configureTestingModule({
      imports: [SellerPrintingPageComponent],
      providers: [provideRouter([]), { provide: SellerPrintingService, useValue: printingServiceMock }],
    }).compileComponents();
  });

  it('renders the printing configuration page', () => {
    const fixture = TestBed.createComponent(SellerPrintingPageComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Impressao');
    expect(fixture.nativeElement.textContent).toContain('Preset da impressora');
    expect(fixture.nativeElement.textContent).toContain('POS-58');
  });

  it('explains that the configuration is saved for the current store and highlights the platform recommendations', () => {
    const fixture = TestBed.createComponent(SellerPrintingPageComponent);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent;

    expect(text).toContain('configuracao da impressora da loja atual');
    expect(text).toContain('Android');
    expect(text).toContain('Bluetooth preferencial');
    expect(text).toContain('iOS, Windows, Linux e macOS');
    expect(text).toContain('Wi-Fi preferencial');
    expect(text).toContain('local-agent');
  });

  it('warns that browser print is manual outside kiosk or silent print', () => {
    const fixture = TestBed.createComponent(SellerPrintingPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('kiosk');
    expect(fixture.nativeElement.textContent).toContain('silent print');
    expect(fixture.nativeElement.textContent).toContain('manual/interativo');
  });

  it('spells out the final operational rules for store, platform and accepted-order printing', () => {
    const fixture = TestBed.createComponent(SellerPrintingPageComponent);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent;

    expect(text).toContain('loja atual do dashboard');
    expect(text).toContain('58mm sem guilhotina');
    expect(text).toContain('iOS, Windows, Linux e macOS usam Wi-Fi como preferencial');
    expect(text).toContain('Ao aceitar um pedido, a impressao automatica usa esta configuracao atual da loja');
  });

  it('highlights local-agent as the preferred desktop automatic mode', () => {
    const fixture = TestBed.createComponent(SellerPrintingPageComponent);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent;

    expect(text).toContain('modo desktop automatico preferencial');
    expect(text).toContain('Agente local detectado');
    expect(text).toContain('Windows, Linux e macOS');
  });

  it('has form controls for connection, paper, and copies', () => {
    const fixture = TestBed.createComponent(SellerPrintingPageComponent);
    fixture.detectChanges();

    const selects = fixture.nativeElement.querySelectorAll('select');
    expect(selects.length).toBeGreaterThanOrEqual(3);
  });

  it('has test print button and auto-saves on change', () => {
    const fixture = TestBed.createComponent(SellerPrintingPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Imprimir teste');
    fixture.componentInstance.updateCopies(2);
    const service = TestBed.inject(SellerPrintingService);
    expect(service.config().copies).toBe(2);
  });

  it('shows toggle options for kitchen, counter and customer copies', () => {
    const fixture = TestBed.createComponent(SellerPrintingPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Via cozinha');
    expect(fixture.nativeElement.textContent).toContain('Via balcao');
    expect(fixture.nativeElement.textContent).toContain('Via cliente');
  });

  it('shows footer text input', () => {
    const fixture = TestBed.createComponent(SellerPrintingPageComponent);
    fixture.detectChanges();

    const textarea = fixture.nativeElement.querySelector('textarea');
    expect(textarea).toBeTruthy();
  });

  it('keeps auto cut disabled when prioritizing the 58mm POS flow', () => {
    const fixture = TestBed.createComponent(SellerPrintingPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.updatePaperWidth('58mm');
    fixture.componentInstance.updateFlag('autoCut', true);

    expect(fixture.componentInstance.form().autoCut).toBe(false);
  });

  it('gives the locked auto-cut explanation a full-width option row', () => {
    const fixture = TestBed.createComponent(SellerPrintingPageComponent);
    fixture.detectChanges();

    const option = fixture.nativeElement.querySelector('.auto-cut-option');

    expect(option).not.toBeNull();
    expect(option.textContent).toContain('Travado para POS-58 / 58mm');
    expect(fixture.componentInstance.isAutoCutLocked()).toBe(true);
  });
});
