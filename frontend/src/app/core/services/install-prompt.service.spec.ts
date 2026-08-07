import { TestBed } from '@angular/core/testing';
import { InstallPromptService } from './install-prompt.service';

describe('InstallPromptService', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({});
  });

  it('captures beforeinstallprompt and exposes install availability', () => {
    const service = TestBed.inject(InstallPromptService);
    const event = new Event('beforeinstallprompt') as any;
    event.preventDefault = jest.fn();
    event.prompt = jest.fn().mockResolvedValue(undefined);
    event.userChoice = Promise.resolve({ outcome: 'accepted' });

    window.dispatchEvent(event);

    expect(event.preventDefault).toHaveBeenCalled();
    expect(service.canInstall()).toBe(true);
  });

  it('falls back when browser has no install prompt', () => {
    const service = TestBed.inject(InstallPromptService);

    expect(service.canInstall()).toBe(false);
    expect(service.fallbackMessage()).toContain('menu do navegador');
  });
});
