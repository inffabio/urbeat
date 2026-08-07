import { PrintableOrder, PrintingConfig } from '../seller-printing.models';
import { BluetoothDevice } from '../bluetooth-printer.types';

export type AdapterPlatform = 'android' | 'ios' | 'web';

export abstract class PrinterAdapter {
  abstract readonly id: string;
  abstract readonly name: string;
  abstract readonly manufacturer: string;
  abstract readonly description: string;

  abstract isAvailable(): boolean;
  abstract platform(): AdapterPlatform;

  scanDevices?(): Promise<BluetoothDevice[]>;
  connect?(macAddress: string): Promise<void>;
  disconnect?(): Promise<void>;
  getConnectedDevice?(): BluetoothDevice | null;
  getDevices?(): BluetoothDevice[];

  abstract printTestPage(config: PrintingConfig): Promise<void>;
  abstract printOrder(order: PrintableOrder, config: PrintingConfig): Promise<void>;
}
