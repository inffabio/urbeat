import { TestBed } from '@angular/core/testing';
import * as fs from 'fs';
import * as path from 'path';
import { ORDER_SOUND_STORAGE_KEY, OrderSoundAlertService } from './order-sound-alert.service';

const SOUND_KEY = ORDER_SOUND_STORAGE_KEY;

describe('OrderSoundAlertService', () => {
  const createService = (): OrderSoundAlertService => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({});
    return TestBed.inject(OrderSoundAlertService);
  };

  const emitStorage = (newValue: string | null): void => {
    window.dispatchEvent(new StorageEvent('storage', { key: SOUND_KEY, newValue }));
  };

  interface PendingPlayback {
    promise: Promise<void>;
    resolve: () => void;
    reject: (error: unknown) => void;
  }

  const deferredPlayback = (): PendingPlayback => {
    let resolve!: () => void;
    let reject!: (error: unknown) => void;
    const promise = new Promise<void>((res, rej) => {
      resolve = res;
      reject = rej;
    });
    return { promise, resolve, reject };
  };

  const asPlayResult = (pending: PendingPlayback): Promise<void> => pending.promise;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({});
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('defaults to enabled with the activation invite when no preference was ever stored', () => {
    const service = createService();

    expect(service.enabled()).toBe(true);
    // The browser still needs a gesture to unlock audio, so the invite stays visible
    // until a real playback succeeds. Enabling by default never promises sound.
    expect(service.needsActivation()).toBe(true);
    // Reading the default must not create the key: it distinguishes "never chose" from
    // an explicit preference, and keeps future preference values intact.
    expect(localStorage.getItem(SOUND_KEY)).toBeNull();
  });

  it('keeps an explicit off preference and starts silent', () => {
    localStorage.setItem(SOUND_KEY, 'off');

    const service = createService();

    expect(service.enabled()).toBe(false);
    expect(service.needsActivation()).toBe(false);
    expect(localStorage.getItem(SOUND_KEY)).toBe('off');
  });

  it('keeps an explicit on preference but still offers activation after a reload', () => {
    localStorage.setItem(SOUND_KEY, 'on');

    const service = createService();

    // A persisted preference proves the seller wants sound, not that this new
    // document is allowed to autoplay. Offering activation keeps the UI honest.
    expect(service.enabled()).toBe(true);
    expect(service.needsActivation()).toBe(true);
    expect(localStorage.getItem(SOUND_KEY)).toBe('on');
  });

  it('preserves an unknown future preference instead of overwriting it on boot', () => {
    localStorage.setItem(SOUND_KEY, 'notify-v2');

    const service = createService();

    expect(service.enabled()).toBe(true);
    expect(service.needsActivation()).toBe(true);
    expect(localStorage.getItem(SOUND_KEY)).toBe('notify-v2');
  });

  it('falls back to the enabled default when storage is unavailable', () => {
    jest.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('storage disabled');
    });

    const service = createService();

    expect(service.enabled()).toBe(true);
    expect(service.needsActivation()).toBe(true);
  });

  it('keeps the session choice in memory when storage rejects writes', () => {
    jest.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('quota exceeded');
    });
    const service = createService();

    expect(() => service.disable()).not.toThrow();
    expect(service.enabled()).toBe(false);
    expect(service.needsActivation()).toBe(false);
  });

  it('should persist enabled preference when enabled', async () => {
    jest.spyOn(window.HTMLMediaElement.prototype, 'play').mockResolvedValueOnce(undefined);
    const service = createService();

    const enabled = await service.enable();

    expect(enabled).toBe(true);
    expect(service.enabled()).toBe(true);
    expect(service.needsActivation()).toBe(false);
    expect(localStorage.getItem(SOUND_KEY)).toBe('on');
  });

  it('should report playback failure when audio is blocked during enable', async () => {
    jest.spyOn(window.HTMLMediaElement.prototype, 'play').mockRejectedValue(new DOMException('blocked'));
    const service = createService();

    const enabled = await service.enable();

    expect(enabled).toBe(false);
    expect(service.enabled()).toBe(true);
    expect(service.needsActivation()).toBe(true);
    expect(localStorage.getItem(SOUND_KEY)).toBe('on');
  });

  it('should return false when audio playback is blocked', async () => {
    const service = createService();
    jest.spyOn(window.HTMLMediaElement.prototype, 'play').mockRejectedValue(new DOMException('blocked'));

    await service.enable();
    const played = await service.playNewOrder();

    expect(played).toBe(false);
    expect(service.needsActivation()).toBe(true);
  });

  it('should clear the activation request once a later playback attempt succeeds', async () => {
    jest
      .spyOn(window.HTMLMediaElement.prototype, 'play')
      .mockRejectedValueOnce(new DOMException('blocked'))
      .mockResolvedValueOnce(undefined);
    const service = createService();

    await service.enable();
    expect(service.needsActivation()).toBe(true);

    const played = await service.playNewOrder();

    expect(played).toBe(true);
    expect(service.needsActivation()).toBe(false);
  });

  it('should not attempt playback while disabled', async () => {
    localStorage.setItem(SOUND_KEY, 'off');
    const play = jest.spyOn(window.HTMLMediaElement.prototype, 'play');
    const service = createService();

    const played = await service.playNewOrder();

    expect(played).toBe(false);
    expect(play).not.toHaveBeenCalled();
    expect(service.needsActivation()).toBe(false);
  });

  it('should disable sound, persist the choice, and clear the activation request', async () => {
    jest.spyOn(window.HTMLMediaElement.prototype, 'play').mockResolvedValueOnce(undefined);
    const service = createService();
    await service.enable();
    expect(service.needsActivation()).toBe(false);

    service.disable();

    expect(service.enabled()).toBe(false);
    expect(service.needsActivation()).toBe(false);
    expect(localStorage.getItem(SOUND_KEY)).toBe('off');
  });

  it('keeps offering activation after a reload when playback is still blocked', async () => {
    localStorage.setItem(SOUND_KEY, 'on');
    const play = jest
      .spyOn(window.HTMLMediaElement.prototype, 'play')
      .mockRejectedValue(new DOMException('blocked'));
    const service = createService();

    expect(service.enabled()).toBe(true);
    expect(service.needsActivation()).toBe(true);

    const played = await service.playNewOrder();

    expect(played).toBe(false);
    expect(service.needsActivation()).toBe(true);
    expect(localStorage.getItem(SOUND_KEY)).toBe('on');
    expect(play).toHaveBeenCalled();
  });

  describe('stale playback results', () => {
    it('ignores a rejection from an attempt cancelled by a local disable', async () => {
      const attempt = deferredPlayback();
      jest
        .spyOn(window.HTMLMediaElement.prototype, 'play')
        .mockReturnValueOnce(asPlayResult(attempt));
      const service = createService();

      const playing = service.playNewOrder();
      service.disable();
      attempt.reject(new DOMException('aborted'));

      await expect(playing).resolves.toBe(false);
      expect(service.enabled()).toBe(false);
      // A cancelled attempt must never bring the activation invite back.
      expect(service.needsActivation()).toBe(false);
      expect(localStorage.getItem(SOUND_KEY)).toBe('off');
    });

    it('ignores a rejection from an attempt cancelled by the other window', async () => {
      const attempt = deferredPlayback();
      jest
        .spyOn(window.HTMLMediaElement.prototype, 'play')
        .mockReturnValueOnce(asPlayResult(attempt));
      const service = createService();

      const playing = service.playNewOrder();
      emitStorage('off');
      attempt.reject(new DOMException('aborted'));

      await expect(playing).resolves.toBe(false);
      expect(service.enabled()).toBe(false);
      expect(service.needsActivation()).toBe(false);
    });

    it('does not unlock this document when a stale success lands after disable', async () => {
      const attempt = deferredPlayback();
      jest
        .spyOn(window.HTMLMediaElement.prototype, 'play')
        .mockReturnValueOnce(asPlayResult(attempt));
      const service = createService();

      const playing = service.playNewOrder();
      service.disable();
      attempt.resolve();

      await expect(playing).resolves.toBe(false);
      expect(service.enabled()).toBe(false);

      // If the stale success had unlocked playback, enabling from another window
      // would silently skip the activation invite in this document.
      emitStorage('on');
      expect(service.needsActivation()).toBe(true);
    });

    it('ignores a stale rejection when a newer attempt already succeeded', async () => {
      const first = deferredPlayback();
      const second = deferredPlayback();
      jest
        .spyOn(window.HTMLMediaElement.prototype, 'play')
        .mockReturnValueOnce(asPlayResult(first))
        .mockReturnValueOnce(asPlayResult(second));
      const service = createService();

      const firstPlay = service.playNewOrder();
      const secondPlay = service.playNewOrder();

      second.resolve();
      await expect(secondPlay).resolves.toBe(true);
      expect(service.needsActivation()).toBe(false);

      first.reject(new DOMException('aborted'));
      await expect(firstPlay).resolves.toBe(false);
      expect(service.needsActivation()).toBe(false);
    });
  });

  describe('preference sync between the dashboard and the detached window', () => {
    it('applies an off preference from the other window without audio or writes', async () => {
      const play = jest.spyOn(window.HTMLMediaElement.prototype, 'play');
      const setItem = jest.spyOn(Storage.prototype, 'setItem');
      const service = createService();
      expect(service.enabled()).toBe(true);

      emitStorage('off');

      expect(service.enabled()).toBe(false);
      expect(service.needsActivation()).toBe(false);
      expect(play).not.toHaveBeenCalled();
      expect(setItem).not.toHaveBeenCalled();
    });

    it('applies an on preference from the other window without audio or writes', () => {
      localStorage.setItem(SOUND_KEY, 'off');
      const play = jest.spyOn(window.HTMLMediaElement.prototype, 'play');
      const setItem = jest.spyOn(Storage.prototype, 'setItem');
      const service = createService();
      expect(service.enabled()).toBe(false);

      emitStorage('on');

      expect(service.enabled()).toBe(true);
      // This document still has to unlock autoplay itself.
      expect(service.needsActivation()).toBe(true);
      expect(play).not.toHaveBeenCalled();
      expect(setItem).not.toHaveBeenCalled();
    });

    it('does not ask for activation again when this document already played', async () => {
      jest.spyOn(window.HTMLMediaElement.prototype, 'play').mockResolvedValue(undefined);
      const service = createService();
      await service.enable();
      expect(service.needsActivation()).toBe(false);

      emitStorage('off');
      emitStorage('on');

      expect(service.enabled()).toBe(true);
      expect(service.needsActivation()).toBe(false);
    });

    it('returns to the enabled default when the key is removed elsewhere', () => {
      localStorage.setItem(SOUND_KEY, 'off');
      const service = createService();
      expect(service.enabled()).toBe(false);

      emitStorage(null);

      expect(service.enabled()).toBe(true);
      expect(service.needsActivation()).toBe(true);
    });

    it('ignores storage events from unrelated keys', () => {
      const service = createService();

      window.dispatchEvent(new StorageEvent('storage', { key: 'other-key', newValue: 'off' }));

      expect(service.enabled()).toBe(true);
    });

    it('does not throw when storage is unavailable and an event arrives', () => {
      jest.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
        throw new DOMException('storage disabled');
      });
      const service = createService();

      expect(() => emitStorage('off')).not.toThrow();
      expect(service.enabled()).toBe(false);
    });
  });
});

describe('new-order sound asset', () => {
  it('should ship the mp3 at the path the service URL resolves to', () => {
    const expectedAsset = path.resolve(__dirname, '../../../assets/sounds/new-order.mp3');

    expect(fs.existsSync(expectedAsset)).toBe(true);
    expect(fs.statSync(expectedAsset).size).toBeGreaterThan(0);
  });
});
