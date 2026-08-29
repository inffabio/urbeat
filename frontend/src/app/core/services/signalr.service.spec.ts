import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import * as signalR from '@microsoft/signalr';
import { AuthService } from './auth.service';
import { SignalRService } from './signalr.service';

let lastWithUrlOptions: { accessTokenFactory?: () => string } | null = null;

const mockWithUrl = jest.fn(function (this: unknown, _url: string, options?: unknown) {
  lastWithUrlOptions = (options ?? null) as typeof lastWithUrlOptions;
  return this;
});
const mockWithAutomaticReconnect = jest.fn();
const mockBuild = jest.fn();

jest.mock('@microsoft/signalr', () => ({
  HubConnectionState: { Connected: 'Connected' },
  HttpTransportType: { WebSockets: 1 },
  HubConnectionBuilder: jest.fn().mockImplementation(() => ({
    withUrl: mockWithUrl,
    withAutomaticReconnect: mockWithAutomaticReconnect.mockReturnThis(),
    build: mockBuild,
  })),
}));

describe('SignalRService', () => {
  let consoleLogSpy: jest.SpyInstance;
  let consoleWarnSpy: jest.SpyInstance;
  let consoleErrorSpy: jest.SpyInstance;
  let authTokenSignal: ReturnType<typeof signal<string | null>>;

  beforeEach(() => {
    lastWithUrlOptions = null;
    mockWithUrl.mockClear();
    mockWithAutomaticReconnect.mockClear();
    mockBuild.mockReset();
    consoleLogSpy = jest.spyOn(console, 'log').mockImplementation(() => undefined);
    consoleWarnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    consoleErrorSpy = jest.spyOn(console, 'error').mockImplementation(() => undefined);

    authTokenSignal = signal<string | null>(null);

    TestBed.configureTestingModule({
      providers: [
        {
          provide: AuthService,
          useValue: {
            getToken: jest.fn(() => authTokenSignal()),
            token: authTokenSignal,
          },
        },
      ],
    });
  });

  afterEach(() => {
    consoleLogSpy.mockRestore();
    consoleWarnSpy.mockRestore();
    consoleErrorSpy.mockRestore();
  });

  it('should stop a stale seller connection if it finishes starting after reset and reinit', async () => {
    let resolveFirstStart: (() => void) | undefined;
    const firstConnection = createConnectionMock(
      new Promise<void>((resolve) => {
        resolveFirstStart = resolve;
      }),
    );
    const secondConnection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValueOnce(firstConnection).mockReturnValueOnce(secondConnection);
    const service = TestBed.inject(SignalRService);

    const firstStart = service.startSellerHub();
    await Promise.resolve();
    service.stopSellerHub();
    await service.startSellerHub();
    resolveFirstStart?.();
    await firstStart;

    expect(firstConnection.stop).toHaveBeenCalledTimes(2);
    expect(secondConnection.stop).not.toHaveBeenCalled();
    expect(service.isSellerConnected()).toBe(true);
  });

  it('should not clear a newer seller connection when a stale connection closes', async () => {
    const firstConnection = createConnectionMock(Promise.resolve());
    const secondConnection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValueOnce(firstConnection).mockReturnValueOnce(secondConnection);
    const service = TestBed.inject(SignalRService);

    await service.startSellerHub();
    service.stopSellerHub();
    await service.startSellerHub();
    firstConnection.closeHandler?.();

    expect(service.isSellerConnected()).toBe(true);
    expect(secondConnection.stop).not.toHaveBeenCalled();
  });

  it('should not clear a newer seller connection when a stale start fails', async () => {
    let rejectFirstStart: ((error: unknown) => void) | undefined;
    const firstConnection = createConnectionMock(
      new Promise<void>((_resolve, reject) => {
        rejectFirstStart = reject;
      }),
    );
    const secondConnection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValueOnce(firstConnection).mockReturnValueOnce(secondConnection);
    const service = TestBed.inject(SignalRService);

    const firstStart = service.startSellerHub().catch(() => undefined);
    await Promise.resolve();
    service.stopSellerHub();
    await service.startSellerHub();
    rejectFirstStart?.(new Error('stale start failed'));
    await firstStart;

    expect(service.isSellerConnected()).toBe(true);
    expect(secondConnection.stop).not.toHaveBeenCalled();
  });

  it('should update seller connected state after start even when read while connecting', async () => {
    let resolveStart: (() => void) | undefined;
    const connection = createConnectionMock(
      new Promise<void>((resolve) => {
        resolveStart = resolve;
      }),
    );
    mockBuild.mockReturnValue(connection);
    const service = TestBed.inject(SignalRService);

    const start = service.startSellerHub();
    expect(service.isSellerConnected()).toBe(false);
    resolveStart?.();
    await start;

    expect(service.isSellerConnected()).toBe(true);
  });

  it('attaches a customer listener registered before start to the eventual connection', async () => {
    const connection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValue(connection);
    const service = TestBed.inject(SignalRService);
    const callback = jest.fn();

    service.onCustomerEvent('OrderStatusUpdated', callback);
    expect(consoleWarnSpy).not.toHaveBeenCalled();

    await service.startCustomerHub();

    expect(connection.on).toHaveBeenCalledWith('OrderStatusUpdated', callback);
  });

  it('removes a customer listener registered before start via removeCustomerListener', async () => {
    const connection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValue(connection);
    const service = TestBed.inject(SignalRService);
    const callback = jest.fn();

    service.onCustomerEvent('OrderStatusUpdated', callback);
    service.removeCustomerListener('OrderStatusUpdated', callback);

    await service.startCustomerHub();

    expect(connection.on).not.toHaveBeenCalledWith('OrderStatusUpdated', callback);
  });

  it('re-attaches pre-start customer listeners to a new connection after reconnect', async () => {
    const firstConnection = createConnectionMock(Promise.resolve());
    const secondConnection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValueOnce(firstConnection).mockReturnValueOnce(secondConnection);
    const service = TestBed.inject(SignalRService);
    const callback = jest.fn();

    service.onCustomerEvent('OrderStatusUpdated', callback);
    await service.startCustomerHub();
    expect(firstConnection.on).toHaveBeenCalledWith('OrderStatusUpdated', callback);

    service.stopCustomerHub();
    await service.startCustomerHub();

    expect(secondConnection.on).toHaveBeenCalledWith('OrderStatusUpdated', callback);
  });

  it('should stop a stale customer connection if it finishes starting after stop and restart', async () => {
    let resolveFirstStart: (() => void) | undefined;
    const firstConnection = createConnectionMock(
      new Promise<void>((resolve) => {
        resolveFirstStart = resolve;
      }),
    );
    const secondConnection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValueOnce(firstConnection).mockReturnValueOnce(secondConnection);
    const service = TestBed.inject(SignalRService);

    const firstStart = service.startCustomerHub();
    await Promise.resolve();
    service.stopCustomerHub();
    await service.startCustomerHub();
    resolveFirstStart?.();
    await firstStart;

    expect(firstConnection.stop).toHaveBeenCalledTimes(2);
    expect(secondConnection.stop).not.toHaveBeenCalled();
    expect(service.isCustomerConnected()).toBe(true);
  });

  it('should not clear a newer customer connection when a stale connection closes', async () => {
    const firstConnection = createConnectionMock(Promise.resolve());
    const secondConnection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValueOnce(firstConnection).mockReturnValueOnce(secondConnection);
    const service = TestBed.inject(SignalRService);

    await service.startCustomerHub();
    service.stopCustomerHub();
    await service.startCustomerHub();
    firstConnection.closeHandler?.();

    expect(service.isCustomerConnected()).toBe(true);
    expect(secondConnection.stop).not.toHaveBeenCalled();
  });

  it('should not clear a newer customer connection when a stale start fails', async () => {
    let rejectFirstStart: ((error: unknown) => void) | undefined;
    const firstConnection = createConnectionMock(
      new Promise<void>((_resolve, reject) => {
        rejectFirstStart = reject;
      }),
    );
    const secondConnection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValueOnce(firstConnection).mockReturnValueOnce(secondConnection);
    const service = TestBed.inject(SignalRService);

    const firstStart = service.startCustomerHub().catch(() => undefined);
    await Promise.resolve();
    service.stopCustomerHub();
    await service.startCustomerHub();
    rejectFirstStart?.(new Error('stale start failed'));
    await firstStart;

    expect(service.isCustomerConnected()).toBe(true);
    expect(secondConnection.stop).not.toHaveBeenCalled();
  });

  it('re-attaches customer listeners registered after connection to a new connection after reconnect', async () => {
    const firstConnection = createConnectionMock(Promise.resolve());
    const secondConnection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValueOnce(firstConnection).mockReturnValueOnce(secondConnection);
    const service = TestBed.inject(SignalRService);
    const callback = jest.fn();

    await service.startCustomerHub();
    service.onCustomerEvent('OrderStatusUpdated', callback);
    expect(firstConnection.on).toHaveBeenCalledWith('OrderStatusUpdated', callback);

    service.stopCustomerHub();
    await service.startCustomerHub();

    expect(secondConnection.on).toHaveBeenCalledWith('OrderStatusUpdated', callback);
  });

  it('notifies customer state changes when the connection drops and reconnects', async () => {
    const connection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValue(connection);
    const service = TestBed.inject(SignalRService);
    const callback = jest.fn();
    service.onCustomerStateChange(callback);

    await service.startCustomerHub();

    connection.reconnectedHandler?.('conn-id');
    expect(callback).toHaveBeenCalledWith(true);

    connection.reconnectingHandler?.(new Error('dropped'));
    expect(callback).toHaveBeenCalledWith(false);

    connection.closeHandler?.(new Error('closed'));
    expect(callback).toHaveBeenCalledWith(false);
    expect(callback).toHaveBeenCalledTimes(3);
  });

  it('does not create a second connection for concurrent customer starts', async () => {
    const connection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValue(connection);
    const service = TestBed.inject(SignalRService);

    await Promise.all([service.startCustomerHub(), service.startCustomerHub()]);

    expect(mockBuild).toHaveBeenCalledTimes(1);
    expect(connection.start).toHaveBeenCalledTimes(1);
    expect(service.isCustomerConnected()).toBe(true);
  });

  it('stops a customer connection created but discarded when stop happens during creation', async () => {
    const connection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValue(connection);
    const service = TestBed.inject(SignalRService);

    const startPromise = service.startCustomerHub();
    service.stopCustomerHub();
    await startPromise;

    expect(connection.start).not.toHaveBeenCalled();
    expect(connection.stop).toHaveBeenCalled();
    expect(service.isCustomerConnected()).toBe(false);
  });

  it('removes a failed customer connection from tracking and stops it safely', async () => {
    let rejectStart: ((error: unknown) => void) | undefined;
    const connection = createConnectionMock(
      new Promise<void>((_resolve, reject) => {
        rejectStart = reject;
      }),
    );
    mockBuild.mockReturnValue(connection);
    const service = TestBed.inject(SignalRService);
    const callback = jest.fn();

    service.onCustomerEvent('OrderStatusUpdated', callback);
    const startPromise = service.startCustomerHub().catch(() => undefined);
    await Promise.resolve();
    rejectStart?.(new Error('start failed'));
    await startPromise;

    expect(service.isCustomerConnected()).toBe(false);
    expect(connection.stop).toHaveBeenCalled();

    service.removeCustomerListener('OrderStatusUpdated', callback);
    expect(connection.off).not.toHaveBeenCalledWith('OrderStatusUpdated', callback);
  });

  it('stops a failed seller connection safely', async () => {
    let rejectStart: ((error: unknown) => void) | undefined;
    const connection = createConnectionMock(
      new Promise<void>((_resolve, reject) => {
        rejectStart = reject;
      }),
    );
    mockBuild.mockReturnValue(connection);
    const service = TestBed.inject(SignalRService);

    const startPromise = service.startSellerHub().catch(() => undefined);
    await Promise.resolve();
    rejectStart?.(new Error('start failed'));
    await startPromise;

    expect(service.isSellerConnected()).toBe(false);
    expect(connection.stop).toHaveBeenCalled();
  });

  it('removes a customer listener from a discarded connection as well', async () => {
    const firstConnection = createConnectionMock(Promise.resolve());
    const secondConnection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValueOnce(firstConnection).mockReturnValueOnce(secondConnection);
    const service = TestBed.inject(SignalRService);
    const callback = jest.fn();

    service.onCustomerEvent('OrderStatusUpdated', callback);
    await service.startCustomerHub();
    service.stopCustomerHub();
    await service.startCustomerHub();
    service.removeCustomerListener('OrderStatusUpdated', callback);

    expect(firstConnection.off).toHaveBeenCalledWith('OrderStatusUpdated', callback);
    expect(secondConnection.off).toHaveBeenCalledWith('OrderStatusUpdated', callback);
  });

  it('reflects reconnecting/reconnected/close in seller connected state', async () => {
    const connection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValue(connection);
    const service = TestBed.inject(SignalRService);

    await service.startSellerHub();
    expect(service.isSellerConnected()).toBe(true);

    connection.reconnectingHandler?.(new Error('dropped'));
    expect(service.isSellerConnected()).toBe(false);

    connection.reconnectedHandler?.('conn-id');
    expect(service.isSellerConnected()).toBe(true);

    connection.closeHandler?.(new Error('closed'));
    expect(service.isSellerConnected()).toBe(false);
  });

  it('reflects reconnecting/reconnected/close in customer connected state', async () => {
    const connection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValue(connection);
    const service = TestBed.inject(SignalRService);

    await service.startCustomerHub();
    expect(service.isCustomerConnected()).toBe(true);

    connection.reconnectingHandler?.(new Error('dropped'));
    expect(service.isCustomerConnected()).toBe(false);

    connection.reconnectedHandler?.('conn-id');
    expect(service.isCustomerConnected()).toBe(true);

    connection.closeHandler?.(new Error('closed'));
    expect(service.isCustomerConnected()).toBe(false);
  });

  it('does not call start twice when the customer hub is already connected', async () => {
    const connection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValue(connection);
    const service = TestBed.inject(SignalRService);

    await service.startCustomerHub();
    await service.startCustomerHub();

    expect(connection.start).toHaveBeenCalledTimes(1);
  });

  it('does not call start twice when the seller hub is already connected', async () => {
    const connection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValue(connection);
    const service = TestBed.inject(SignalRService);

    await service.startSellerHub();
    await service.startSellerHub();

    expect(connection.start).toHaveBeenCalledTimes(1);
  });

  it('joins a store only once across multiple consumers', async () => {
    const connection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValue(connection);
    const service = TestBed.inject(SignalRService);
    await service.startCustomerHub();

    await service.joinStore('store-1');
    await service.joinStore('store-1');

    expect(connection.invoke).toHaveBeenCalledTimes(1);
    expect(connection.invoke).toHaveBeenCalledWith('JoinStore', 'store-1');
  });

  it('leaves a store only when the last consumer leaves', async () => {
    const connection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValue(connection);
    const service = TestBed.inject(SignalRService);
    await service.startCustomerHub();

    await service.joinStore('store-1');
    await service.joinStore('store-1');
    await service.leaveStore('store-1');
    expect(connection.invoke).not.toHaveBeenCalledWith('LeaveStore', 'store-1');

    await service.leaveStore('store-1');
    expect(connection.invoke).toHaveBeenCalledWith('LeaveStore', 'store-1');
  });

  it('leaveStore without a matching join is a no-op', async () => {
    const connection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValue(connection);
    const service = TestBed.inject(SignalRService);
    await service.startCustomerHub();

    await service.leaveStore('store-1');

    expect(connection.invoke).not.toHaveBeenCalled();
  });

  it('swallows LeaveStore invocation failures', async () => {
    const connection = createConnectionMock(Promise.resolve());
    connection.invoke.mockImplementation((method: string) => {
      if (method === 'LeaveStore') return Promise.reject(new Error('closed'));
      return Promise.resolve(undefined);
    });
    mockBuild.mockReturnValue(connection);
    const service = TestBed.inject(SignalRService);
    await service.startCustomerHub();

    await service.joinStore('store-1');

    await expect(service.leaveStore('store-1')).resolves.toBeUndefined();
  });

  it('rejoins a store after the customer connection reconnects', async () => {
    const connection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValue(connection);
    const service = TestBed.inject(SignalRService);
    await service.startCustomerHub();
    await service.joinStore('store-1');
    expect(connection.invoke).toHaveBeenCalledTimes(1);

    connection.reconnectedHandler?.('conn-id-2');
    await Promise.resolve();
    await Promise.resolve();

    expect(connection.invoke).toHaveBeenCalledTimes(2);
    expect(connection.invoke).toHaveBeenNthCalledWith(2, 'JoinStore', 'store-1');
  });

  it('serializes join and leave operations per store', async () => {
    const connection = createConnectionMock(Promise.resolve());
    const order: string[] = [];
    let resolveFirstJoin: (() => void) | undefined;
    let joinCalls = 0;
    connection.invoke.mockImplementation((method: string) => {
      order.push(method);
      if (method === 'JoinStore' && ++joinCalls === 1) {
        return new Promise<void>((resolve) => {
          resolveFirstJoin = resolve;
        });
      }
      return Promise.resolve();
    });
    mockBuild.mockReturnValue(connection);
    const service = TestBed.inject(SignalRService);
    await service.startCustomerHub();

    const firstJoin = service.joinStore('store-1');
    await Promise.resolve();
    const leave = service.leaveStore('store-1');
    await Promise.resolve();
    const secondJoin = service.joinStore('store-1');
    await Promise.resolve();

    expect(order).toEqual(['JoinStore']);

    resolveFirstJoin?.();
    await firstJoin;
    await leave;
    await secondJoin;

    expect(order).toEqual(['JoinStore', 'LeaveStore', 'JoinStore']);
  });

  it('clears joined stores on stop so a fresh start does not rejoin', async () => {
    const firstConnection = createConnectionMock(Promise.resolve());
    const secondConnection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValueOnce(firstConnection).mockReturnValueOnce(secondConnection);
    const service = TestBed.inject(SignalRService);
    await service.startCustomerHub();
    await service.joinStore('store-1');
    expect(firstConnection.invoke).toHaveBeenCalledWith('JoinStore', 'store-1');

    service.stopCustomerHub();
    await service.startCustomerHub();
    await Promise.resolve();
    await Promise.resolve();

    expect(secondConnection.invoke).not.toHaveBeenCalledWith('JoinStore', 'store-1');
  });

  it('does not run a queued LeaveStore against the new connection after stop/restart', async () => {
    const firstConnection = createConnectionMock(Promise.resolve());
    const secondConnection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValueOnce(firstConnection).mockReturnValueOnce(secondConnection);

    let resolveJoin: (() => void) | undefined;
    firstConnection.invoke.mockImplementation((method: string) => {
      if (method === 'JoinStore') {
        return new Promise<void>((resolve) => {
          resolveJoin = resolve;
        });
      }
      return Promise.resolve();
    });

    const service = TestBed.inject(SignalRService);
    await service.startCustomerHub();

    const join = service.joinStore('store-1');
    await Promise.resolve();
    const leave = service.leaveStore('store-1');

    service.stopCustomerHub();
    await service.startCustomerHub();

    resolveJoin?.();
    await join;
    await leave;
    await Promise.resolve();
    await Promise.resolve();

    expect(secondConnection.invoke).not.toHaveBeenCalledWith('LeaveStore', 'store-1');
  });

  it('does not run a queued JoinStore against the new connection after stop/restart', async () => {
    const firstConnection = createConnectionMock(Promise.resolve());
    const secondConnection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValueOnce(firstConnection).mockReturnValueOnce(secondConnection);

    let resolveLeave: (() => void) | undefined;
    let leaveCalls = 0;
    firstConnection.invoke.mockImplementation((method: string) => {
      if (method === 'LeaveStore' && ++leaveCalls === 1) {
        return new Promise<void>((resolve) => {
          resolveLeave = resolve;
        });
      }
      return Promise.resolve();
    });

    const service = TestBed.inject(SignalRService);
    await service.startCustomerHub();

    await service.joinStore('store-1');
    const leave = service.leaveStore('store-1');
    await Promise.resolve();
    const rejoin = service.joinStore('store-1');

    service.stopCustomerHub();
    await service.startCustomerHub();

    resolveLeave?.();
    await leave;
    await rejoin;
    await Promise.resolve();
    await Promise.resolve();

    expect(secondConnection.invoke).not.toHaveBeenCalledWith('JoinStore', 'store-1');
  });

  it('rejoins stores after a full close and restart', async () => {
    const firstConnection = createConnectionMock(Promise.resolve());
    const secondConnection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValueOnce(firstConnection).mockReturnValueOnce(secondConnection);
    const service = TestBed.inject(SignalRService);
    await service.startCustomerHub();
    await service.joinStore('store-1');

    firstConnection.closeHandler?.(new Error('closed'));
    await service.startCustomerHub();
    await Promise.resolve();
    await Promise.resolve();

    expect(secondConnection.invoke).toHaveBeenCalledWith('JoinStore', 'store-1');
  });

  it('stops a seller connection created but discarded when stop happens during creation', async () => {
    const connection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValue(connection);
    const service = TestBed.inject(SignalRService);

    const startPromise = service.startSellerHub();
    service.stopSellerHub();
    await startPromise;

    expect(connection.start).not.toHaveBeenCalled();
    expect(connection.stop).toHaveBeenCalled();
    expect(service.isSellerConnected()).toBe(false);
  });

  it('does not flip customer connected state when a stale connection reports reconnecting', async () => {
    const firstConnection = createConnectionMock(Promise.resolve());
    const secondConnection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValueOnce(firstConnection).mockReturnValueOnce(secondConnection);
    const service = TestBed.inject(SignalRService);

    await service.startCustomerHub();
    service.stopCustomerHub();
    await service.startCustomerHub();

    firstConnection.reconnectingHandler?.(new Error('stale drop'));

    expect(service.isCustomerConnected()).toBe(true);
  });

  it('does not flip seller connected state when a stale connection reports reconnecting', async () => {
    const firstConnection = createConnectionMock(Promise.resolve());
    const secondConnection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValueOnce(firstConnection).mockReturnValueOnce(secondConnection);
    const service = TestBed.inject(SignalRService);

    await service.startSellerHub();
    service.stopSellerHub();
    await service.startSellerHub();

    firstConnection.reconnectingHandler?.(new Error('stale drop'));

    expect(service.isSellerConnected()).toBe(true);
  });

  it('does not notify customer state when a stale connection reports reconnected', async () => {
    const firstConnection = createConnectionMock(Promise.resolve());
    const secondConnection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValueOnce(firstConnection).mockReturnValueOnce(secondConnection);
    const service = TestBed.inject(SignalRService);
    const callback = jest.fn();
    service.onCustomerStateChange(callback);

    await service.startCustomerHub();
    service.stopCustomerHub();
    await service.startCustomerHub();

    firstConnection.reconnectedHandler?.('stale-id');

    expect(callback).not.toHaveBeenCalled();
  });

  it('removes customer event listeners from the connection when stopping the customer hub', async () => {
    const connection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValue(connection);
    const service = TestBed.inject(SignalRService);
    const callback = jest.fn();

    await service.startCustomerHub();
    service.onCustomerEvent('OrderStatusUpdated', callback);

    service.stopCustomerHub();

    expect(connection.off).toHaveBeenCalledWith('OrderStatusUpdated');
  });

  it('removes seller event listeners from the connection when stopping the seller hub', async () => {
    const connection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValue(connection);
    const service = TestBed.inject(SignalRService);
    const callback = jest.fn();

    await service.startSellerHub();
    service.onSellerEvent('OrderStatusUpdated', callback);

    service.stopSellerHub();

    expect(connection.off).toHaveBeenCalledWith('OrderStatusUpdated');
  });

  it('re-attaches seller listeners to a new connection after reconnect', async () => {
    const firstConnection = createConnectionMock(Promise.resolve());
    const secondConnection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValueOnce(firstConnection).mockReturnValueOnce(secondConnection);
    const service = TestBed.inject(SignalRService);
    const callback = jest.fn();

    await service.startSellerHub();
    service.onSellerEvent('OrderStatusUpdated', callback);
    expect(firstConnection.on).toHaveBeenCalledWith('OrderStatusUpdated', callback);

    service.stopSellerHub();
    await service.startSellerHub();

    expect(secondConnection.on).toHaveBeenCalledWith('OrderStatusUpdated', callback);
  });

  it('removes customer event listeners when a failed connection is discarded', async () => {
    let rejectStart: ((error: unknown) => void) | undefined;
    const connection = createConnectionMock(
      new Promise<void>((_resolve, reject) => {
        rejectStart = reject;
      }),
    );
    mockBuild.mockReturnValue(connection);
    const service = TestBed.inject(SignalRService);
    const callback = jest.fn();

    service.onCustomerEvent('OrderStatusUpdated', callback);
    const startPromise = service.startCustomerHub().catch(() => undefined);
    await Promise.resolve();
    rejectStart?.(new Error('start failed'));
    await startPromise;

    expect(connection.off).toHaveBeenCalledWith('OrderStatusUpdated');
  });

  it('uses the current token through a dynamic access token factory', async () => {
    const connection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValue(connection);
    const service = TestBed.inject(SignalRService);

    await service.startCustomerHub();

    expect(lastWithUrlOptions?.accessTokenFactory).toBeDefined();
    expect(lastWithUrlOptions!.accessTokenFactory!()).toBe('');

    authTokenSignal.set('customer-token');

    expect(lastWithUrlOptions!.accessTokenFactory!()).toBe('customer-token');
  });

  it('restarts the customer hub and re-attaches listeners when the auth token changes', async () => {
    const firstConnection = createConnectionMock(Promise.resolve());
    const secondConnection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValueOnce(firstConnection).mockReturnValueOnce(secondConnection);
    const service = TestBed.inject(SignalRService);
    const callback = jest.fn();

    service.onCustomerEvent('OrderStatusUpdated', callback);
    await service.startCustomerHub();
    expect(firstConnection.on).toHaveBeenCalledWith('OrderStatusUpdated', callback);
    expect(firstConnection.stop).not.toHaveBeenCalled();

    authTokenSignal.set(encodeTokenPayload({ role: 'Customer' }));
    TestBed.flushEffects();
    await flushAsync();

    expect(firstConnection.stop).toHaveBeenCalled();
    expect(secondConnection.start).toHaveBeenCalled();
    expect(secondConnection.on).toHaveBeenCalledWith('OrderStatusUpdated', callback);
    expect(secondConnection.on).toHaveBeenCalledTimes(1);
    expect(service.isCustomerConnected()).toBe(true);
  });

  it('does not restart the customer hub when the token changes to a seller token', async () => {
    const connection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValue(connection);
    const service = TestBed.inject(SignalRService);
    const callback = jest.fn();

    service.onCustomerEvent('OrderStatusUpdated', callback);
    await service.startCustomerHub();
    expect(connection.start).toHaveBeenCalledTimes(1);
    expect(service.isCustomerConnected()).toBe(true);

    authTokenSignal.set(encodeTokenPayload({ role: 'Seller' }));
    TestBed.flushEffects();
    await flushAsync();

    expect(service.isCustomerConnected()).toBe(false);
    expect(connection.stop).toHaveBeenCalled();
    expect(connection.start).toHaveBeenCalledTimes(1);
  });

  it('does not restart the customer hub when the token stays the same', async () => {
    const connection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValue(connection);
    const service = TestBed.inject(SignalRService);

    await service.startCustomerHub();
    authTokenSignal.set(null);
    TestBed.flushEffects();
    await flushAsync();

    expect(connection.start).toHaveBeenCalledTimes(1);
    expect(connection.stop).not.toHaveBeenCalled();
  });

  it('notifies customer state disconnected when a token-change restart fails', async () => {
    const firstConnection = createConnectionMock(Promise.resolve());
    let rejectSecondStart: ((error: unknown) => void) | undefined;
    const secondConnection = createConnectionMock(
      new Promise<void>((_resolve, reject) => {
        rejectSecondStart = reject;
      }),
    );
    mockBuild.mockReturnValueOnce(firstConnection).mockReturnValueOnce(secondConnection);

    authTokenSignal.set(encodeTokenPayload({ role: 'Customer', sub: 'old' }));
    const service = TestBed.inject(SignalRService);
    const callback = jest.fn();
    service.onCustomerStateChange(callback);

    await service.startCustomerHub();
    expect(service.isCustomerConnected()).toBe(true);

    authTokenSignal.set(encodeTokenPayload({ role: 'Customer', sub: 'new' }));
    TestBed.flushEffects();
    await flushAsync();

    rejectSecondStart?.(new Error('start failed'));
    await flushAsync();

    expect(service.isCustomerConnected()).toBe(false);
    expect(callback).toHaveBeenCalledWith(false);
  });

  it('stops the customer hub without affecting the seller hub when the token is cleared', async () => {
    authTokenSignal.set('customer-token');
    const customerConnection = createConnectionMock(Promise.resolve());
    const sellerConnection = createConnectionMock(Promise.resolve());
    mockBuild.mockReturnValueOnce(customerConnection).mockReturnValueOnce(sellerConnection);
    const service = TestBed.inject(SignalRService);

    await service.startCustomerHub();
    await service.startSellerHub();
    expect(service.isCustomerConnected()).toBe(true);
    expect(service.isSellerConnected()).toBe(true);

    authTokenSignal.set(null);
    TestBed.flushEffects();

    expect(service.isCustomerConnected()).toBe(false);
    expect(service.isSellerConnected()).toBe(true);
    expect(customerConnection.stop).toHaveBeenCalled();
    expect(sellerConnection.stop).not.toHaveBeenCalled();
    expect(mockBuild).toHaveBeenCalledTimes(2);
  });
});

