import { TestBed } from '@angular/core/testing';
import * as signalR from '@microsoft/signalr';
import { AuthService } from './auth.service';
import { SignalRService } from './signalr.service';

const mockWithUrl = jest.fn();
const mockWithAutomaticReconnect = jest.fn();
const mockBuild = jest.fn();

jest.mock('@microsoft/signalr', () => ({
  HubConnectionState: { Connected: 'Connected' },
  HttpTransportType: { WebSockets: 1 },
  HubConnectionBuilder: jest.fn().mockImplementation(() => ({
    withUrl: mockWithUrl.mockReturnThis(),
    withAutomaticReconnect: mockWithAutomaticReconnect.mockReturnThis(),
    build: mockBuild,
  })),
}));

describe('SignalRService', () => {
  let consoleLogSpy: jest.SpyInstance;
  let consoleWarnSpy: jest.SpyInstance;
  let consoleErrorSpy: jest.SpyInstance;

  beforeEach(() => {
    mockWithUrl.mockClear();
    mockWithAutomaticReconnect.mockClear();
    mockBuild.mockReset();
    consoleLogSpy = jest.spyOn(console, 'log').mockImplementation(() => undefined);
    consoleWarnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    consoleErrorSpy = jest.spyOn(console, 'error').mockImplementation(() => undefined);

    TestBed.configureTestingModule({
      providers: [{ provide: AuthService, useValue: { getToken: jest.fn(() => 'seller-token') } }],
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
    onreconnecting: jest.fn(),
    onreconnected: jest.fn(),
    onclose: jest.fn((handler: () => void) => {
      connection.closeHandler = handler;
    }),
    closeHandler: undefined as (() => void) | undefined,
  };

  return connection;
}
