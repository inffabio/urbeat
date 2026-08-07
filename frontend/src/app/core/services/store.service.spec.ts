import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { StoreService } from './store.service';
import { ApiService } from './api.service';
import { CuisineTypeDto, StoreResponse } from '../../shared/models/store.model';

describe('StoreService', () => {
  let service: StoreService;
  let httpMock: HttpTestingController;

  const mockCuisineTypes: CuisineTypeDto[] = [
    { id: '1', name: 'Hamburgueria' },
    { id: '2', name: 'Pizzaria' }
  ];

  const mockStore: StoreResponse = {
    id: 'store-123',
    name: 'Test Store',
    slug: 'test-store',
    storePath: 'test_store',
    cuisineType: 'Hamburgueria',
    phoneNumber: '11999999999',
    supportsDelivery: true,
    supportsPickup: false,
    estimatedDeliveryTime: '30-40 min',
    minimumOrderValue: 25.0,
    isActive: true,
    isPublished: false
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        StoreService,
        ApiService,
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });

    service = TestBed.inject(StoreService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('getCuisineTypes', () => {
    it('should return an array of cuisine types', () => {
      service.getCuisineTypes().subscribe(types => {
        expect(types.length).toBe(2);
        expect(types).toEqual(mockCuisineTypes);
      });

      const req = httpMock.expectOne('/api/stores/cuisine-types');
      expect(req.request.method).toBe('GET');
      req.flush(mockCuisineTypes);
    });
  });

  describe('getMyStore', () => {
    it('should return the seller store details', () => {
      service.getMyStore().subscribe(store => {
        expect(store.id).toBe('store-123');
        expect(store.name).toBe('Test Store');
      });

      const req = httpMock.expectOne('/api/stores/my-store');
      expect(req.request.method).toBe('GET');
      req.flush(mockStore);
    });
  });

  describe('createStore', () => {
    it('should create a new store', () => {
      const createReq = {
        name: 'New Store',
        slug: 'new-store',
        storePath: 'new_store',
        cuisineType: 'Pizzaria',
        phoneNumber: '11988888888',
        supportsDelivery: true,
        supportsPickup: true
      };

      service.createStore(createReq).subscribe(store => {
        expect(store.name).toBe('New Store');
      });

      const req = httpMock.expectOne('/api/stores');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(createReq);
      req.flush({ ...mockStore, name: 'New Store' });
    });
  });
});
