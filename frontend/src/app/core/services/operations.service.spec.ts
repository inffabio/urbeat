import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SKIP_SERVER_ACCESS_OVERLAY } from '../interceptors/server-access.context';
import { ApiService } from './api.service';
import { OperationsService } from './operations.service';

const OUTBOX_OPERATIONS_URL = '/api/admin/operations/outbox';

describe('OperationsService', () => {
  let service: OperationsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        OperationsService,
        ApiService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });

    service = TestBed.inject(OperationsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('tags the automatic background refresh so it never flashes the overlay', () => {
    service.getOutboxOperations({ background: true }).subscribe();

    const req = httpMock.expectOne(OUTBOX_OPERATIONS_URL);
    expect(req.request.context.get(SKIP_SERVER_ACCESS_OVERLAY)).toBe(true);
    req.flush({});
  });

  it('keeps the foreground read tracked by the global overlay', () => {
    service.getOutboxOperations().subscribe();

    const req = httpMock.expectOne(OUTBOX_OPERATIONS_URL);
    expect(req.request.context.get(SKIP_SERVER_ACCESS_OVERLAY)).toBe(false);
    req.flush({});
  });
});
