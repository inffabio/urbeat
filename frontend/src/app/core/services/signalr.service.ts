import { Injectable, inject, signal, computed, effect } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { environment } from '../../../environments/environment';
import { AuthService } from './auth.service';
import { isCustomer } from '../utils/jwt.helper';

export type HubType = 'customer' | 'seller';

@Injectable({
  providedIn: 'root',
})
export class SignalRService {
  private readonly authService = inject(AuthService);

  private customerConnection = signal<signalR.HubConnection | null>(null);
  private sellerConnection = signal<signalR.HubConnection | null>(null);
  private sellerConnectionVersion = 0;
  private customerConnectionVersion = 0;
  private customerConnected = signal(false);
  private sellerConnected = signal(false);
  private readonly storeJoinRefCounts = new Map<string, number>();
  private readonly storeOpChains = new Map<string, Promise<void>>();
  private customerOpGeneration = 0;

  private customerConnectionToken: string | null = null;
  private customerActive = false;
  private lastCustomerToken: string | null;

  private customerStartPromise: Promise<void> | null = null;
  private sellerStartPromise: Promise<void> | null = null;

  private customerConnections = new Set<signalR.HubConnection>();

  private customerListeners = new Map<string, ((...args: any[]) => void)[]>();
  private sellerListeners = new Map<string, ((...args: any[]) => void)[]>();
  private customerStateCallbacks = new Set<(connected: boolean) => void>();

  public isCustomerConnected = computed(() => this.customerConnected());
  public isSellerConnected = computed(() => this.sellerConnected());

  constructor() {
    this.lastCustomerToken = this.authService.getToken();
    effect(() => {
      const token = this.authService.token();
      if (token === this.lastCustomerToken) return;
      this.lastCustomerToken = token;
      this.onCustomerTokenChanged(token);
    });
  }

  private getBaseUrl(): string {
    // If apiUrl is relative (e.g., ''), we assume the same origin.
    // If it's absolute, we use its origin.
    if (environment.apiUrl.startsWith('http')) {
      const url = new URL(environment.apiUrl);
      return `${url.protocol}//${url.host}`;
    }
    return window.location.origin;
  }

  private buildHubUrl(hubType: HubType): string {
    const baseUrl = this.getBaseUrl();
    const hubPath = hubType === 'customer' ? '/hubs/customer-notifications' : '/hubs/seller-notifications';
    return `${baseUrl}${hubPath}`;
  }

