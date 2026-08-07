import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { SellerShellFacade } from '../seller-shell/seller-shell.facade';
import { SellerMarketingPageComponent } from './seller-marketing-page.component';

describe('SellerMarketingPageComponent', () => {
  let shellFacadeMock: { store: ReturnType<typeof signal<any>> };

  beforeEach(async () => {
    shellFacadeMock = {
      store: signal({
        id: 'store1',
        name: 'Loja Teste',
        slug: 'loja-teste',
        averageRating: 4.5,
        totalReviews: 12,
        isOpen: true,
      }),
    };

    await TestBed.configureTestingModule({
      imports: [SellerMarketingPageComponent],
      providers: [{ provide: SellerShellFacade, useValue: shellFacadeMock }],
    }).compileComponents();
  });

  it('renders shareable store marketing information', () => {
    const fixture = TestBed.createComponent(SellerMarketingPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Loja Teste');
    expect(fixture.nativeElement.textContent).toContain('/loja-teste');
    expect(fixture.nativeElement.textContent).toContain('4,5');
    expect(fixture.nativeElement.textContent).toContain('12 avaliacoes');
  });

  it('explains that campaigns and coupons need a future backend contract', () => {
    const fixture = TestBed.createComponent(SellerMarketingPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Campanhas e cupons ainda precisam de contrato backend dedicado');
  });
});
