export interface BluetoothDevice {
  name: string;
  address: string;
  paired: boolean;
}

export type BluetoothPrinterStatus =
  | 'unavailable'
  | 'disabled'
  | 'scanning'
  | 'disconnected'
  | 'connecting'
  | 'connected'
  | 'printing'
  | 'error';

export interface BluetoothPrinterState {
  status: BluetoothPrinterStatus;
  devices: BluetoothDevice[];
  connectedDevice: BluetoothDevice | null;
  lastError: string | null;
}

export function isNativeBluetoothAvailable(): boolean {
  try {
    const w = window as unknown as Record<string, unknown>;
    return typeof (w as Record<string, unknown>)['bluetoothSerial'] !== 'undefined';
  } catch {
    return false;
  }
}
