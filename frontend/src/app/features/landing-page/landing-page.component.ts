import { Component, OnInit, inject, signal, ViewEncapsulation } from '@angular/core';
import { CommonModule, DOCUMENT } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Title, Meta } from '@angular/platform-browser';

import { LandingPageService, LandingPageContent } from '../../core/services/landing-page.service';

/*
  ── DIRECTION CONTRACT (landpage UrBeat Green) ─────────────────────────────
  THESIS: Vende o app próprio de delivery para lojistas iniciantes e recusa o
  layout genérico de marketplaces gigantes; toda conversão passa pelo
  "Criar meu cardápio" e pelo "Entrar".
  OWN-WORLD: UrBeat Green — #6EAF4A sólido sobre branco/#F9FAFB, cartões
  escuros zinc-900, radius 24–40px, sombras verdes suaves, badges pill
  #E8F5E9 com texto #2E7D32, tipografia Plus Jakarta Sans (var(--app-font)).
  STORY: O lojista entende que monta um app próprio sem taxa, em 15 minutos e
  sem cartão; acredita nos 4 passos e age: cria o cardápio ou entra.
  FIRST VIEWPORT: header sticky branco (logo, nav, "Entrar", CTA verde pill);
  hero em 2 colunas — à esquerda badge pulsante + H1 48px + bullets de
  confiança; à direita foto da empreendedora com cards flutuantes.
  FORM: réplica fiel do protótipo React, em Angular standalone mobile-first,
  conteúdo editável pelo admin via LandingPageService (fallbacks do protótipo).
  ────────────────────────────────────────────────────────────────────────────
*/

export interface LandingFeature {
  icon: string;
  title: string;
  contentKey: string;
  fallback: string;
}

@Component({
  selector: 'app-landing-page',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './landing-page.component.html',
  styleUrls: ['./landing-page.component.scss'],
  encapsulation: ViewEncapsulation.None,
  host: { '[class.urbeat-onboarding]': 'true' },
})
export class LandingPageComponent implements OnInit {
  private readonly landingPageService = inject(LandingPageService);
  private readonly title = inject(Title);
  private readonly meta = inject(Meta);
  private readonly document = inject(DOCUMENT);

  readonly content = signal<LandingPageContent[]>([]);
  readonly loading = signal(true);
  readonly menuOpen = signal(false);
  readonly currentYear = new Date().getFullYear();

  readonly features: LandingFeature[] = [
    { icon: 'store', title: 'App com sua marca', contentKey: 'AppBrand_Desc', fallback: 'Seus clientes usam seu app, com sua marca. Sem necessidade de você dividir seu lucro.' },
    { icon: 'whatsapp', title: 'Cardápio inteligente pro WhatsApp', contentKey: 'WhatsappMenu_Desc', fallback: 'Link único que abre cardápio lindo e já manda pedido formatado no WhatsApp. Zero fricção.' },
    { icon: 'dashboard', title: 'Painel de pedidos em tempo real', contentKey: 'OrdersPanel_Desc', fallback: 'Aceite, acompanhe e dispare entregas. Som de novo pedido, tudo organizado.' },
    { icon: 'printer', title: 'Impressão automática na cozinha', contentKey: 'AutoPrint_Desc', fallback: 'Pediu, imprimiu. Integra com impressora térmica via Wi-Fi. Sem erro, sem atraso.' },
    { icon: 'sliders', title: 'Gestão sem stress', contentKey: 'EasyMgmt_Desc', fallback: 'Cardápio, preços, horários e taxas num painel simples. Edita e publica em segundos.' },
    { icon: 'chart', title: 'Relatórios e base de clientes', contentKey: 'Reports_Desc', fallback: 'Veja quem compra, quanto gasta, qual horário vende mais. Exporte base e faça reengajamento.' },
  ];

  readonly steps = [
    { n: '01', icon: 'store', title: 'Você mesmo cadastra sua loja', desc: 'Temos manual passo a passo em vídeo. Em 15 minutos sua loja está pronta.' },
    { n: '02', icon: 'smartphone', title: 'Divulga o link do seu app', desc: 'Um link único. Coloca no Instagram, no status do WhatsApp, onde quiser.' },
    { n: '03', icon: 'wallet', title: 'Cliente escolhe, informa nome, endereço e pagamento', desc: 'Cardápio lindo, pedido formatado, sem bagunça.' },
    { n: '04', icon: 'printer', title: 'Pedido chega no painel e imprime. Cliente acompanha status', desc: 'Você aceita, cozinha despacha e cliente vê “em preparo” “saiu para entrega”.' },
  ];

