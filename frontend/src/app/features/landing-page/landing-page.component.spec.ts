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

  it('uses the single-plan title and keeps the plan CTA slightly larger', () => {
    flushOffer([plan({})]);

    const text = fixture.nativeElement.textContent as string;
    const cta = fixture.nativeElement.querySelector('.lp-btn-plan') as HTMLAnchorElement;

    expect(text).toContain('Crie hoje mesmo sua loja');
    expect(text).not.toContain('Escolha o plano ideal para o seu momento');
    expect(cta.classList.contains('is-single')).toBe(true);
    expect(fixture.nativeElement.querySelector('.lp-plans-grid').classList.contains('is-single')).toBe(true);
  });

  it('uses the multi-plan title and sends only plan and period to registration', () => {
    flushOffer([
      plan({ id: 'plan-1', name: 'Começar', level: 0 }),
      plan({ id: 'plan-2', name: 'Crescer', level: 1 }),
    ]);

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Escolha o plano ideal para o seu momento');

    const ctas = fixture.nativeElement.querySelectorAll('.lp-btn-plan') as NodeListOf<HTMLAnchorElement>;
    expect(ctas[0].getAttribute('href')).toBe('/cadastro?planId=plan-1&billingPeriod=monthly');
    expect(ctas[0].getAttribute('href')).not.toContain('39.90');
    expect(ctas[0].getAttribute('href')).not.toContain('trial');
  });

  it('changes the CTA query param when the annual period is selected', () => {
    flushOffer([plan({ id: 'plan-annual' })]);

    const periodRadios = fixture.nativeElement.querySelectorAll('.lp-plan-period-option input') as NodeListOf<HTMLInputElement>;
    expect(periodRadios).toHaveLength(2);

    periodRadios[1].dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const cta = fixture.nativeElement.querySelector('.lp-btn-plan') as HTMLAnchorElement;
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
    expect(host.querySelectorAll('.lp-btn-plan')).toHaveLength(1);
  });
});
