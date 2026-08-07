import { Injectable, signal } from '@angular/core';
import { Capacitor } from '@capacitor/core';
import { formatSaoPauloDateTime } from '../../core/utils/sao-paulo-date.helper';
import { BluetoothDevice, BluetoothPrinterState } from './bluetooth-printer.types';

interface CordovaBluetoothSerial {
  isEnabled: (ok: () => void, err: (msg: string) => void) => void;
  enable: (ok: () => void, err: (msg: string) => void) => void;
  list: (ok: (devices: { name: string; address: string; class: number }[]) => void, err: (msg: string) => void) => void;
  connectInsecure: (mac: string, ok: () => void, err: (msg: string) => void) => void;
  disconnect: (ok: () => void, err: (msg: string) => void) => void;
  write: (data: Uint8Array, ok: () => void, err: (msg: string) => void) => void;
  isConnected: (ok: () => void, err: (msg: string) => void) => void;
  discoverUnpaired: (ok: () => void, err: (msg: string) => void) => void;
  setDeviceDiscoveredListener: (cb: (device: { name: string; address: string }) => void) => void;
  clearDeviceDiscoveredListener: () => void;
  showBluetoothSettings: (ok: () => void, err: (msg: string) => void) => void;
}

@Injectable({ providedIn: 'root' })
export class BluetoothPrinterAdapter {
  readonly platform = Capacitor.getPlatform();
  readonly isNative = Capacitor.isNativePlatform();

  readonly state = signal<BluetoothPrinterState>({
    status: 'unavailable',
    devices: [],
    connectedDevice: null,
    lastError: null,
  });

  isAvailable(): boolean {
    if (this.platform === 'ios') return false;
    if (!this.isNative) return false;
    try {
      return !!(window as unknown as Record<string, unknown>)['bluetoothSerial'];
    } catch {
      return false;
    }
  }

  async ensureEnabled(): Promise<void> {
    const bt = this.getPlugin();
    if (!bt) throw new Error('BluetoothSerial indisponivel nesta plataforma.');

    return new Promise((resolve, reject) => {
      bt.isEnabled(
        () => resolve(),
        () => {
          bt.enable(
            () => resolve(),
            (err) => reject(new Error(`Bluetooth nao pode ser ativado: ${err}`)),
          );
        },
      );
    });
  }

  async listDevices(): Promise<BluetoothDevice[]> {
    const bt = this.getPlugin();
    if (!bt) throw new Error('BluetoothSerial indisponivel.');

    return new Promise((resolve, reject) => {
      bt.list(
        (devices) => resolve(devices.map((item) => ({ name: item.name || 'Sem nome', address: item.address, paired: true }))),
        (err) => reject(new Error(`Erro ao listar dispositivos: ${err}`)),
      );
    });
  }

  async scanUnpaired(): Promise<BluetoothDevice[]> {
    const bt = this.getPlugin();
    if (!bt) throw new Error('BluetoothSerial indisponivel.');

    const found: BluetoothDevice[] = [];
    this.state.set({ ...this.state(), status: 'scanning', devices: [], lastError: null });

    return new Promise((resolve, reject) => {
      bt.setDeviceDiscoveredListener((device) => {
        if (device.name) {
          found.push({ name: device.name, address: device.address, paired: false });
          this.state.set({ ...this.state(), status: 'scanning', devices: [...found] });
        }
      });

      bt.discoverUnpaired(
        () => {
          bt.clearDeviceDiscoveredListener();
          this.state.set({ ...this.state(), status: 'disconnected', devices: found });
          resolve(found);
        },
        (err) => {
          bt.clearDeviceDiscoveredListener();
          this.state.set({ ...this.state(), status: 'error', lastError: err, devices: found });
          reject(new Error(`Busca de dispositivos falhou: ${err}`));
        },
      );
    });
  }

  async connect(macAddress: string): Promise<void> {
    const bt = this.getPlugin();
    if (!bt) throw new Error('BluetoothSerial indisponivel.');

    this.state.set({ ...this.state(), status: 'connecting', lastError: null });

    return new Promise((resolve, reject) => {
      bt.connectInsecure(
        macAddress,
        () => {
          const found = this.state().devices.find((item) => item.address === macAddress) ?? null;
          this.state.set({ ...this.state(), status: 'connected', connectedDevice: found });
          resolve();
        },
        (err) => {
          this.state.set({ ...this.state(), status: 'error', lastError: err });
          reject(new Error(`Conexao com impressora falhou: ${err}`));
        },
      );
    });
  }

