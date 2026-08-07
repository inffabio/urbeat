import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { InstallPromptService } from '../../core/services/install-prompt.service';
import { SellerInstallPageComponent } from './seller-install-page.component';

describe('SellerInstallPageComponent', () => {
  let installPromptMock: { canInstall: jest.Mock; isInstalled: jest.Mock; fallbackMessage: jest.Mock; promptInstall: jest.Mock };

  beforeEach(async () => {
    installPromptMock = {
      canInstall: jest.fn(() => false),
      isInstalled: jest.fn(() => false),
      fallbackMessage: jest.fn(() => 'Use o menu do navegador para instalar o Urbeat.'),
      promptInstall: jest.fn().mockResolvedValue(false),
    };

    await TestBed.configureTestingModule({
      imports: [SellerInstallPageComponent],
      providers: [provideRouter([]), { provide: InstallPromptService, useValue: installPromptMock }],
    }).compileComponents();
  });

  it('shows browser fallback when install prompt is unavailable', () => {
    const fixture = TestBed.createComponent(SellerInstallPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Instalar aplicativo');
    expect(fixture.nativeElement.textContent).toContain('Plataformas recomendadas');
    expect(fixture.nativeElement.textContent).toContain('Baixar agents locais');
    expect(fixture.nativeElement.textContent).toContain('Baixar agent para Windows');
    expect(fixture.nativeElement.textContent).toContain('Baixar agent para Linux');
    expect(fixture.nativeElement.textContent).toContain('Driver Windows da POS-58');
    expect(fixture.nativeElement.textContent).toContain('Ordem ideal de instalação');
    expect(fixture.nativeElement.textContent).toContain('Plataformas recomendadas');
    expect(fixture.nativeElement.textContent).toContain('Use o menu do navegador para instalar o Urbeat.');
  });

  it('prompts installation when browser supports it', () => {
    installPromptMock.canInstall.mockReturnValue(true);
    const fixture = TestBed.createComponent(SellerInstallPageComponent);
    fixture.detectChanges();

    fixture.nativeElement.querySelector('.btn-instalar').click();

    expect(installPromptMock.promptInstall).toHaveBeenCalled();
  });
});
