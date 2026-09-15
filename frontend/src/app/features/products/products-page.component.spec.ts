import { TestBed, ComponentFixture } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA, CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { of, throwError } from 'rxjs';

import { ProductsPageComponent } from './products-page.component';
import { StoreService } from '../../core/services/store.service';
import { CatalogService } from '../../core/services/catalog.service';
import { ApiService } from '../../core/services/api.service';
import { ToastService } from '../../core/services/toast.service';
import { Router } from '@angular/router';

describe('ProductsPageComponent — legacy product image upload', () => {
  let fixture: ComponentFixture<ProductsPageComponent>;
  let component: ProductsPageComponent;

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

  it('accepts every required format with a matching MIME type', () => {
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
      component.onImageSelected(fileEvent(file).event);
      expect(apiMock.post).toHaveBeenCalledTimes(1);
      apiMock.post.mockClear();
    }
  });

  it('accepts allowed extensions with an empty MIME type', () => {
    apiMock.post.mockReturnValue(of({ imageUrl: 'https://img.test/x' }));
    const { event } = fileEvent(new File(['x'], 'asset.png', { type: '' }));

    component.onImageSelected(event);

    expect(apiMock.post).toHaveBeenCalledTimes(1);
  });

  it('rejects unsupported formats, resets the input and does not upload', () => {
    const { event, input } = fileEvent(new File(['x'], 'asset.gif', { type: 'image/gif' }));

    component.onImageSelected(event);

    expect(apiMock.post).not.toHaveBeenCalled();
    expect(toastMock.showError).toHaveBeenCalledWith(expect.stringContaining('Formato'));
    expect(input.value).toBe('');
  });

  it('rejects files above the 6 MB product limit, resets the input and does not upload', () => {
    const oversized = new File([new Uint8Array(6 * 1024 * 1024 + 1)], 'asset.png', { type: 'image/png' });
    const { event, input } = fileEvent(oversized);

    component.onImageSelected(event);

    expect(apiMock.post).not.toHaveBeenCalled();
    expect(toastMock.showError).toHaveBeenCalledWith('O arquivo deve ter no máximo 6 MB.');
    expect(input.value).toBe('');
  });

  it('surfaces the backend content error message', () => {
    apiMock.post.mockReturnValue(
      throwError(() => ({ status: 400, error: { error: 'O arquivo de imagem é inválido ou está corrompido.' } })),
    );
    const { event, input } = fileEvent(new File(['x'], 'asset.png', { type: 'image/png' }));

    component.onImageSelected(event);

    expect(toastMock.showError).toHaveBeenCalledWith('O arquivo de imagem é inválido ou está corrompido.');
    expect(input.value).toBe('');
  });

  it('shows the 6 MB limit message on HTTP 413 for the legacy endpoint', () => {
    apiMock.post.mockReturnValue(throwError(() => ({ status: 413 })));
    const { event, input } = fileEvent(new File(['x'], 'asset.png', { type: 'image/png' }));

    component.onImageSelected(event);

    expect(toastMock.showError).toHaveBeenCalledWith('O arquivo deve ter no máximo 6 MB.');
    expect(input.value).toBe('');
  });
});
