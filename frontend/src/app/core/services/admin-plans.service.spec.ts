import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AdminPlansService } from './admin-plans.service';

describe('AdminPlansService', () => {
  let service: AdminPlansService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AdminPlansService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('lists the administrative catalog', () => {
    service.list().subscribe((plans) => expect(plans).toEqual([]));

    const request = httpMock.expectOne('/api/admin/plans');
    expect(request.request.method).toBe('GET');
    request.flush([]);
  });

  it('creates a plan with monthly and annual configuration', () => {
    const payload = {
      name: 'Crescer',
      monthlyAmount: 39.9,
      description: 'Plano',
      isActive: true,
      monthlyEnabled: true,
      annualEnabled: true,
      annualDiscountPercent: 15,
      level: 2,
    };

    service.create(payload).subscribe();

    const request = httpMock.expectOne('/api/admin/plans');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(payload);
    request.flush({});
  });

  it('updates plan status and a plan-scoped gateway configuration through the admin endpoints', () => {
    service.updateStatus('plan-1', false).subscribe();
    const status = httpMock.expectOne('/api/admin/plans/plan-1/status');
    expect(status.request.method).toBe('PATCH');
    expect(status.request.body).toEqual({ isActive: false });
    status.flush({});

    service
      .updateGatewayConfiguration('plan-1', {
        provider: 'Asaas',
        environment: 'Sandbox',
        apiBaseUrl: 'https://sandbox.asaas.com',
        webhookUrl: '',
        apiKeySecretReference: 'vault://urbeat/asaas/api-key',
        isEnabled: false,
      })
      .subscribe();

    const gateway = httpMock.expectOne('/api/admin/plans/plan-1/gateway-configuration');
    expect(gateway.request.method).toBe('PUT');
    expect(gateway.request.body.apiKeySecretReference).toBe('vault://urbeat/asaas/api-key');
    gateway.flush({});
  });

  it('reads a plan-scoped gateway configuration and never calls a global endpoint', () => {
    service.getGatewayConfiguration('plan-2').subscribe();

    const request = httpMock.expectOne('/api/admin/plans/plan-2/gateway-configuration');
    expect(request.request.method).toBe('GET');
    request.flush({});
  });
});
