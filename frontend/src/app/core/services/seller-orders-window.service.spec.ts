import { TestBed } from '@angular/core/testing';

import {
  SELLER_ORDERS_WINDOW_CHANNEL,
  SELLER_ORDERS_WINDOW_NAME,
  SELLER_ORDERS_WINDOW_ROUTE,
  SellerOrdersWindowMessage,
  SellerOrdersWindowService,
} from './seller-orders-window.service';

class FakeBroadcastChannel {
  static instances: FakeBroadcastChannel[] = [];

  readonly posted: unknown[] = [];
  closed = false;
  onmessage: ((event: MessageEvent<unknown>) => void) | null = null;

  constructor(readonly name: string) {
    FakeBroadcastChannel.instances.push(this);
  }

  postMessage(message: unknown): void {
    this.posted.push(message);
  }

  close(): void {
    this.closed = true;
  }

  emit(message: unknown): void {
    this.onmessage?.({ data: message } as MessageEvent<unknown>);
  }
}

interface FakeWindowRef {
  closed: boolean;
  focus: jest.Mock;
  close: jest.Mock;
}

const createWindowRef = (): FakeWindowRef => {
  const ref: FakeWindowRef = {
    closed: false,
    focus: jest.fn(),
    close: jest.fn(() => {
      ref.closed = true;
    }),
  };
  return ref;
};

const lastChannel = (): FakeBroadcastChannel =>
  FakeBroadcastChannel.instances[FakeBroadcastChannel.instances.length - 1];

const message = (
  type: SellerOrdersWindowMessage['type'],
  source: SellerOrdersWindowMessage['source'] = 'secondary',
): SellerOrdersWindowMessage => ({
  type,
  source,
  sentAt: Date.now(),
});

