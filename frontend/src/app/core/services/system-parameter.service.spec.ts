import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SystemParameterService } from './system-parameter.service';
import { SystemParameter } from '../../shared/models/system-parameter.model';

const PARAMETER: SystemParameter = {
  id: 'id-1',
  key: 'Store.DefaultDeliveryFee',
  value: '19.90',
  type: 'Decimal',
  group: 'Business',
  description: 'Taxa de entrega padrão',
  createdAtUtc: '2026-01-01T00:00:00Z',
  updatedAtUtc: null,
  maxLength: 18,
  display: 'currency',
  unit: null,
  minValue: 0,
  maxValue: 1000000,
  isReadOnly: false,
  requiresConfirmation: false,
  isSecret: false,
};

describe('SystemParameterService', () => {
  let service: SystemParameterService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        SystemParameterService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });

    service = TestBed.inject(SystemParameterService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('loads only the business parameters from the admin settings endpoint', () => {
    service.list().subscribe((parameters) => {
      expect(parameters).toHaveLength(1);
      expect(parameters[0].key).toBe('Store.DefaultDeliveryFee');
      expect(parameters[0].display).toBe('currency');
    });

    const request = httpMock.expectOne('/api/admin/system-parameters/business');
    expect(request.request.method).toBe('GET');
    request.flush([PARAMETER]);
  });

  it('updates a parameter using the canonical value and confirmation flag', () => {
    service.update('Subscription.DueSoonDays', '5', true).subscribe((updated) => {
      expect(updated.value).toBe('5');
    });

    const request = httpMock.expectOne(
      '/api/admin/system-parameters/business/Subscription.DueSoonDays',
    );
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({
      value: '5',
      confirmRestrictedChange: true,
    });
    request.flush({
      ...PARAMETER,
      key: 'Subscription.DueSoonDays',
      value: '5',
      type: 'Int32',
      display: 'integer',
    });
  });

  it('encodes parameter keys in the business URL', () => {
    service.update('Order Code Prefix', 'URB-').subscribe();

    const request = httpMock.expectOne(
      '/api/admin/system-parameters/business/Order%20Code%20Prefix',
    );
    expect(request.request.body).toEqual({
      value: 'URB-',
      confirmRestrictedChange: false,
    });
    request.flush(PARAMETER);
  });
});
