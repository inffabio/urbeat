import { Injectable, signal } from '@angular/core';

const SOUND_KEY = 'urbeat:seller-order-sound';

@Injectable({ providedIn: 'root' })
export class OrderSoundAlertService {
  readonly enabled = signal(localStorage.getItem(SOUND_KEY) === 'on');
  readonly needsActivation = signal(false);

  private readonly audio = new Audio('/assets/sounds/new-order.mp3');

  async enable(): Promise<boolean> {
    this.enabled.set(true);
    localStorage.setItem(SOUND_KEY, 'on');
    const played = await this.playNewOrder();
    this.needsActivation.set(!played);
    return true;
  }

  disable(): void {
    this.enabled.set(false);
    this.needsActivation.set(false);
    localStorage.setItem(SOUND_KEY, 'off');
  }

  async playNewOrder(): Promise<boolean> {
    if (!this.enabled()) return false;

    try {
      this.audio.currentTime = 0;
      await this.audio.play();
      this.needsActivation.set(false);
      return true;
    } catch {
      this.needsActivation.set(true);
      return false;
    }
  }
}
