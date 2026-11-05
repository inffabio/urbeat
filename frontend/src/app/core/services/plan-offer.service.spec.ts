import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PlanOfferService } from './plan-offer.service';

describe('PlanOfferService', () => {
  let service: PlanOfferService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [PlanOfferService, provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(PlanOfferService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('loads the public plan offer', () => {
    service.getOffer().subscribe((offer) => {
      expect(offer.plans).toHaveLength(1);
      expect(offer.plans[0].name).toBe('Começar');
      expect(offer.plans[0].trialDays).toBe(9);
      expect(offer.plans[0].benefits).toEqual(['Cardápio digital']);
    });

    const req = httpMock.expectOne('/api/public/plans');
    expect(req.request.method).toBe('GET');
    req.flush({
      plans: [
        {
          id: 'plan-1',
          name: 'Começar',
          amount: 19.9,
          description: 'Plano de entrada',
          trialDays: 9,
          benefits: ['Cardápio digital'],
        },
      ],
    });
  });
});