describe('SellerOrdersWindowService', () => {
  let originalBroadcastChannel: typeof BroadcastChannel | undefined;
  let service: SellerOrdersWindowService | null;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    FakeBroadcastChannel.instances = [];
    originalBroadcastChannel = (globalThis as { BroadcastChannel?: typeof BroadcastChannel }).BroadcastChannel;
    (globalThis as { BroadcastChannel?: unknown }).BroadcastChannel = FakeBroadcastChannel;
    TestBed.configureTestingModule({});
    service = null;
  });

  afterEach(() => {
    service?.dispose();
    service = null;
    jest.restoreAllMocks();
    jest.useRealTimers();
    if (originalBroadcastChannel) {
      (globalThis as { BroadcastChannel?: unknown }).BroadcastChannel = originalBroadcastChannel;
    } else {
      delete (globalThis as { BroadcastChannel?: unknown }).BroadcastChannel;
    }
  });

  const createService = (): SellerOrdersWindowService => {
    service = TestBed.inject(SellerOrdersWindowService);
    return service;
  };

  it('starts in the normal dashboard mode', () => {
    const windowService = createService();

    expect(windowService.isDetached()).toBe(false);
    expect(windowService.isDetachedRoute()).toBe(false);
    expect(windowService.isDetachedHost()).toBe(false);
    expect(windowService.secondaryWindowOpen()).toBe(false);
  });

  it('keeps the dashboard normal when the popup is blocked', () => {
    const openSpy = jest.spyOn(window, 'open').mockReturnValue(null);
    const windowService = createService();

    expect(windowService.openDetachedWindow()).toBe(false);

    expect(openSpy).toHaveBeenCalledWith(
      SELLER_ORDERS_WINDOW_ROUTE,
      SELLER_ORDERS_WINDOW_NAME,
      expect.stringContaining('popup=yes'),
    );
    expect(windowService.isDetached()).toBe(false);
    expect(windowService.isDetachedHost()).toBe(false);
    expect(windowService.secondaryWindowOpen()).toBe(false);
    expect(lastChannel().posted).toEqual([]);
  });

  it('opens the dedicated route in a new window and marks the host detached', () => {
    jest.useFakeTimers();
    const windowRef = createWindowRef();
    jest.spyOn(window, 'open').mockReturnValue(windowRef as unknown as Window);
    const windowService = createService();

    expect(windowService.openDetachedWindow()).toBe(true);

    expect(windowService.isDetached()).toBe(true);
    expect(windowService.isDetachedHost()).toBe(true);
    expect(windowService.secondaryWindowOpen()).toBe(true);
    expect(lastChannel().posted).toContainEqual(
      expect.objectContaining({ type: 'detached-open', source: 'host' }),
    );
  });

  it('focuses an already open secondary window instead of opening another one', () => {
    jest.useFakeTimers();
    const windowRef = createWindowRef();
    const openSpy = jest.spyOn(window, 'open').mockReturnValue(windowRef as unknown as Window);
    const windowService = createService();

    windowService.openDetachedWindow();
    expect(windowService.openDetachedWindow()).toBe(true);

    expect(openSpy).toHaveBeenCalledTimes(1);
    expect(windowRef.focus).toHaveBeenCalledTimes(1);
  });

  it('reasserts the detached mode when the secondary window announces it is ready', () => {
    jest.useFakeTimers();
    jest.spyOn(window, 'open').mockReturnValue(createWindowRef() as unknown as Window);
    const windowService = createService();
    windowService.openDetachedWindow();

    const channel = lastChannel();
    channel.posted.length = 0;
    channel.emit(message('detached-ready'));

    expect(windowService.isDetached()).toBe(true);
    expect(channel.posted).toContainEqual(
      expect.objectContaining({ type: 'detached-open', source: 'host' }),
    );
  });

  it('publishes the close message and restores the dashboard on Fixar ao Dashboard', () => {
    jest.useFakeTimers();
    const windowRef = createWindowRef();
    jest.spyOn(window, 'open').mockReturnValue(windowRef as unknown as Window);
    const windowService = createService();
    windowService.openDetachedWindow();

    windowService.closeDetachedWindow();

    expect(lastChannel().posted).toContainEqual(
      expect.objectContaining({ type: 'detached-close', source: 'host' }),
    );
    expect(windowService.isDetached()).toBe(false);
    expect(windowService.secondaryWindowOpen()).toBe(false);
    expect(windowRef.close).toHaveBeenCalled();
  });

  it('detects a secondary window closed by the user and restores the host board', () => {
    jest.useFakeTimers();
    const windowRef = createWindowRef();
    jest.spyOn(window, 'open').mockReturnValue(windowRef as unknown as Window);
    const windowService = createService();
    windowService.openDetachedWindow();

    windowRef.closed = true;
    jest.advanceTimersByTime(1_000);

    expect(windowService.isDetached()).toBe(false);
    expect(windowService.isDetachedHost()).toBe(false);
    expect(windowService.secondaryWindowOpen()).toBe(false);
    expect(lastChannel().posted).toContainEqual(
      expect.objectContaining({ type: 'secondary-closed', source: 'host' }),
    );
  });

  it('restores the dashboard on the secondary-closed message without a monitored window', () => {
    const windowService = createService();
    windowService.isDetached.set(true);
    windowService.secondaryWindowOpen.set(true);
    const channel = lastChannel();

    channel.emit(message('secondary-closed', 'secondary'));

    expect(windowService.isDetached()).toBe(false);
    expect(windowService.secondaryWindowOpen()).toBe(false);
    // The host must not answer the restore message, otherwise the two
    // instances would keep resetting each other.
    expect(channel.posted).toEqual([]);
  });

  it('publishes secondary-closed and restores the host when the secondary view is released', () => {
    const closeSpy = jest.spyOn(window, 'close').mockImplementation(jest.fn());
    const windowService = createService();
    windowService.markDetachedRoute();
    windowService.isDetached.set(true);
    const channel = lastChannel();
    channel.posted.length = 0;

    windowService.releaseDetachedRoute();
    // Releasing twice must not duplicate the restore message.
    windowService.releaseDetachedRoute();

    expect(channel.posted).toEqual([
      expect.objectContaining({ type: 'secondary-closed', source: 'secondary' }),
    ]);
    expect(windowService.isDetachedRoute()).toBe(false);
    expect(windowService.isDetached()).toBe(false);
    expect(windowService.secondaryWindowOpen()).toBe(false);
    expect(closeSpy).not.toHaveBeenCalled();
    expect(channel.closed).toBe(false);
  });

  it('registers the detached route again after it was released', () => {
    const windowService = createService();
    windowService.markDetachedRoute();
    windowService.releaseDetachedRoute();
    const channel = lastChannel();
    channel.posted.length = 0;

    windowService.markDetachedRoute();

    expect(windowService.isDetachedRoute()).toBe(true);
    expect(channel.posted).toEqual([
      expect.objectContaining({ type: 'detached-ready', source: 'secondary' }),
    ]);
  });

  it('keeps the detached mode normal when releasing from a non-detached view', () => {
    const windowService = createService();
    windowService.isDetached.set(true);
    const channel = lastChannel();

    windowService.releaseDetachedRoute();

    expect(channel.posted).toEqual([]);
    expect(windowService.isDetached()).toBe(true);
  });

  it('registers the secondary route and publishes detached-ready', () => {
    const windowService = createService();

    windowService.markDetachedRoute();

    expect(windowService.isDetachedRoute()).toBe(true);
    expect(windowService.isDetachedHost()).toBe(false);
    expect(lastChannel().posted).toContainEqual(
      expect.objectContaining({ type: 'detached-ready', source: 'secondary' }),
    );
  });

  it('closes the secondary instance when the dashboard publishes detached-close', () => {
    const closeSpy = jest.spyOn(window, 'close').mockImplementation(jest.fn());
    const windowService = createService();
    windowService.markDetachedRoute();
    windowService.isDetached.set(true);

    lastChannel().emit(message('detached-close', 'host'));

    expect(windowService.isDetached()).toBe(false);
    expect(closeSpy).toHaveBeenCalled();
  });

  it('falls back to the storage event when BroadcastChannel is unavailable', () => {
    delete (globalThis as { BroadcastChannel?: unknown }).BroadcastChannel;
    const windowService = createService();
    const windowRef = createWindowRef();
    jest.spyOn(window, 'open').mockReturnValue(windowRef as unknown as Window);
    jest.useFakeTimers();

    expect(windowService.openDetachedWindow()).toBe(true);

    const stored = JSON.parse(localStorage.getItem(SELLER_ORDERS_WINDOW_CHANNEL) ?? '{}') as SellerOrdersWindowMessage;
    expect(stored).toMatchObject({ type: 'detached-open', source: 'host' });

    window.dispatchEvent(new StorageEvent('storage', {
      key: SELLER_ORDERS_WINDOW_CHANNEL,
      newValue: JSON.stringify(message('detached-close', 'secondary')),
    }));

    expect(windowService.isDetached()).toBe(false);
    expect(windowService.secondaryWindowOpen()).toBe(false);
  });

  it('ignores malformed storage fallback payloads', () => {
    delete (globalThis as { BroadcastChannel?: unknown }).BroadcastChannel;
    const windowService = createService();
    windowService.isDetached.set(true);

    expect(() => window.dispatchEvent(new StorageEvent('storage', {
      key: SELLER_ORDERS_WINDOW_CHANNEL,
      newValue: '{not-json',
    }))).not.toThrow();

    expect(windowService.isDetached()).toBe(true);
  });

  it('cleans up the channel, the storage listener and the closed-window monitor on dispose', () => {
    jest.useFakeTimers();
    jest.spyOn(window, 'open').mockReturnValue(createWindowRef() as unknown as Window);
    const removeListenerSpy = jest.spyOn(window, 'removeEventListener');
    const windowService = createService();
    windowService.openDetachedWindow();
    const channel = lastChannel();
    channel.posted.length = 0;

    windowService.dispose();

    expect(channel.closed).toBe(true);
    expect(removeListenerSpy).toHaveBeenCalledWith('storage', expect.any(Function));
    expect(windowService.isDetached()).toBe(false);
    expect(windowService.secondaryWindowOpen()).toBe(false);

    jest.advanceTimersByTime(5_000);
    expect(channel.posted).toEqual([]);
  });
});