  readonly pains = [
    { icon: 'whatsapp', tone: 'amber', title: 'Pedidos se perdem no WhatsApp', desc: 'Print confuso, cliente esperando, você anotando em papel. Um pedido errado e já perde cliente.' },
    { icon: 'smartphone', tone: 'blue', title: 'Medo de tecnologia', desc: '"Achei que precisava de programador, mas a UrBeat é tão simples quanto postar no Instagram"', sub: '— Relato de cliente iniciante' },
    { icon: 'wallet', tone: 'rose', title: 'Sem controle do que vende', desc: 'Sem saber quem comprou, quanto lucrou, sem histórico. No fim do mês, cadê o lucro?' },
  ];

  readonly starterBenefits = [
    { icon: 'badge', title: 'Sem mensalidade absurda', desc: 'Plano que cabe no bolso de quem vende da cozinha de casa. Comece pequeno, cresça com lucro.' },
    { icon: 'whatsapp', title: 'Suporte humano no WhatsApp', desc: 'Travou? Chama a gente. Suporte de verdade, com gente que entende de delivery iniciante.' },
    { icon: 'book', title: 'Manual completo para iniciantes', desc: 'Vídeos curtos e diretos. Como cadastrar produto, imprimir, configurar entrega. Sem jargão.' },
  ];

  readonly planFeatures = [
    'Cardápio personalizável',
    'Painel para gestão completa',
    'Sem limites de produtos e pedidos',
    'Impressão automática',
    'Controle via celular',
    'Aplicativo Android do painel',
    'Suporte via WhatsApp',
    'Geração de QR Code',
    'Dashboard com resumo das vendas',
    'Controle de finanças',
    'Relatórios de vendas',
    'Link para bio de redes sociais',
    'Banners no cardápio',
    'Usuários e permissões',
    'Automatização de WhatsApp',
  ];

  readonly planAssurances = [
    'Não precisa cadastrar cartão de crédito',
    'Não precisa pagar antecipado',
    'Teste primeiro e pague apenas se gostar',
  ];

  ngOnInit(): void {
    this.setupSEO();
    this.loadContent();
  }

  toggleMenu(): void {
    this.menuOpen.update((v) => !v);
  }

  private setupSEO(): void {
    this.title.setTitle('UrBeat — Cardápio digital para quem está começando');

    const metaTags = [
      { name: 'description', content: 'Crie seu app de delivery próprio, sem taxa por pedido. Cardápio digital, pedidos no WhatsApp e impressão automática na cozinha. Teste 15 dias grátis.' },
      { name: 'keywords', content: 'delivery, cardápio digital, app para delivery, pedidos online, urbeat' },
      { property: 'og:type', content: 'website' },
      { property: 'og:url', content: 'https://urbeat.com.br/' },
      { property: 'og:title', content: 'UrBeat — Nunca vendeu por delivery? Comece hoje com seu app próprio.' },
      { property: 'og:description', content: 'Sem taxa por pedido, sem complicação. Você cadastra sua loja, divulga seu link e começa a receber pedidos organizados.' },
      { property: 'og:image', content: 'https://urbeat.com.br/assets/images/empreendedora-urbeat.jpg' },
      { property: 'twitter:card', content: 'summary_large_image' },
      { property: 'twitter:title', content: 'UrBeat — Cardápio digital para iniciantes' },
      { property: 'twitter:description', content: 'Seu delivery, seu lucro. Teste 15 dias grátis, sem cartão.' },
      { property: 'twitter:image', content: 'https://urbeat.com.br/assets/images/empreendedora-urbeat.jpg' }
    ];

    metaTags.forEach(tag => {
      const key = Object.keys(tag)[0];
      const value = Object.values(tag)[0] as string;
      const meta = this.document.querySelector(`meta[${key}="${value}"]`);
      if (!meta) {
        const created = this.document.createElement('meta');
        created.setAttribute(key, value);
        this.document.head.appendChild(created);
      }
    });
  }

  private loadContent(): void {
    this.landingPageService.getAll().subscribe({
      next: (data) => {
        this.content.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
      }
    });
  }

  getContent(section: string, key: string, defaultValue: string): string {
    const item = this.content().find((c) => c.section === section && c.key === key);
    return item ? item.value : defaultValue;
  }

  /** Título do hero editável pelo admin, com destaque sobre a palavra "delivery?" quando presente. */
  heroTitleParts(): { before: string; highlight: string; after: string } {
    const title = this.getContent('Hero', 'Title', 'Nunca vendeu por delivery? Comece hoje com seu app próprio.');
    const marker = 'delivery?';
    const idx = title.indexOf(marker);
    if (idx === -1) {
      return { before: title, highlight: '', after: '' };
    }
    return { before: title.slice(0, idx), highlight: marker, after: title.slice(idx + marker.length) };
  }
}