function createConnectionMock(startResult: Promise<void>): any {
  const connection = {
    state: 'Disconnected',
    start: jest.fn(() =>
      startResult.then(() => {
        connection.state = 'Connected';
        return undefined;
      }),
    ),
    stop: jest.fn().mockImplementation(() => {
      connection.state = 'Disconnected';
      return Promise.resolve();
    }),
    on: jest.fn(),
    off: jest.fn(),
    invoke: jest.fn().mockResolvedValue(undefined),
    onreconnecting: jest.fn((handler: (error?: Error) => void) => {
      connection.reconnectingHandler = handler;
    }),
    onreconnected: jest.fn((handler: (connectionId?: string) => void) => {
      connection.reconnectedHandler = handler;
    }),
    onclose: jest.fn((handler: (error?: Error) => void) => {
      connection.closeHandler = handler;
    }),
    closeHandler: undefined as ((error?: Error) => void) | undefined,
    reconnectingHandler: undefined as ((error?: Error) => void) | undefined,
    reconnectedHandler: undefined as ((connectionId?: string) => void) | undefined,
  };

  return connection;
}

async function flushAsync(): Promise<void> {
  for (let i = 0; i < 10; i++) {
    await Promise.resolve();
  }
}

function encodeTokenPayload(payload: unknown): string {
  return `eyJhbGciOiJIUzI1NiJ9.${btoa(JSON.stringify(payload))}.signature`;
}
