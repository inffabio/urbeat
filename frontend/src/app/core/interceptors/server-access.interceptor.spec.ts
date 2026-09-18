import { TestBed } from '@angular/core/testing';
import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { serverAccessInterceptor } from './server-access.interceptor';
import { SKIP_SERVER_ACCESS_OVERLAY } from './server-access.context';
import { ServerAccessService } from '../services/server-access.service';

const LOCAL_AGENT_ORIGIN = 'http://127.0.0.1:43111';
const LOCAL_AGENT_URL = `${LOCAL_AGENT_ORIGIN}/printers`;

describe('serverAccessInterceptor', () => {
  let httpMock: HttpTestingController;
  let httpClient: HttpClient;
  let service: ServerAccessService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([serverAccessInterceptor])),
        provideHttpClientTesting(),
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
    httpClient = TestBed.inject(HttpClient);
    service = TestBed.inject(ServerAccessService);
  });

  afterEach(() => {
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  it('marks the server busy while a request is in flight and idle on success', () => {
    httpClient.get('/api/stores').subscribe();

    expect(service.isLoading()).toBe(true);

    httpMock.expectOne('/api/stores').flush({});

    expect(service.isLoading()).toBe(false);
  });

  it('marks the server idle when a request fails', () => {
    let errored = false;

    httpClient.get('/api/stores').subscribe({ error: () => { errored = true; } });

    expect(service.isLoading()).toBe(true);

    httpMock.expectOne('/api/stores').flush('boom', { status: 500, statusText: 'Server Error' });

    expect(errored).toBe(true);
    expect(service.isLoading()).toBe(false);
  });

  it('keeps the overlay up until every parallel request settles', () => {
    httpClient.get('/api/a').subscribe();
    httpClient.get('/api/b').subscribe();

    expect(service.activeRequests()).toBe(2);
    expect(service.isLoading()).toBe(true);

    httpMock.expectOne('/api/a').flush({});

    expect(service.isLoading()).toBe(true);

    httpMock.expectOne('/api/b').flush({});

    expect(service.isLoading()).toBe(false);
  });

  it('ignores a failed local print agent request', () => {
    httpClient.get(LOCAL_AGENT_URL).subscribe({ error: () => {} });

    expect(service.isLoading()).toBe(false);

    httpMock
      .expectOne(LOCAL_AGENT_URL)
      .error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });

    expect(service.isLoading()).toBe(false);
  });

  it.each([
    ['a path', `${LOCAL_AGENT_ORIGIN}/printers`],
    ['the root with a trailing slash', `${LOCAL_AGENT_ORIGIN}/`],
    ['the origin without a trailing slash', LOCAL_AGENT_ORIGIN],
    ['a query string', `${LOCAL_AGENT_ORIGIN}/health?mode=local`],
  ])('ignores the local print agent origin with %s', (_variant, url) => {
    httpClient.get(url).subscribe();

    expect(service.isLoading()).toBe(false);

    httpMock.expectOne(url).flush([]);

    expect(service.isLoading()).toBe(false);
    expect(service.activeRequests()).toBe(0);
  });

  it.each([
    ['a host that only shares a string prefix', 'http://127.0.0.1:43111.evil.com/printers'],
    ['a longer port', 'http://127.0.0.1:431110/printers'],
    ['a different scheme', 'https://127.0.0.1:43111/printers'],
    ['an absolute backend origin', 'https://api.urbeat.com.br/api/stores'],
  ])('tracks %s instead of excluding it', (_variant, url) => {
    httpClient.get(url).subscribe();

    expect(service.isLoading()).toBe(true);

    httpMock.expectOne(url).flush({});

    expect(service.isLoading()).toBe(false);
    expect(service.activeRequests()).toBe(0);
  });

  it('tracks a relative backend request', () => {
    httpClient.get('/api/stores').subscribe();

    expect(service.isLoading()).toBe(true);

    httpMock.expectOne('/api/stores').flush({});

    expect(service.isLoading()).toBe(false);
  });

  it('does not let the skip context token bypass the overlay for a backend request', () => {
    const context = new HttpContext().set(SKIP_SERVER_ACCESS_OVERLAY, true);
    httpClient.get('/api/orders/order1', { context }).subscribe();

    expect(service.isLoading()).toBe(true);

    httpMock.expectOne('/api/orders/order1').flush({});

    expect(service.isLoading()).toBe(false);
    expect(service.activeRequests()).toBe(0);
  });

  it('still tracks a skipped background request that fails', () => {
    const context = new HttpContext().set(SKIP_SERVER_ACCESS_OVERLAY, true);
    httpClient.get('/api/orders/order1', { context }).subscribe({ error: () => {} });

    expect(service.isLoading()).toBe(true);

    httpMock
      .expectOne('/api/orders/order1')
      .flush('boom', { status: 500, statusText: 'Server Error' });

    expect(service.isLoading()).toBe(false);
    expect(service.activeRequests()).toBe(0);
  });

  it('releases the overlay when a foreground request is cancelled before settling', () => {
    const subscription = httpClient.get('/api/stores').subscribe();

    expect(service.isLoading()).toBe(true);

    const req = httpMock.expectOne('/api/stores');
    subscription.unsubscribe();

    expect(req.cancelled).toBe(true);
    expect(service.isLoading()).toBe(false);
    expect(service.activeRequests()).toBe(0);
  });
});
