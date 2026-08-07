import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { StoreService } from '../../core/services/store.service';
import { SellerShellFacade } from '../seller-shell/seller-shell.facade';
import { SellerReviewsPageComponent } from './seller-reviews-page.component';

describe('SellerReviewsPageComponent', () => {
  let storeServiceMock: { getSellerReviews: jest.Mock };
  let shellFacadeMock: { store: ReturnType<typeof signal<any>> };

  beforeEach(async () => {
    storeServiceMock = { getSellerReviews: jest.fn() };
    shellFacadeMock = { store: signal({ id: 'store1', averageRating: 4.5, totalReviews: 2 }) };

    await TestBed.configureTestingModule({
      imports: [SellerReviewsPageComponent],
      providers: [
        { provide: StoreService, useValue: storeServiceMock },
        { provide: SellerShellFacade, useValue: shellFacadeMock },
      ],
    }).compileComponents();
  });

  it('loads and renders store reviews', () => {
    storeServiceMock.getSellerReviews.mockReturnValue(of([
      { id: 'r1', customerName: 'Cliente Teste', rating: 5, comment: 'Muito bom', createdAtUtc: '2026-07-29T10:00:00Z' },
      { id: 'r2', customerName: 'Maria', rating: 4, comment: 'Chegou rapido', createdAtUtc: '2026-07-28T10:00:00Z' },
    ]));

    const fixture = TestBed.createComponent(SellerReviewsPageComponent);
    fixture.detectChanges();

    expect(storeServiceMock.getSellerReviews).toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('4,5');
    expect(fixture.nativeElement.textContent).toContain('2 avaliacoes');
    expect(fixture.nativeElement.textContent).toContain('Cliente Teste');
    expect(fixture.nativeElement.textContent).toContain('Muito bom');
  });

  it('shows empty state when the store has no reviews', () => {
    shellFacadeMock.store.set({ id: 'store1', averageRating: 0, totalReviews: 0 });
    storeServiceMock.getSellerReviews.mockReturnValue(of([]));

    const fixture = TestBed.createComponent(SellerReviewsPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Nenhuma avaliacao ainda');
  });

  it('shows retry state when reviews fail to load', () => {
    storeServiceMock.getSellerReviews.mockReturnValue(throwError(() => new Error('network')));

    const fixture = TestBed.createComponent(SellerReviewsPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Nao foi possivel carregar as avaliacoes');
    expect(fixture.nativeElement.querySelector('button').textContent).toContain('Tentar novamente');
  });
});
