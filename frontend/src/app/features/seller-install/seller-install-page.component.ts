import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { InstallPromptService } from '../../core/services/install-prompt.service';

interface AgentDownloadOption {
  label: string;
  platform: string;
  format: string;
  href: string;
  description: string;
  badges: string[];
}

interface InstallStep {
  number: string;
  title: string;
  description: string;
  emphasis?: string;
}

interface PlatformGuide {
  title: string;
  recommendation: string;
  detail: string;
  tone: 'neutral' | 'recommended' | 'warning';
}

@Component({
  selector: 'app-seller-install-page',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './seller-install-page.component.html',
  styleUrl: './seller-install-page.component.scss',
})
export class SellerInstallPageComponent {
  readonly installPrompt = inject(InstallPromptService);
  readonly installing = signal(false);
  readonly installSteps: InstallStep[] = [
    {
      number: '01',
      title: 'Instale o painel',
      description: 'Fixe o Urbeat na tela inicial do dispositivo para deixar o dashboard sempre pronto durante o expediente.',
      emphasis: 'PWA seller',
    },
    {
      number: '02',
      title: 'Prepare a impressora',
      description: 'No Windows, instale primeiro o driver da POS-58. Depois baixe o local-agent da plataforma da loja.',
      emphasis: 'POS-58 primeiro',
    },
    {
      number: '03',
      title: 'Finalize no dashboard',
      description: 'Abra Configurar impressão, selecione a impressora da loja e deixe o aceite do pedido imprimir automaticamente.',
      emphasis: 'Sem popup no aceite',
    },
  ];
  readonly platformGuides: PlatformGuide[] = [
    {
      title: 'Android',
      recommendation: 'Bluetooth + POS-58',
      detail: 'Melhor opção para balcão móvel ou operação com tablet/celular.',
      tone: 'recommended',
    },
    {
      title: 'Windows',
      recommendation: 'Driver POS-58 + local-agent',
      detail: 'Fluxo mais automático e robusto para computador fixo da loja.',
      tone: 'recommended',
    },
    {
      title: 'Linux',
      recommendation: 'CUPS + local-agent',
      detail: 'Padrão oficial do Urbeat para mini PC e terminais Linux.',
      tone: 'neutral',
    },
    {
      title: 'Navegador',
      recommendation: 'Fallback manual',
      detail: 'Use só como contingência. Automático de verdade apenas em kiosk/silent print.',
      tone: 'warning',
    },
  ];
  readonly agentDownloads: AgentDownloadOption[] = [
    {
      label: 'Baixar agent para Windows',
      platform: 'Windows',
      format: '.zip/.exe',
      href: '/downloads/urbeat-print-agent/windows/Urbeat.PrintAgent-win-x64.zip',
      description: 'Recomendado para balcão com PC fixo e impressão automática via local-agent.',
      badges: ['Recomendado', 'Desktop', 'Automático'],
    },
    {
      label: 'Baixar agent para Linux',
      platform: 'Linux',
      format: '.tar.gz',
      href: '/downloads/urbeat-print-agent/linux/Urbeat.PrintAgent-linux-x64.tar.gz',
      description: 'Use em mini PC ou terminal Linux com impressão local automática.',
      badges: ['Linux', 'CUPS', 'Automático'],
    },
  ];
  readonly agentGuideHref = '/downloads/urbeat-print-agent/README.md';
  readonly pos58DriverHref = '/downloads/POSPrinterDriverSetup58mm.exe';
  readonly linuxCupsGuideHref = '/downloads/urbeat-print-agent/linux/README.md';

  async install(): Promise<void> {
    if (!this.installPrompt.canInstall() || this.installing()) return;

    this.installing.set(true);
    try {
      await this.installPrompt.promptInstall();
    } finally {
      this.installing.set(false);
    }
  }
}