  async disconnect(): Promise<void> {
    const bt = this.getPlugin();
    if (!bt) return;

    return new Promise((resolve) => {
      bt.disconnect(
        () => {
          this.state.set({ ...this.state(), status: 'disconnected', connectedDevice: null });
          resolve();
        },
        () => {
          this.state.set({ ...this.state(), status: 'disconnected', connectedDevice: null });
          resolve();
        },
      );
    });
  }

  async openBluetoothSettings(): Promise<void> {
    const bt = this.getPlugin();
    if (!bt) throw new Error('BluetoothSerial indisponivel.');

    return new Promise((resolve, reject) => {
      bt.showBluetoothSettings(
        () => resolve(),
        (err) => reject(new Error(`Nao foi possivel abrir as configuracoes Bluetooth: ${err}`)),
      );
    });
  }

  async sendEscPos(commands: Uint8Array): Promise<void> {
    const bt = this.getPlugin();
    if (!bt) throw new Error('BluetoothSerial indisponivel.');

    const chunkSize = 200;
    for (let offset = 0; offset < commands.length; offset += chunkSize) {
      const chunk = commands.slice(offset, offset + chunkSize);
      await new Promise<void>((resolve, reject) => {
        bt.write(chunk, () => resolve(), (err) => reject(new Error(`Falha ao enviar dados: ${err}`)));
      });
    }
  }

  async printTestPage(): Promise<void> {
    await this.ensureEnabled();
    const commands = this.buildTestEscPos();
    this.state.set({ ...this.state(), status: 'printing' });
    try {
      await this.sendEscPos(commands);
      this.state.set({ ...this.state(), status: 'connected' });
    } catch (error) {
      this.state.set({ ...this.state(), status: 'error', lastError: (error as Error).message });
      throw error;
    }
  }

  private getPlugin(): CordovaBluetoothSerial | null {
    try {
      const w = window as unknown as Record<string, CordovaBluetoothSerial>;
      const plugin = w['bluetoothSerial'];
      if (!plugin) return null;
      return plugin;
    } catch {
      return null;
    }
  }

  private buildTestEscPos(): Uint8Array {
    const encodeSafe = (value: string): Uint8Array => {
      const bytes = new Uint8Array(value.length);
      for (let i = 0; i < value.length; i++) {
        const cp = value.charCodeAt(i);
        bytes[i] = cp > 255 ? 0x3f : cp;
      }
      return bytes;
    };

    const lines = [
      this.escPosInit(),
      this.escPosAlign('center'),
      this.escPosBold(true),
      encodeSafe('URBEAT - TESTE'),
      this.escPosBold(false),
      this.escPosAlign('left'),
      new Uint8Array([0x0A]),
      encodeSafe('Impressora configurada!'),
      new Uint8Array([0x0A]),
      encodeSafe('TC-163 / Havendo'),
      new Uint8Array([0x0A]),
      encodeSafe(formatSaoPauloDateTime(new Date())),
      new Uint8Array([0x0A, 0x0A, 0x0A, 0x0A]),
      this.escPosFeedEnd(),
    ];

    const total = lines.reduce((sum, arr) => sum + arr.length, 0);
    const result = new Uint8Array(total);
    let offset = 0;
    for (const arr of lines) {
      result.set(arr, offset);
      offset += arr.length;
    }
    return result;
  }

  private escPosInit(): Uint8Array {
    return new Uint8Array([0x1b, 0x40]);
  }

  private escPosAlign(align: 'left' | 'center' | 'right'): Uint8Array {
    const value = align === 'center' ? 1 : align === 'right' ? 2 : 0;
    return new Uint8Array([0x1b, 0x61, value]);
  }

  private escPosBold(on: boolean): Uint8Array {
    return new Uint8Array([0x1b, 0x45, on ? 1 : 0]);
  }

  private escPosFeedEnd(): Uint8Array {
    return new Uint8Array([0x1b, 0x64, 0x03]);
  }
}
