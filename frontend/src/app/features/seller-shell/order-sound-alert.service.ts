import { DestroyRef, Injectable, inject, signal } from '@angular/core';

export const ORDER_SOUND_STORAGE_KEY = 'urbeat:seller-order-sound';

type StoredSoundPreference = 'on' | 'off';

/**
 * Reads the persisted preference without ever throwing: private mode, blocked
 * third-party storage and hardened webviews can all deny `localStorage`.
 */
function readStoredPreference(): string | null {
  try {
    return localStorage.getItem(ORDER_SOUND_STORAGE_KEY);
  } catch {
    return null;
  }
}

function persistPreference(value: StoredSoundPreference): void {
  try {
    localStorage.setItem(ORDER_SOUND_STORAGE_KEY, value);
  } catch {
    // Storage unavailable: keep the in-memory choice for the current session.
  }
}

@Injectable({ providedIn: 'root' })
export class OrderSoundAlertService {
  private readonly destroyRef = inject(DestroyRef);
  private readonly storedPreference = readStoredPreference();
  /** Proof that this document (not the stored preference) is allowed to play. */
  private playbackUnlocked = false;
  /**
   * Monotonic token for playback attempts. Any state change that can invalidate
   * an in-flight `play()` (disable, another window's preference, a newer
   * attempt) increments it, so a late resolution/rejection never rewrites the
   * current state.
   */
  private playbackSequence = 0;
  private storageListener?: (event: StorageEvent) => void;

  /**
   * New order alerts ship enabled for every seller who never chose: the key is
   * only written after an explicit on/off. An unknown value (a future preference
   * shape) is treated as "no explicit choice" and is never overwritten on boot,
   * so a newer client keeps ownership of it.
   */
  readonly enabled = signal(this.storedPreference !== 'off');

  /**
   * A stored `on` is a wish, not proof that this document may autoplay: browsers
   * only allow audio after a gesture. The invite therefore starts visible for
   * every enabled session and disappears only after a real playback success,
   * never promising sound that the browser may still block.
   */
  readonly needsActivation = signal(this.storedPreference !== 'off');

  private readonly audio = new Audio('/assets/sounds/new-order.mp3');

  constructor() {
    this.listenForPreferenceChanges();
  }

  async enable(): Promise<boolean> {
    this.enabled.set(true);
    persistPreference('on');
    // playNewOrder owns `needsActivation`: it clears the invite only when the
    // browser really played the sound.
    return this.playNewOrder();
  }

  disable(): void {
    // Invalidate first: an aborted `play()` rejects asynchronously and must not
    // bring the activation invite back after the seller muted the sound.
    this.playbackSequence += 1;
    this.stopPlayback();
    this.enabled.set(false);
    this.needsActivation.set(false);
    persistPreference('off');
  }

  async playNewOrder(): Promise<boolean> {
    if (!this.enabled()) return false;

    const attempt = ++this.playbackSequence;
    try {
      this.audio.currentTime = 0;
      await this.audio.play();
      if (!this.isCurrentAttempt(attempt)) return false;
      this.playbackUnlocked = true;
      this.needsActivation.set(false);
      return true;
    } catch {
      if (!this.isCurrentAttempt(attempt)) return false;
      this.needsActivation.set(true);
      return false;
    }
  }

  /** A result is only applied while it still belongs to the newest attempt. */
  private isCurrentAttempt(attempt: number): boolean {
    return attempt === this.playbackSequence && this.enabled();
  }

  private stopPlayback(): void {
    try {
      if (!this.audio.paused) this.audio.pause();
    } catch {
      // Media not ready or already detached; the sequence guard already applies.
    }
    try {
      this.audio.currentTime = 0;
    } catch {
      // Ignore: nothing to reset when the element is not loaded.
    }
  }

  /**
   * Keeps the dashboard and the detached orders window on the same preference.
   * The window that wrote the value owns it: this side only applies the change,
   * never writes back and never plays audio (autoplay is per document and still
   * needs its own gesture here).
   */
  private listenForPreferenceChanges(): void {
    if (typeof window === 'undefined' || typeof window.addEventListener !== 'function') return;

    this.storageListener = (event: StorageEvent) => {
      if (event.key !== ORDER_SOUND_STORAGE_KEY) return;

      // The other window's choice invalidates any in-flight playback result here.
      this.playbackSequence += 1;
      const enabled = event.newValue !== 'off';
      this.enabled.set(enabled);
      this.needsActivation.set(enabled && !this.playbackUnlocked);
    };

    window.addEventListener('storage', this.storageListener);
    this.destroyRef.onDestroy(() => {
      if (!this.storageListener) return;
      window.removeEventListener('storage', this.storageListener);
      this.storageListener = undefined;
    });
  }
}
