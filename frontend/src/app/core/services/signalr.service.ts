import { Injectable, inject, signal, computed } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { environment } from '../../../environments/environment';
import { AuthService } from './auth.service';

export type HubType = 'customer' | 'seller';

@Injectable({
  providedIn: 'root',
})
export class SignalRService {
  private readonly authService = inject(AuthService);

  private customerConnection = signal<signalR.HubConnection | null>(null);
  private sellerConnection = signal<signalR.HubConnection | null>(null);
  private sellerConnectionVersion = 0;

  public isCustomerConnected = computed(() => this.customerConnection()?.state === signalR.HubConnectionState.Connected);
  public isSellerConnected = computed(() => this.sellerConnection()?.state === signalR.HubConnectionState.Connected);

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
    const token = this.authService.getToken();

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(url, {
        accessTokenFactory: () => token || '',
        skipNegotiation: true,
        transport: signalR.HttpTransportType.WebSockets,
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .build();

    connection.onreconnecting((error) => {
      console.warn(`SignalR ${hubType} reconnecting:`, error);
    });

    connection.onreconnected((connectionId) => {
      console.log(`SignalR ${hubType} reconnected. ConnectionId: ${connectionId}`);
    });

    connection.onclose((error) => {
      console.warn(`SignalR ${hubType} closed:`, error);
      if (hubType === 'customer') {
        if (this.customerConnection() === connection) {
          this.customerConnection.set(null);
        }
      } else {
        if (this.sellerConnection() === connection) {
          this.sellerConnection.set(null);
        }
      }
    });

    return connection;
  }

  async startCustomerHub(): Promise<void> {
    if (this.isCustomerConnected()) return;

    let connection = this.customerConnection();
    if (!connection) {
      connection = await this.createConnection('customer');
      this.customerConnection.set(connection);
    }

    try {
      await connection.start();
      console.log('SignalR Customer Hub connected.');
    } catch (err) {
      console.error('SignalR Customer Hub connection failed:', err);
      this.customerConnection.set(null);
      throw err;
    }
  }

  async startSellerHub(): Promise<void> {
    if (this.isSellerConnected()) return;

    let connection = this.sellerConnection();
    let connectionVersion = this.sellerConnectionVersion;
    if (!connection) {
      connection = await this.createConnection('seller');
      connectionVersion = ++this.sellerConnectionVersion;
      this.sellerConnection.set(connection);
    }

    try {
      await connection.start();
      if (this.sellerConnection() !== connection || this.sellerConnectionVersion !== connectionVersion) {
        await connection.stop();
        return;
      }
      console.log('SignalR Seller Hub connected.');
    } catch (err) {
      console.error('SignalR Seller Hub connection failed:', err);
      if (this.sellerConnection() === connection && this.sellerConnectionVersion === connectionVersion) {
        this.sellerConnection.set(null);
      }
      throw err;
    }
  }

  stopCustomerHub(): void {
    const connection = this.customerConnection();
    if (connection) {
      connection.stop().catch((err) => console.error('Error stopping customer hub:', err));
      this.customerConnection.set(null);
    }
  }

  stopSellerHub(): void {
    const connection = this.sellerConnection();
    this.sellerConnectionVersion++;
    if (connection) {
      connection.stop().catch((err) => console.error('Error stopping seller hub:', err));
      this.sellerConnection.set(null);
    }
  }

  onCustomerEvent(eventName: string, callback: (...args: any[]) => void): void {
    const connection = this.customerConnection();
    if (connection) {
      connection.on(eventName, callback);
    } else {
      console.warn(`Cannot register listener for '${eventName}': Customer hub not connected.`);
    }
  }

  onSellerEvent(eventName: string, callback: (...args: any[]) => void): void {
    const connection = this.sellerConnection();
    if (connection) {
      connection.on(eventName, callback);
    } else {
      console.warn(`Cannot register listener for '${eventName}': Seller hub not connected.`);
    }
  }

  removeCustomerListener(eventName: string, callback?: (...args: any[]) => void): void {
    const connection = this.customerConnection();
    if (connection && callback) {
      connection.off(eventName, callback);
    }
  }

  removeSellerListener(eventName: string, callback?: (...args: any[]) => void): void {
    const connection = this.sellerConnection();
    if (connection && callback) {
      connection.off(eventName, callback);
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
}
