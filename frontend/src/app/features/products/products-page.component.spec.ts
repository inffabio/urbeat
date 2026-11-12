import { TestBed, ComponentFixture } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA, CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { of, throwError } from 'rxjs';

import { ProductsPageComponent } from './products-page.component';
import { StoreService } from '../../core/services/store.service';
import { CatalogService } from '../../core/services/catalog.service';
import { ApiService } from '../../core/services/api.service';
import { ToastService } from '../../core/services/toast.service';
import { Router } from '@angular/router';
import { IMAGE_OVER_LIMIT_ERROR } from '../../shared/utils/image-upload.utils';

describe('ProductsPageComponent — legacy product image upload', () => {
  let fixture: ComponentFixture<ProductsPageComponent>;
  let component: ProductsPageComponent;

  const originalImage = window.Image;
  const originalFileReader = window.FileReader;
  const originalCreateElement = document.createElement.bind(document);

  const storeServiceMock = { getMyStore: jest.fn() };
  const catalogServiceMock = {};
  const apiMock = {
    get: jest.fn().mockReturnValue(of([])),
    post: jest.fn(),
    put: jest.fn(),
    delete: jest.fn(),
  };
  const toastMock = { showError: jest.fn(), showSuccess: jest.fn(), showWarning: jest.fn() };
  const routerMock = { navigate: jest.fn() };

  const fileEvent = (file: File): { event: Event; input: { files: File[]; value: string } } => {
    const input = { files: [file], value: `C:\\fakepath\\${file.name}` };
    return { event: { target: input } as unknown as Event, input };
  };

  beforeEach(async () => {
    jest.clearAllMocks();
    apiMock.get.mockReturnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [ProductsPageComponent],
      schemas: [NO_ERRORS_SCHEMA, CUSTOM_ELEMENTS_SCHEMA],
      providers: [
        { provide: StoreService, useValue: storeServiceMock },
        { provide: CatalogService, useValue: catalogServiceMock },
        { provide: ApiService, useValue: apiMock },
        { provide: ToastService, useValue: toastMock },
        { provide: Router, useValue: routerMock },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ProductsPageComponent);
    component = fixture.componentInstance;
    component.storeId.set('store-1');
    component.selectedProduct.set({ id: 'product-1' } as never);
  });

  afterEach(() => {
    window.Image = originalImage;
    window.FileReader = originalFileReader;
    jest.restoreAllMocks();
  });

  it('accepts every required format with a matching MIME type', async () => {
    apiMock.post.mockReturnValue(of({ imageUrl: 'https://img.test/x' }));

    const files = [
      new File(['x'], 'asset.avif', { type: 'image/avif' }),
      new File(['x'], 'asset.png', { type: 'image/png' }),
      new File(['x'], 'asset.svg', { type: 'image/svg+xml' }),
      new File(['x'], 'asset.webp', { type: 'image/webp' }),
      new File(['x'], 'asset.jpg', { type: 'image/jpeg' }),
      new File(['x'], 'asset.jpeg', { type: 'image/jpg' }),
    ];

    for (const file of files) {
      await component.onImageSelected(fileEvent(file).event);
      expect(apiMock.post).toHaveBeenCalledTimes(1);
      apiMock.post.mockClear();
    }
  });

  it('accepts allowed extensions with an empty MIME type', async () => {
    apiMock.post.mockReturnValue(of({ imageUrl: 'https://img.test/x' }));
    const { event } = fileEvent(new File(['x'], 'asset.png', { type: '' }));

    await component.onImageSelected(event);

    expect(apiMock.post).toHaveBeenCalledTimes(1);
  });

  it('rejects unsupported formats, resets the input and does not upload', async () => {
    const { event, input } = fileEvent(new File(['x'], 'asset.gif', { type: 'image/gif' }));

    await component.onImageSelected(event);

    expect(apiMock.post).not.toHaveBeenCalled();
    expect(toastMock.showError).toHaveBeenCalledWith(expect.stringContaining('Formato'));
    expect(input.value).toBe('');
  });

  it('sends the browser-compressed file through the legacy endpoint', async () => {
    apiMock.post.mockReturnValue(of({ imageUrl: 'https://img.test/x' }));
    (window as unknown as { FileReader: unknown }).FileReader = class {
      onerror: (() => void) | null = null;
      onabort: (() => void) | null = null;
      onload: ((event: { target: { result: string } }) => void) | null = null;
      readAsDataURL() {
        this.onload?.({ target: { result: 'data:image/png;base64,AAAA' } });
      }
    };
    (window as unknown as { Image: unknown }).Image = class {
      onerror: (() => void) | null = null;
      onload: (() => void) | null = null;
      naturalWidth = 1600;
      naturalHeight = 900;
      width = 1600;
      height = 900;
      set src(_value: string) {
        this.onload?.();
      }
    };
    const toBlob = jest.fn((callback: BlobCallback, type?: string) => {
      callback(new Blob([new Uint8Array(500)], { type }));
    });
    jest.spyOn(document, 'createElement').mockImplementation((tagName: string) => {
      if (tagName === 'canvas') {
        return {
          width: 0,
          height: 0,
          getContext: jest.fn(() => ({ drawImage: jest.fn(), clearRect: jest.fn(), fillRect: jest.fn() })),
          toBlob,
        } as unknown as HTMLElement;
      }
      return originalCreateElement(tagName);
    });

    const file = new File([new Uint8Array(1536 * 1024)], 'product.png', { type: 'image/png' });
    const { event } = fileEvent(file);

    await component.onImageSelected(event);

    expect(toBlob).toHaveBeenCalled();
    expect(apiMock.post).toHaveBeenCalledWith('/api/stores/store-1/products/product-1/images', expect.any(FormData));
    const formData = apiMock.post.mock.calls[0][1] as FormData;
    const uploaded = formData.get('file') as File;
    expect(uploaded).not.toBe(file);
    expect(uploaded.size).toBe(500);
    expect(uploaded.size).toBeLessThanOrEqual(1024 * 1024);
    expect(toastMock.showError).not.toHaveBeenCalled();
  });

  it('rejects files that are still above 1 MB after compression without uploading', async () => {
    (window as unknown as { Image: unknown }).Image = class {
      onerror: (() => void) | null = null;
      onload: (() => void) | null = null;
      width = 0;
      height = 0;
      set src(_value: string) {
        this.onerror?.();
      }
    };
    const oversized = new File([new Uint8Array(2 * 1024 * 1024)], 'asset.png', { type: 'image/png' });
    const { event, input } = fileEvent(oversized);

    await component.onImageSelected(event);

    expect(apiMock.post).not.toHaveBeenCalled();
    expect(toastMock.showError).toHaveBeenCalledWith(IMAGE_OVER_LIMIT_ERROR);
    expect(input.value).toBe('');
  });

  it('surfaces the backend content error message', async () => {
    apiMock.post.mockReturnValue(
      throwError(() => ({ status: 400, error: { error: 'O arquivo de imagem é inválido ou está corrompido.' } })),
    );
    const { event, input } = fileEvent(new File(['x'], 'asset.png', { type: 'image/png' }));

    await component.onImageSelected(event);

    expect(toastMock.showError).toHaveBeenCalledWith('O arquivo de imagem é inválido ou está corrompido.');
    expect(input.value).toBe('');
  });

  it('shows the 1 MB limit message on HTTP 413 for the legacy endpoint', async () => {
    apiMock.post.mockReturnValue(throwError(() => ({ status: 413 })));
    const { event, input } = fileEvent(new File(['x'], 'asset.png', { type: 'image/png' }));

    await component.onImageSelected(event);

    expect(toastMock.showError).toHaveBeenCalledWith(IMAGE_OVER_LIMIT_ERROR);
    expect(input.value).toBe('');
  });

  describe('special classification filter', () => {
    type Classification = 'Offers' | 'BestSeller' | 'New' | null;

    const product = (name: string, specialClassification: Classification, categoryName = 'Lanches') => ({
      id: name,
      categoryId: 'cat-1',
      categoryName,
      name,
      description: '',
      price: 10,
      isAvailable: true,
      specialClassification,
      displayOrder: 0,
      additionals: [],
      choiceOptions: [],
      variations: [],
    });

    const visibleNames = () => component.filteredProducts().map((item) => item.name);

    beforeEach(() => {
      component.products.set([
        product('X-Bacon', 'Offers'),
        product('Batata', 'BestSeller'),
        product('Brownie', 'New', 'Doces'),
        product('X-Salada', null),
      ] as never);
    });

    it('starts on Todas and keeps every product, including the unclassified one', () => {
      expect(component.classificationFilter()).toBeNull();
      expect(visibleNames()).toEqual(['X-Bacon', 'Batata', 'Brownie', 'X-Salada']);
    });

    it.each([
      ['Offers', ['X-Bacon']],
      ['BestSeller', ['Batata']],
      ['New', ['Brownie']],
    ] as const)('filters by %s and hides the other classifications', (filter, expected) => {
      component.classificationFilter.set(filter);

      expect(visibleNames()).toEqual([...expected]);
    });

    it.each([
      ['Offers'],
      ['BestSeller'],
      ['New'],
    ] as const)('never shows the unclassified product when %s is selected', (filter) => {
      component.classificationFilter.set(filter);

      expect(visibleNames()).not.toContain('X-Salada');
    });

    it('returns every product again when the filter is cleared back to null', () => {
      component.classificationFilter.set('Offers');
      expect(visibleNames()).toEqual(['X-Bacon']);

      component.classificationFilter.set(null);

      expect(visibleNames()).toEqual(['X-Bacon', 'Batata', 'Brownie', 'X-Salada']);
    });

    it('combines the classification filter with the existing search query', () => {
      component.classificationFilter.set('Offers');
      component.searchQuery.set('bacon');

      expect(visibleNames()).toEqual(['X-Bacon']);

      component.searchQuery.set('batata');

      expect(visibleNames()).toEqual([]);
    });

    it('keeps the search over the category name working inside a classification filter', () => {
      component.classificationFilter.set('New');
      component.searchQuery.set('doces');

      expect(visibleNames()).toEqual(['Brownie']);
    });

    it('renders the four approved options without the legacy Destaque label', () => {
      storeServiceMock.getMyStore.mockReturnValue(of({ id: 'store-1' }));
      fixture.detectChanges();

      const select = fixture.nativeElement.querySelector(
        'select[aria-label="Filtrar por classificação especial"]',
      ) as HTMLSelectElement | null;
      expect(select).toBeTruthy();
      const labels = [...select!.options].map((option) => (option.textContent ?? '').trim());

      expect(labels).toEqual(['Todas', 'Ofertas', 'Mais vendido', 'Novidade']);
      expect(labels.join(' ')).not.toContain('Destaque');
    });
  });
});
