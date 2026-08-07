import { Injectable, signal } from '@angular/core';

interface BeforeInstallPromptEvent extends Event {
  prompt: () => Promise<void>;
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed'; platform?: string }>;
}

@Injectable({ providedIn: 'root' })
export class InstallPromptService {
  private readonly deferredPrompt = signal<BeforeInstallPromptEvent | null>(null);

  readonly canInstall = signal(false);
  readonly isInstalled = signal(window.matchMedia?.('(display-mode: standalone)').matches ?? false);

  constructor() {
    window.addEventListener('beforeinstallprompt', (event) => {
      event.preventDefault();
      this.deferredPrompt.set(event as BeforeInstallPromptEvent);
      this.canInstall.set(true);
    });

    window.addEventListener('appinstalled', () => {
      this.deferredPrompt.set(null);
      this.canInstall.set(false);
      this.isInstalled.set(true);
    });
  }

  fallbackMessage(): string {
    return 'Use o menu do navegador para instalar o Urbeat na tela inicial quando esta opcao estiver disponivel.';
  }

  async promptInstall(): Promise<boolean> {
    const prompt = this.deferredPrompt();
    if (!prompt) return false;

    await prompt.prompt();
    const choice = await prompt.userChoice;
    this.deferredPrompt.set(null);
    this.canInstall.set(false);
    return choice.outcome === 'accepted';
  }
}
