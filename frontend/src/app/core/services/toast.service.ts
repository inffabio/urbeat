import { Injectable, inject } from '@angular/core';
import { ToastController } from '@ionic/angular/standalone';

export type ToastType = 'error' | 'success' | 'warning' | 'info';

export interface ToastLine {
  type: ToastType;
  text: string;
}

@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly toastController = inject(ToastController);
  private current: HTMLIonToastElement | null = null;

  /** Sinal exibido no início de cada linha da mensagem. */
  private readonly signals: Record<ToastType, string> = {
    error: '\u274C',   // ❌
    warning: '\u26A0\uFE0F', // ⚠️
    success: '\u2705', // ✅
    info: '\u2139\uFE0F',    // ℹ️
  };

  private readonly severityRank: Record<ToastType, number> = {
    error: 3,
    warning: 2,
    success: 1,
    info: 0,
  };

  /** Fecha a mensagem atual antes de abrir outra (evita sobreposição na mesma posição). */
  private async dismissCurrent(): Promise<void> {
    const toast = this.current;
    this.current = null;
    if (toast?.dismiss) {
      try {
        await toast.dismiss();
      } catch {
        /* ignore */
      }
    }
  }

  private async present(
    message: string,
    type: ToastType,
    duration: number,
    extraClass = '',
  ): Promise<void> {
    await this.dismissCurrent();

    const cssClass = `urbeat-toast urbeat-toast-${type}${extraClass ? ` ${extraClass}` : ''}`;
    const toast = await this.toastController.create({
      message,
      duration,
      position: 'top',
      cssClass,
      buttons: [{ icon: 'close', role: 'cancel' }],
    });

    this.current = toast;
    toast.onDidDismiss?.().then(() => {
      if (this.current === toast) this.current = null;
    });

    await toast.present();
  }

  showError(message: string, duration = 4000): Promise<void> {
    return this.present(message, 'error', duration);
  }
  showSuccess(message: string, duration = 4000): Promise<void> {
    return this.present(message, 'success', duration);
  }
  showWarning(message: string, duration = 4000): Promise<void> {
    return this.present(message, 'warning', duration);
  }
  showInfo(message: string, duration = 4000): Promise<void> {
    return this.present(message, 'info', duration);
  }

  /**
   * Mostra várias mensagens numa ÚNICA caixa, cada linha com seu sinal
   * (❌ erro, ⚠️ aviso, ✅ ok, ℹ️ info). A cor da caixa segue a linha
   * mais severa. Duração maior por padrão (20s) por conter mais texto.
   *
   * Princípio único para todo o sistema: em vez de disparar vários toasts
   * que se cobrem na mesma posição, agrupe tudo em uma mensagem só.
   */
  showGrouped(lines: ToastLine[], duration = 20000): Promise<void> {
    const filtered = lines.filter((l) => l.text?.trim());
    if (filtered.length === 0) return Promise.resolve();

    const message = filtered
      .map((l) => `${this.signals[l.type]} ${l.text}`)
      .join('\n');

    const severity = filtered.reduce<ToastType>(
      (acc, l) => (this.severityRank[l.type] > this.severityRank[acc] ? l.type : acc),
      'info',
    );

    return this.present(message, severity, duration, 'urbeat-toast-grouped');
  }
}
