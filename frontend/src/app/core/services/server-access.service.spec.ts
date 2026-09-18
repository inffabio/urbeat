import { TestBed } from '@angular/core/testing';
import { ServerAccessService } from './server-access.service';

describe('ServerAccessService', () => {
  let service: ServerAccessService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(ServerAccessService);
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('starts idle', () => {
    expect(service.activeRequests()).toBe(0);
    expect(service.isLoading()).toBe(false);
  });

  it('stays busy until every parallel request finishes', () => {
    service.begin();
    service.begin();

    expect(service.activeRequests()).toBe(2);
    expect(service.isLoading()).toBe(true);

    service.end();

    expect(service.activeRequests()).toBe(1);
    expect(service.isLoading()).toBe(true);

    service.end();

    expect(service.activeRequests()).toBe(0);
    expect(service.isLoading()).toBe(false);
  });

  it('never counts below zero when end has no matching begin', () => {
    service.end();

    expect(service.activeRequests()).toBe(0);
    expect(service.isLoading()).toBe(false);
  });
});