  private async createConnection(hubType: HubType): Promise<signalR.HubConnection> {
    const url = this.buildHubUrl(hubType);

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(url, {
        accessTokenFactory: () => this.authService.getToken() || '',
        skipNegotiation: true,
        transport: signalR.HttpTransportType.WebSockets,
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .build();

    connection.onreconnecting((error) => {
      console.warn(`SignalR ${hubType} reconnecting:`, error);
      if (hubType === 'customer') {
        if (this.customerConnection() !== connection) return;
        this.customerConnected.set(false);
        this.notifyCustomerState(false);
      } else {
        if (this.sellerConnection() !== connection) return;
        this.sellerConnected.set(false);
      }
    });

    connection.onreconnected((connectionId) => {
      console.log(`SignalR ${hubType} reconnected. ConnectionId: ${connectionId}`);
      if (hubType === 'customer') {
        if (this.customerConnection() !== connection) return;
        this.customerConnected.set(true);
        this.notifyCustomerState(true);
        this.rejoinStores();
      } else {
        if (this.sellerConnection() !== connection) return;
        this.sellerConnected.set(true);
      }
    });

    connection.onclose((error) => {
      console.warn(`SignalR ${hubType} closed:`, error);
      if (hubType === 'customer') {
        this.customerConnections.delete(connection);
        if (this.customerConnection() === connection) {
          this.customerConnected.set(false);
          this.customerConnection.set(null);
          this.notifyCustomerState(false);
        }
      } else {
        if (this.sellerConnection() === connection) {
          this.sellerConnected.set(false);
          this.sellerConnection.set(null);
        }
      }
    });

    return connection;
  }

  async startCustomerHub(): Promise<void> {
    this.customerActive = true;
    if (this.isCustomerConnected()) return;
    if (this.customerStartPromise) {
      return this.customerStartPromise;
    }
    const promise = this.performStartCustomerHub();
    this.customerStartPromise = promise;
    return promise.finally(() => {
      if (this.customerStartPromise === promise) {
        this.customerStartPromise = null;
      }
    });
  }

  private async performStartCustomerHub(): Promise<void> {
    let connection = this.customerConnection();
    const versionBeforeCreate = this.customerConnectionVersion;
    let connectionVersion = versionBeforeCreate;

    if (!connection) {
      connection = await this.createConnection('customer');
      if (this.customerConnectionVersion !== versionBeforeCreate) {
        try {
          await connection.stop();
        } catch (err) {
          console.warn('Error stopping discarded customer hub connection:', err);
        }
        return;
      }
      connectionVersion = ++this.customerConnectionVersion;
      this.customerConnection.set(connection);
      this.customerConnections.add(connection);
      this.attachCustomerListeners(connection);
      this.customerConnectionToken = this.authService.getToken();
    }

    try {
      await connection.start();
      if (this.customerConnection() !== connection || this.customerConnectionVersion !== connectionVersion) {
        this.detachCustomerListeners(connection);
        await connection.stop();
        return;
      }
      this.customerConnected.set(true);
      this.rejoinStores();
      console.log('SignalR Customer Hub connected.');
    } catch (err) {
      console.error('SignalR Customer Hub connection failed:', err);
      this.customerConnections.delete(connection);
      if (this.customerConnection() === connection && this.customerConnectionVersion === connectionVersion) {
        this.customerConnection.set(null);
        this.customerConnected.set(false);
      }
      this.detachCustomerListeners(connection);
      try {
        await connection.stop();
      } catch (stopErr) {
        console.warn('Error stopping failed customer hub connection:', stopErr);
      }
      throw err;
    }
  }

  async startSellerHub(): Promise<void> {
    if (this.isSellerConnected()) return;
    if (this.sellerStartPromise) {
      return this.sellerStartPromise;
    }
    const promise = this.performStartSellerHub();
    this.sellerStartPromise = promise;
    return promise.finally(() => {
      if (this.sellerStartPromise === promise) {
        this.sellerStartPromise = null;
      }
    });
  }

  private async performStartSellerHub(): Promise<void> {
    let connection = this.sellerConnection();
    const versionBeforeCreate = this.sellerConnectionVersion;
    let connectionVersion = versionBeforeCreate;

    if (!connection) {
      connection = await this.createConnection('seller');
      if (this.sellerConnectionVersion !== versionBeforeCreate) {
        try {
          await connection.stop();
        } catch (err) {
          console.warn('Error stopping discarded seller hub connection:', err);
        }
        return;
      }
      connectionVersion = ++this.sellerConnectionVersion;
      this.sellerConnection.set(connection);
      this.attachSellerListeners(connection);
    }

    try {
      await connection.start();
      if (this.sellerConnection() !== connection || this.sellerConnectionVersion !== connectionVersion) {
        this.detachSellerListeners(connection);
        await connection.stop();
        return;
      }
      this.sellerConnected.set(true);
      console.log('SignalR Seller Hub connected.');
    } catch (err) {
      console.error('SignalR Seller Hub connection failed:', err);
      if (this.sellerConnection() === connection && this.sellerConnectionVersion === connectionVersion) {
        this.sellerConnection.set(null);
        this.sellerConnected.set(false);
      }
      this.detachSellerListeners(connection);
      try {
        await connection.stop();
      } catch (stopErr) {
        console.warn('Error stopping failed seller hub connection:', stopErr);
      }
      throw err;
    }
  }

  stopCustomerHub(): void {
    this.customerActive = false;
    this.customerConnectionToken = null;
    this.customerConnectionVersion++;
    this.customerOpGeneration++;
    this.customerStartPromise = null;
    this.customerConnected.set(false);
    this.storeJoinRefCounts.clear();
    this.storeOpChains.clear();
    const connection = this.customerConnection();
    if (connection) {
      this.detachCustomerListeners(connection);
      connection.stop().catch((err) => console.error('Error stopping customer hub:', err));
      this.customerConnection.set(null);
    }
  }

  stopSellerHub(): void {
    this.sellerConnectionVersion++;
    this.sellerStartPromise = null;
    this.sellerConnected.set(false);
    const connection = this.sellerConnection();
    if (connection) {
      this.detachSellerListeners(connection);
      connection.stop().catch((err) => console.error('Error stopping seller hub:', err));
      this.sellerConnection.set(null);
    }
  }

  private onCustomerTokenChanged(token: string | null): void {
    if (!this.customerActive) return;
    if (this.customerConnectionToken === token) return;
    if (!token || !isCustomer(token)) {
      this.stopCustomerHub();
      return;
    }
    void this.restartCustomerHub();
  }

  private async restartCustomerHub(): Promise<void> {
    const connection = this.customerConnection();
    this.customerConnectionVersion++;
    this.customerOpGeneration++;
    this.customerStartPromise = null;
    this.customerConnected.set(false);
    if (connection) {
      this.detachCustomerListeners(connection);
      connection.stop().catch((err) => console.error('Error stopping customer hub on token change:', err));
      this.customerConnection.set(null);
    }
    this.customerConnectionToken = null;
    try {
      await this.startCustomerHub();
    } catch {
      this.customerConnected.set(false);
      this.notifyCustomerState(false);
    }
  }

  onCustomerEvent(eventName: string, callback: (...args: any[]) => void): void {
    const callbacks = this.customerListeners.get(eventName) ?? [];
    callbacks.push(callback);
    this.customerListeners.set(eventName, callbacks);

    const connection = this.customerConnection();
    if (connection) {
      connection.on(eventName, callback);
    }
  }

  onSellerEvent(eventName: string, callback: (...args: any[]) => void): void {
    const callbacks = this.sellerListeners.get(eventName) ?? [];
    callbacks.push(callback);
    this.sellerListeners.set(eventName, callbacks);

    const connection = this.sellerConnection();
    if (connection) {
      connection.on(eventName, callback);
    }
  }

  removeCustomerListener(eventName: string, callback?: (...args: any[]) => void): void {
    this.customerConnections.forEach((connection) => {
      if (callback) {
        connection.off(eventName, callback);
      } else {
        connection.off(eventName);
      }
    });

    if (callback) {
      const pending = this.customerListeners.get(eventName);
      if (pending) {
        const next = pending.filter((cb) => cb !== callback);
        if (next.length === 0) {
          this.customerListeners.delete(eventName);
        } else {
          this.customerListeners.set(eventName, next);
        }
      }
    } else {
      this.customerListeners.delete(eventName);
    }
  }

  removeSellerListener(eventName: string, callback?: (...args: any[]) => void): void {
    const connection = this.sellerConnection();
    if (connection) {
      if (callback) {
        connection.off(eventName, callback);
      } else {
        connection.off(eventName);
      }
    }

    if (callback) {
      const pending = this.sellerListeners.get(eventName);
      if (pending) {
        const next = pending.filter((cb) => cb !== callback);
        if (next.length === 0) {
          this.sellerListeners.delete(eventName);
        } else {
          this.sellerListeners.set(eventName, next);
        }
      }
    } else {
      this.sellerListeners.delete(eventName);
    }
  }

  async invokeCustomerMethod(methodName: string, ...args: any[]): Promise<any> {
    const connection = this.customerConnection();
    if (!connection) {
      throw new Error('Customer hub is not connected.');
    }
    return connection.invoke(methodName, ...args);
  }

  async invokeSellerMethod(methodName: string, ...args: any[]): Promise<any> {
    const connection = this.sellerConnection();
    if (!connection) {
      throw new Error('Seller hub is not connected.');
    }
    return connection.invoke(methodName, ...args);
  }

  async joinStore(storeId: string): Promise<void> {
    if (!storeId) return;
    const count = this.storeJoinRefCounts.get(storeId) ?? 0;
    this.storeJoinRefCounts.set(storeId, count + 1);
    if (count > 0) return;
    await this.enqueueStoreOp(storeId, () => this.invokeCustomerMethod('JoinStore', storeId));
  }

  async leaveStore(storeId: string): Promise<void> {
    if (!storeId) return;
    const count = this.storeJoinRefCounts.get(storeId) ?? 0;
    if (count <= 0) return;
    const next = count - 1;
    if (next === 0) {
      this.storeJoinRefCounts.delete(storeId);
    } else {
      this.storeJoinRefCounts.set(storeId, next);
    }
    if (next > 0) return;
    await this.enqueueStoreOp(storeId, async () => {
      try {
        await this.invokeCustomerMethod('LeaveStore', storeId);
      } catch (err) {
        console.warn('SignalR LeaveStore failed:', err);
      }
    });
  }

  private enqueueStoreOp(storeId: string, op: () => Promise<void>): Promise<void> {
    const generation = this.customerOpGeneration;
    const previous = this.storeOpChains.get(storeId) ?? Promise.resolve();
    const guarded = () => {
      if (generation !== this.customerOpGeneration) return Promise.resolve();
      return op();
    };
    const next = previous.then(guarded, guarded);
    this.storeOpChains.set(storeId, next);
    const cleanup = () => {
      if (this.storeOpChains.get(storeId) === next) {
        this.storeOpChains.delete(storeId);
      }
    };
    next.then(cleanup, cleanup);
    return next;
  }

  private rejoinStores(): void {
    this.storeJoinRefCounts.forEach((count, storeId) => {
      if (count <= 0) return;
      void this.enqueueStoreOp(storeId, async () => {
        try {
          await this.invokeCustomerMethod('JoinStore', storeId);
        } catch {
          // Best-effort rejoin after reconnect; the next reconnect will retry.
        }
      });
    });
  }

  private attachCustomerListeners(connection: signalR.HubConnection): void {
    this.customerListeners.forEach((callbacks, eventName) => {
      callbacks.forEach((callback) => connection.on(eventName, callback));
    });
  }

  private attachSellerListeners(connection: signalR.HubConnection): void {
    this.sellerListeners.forEach((callbacks, eventName) => {
      callbacks.forEach((callback) => connection.on(eventName, callback));
    });
  }

  private detachCustomerListeners(connection: signalR.HubConnection): void {
    this.customerListeners.forEach((_callbacks, eventName) => connection.off(eventName));
  }

  private detachSellerListeners(connection: signalR.HubConnection): void {
    this.sellerListeners.forEach((_callbacks, eventName) => connection.off(eventName));
  }

  onCustomerStateChange(callback: (connected: boolean) => void): () => void {
    this.customerStateCallbacks.add(callback);
    return () => {
      this.customerStateCallbacks.delete(callback);
    };
  }

  private notifyCustomerState(connected: boolean): void {
    this.customerStateCallbacks.forEach((callback) => callback(connected));
  }
}
