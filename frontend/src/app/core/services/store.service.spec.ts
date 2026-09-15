import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { StoreService } from './store.service';
import { ApiService } from './api.service';
import { CuisineTypeDto, StoreResponse } from '../../shared/models/store.model';
import { ProductOptionGroupTemplate } from '../../shared/models/product.model';

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

  describe('getProductOptionGroupTemplates', () => {
    it('loads the reusable option groups for the store', () => {
      const templates: ProductOptionGroupTemplate[] = [
        {
          id: 'tpl-1',
          name: 'Escolha um molho',
          isRequired: false,
          choiceType: 'multiple',
          minChoices: 0,
          maxChoices: 2,
          displayOrder: 1,
          items: [{ id: 'item-1', name: 'Molho 1', price: 5, displayOrder: 1 }],
        },
      ];

      service.getProductOptionGroupTemplates('store-1').subscribe((result) => {
        expect(result).toEqual(templates);
      });

      const req = httpMock.expectOne('/api/stores/store-1/products/option-groups');
      expect(req.request.method).toBe('GET');
      req.flush(templates);
    });
  });

  describe('uploadImage', () => {
    it('posts the file to upload-image with the media type as a type=logo query param', () => {
      const file = new File(['svg'], 'logo.svg', { type: 'image/svg+xml' });
      const formData = new FormData();
      formData.append('file', file);

      service.uploadImage(file, 'logo').subscribe((res) => {
        expect(res.url).toBe('https://res.cloudinary.com/demo/image/upload/v1/urbeat/logo.png');
      });

      const req = httpMock.expectOne('/api/stores/upload-image?type=logo');
      expect(req.request.method).toBe('POST');
      expect(req.request.body.get('file')).toBe(file);
      req.flush({ url: 'https://res.cloudinary.com/demo/image/upload/v1/urbeat/logo.png' });
    });

    it('builds a type=banner query param and URL-encodes the type value', () => {
      const file = new File(['webp'], 'banner.webp', { type: 'image/webp' });

      service.uploadImage(file, 'banner').subscribe(() => {});

      const req = httpMock.expectOne('/api/stores/upload-image?type=banner');
      expect(req.request.urlWithParams).toBe('/api/stores/upload-image?type=banner');
      req.flush({ url: 'https://res.cloudinary.com/demo/image/upload/v1/urbeat/banner.webp' });
    });
  });
});
