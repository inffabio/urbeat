import {
  PRINTER_CATALOG,
  buildSelectablePrinters,
  filterCatalogForPlatform,
} from './printer-catalog';

describe('printer catalog', () => {
  it('includes generic 58, generic 80 and Epson profiles', () => {
    const manufacturers = PRINTER_CATALOG.map((profile) => profile.manufacturer);
    const widths = PRINTER_CATALOG.map((profile) => profile.paperWidth);

    expect(manufacturers).toContain('Generica');
    expect(manufacturers).toContain('Epson');
    expect(widths).toContain('58mm');
    expect(widths).toContain('80mm');
  });

  it('exposes platform, transport, protocol and profile on every catalog entry', () => {
    for (const profile of PRINTER_CATALOG) {
      expect(profile.platforms.length).toBeGreaterThan(0);
      expect(profile.transport).toBeTruthy();
      expect(profile.protocol).toBeTruthy();
      expect(profile.paperWidth).toMatch(/^58mm$|^80mm$/);
      expect(profile.connectionType).toBeTruthy();
    }
  });

  it('filters generic Bluetooth profiles to Android only', () => {
    const android = filterCatalogForPlatform('android');
    const windows = filterCatalogForPlatform('windows');
    const linux = filterCatalogForPlatform('linux');

    expect(android.some((entry) => entry.transport === 'android-bluetooth')).toBe(true);
    expect(windows.some((entry) => entry.transport === 'android-bluetooth')).toBe(false);
    expect(linux.some((entry) => entry.transport === 'android-bluetooth')).toBe(false);
  });

  it('includes installed USB printers only on Windows and Linux', () => {
    const windows = buildSelectablePrinters('windows', ['POS-58 USB']);
    const linux = buildSelectablePrinters('linux', ['POS-58 USB']);
    const android = buildSelectablePrinters('android', ['POS-58 USB']);
    const macos = buildSelectablePrinters('macos', ['POS-58 USB']);

    expect(windows.some((entry) => entry.source === 'installed' && entry.name === 'POS-58 USB')).toBe(true);
    expect(linux.some((entry) => entry.source === 'installed' && entry.name === 'POS-58 USB')).toBe(true);
    expect(android.some((entry) => entry.source === 'installed')).toBe(false);
    expect(macos.some((entry) => entry.source === 'installed')).toBe(false);
  });

  it('marks installed printers as USB transport and local-agent connection type', () => {
    const windows = buildSelectablePrinters('windows', ['POS-58 USB']);
    const installed = windows.find((entry) => entry.source === 'installed');

    expect(installed).toBeTruthy();
    expect(installed!.transport).toBe('usb');
    expect(installed!.connectionType).toBe('local-agent');
  });

  it('maps USB catalog profiles to the local-agent connection type', () => {
    const windows = buildSelectablePrinters('windows', []);
    const usbEntry = windows.find((entry) => entry.transport === 'usb');

    expect(usbEntry).toBeTruthy();
    expect(usbEntry!.connectionType).toBe('local-agent');
  });
});
