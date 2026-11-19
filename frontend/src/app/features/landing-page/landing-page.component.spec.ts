import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { LandingPageComponent } from './landing-page.component';
import { PublicPlanOfferItem } from '../../shared/models/plan-offer.model';

function plan(overrides: Partial<PublicPlanOfferItem>): PublicPlanOfferItem {
  return {
    id: 'plan-1',
    name: 'Crescer',
    amount: 39.9,
    monthlyAmount: 39.9,
    annualAmount: 407,
    annualDiscountPercent: 15,
    monthlyEnabled: true,
    annualEnabled: true,
    description: 'Para negócios em crescimento.',
    trialDays: 9,
    benefits: ['Relatórios de vendas'],
    level: 1,
    ...overrides,
  };
}

describe('LandingPageComponent plans', () => {
  let fixture: ComponentFixture<LandingPageComponent>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LandingPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(LandingPageComponent);
    httpMock = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    Element.prototype.scrollIntoView = jest.fn();
  });

  afterEach(() => {
    httpMock.verify();
  });

  function flushOffer(plans: PublicPlanOfferItem[]): void {
    httpMock.expectOne('/api/landingpagecontent').flush([]);
    httpMock.expectOne('/api/public/plans').flush({ plans });
    fixture.detectChanges();
  }

  it('renders active plans with monthly and annual prices and trial days', () => {
    flushOffer([plan({})]);
    const host: HTMLElement = fixture.nativeElement;
    const text = host.textContent ?? '';

    expect(host.querySelectorAll('.lp-plan-card')).toHaveLength(1);
    expect(text).toContain('Crescer');
    expect(text).toContain('39,90');
    expect(text).toContain('407,00');
    expect(text).toContain('9 dias grátis');
    expect(text).toContain('Relatórios de vendas');
  });

  it('uses the single-plan title and places the only CTA in the plan header', () => {
    flushOffer([plan({})]);

    const host: HTMLElement = fixture.nativeElement;
    const text = host.textContent as string;
    const headerCta = host.querySelector('.lp-plan-head .lp-plan-cta') as HTMLAnchorElement;

    expect(text).toContain('Crie hoje mesmo sua loja');
    expect(text).not.toContain('Escolha o plano ideal para o seu momento');
    expect(headerCta).not.toBeNull();
    expect(host.querySelectorAll('.lp-plan-cta')).toHaveLength(1);
    expect(host.querySelector('.lp-plans-grid').classList.contains('is-single')).toBe(true);
    expect(host.querySelectorAll('.lp-plans-grid > .lp-plan-card')).toHaveLength(1);
    expect(host.querySelector('.lp-plan-cta')?.textContent?.trim()).toBe('Começar');
    expect(host.querySelector('.lp-btn-plan')).toBeNull();
    expect(host.querySelector('.lp-plan-footer')).toBeNull();
  });

  it('centers the single plan card with a comfortable single-column layout', () => {
    flushOffer([plan({})]);

    const scss = readFileSync(
      resolve(process.cwd(), 'src/app/features/landing-page/landing-page.component.scss'),
      'utf8',
    );
    const singleRule = /\.lp-plans-grid\.is-single\s*\{([^}]*)\}/.exec(scss)?.[1] ?? '';

    expect(singleRule).toContain('grid-template-columns: minmax(0, 1fr)');
    expect(singleRule).toContain('margin-inline: auto');
    expect(singleRule).toMatch(/width:\s*100%/);

    const maxWidth = Number(/max-width:\s*(\d+)px/.exec(singleRule)?.[1]);
    // Faixa confortável: ~560px de card sem virar coluna fina nem esticar no desktop.
    expect(maxWidth).toBeGreaterThanOrEqual(520);
    expect(maxWidth).toBeLessThanOrEqual(600);
  });

  it('keeps the multi-plan grid clearly narrower than the page container', () => {
    flushOffer([plan({})]);

    const scss = readFileSync(
      resolve(process.cwd(), 'src/app/features/landing-page/landing-page.component.scss'),
      'utf8',
    );
    const gridRule = /^\.lp-plans-grid\s*\{([^}]*)\}/m.exec(scss)?.[1] ?? '';

    expect(gridRule).toContain('margin-inline: auto');

    // Teto bem abaixo do container da página (1180px), centralizado na seção.
    const desktopMaxWidth = Number(/max-width:\s*(\d+)px/.exec(gridRule)?.[1]);
    expect(desktopMaxWidth).toBeGreaterThanOrEqual(920);
    expect(desktopMaxWidth).toBeLessThanOrEqual(1000);
    // 3 colunas com gap de 24px: cada card para em ~304px, nunca acima de 340px.
    expect((desktopMaxWidth - 2 * 24) / 3).toBeLessThanOrEqual(340);

    // A faixa de 2 colunas também tem teto, para o card não passar de ~350px.
    expect(scss).toMatch(
      /@media \(min-width: 720px\) \{\s*grid-template-columns: repeat\(2, minmax\(0, 1fr\)\);\s*max-width: 720px;/,
    );
    // 3 colunas só entram quando cada card ainda cabe confortavelmente.
    expect(scss).toMatch(/@media \(min-width: 11[2-9]\dpx\) \{\s*grid-template-columns: repeat\(3/);
  });

  it('uses the multi-plan title and sends plan and period from each header CTA', () => {
    flushOffer([
      plan({ id: 'plan-essential', name: 'Essencial', level: 0 }),
      plan({ id: 'plan-growth', name: 'Crescer', level: 1 }),
    ]);

    const host: HTMLElement = fixture.nativeElement;
    const text = host.textContent as string;
    expect(text).toContain('Escolha o plano ideal para o seu momento');

    const grid = host.querySelector('.lp-plans-grid') as HTMLElement;
    expect(grid.classList.contains('is-single')).toBe(false);
    expect(grid.querySelectorAll(':scope > .lp-plan-card')).toHaveLength(2);

    const ctas = host.querySelectorAll('.lp-plan-cta') as NodeListOf<HTMLAnchorElement>;
    expect(ctas).toHaveLength(2);
    expect(ctas[0].closest('.lp-plan-head')).not.toBeNull();
    expect(ctas[1].closest('.lp-plan-head')).not.toBeNull();
    for (const card of Array.from(host.querySelectorAll('.lp-plan-card'))) {
      expect(card.querySelectorAll('.lp-plan-cta')).toHaveLength(1);
    }
    expect(ctas[0].textContent?.trim()).toBe('Começar');
    expect(ctas[1].textContent?.trim()).toBe('Começar');
    expect(ctas[0].getAttribute('href')).toBe('/cadastro?planId=plan-essential&billingPeriod=monthly');
    expect(ctas[1].getAttribute('href')).toBe('/cadastro?planId=plan-growth&billingPeriod=monthly');
    expect(ctas[0].getAttribute('href')).not.toContain('39.90');
    expect(ctas[0].getAttribute('href')).not.toContain('trial');
    expect(ctas[0].getAttribute('aria-label')).toBe('Começar com o plano Essencial');
    expect(ctas[1].getAttribute('aria-label')).toBe('Começar com o plano Crescer');
    expect(host.querySelector('.lp-btn-plan')).toBeNull();
    expect(host.querySelector('.lp-plan-footer')).toBeNull();
  });

  it('changes the header CTA query param when the annual period is selected', () => {
    flushOffer([plan({ id: 'plan-annual' })]);

    const periodRadios = fixture.nativeElement.querySelectorAll('.lp-plan-period-option input') as NodeListOf<HTMLInputElement>;
    expect(periodRadios).toHaveLength(2);

    periodRadios[1].dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const cta = fixture.nativeElement.querySelector('.lp-plan-cta') as HTMLAnchorElement;
    expect(cta.getAttribute('href')).toBe('/cadastro?planId=plan-annual&billingPeriod=annual');
    expect(fixture.nativeElement.textContent).toContain('407,00');
  });

  it('scrolls the create-account CTA to the plans section and moves focus there', () => {
    flushOffer([plan({})]);

    const section = fixture.nativeElement.querySelector('#plano') as HTMLElement;
    const cta = Array.from(fixture.nativeElement.querySelectorAll('a') as NodeListOf<HTMLAnchorElement>)
      .find((anchor) => anchor.textContent?.trim() === 'Criar meu cadastro');
    expect(cta).toBeDefined();

    cta!.click();
    fixture.detectChanges();

    expect(Element.prototype.scrollIntoView).toHaveBeenCalled();
    expect(section.getAttribute('tabindex')).toBe('-1');
  });

  it('does not invent plans when the offer succeeds with no active plan', () => {
    flushOffer([]);
    const host: HTMLElement = fixture.nativeElement;
    const text = host.textContent ?? '';

    expect(host.querySelectorAll('.lp-plan-card')).toHaveLength(0);
    expect(text).toContain('Planos indisponíveis no momento');
    expect(text).toContain('Estamos atualizando nossos planos');
    expect(host.querySelector('.lp-plan-cta')).toBeNull();
    expect(host.querySelector('.lp-btn-plan')).toBeNull();
  });

  it('keeps a single-plan fallback only when the public offer fails', () => {
    httpMock.expectOne('/api/landingpagecontent').flush([]);
    httpMock.expectOne('/api/public/plans').error(new ProgressEvent('error'));
    fixture.detectChanges();

    const host: HTMLElement = fixture.nativeElement;
    const text = host.textContent ?? '';

    expect(host.querySelectorAll('.lp-plan-card')).toHaveLength(1);
    expect(text).toContain('Começar');
    expect(text).toContain('19,90');
    expect(text).toContain('15 dias grátis');
    expect(text).not.toContain('39,90');
    expect(text).not.toContain('79,90');
    expect(text).toContain('Crie hoje mesmo sua loja');
    expect(host.querySelectorAll('.lp-plan-cta')).toHaveLength(1);
    expect(host.querySelector('.lp-btn-plan')).toBeNull();
  });
});
