import { RuntimePlatform } from '../../core/utils/platform.helper';
import { PrinterConnectionType, PrinterPaperWidth } from './seller-printing.models';

export type PrinterTransport = 'android-bluetooth' | 'usb' | 'wifi' | 'browser' | 'local-agent' | 'mock';
export type PrinterProtocol = 'esc-pos' | 'browser';

export interface PrinterCatalogProfile {
  id: string;
  name: string;
  manufacturer: string;
  paperWidth: PrinterPaperWidth;
  protocol: PrinterProtocol;
  transport: PrinterTransport;
  connectionType: PrinterConnectionType;
  platforms: RuntimePlatform[];
}

export interface PrinterCatalogEntry extends PrinterCatalogProfile {
  source: 'catalog' | 'installed';
}

export const PRINTER_CATALOG: PrinterCatalogProfile[] = [
  {
    id: 'generic-pos-58-bluetooth-android',
    name: 'Generica 58mm Bluetooth',
    manufacturer: 'Generica',
    paperWidth: '58mm',
    protocol: 'esc-pos',
    transport: 'android-bluetooth',
    connectionType: 'android-bluetooth',
    platforms: ['android'],
  },
  {
    id: 'generic-pos-58-usb',
    name: 'Generica 58mm USB',
    manufacturer: 'Generica',
    paperWidth: '58mm',
    protocol: 'esc-pos',
    transport: 'usb',
    connectionType: 'local-agent',
    platforms: ['windows', 'linux'],
  },
  {
    id: 'generic-pos-80-wifi',
    name: 'Generica 80mm',
    manufacturer: 'Generica',
    paperWidth: '80mm',
    protocol: 'esc-pos',
    transport: 'wifi',
    connectionType: 'wifi',
    platforms: ['android', 'ios', 'windows', 'linux', 'macos', 'web'],
  },
  {
    id: 'epson-tm-t20iii',
    name: 'Epson TM-T20',
    manufacturer: 'Epson',
    paperWidth: '80mm',
    protocol: 'esc-pos',
    transport: 'usb',
    connectionType: 'local-agent',
    platforms: ['windows', 'linux'],
  },
  {
    id: 'epson-tm-m30iii',
    name: 'Epson TM-M30',
    manufacturer: 'Epson',
    paperWidth: '80mm',
    protocol: 'esc-pos',
    transport: 'wifi',
    connectionType: 'wifi',
    platforms: ['android', 'ios', 'windows', 'linux', 'macos', 'web'],
  },
  {
    id: 'epson-tm-t88iii',
    name: 'Epson TM-T88',
    manufacturer: 'Epson',
    paperWidth: '80mm',
    protocol: 'esc-pos',
    transport: 'usb',
    connectionType: 'local-agent',
    platforms: ['windows', 'linux'],
  },
];

export function filterCatalogForPlatform(
  platform: RuntimePlatform,
  profiles: readonly PrinterCatalogProfile[] = PRINTER_CATALOG,
): PrinterCatalogEntry[] {
  return profiles
    .filter((profile) => profile.platforms.includes(platform))
    .map((profile) => ({ ...profile, source: 'catalog' as const }));
}

export function buildSelectablePrinters(
  platform: RuntimePlatform,
  installedPrinters: readonly string[],
): PrinterCatalogEntry[] {
  const catalog = filterCatalogForPlatform(platform);

  if (platform !== 'windows' && platform !== 'linux') {
    return catalog;
  }

  const installed: PrinterCatalogEntry[] = installedPrinters.map((name) => ({
    id: `installed:${name}`,
    name,
    manufacturer: 'Instalada',
    paperWidth: '58mm',
    protocol: 'esc-pos',
    transport: 'usb',
    connectionType: 'local-agent',
    platforms: ['windows', 'linux'],
    source: 'installed',
  }));

  return [...catalog, ...installed];
}
