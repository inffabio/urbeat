import { Component, OnInit, AfterViewInit, OnDestroy, inject, signal, ViewEncapsulation, ElementRef } from '@angular/core';
import { CommonModule, DOCUMENT } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { Title, Meta } from '@angular/platform-browser';
import { IonicModule } from '@ionic/angular';

import { LandingPageService, LandingPageContent } from '../../core/services/landing-page.service';
import { initLandingEffects } from './landing-effects';

@Component({
  selector: 'app-landing-page',
  standalone: true,
  imports: [CommonModule, IonicModule, RouterLink],
  templateUrl: './landing-page.component.html',
  styleUrls: ['./landing-page.component.scss'],
  encapsulation: ViewEncapsulation.None
})
export class LandingPageComponent implements OnInit, AfterViewInit, OnDestroy {
  private readonly landingPageService = inject(LandingPageService);
  private readonly router = inject(Router);
  private readonly title = inject(Title);
  private readonly meta = inject(Meta);
  private readonly document = inject(DOCUMENT);
  private readonly elementRef = inject(ElementRef);

  readonly content = signal<LandingPageContent[]>([]);
  readonly loading = signal(true);

  private cleanupEffects!: () => void;

  ngOnInit(): void {
    this.setupSEO();
    this.loadOriginalCSS();
    this.loadContent();
  }

  ngAfterViewInit(): void {
    this.cleanupEffects = initLandingEffects(this.elementRef.nativeElement);
  }

  ngOnDestroy(): void {
    if (this.cleanupEffects) {
      this.cleanupEffects();
    }
    this.removeOriginalCSS();
  }

  private loadOriginalCSS(): void {
    const link = this.document.createElement('link');
    link.rel = 'stylesheet';
    link.href = 'assets/css/styles.css?v=' + new Date().getTime();
    link.id = 'lp-styles-css';
    this.document.head.appendChild(link);
  }

  private removeOriginalCSS(): void {
    const link = this.document.getElementById('lp-styles-css');
    if (link) link.remove();
  }

  private setupSEO(): void {
    this.title.setTitle("urbeat · Seu delivery, com cara de restaurante.");

    const metaTags = [
      { name: 'description', content: 'Cardápio digital, pedidos online e painel completo. O sistema que faz seu pequeno delivery parecer grande.' },
      { name: 'keywords', content: 'delivery, cardápio digital, pedidos online, sistema para restaurante, happ\'ee, delivery sem taxa' },
      { name: 'author', content: 'happ\'ee' },
      { property: 'og:type', content: 'website' },
      { property: 'og:url', content: 'https://urbeat.com.br/landpage/' },
      { property: 'og:title', content: "urbeat · Seu delivery, com cara de restaurante." },
      { property: 'og:description', content: 'Cardápio digital + pedidos online + painel completo. Tudo em um sistema simples para pequenos empreendedores.' },
      { property: 'og:image', content: 'https://urbeat.com.br/assets/images/imghero.webp' },
      { property: 'twitter:card', content: 'summary_large_image' },
      { property: 'twitter:url', content: 'https://urbeat.com.br/landpage/' },
      { property: 'twitter:title', content: "urbeat · Seu delivery, com cara de restaurante." },
      { property: 'twitter:description', content: 'Cardápio digital + pedidos online + painel completo. Tudo em um sistema simples para pequenos empreendedores.' },
      { property: 'twitter:image', content: 'https://urbeat.com.br/assets/images/imghero.webp' }
    ];

    metaTags.forEach(tag => {
      const key = Object.keys(tag)[0];
      const value = Object.values(tag)[0] as string;
      let meta = this.document.querySelector(`meta[${key}="${value}"]`);
      if (!meta) {
        meta = this.document.createElement('meta');
        meta.setAttribute(key, value);
        this.document.head.appendChild(meta);
      }
    });
  }

  private loadContent(): void {
    this.landingPageService.getAll().subscribe({
      next: (data) => {
        this.content.set(data);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Failed to load landing page content', err);
        this.loading.set(false);
      }
    });
  }

  getContent(section: string, key: string, defaultValue: string): string {
    const item = this.content().find((c) => c.section === section && c.key === key);
    return item ? item.value : defaultValue;
  }
}
































































































