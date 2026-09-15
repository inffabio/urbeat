import { ComponentFixture, TestBed, NO_ERRORS_SCHEMA } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { provideRouter, Router } from '@angular/router';
import { StoreConfigPageComponent } from './store-config-page.component';
import { StoreService } from '../../core/services/store.service';
import { AddressService } from '../../core/services/address.service';
import { AuthService } from '../../core/services/auth.service';
import { ToastService } from '../../core/services/toast.service';
import { SellerShellFacade } from '../seller-shell/seller-shell.facade';
import { of, Subject, throwError } from 'rxjs';
import { CuisineTypeDto } from '../../shared/models/store.model';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

// The 15 protected default categories, in the alphabetical order the UI must render.
const DEFAULT_CUISINE_TYPE_NAMES = [
  'Açaiteria',
  'Cafeteria',
  'Churrascaria',
  'Comida Árabe',
  'Comida Japonesa',
  'Comida Mexicana',
  'Doceria',
  'Hamburgueria',
  'Lanches',
  'Marmitaria',
  'Padaria',
  'Pastelaria',
  'Pizzaria',
  'Sucos e Vitaminas',
  'Tapiocaria',
];

const DEFAULT_CUISINE_TYPES: CuisineTypeDto[] = DEFAULT_CUISINE_TYPE_NAMES.map((name, index) => ({
  id: `default-${index}`,
  name,
  isDefault: true,
  storeId: null,
}));

// Mocks
const mockStoreService = {
  getCuisineTypes: jest.fn(),
  getStoreCuisineTypes: jest.fn(),
  createStoreCuisineType: jest.fn(),
  deleteStoreCuisineType: jest.fn(),
  getMyStore: jest.fn(),
  getStoreAddress: jest.fn(),
  getDeliveryTimeOptions: jest.fn(),
  createStore: jest.fn(),
  updateStore: jest.fn(),
  upsertStoreAddress: jest.fn(),
  updateDeliveryConfig: jest.fn(),
  uploadImage: jest.fn(),
};

const mockAddressService = {
  lookupCep: jest.fn(),
};

const mockAuthService = {
  getSellerProfile: jest.fn(),
  updateSellerProfile: jest.fn(),
};

const mockToastService = {
  showError: jest.fn(),
  showSuccess: jest.fn(),
};

const mockSellerShellFacade = {
  mergeStore: jest.fn(),
};

const mockRouter = {
  navigate: jest.fn(),
};

describe('StoreConfigPageComponent', () => {
  let component: StoreConfigPageComponent;
  let fixture: ComponentFixture<StoreConfigPageComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FormsModule, StoreConfigPageComponent],
      schemas: [NO_ERRORS_SCHEMA],
      providers: [
        provideRouter([]),
        { provide: StoreService, useValue: mockStoreService },
        { provide: AddressService, useValue: mockAddressService },
        { provide: AuthService, useValue: mockAuthService },
        { provide: ToastService, useValue: mockToastService },
        { provide: SellerShellFacade, useValue: mockSellerShellFacade },
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(StoreConfigPageComponent);
    component = fixture.componentInstance;
    
    // Initialize default state
    mockStoreService.getCuisineTypes.mockReturnValue(of([...DEFAULT_CUISINE_TYPES].reverse()));
    mockStoreService.getStoreCuisineTypes.mockReturnValue(of([...DEFAULT_CUISINE_TYPES]));
    mockStoreService.createStoreCuisineType.mockImplementation((storeId: string, name: string) =>
      of({ id: 'custom-new', name, isDefault: false, storeId } as CuisineTypeDto),
    );
    mockStoreService.deleteStoreCuisineType.mockReturnValue(of(undefined));
    mockStoreService.getMyStore.mockReturnValue(throwError(() => new Error('Not found'))); // Simulate new user
    mockStoreService.getDeliveryTimeOptions.mockReturnValue(of([]));
    mockAuthService.getSellerProfile.mockReturnValue(of({}));
    mockAuthService.updateSellerProfile.mockReturnValue(of({
      fullName: 'Contratante Teste',
      document: null,
      phoneNumber: null,
      email: 'seller@urbeat.local',
    }));

    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('renders the store name first and the contractor name second, with the store name limited to 100 characters', () => {
    const storeNameInput = fixture.nativeElement.querySelector('input[name="storeName"]');
    expect(storeNameInput).toBeTruthy();
    const storeLabel = storeNameInput.previousElementSibling;
    expect(storeLabel.tagName).toBe('LABEL');
    expect(storeLabel.textContent.trim()).toBe('Nome da loja');
    expect(storeNameInput.getAttribute('maxlength')).toBe('100');

    const contractorInput = fixture.nativeElement.querySelector('input[name="contractorName"]');
    expect(contractorInput).toBeTruthy();
    const contractorLabel = contractorInput.previousElementSibling;
    expect(contractorLabel.tagName).toBe('LABEL');
    expect(contractorLabel.textContent.trim()).toBe('Nome do contratante/lojista');
    expect(contractorInput.getAttribute('maxlength')).toBe('120');

    const identityInputs = fixture.nativeElement.querySelectorAll('.identity-grid input');
    expect(identityInputs).toHaveLength(2);
    expect(identityInputs[0].getAttribute('name')).toBe('storeName');
    expect(identityInputs[1].getAttribute('name')).toBe('contractorName');
  });

  it('initializes contractorName from the seller profile and leaves storeName empty when there is no store, preserving profile phone and document', () => {
    mockAuthService.getSellerProfile.mockReturnValue(of({
      fullName: 'Nome do Contratante',
      phoneNumber: '11999998888',
      document: '52998224725',
      email: 'contratante@example.com',
    }));

    component.ngOnInit();

    expect(component.contractorName()).toBe('Nome do Contratante');
    expect(component.storeName()).toBe('');
    expect(component.whatsapp()).toBe('(11) 99999-8888');
    expect(component.storeDocument()).toBe('529.982.247-25');
  });

  it('does not render dashboard-only configuration navigation in the wizard', () => {
    expect(fixture.nativeElement.querySelector('app-config-subnav')).toBeNull();
  });

  it('keeps vertical scrolling and footer spacing scoped to wizard surfaces', () => {
    const globalStyles = readFileSync(resolve(__dirname, '../../../theme/global.scss'), 'utf8');
    const componentStyles = readFileSync(resolve(__dirname, 'store-config-page.component.scss'), 'utf8');
    const template = readFileSync(resolve(__dirname, 'store-config-page.component.html'), 'utf8');

    expect(globalStyles).toContain('.app-shell:has(.urbeat-onboarding)');
    expect(globalStyles).toContain('ion-app:has(.urbeat-onboarding)');
    expect(globalStyles).toContain('overflow-y: auto;');
    expect(componentStyles).toContain('padding-bottom: calc(96px + env(safe-area-inset-bottom, 0px));');
    expect(template).toContain('<ion-content class="store-config-content">');
    expect(globalStyles).not.toMatch(/\.seller-main\s*\{[^}]*overflow-y\s*:/s);
  });

  describe('cuisine categories', () => {
    function fillValidForm(): void {
      component.storeName.set('Minha Loja');
      component.contractorName.set('Contratante Teste');
      component.cuisineType.set('Hamburgueria');
      component.whatsapp.set('(11) 99999-9999');
      component.cep.set('20040-010');
      component.street.set('Rua Exemplo');
      component.number.set('10');
      component.neighborhood.set('Centro');
      component.city.set('Rio de Janeiro');
      component.state.set('RJ');
      component.initialMinute.set(30);
      component.finalMinute.set(60);
      component.maxDeliveryRadiusKm.set(10);
    }

    beforeEach(() => {
      global.confirm = jest.fn(() => true) as any;
    });

    it('starts a new store with an empty cuisineType even though defaults are loaded', () => {
      expect(component.cuisineType()).toBe('');
      expect(component.cuisineTypes().length).toBe(15);
    });

    it('renders the 15 protected defaults sorted with localeCompare pt-BR', () => {
      expect(component.sortedCuisineTypes().map((c) => c.name)).toEqual(DEFAULT_CUISINE_TYPE_NAMES);

      fixture.detectChanges();
      const optionNames = [...fixture.nativeElement.querySelectorAll('select[name="cuisineType"] option')]
        .map((option: HTMLOptionElement) => (option.textContent ?? '').trim())
        .filter((name: string) => name && name !== 'Selecione...');
      expect(optionNames).toEqual(DEFAULT_CUISINE_TYPE_NAMES);
    });

    it('blocks goNext and shows an inline error when the category is empty', async () => {
      mockStoreService.createStore.mockReturnValue(of({ id: 'store-1' }));
      const router = TestBed.inject(Router);
      const navigateSpy = jest.spyOn(router, 'navigate').mockResolvedValue(true);
      fillValidForm();
      component.cuisineType.set('');

      await component.goNext();

      expect(mockStoreService.createStore).not.toHaveBeenCalled();
      expect(navigateSpy).not.toHaveBeenCalled();
      expect(component.cuisineTypeErrorVisible()).toBe(true);
      expect(mockToastService.showError).toHaveBeenCalledWith('Por favor, selecione uma categoria para a loja.');

      fixture.detectChanges();
      const alerts = [...fixture.nativeElement.querySelectorAll('[role="alert"]')]
        .map((el: HTMLElement) => el.textContent ?? '')
        .join(' ');
      expect(alerts).toContain('Selecione uma categoria para a loja.');
    });

    it('blocks saveDraft when the category is empty', async () => {
      fillValidForm();
      component.cuisineType.set('');

      await component.saveDraft();

      expect(mockStoreService.createStore).not.toHaveBeenCalled();
      expect(component.cuisineTypeErrorVisible()).toBe(true);
    });

    it('adds a category during new setup only to local state without any HTTP call', () => {
      component.newCatName.set('Comida Vegana');
      component.addCategory();

      expect(component.cuisineType()).toBe('Comida Vegana');
      expect(component.pendingCuisineTypes().some((c) => c.name === 'Comida Vegana')).toBe(true);
      expect(component.cuisineTypes().some((c) => c.name === 'Comida Vegana')).toBe(false);
      expect(mockStoreService.createStoreCuisineType).not.toHaveBeenCalled();
      expect(component.newCatName()).toBe('');
      expect(component.isCatModalOpen()).toBe(false);
      expect(mockToastService.showSuccess).toHaveBeenCalledWith('Categoria adicionada com sucesso!');
    });

    it('submits the pending local category as cuisineType when creating the store', async () => {
      mockStoreService.createStore.mockReturnValue(of({ id: 'store-1' }));
      mockStoreService.upsertStoreAddress.mockReturnValue(of({}));
      mockStoreService.updateDeliveryConfig.mockReturnValue(of({}));
      const router = TestBed.inject(Router);
      jest.spyOn(router, 'navigate').mockResolvedValue(true);
      fillValidForm();
      component.storeUrl.set('minha-loja');
      component.cuisineType.set('');
      component.newCatName.set('Comida Vegana');
      component.addCategory();

      await component.goNext();

      expect(mockStoreService.createStore).toHaveBeenCalledWith(
        expect.objectContaining({ cuisineType: 'Comida Vegana' }),
      );
      expect(mockStoreService.createStoreCuisineType).not.toHaveBeenCalled();
    });

    it('shows an error when the category name is blank', () => {
      component.newCatName.set('   ');
      component.addCategory();

      expect(mockToastService.showError).toHaveBeenCalledWith('O nome da categoria é obrigatório.');
      expect(component.pendingCuisineTypes().length).toBe(0);
    });

    it('rejects a duplicate name against the defaults without creating anything', () => {
      component.newCatName.set('hamburgueria');
      component.addCategory();

      expect(mockToastService.showError).toHaveBeenCalledWith('Já existe uma categoria com esse nome.');
      expect(component.pendingCuisineTypes().length).toBe(0);
    });

    it('rejects a duplicate name against a pending category', () => {
      component.newCatName.set('Comida Vegana');
      component.addCategory();
      component.newCatName.set('comida vegana');
      component.addCategory();

      expect(component.pendingCuisineTypes().length).toBe(1);
      expect(mockToastService.showError).toHaveBeenCalledWith('Já existe uma categoria com esse nome.');
    });

    it('does not delete protected default categories', () => {
      const defaultCat = component.cuisineTypes().find((c) => c.isDefault)!;
      component.deleteCategory(defaultCat);

      expect(mockStoreService.deleteStoreCuisineType).not.toHaveBeenCalled();
      expect(component.cuisineTypes().some((c) => c.id === defaultCat.id)).toBe(true);
    });

    it('loads defaults plus store-scoped categories for an existing store', () => {
      const storeCuisine: CuisineTypeDto[] = [
        ...DEFAULT_CUISINE_TYPES,
        { id: 'custom-9', name: 'Comida Vegana', isDefault: false, storeId: 'store-1' },
      ];
      mockStoreService.getMyStore.mockReturnValue(of({
        id: 'store-1',
        name: 'Loja',
        slug: 'loja',
        phoneNumber: '11999999999',
        cuisineType: 'Hamburgueria',
        isOpen: true,
        supportsDelivery: true,
        supportsPickup: true,
        minimumOrderValue: 25,
      }));
      mockStoreService.getStoreAddress.mockReturnValue(of({
        street: 'Rua',
        number: '1',
        complement: '',
        neighborhood: 'Centro',
        city: 'Rio',
        state: 'RJ',
        zipCode: '20040-010',
      }));
      mockStoreService.getStoreCuisineTypes.mockReturnValue(of(storeCuisine));

      component.ngOnInit();

      expect(mockStoreService.getStoreCuisineTypes).toHaveBeenCalledWith('store-1');
      expect(component.cuisineTypes().some((c) => c.name === 'Comida Vegana')).toBe(true);
      expect(component.cuisineType()).toBe('Hamburgueria');
    });

    it('creates and deletes store-scoped categories through the store endpoint', () => {
      component.existingStoreId.set('store-1');
      component.newCatName.set('Comida Vegana');
      component.addCategory();

      expect(mockStoreService.createStoreCuisineType).toHaveBeenCalledWith('store-1', 'Comida Vegana');
      const created = component.cuisineTypes().find((c) => c.name === 'Comida Vegana')!;
      expect(created).toBeTruthy();

      component.deleteCategory(created);

      expect(mockStoreService.deleteStoreCuisineType).toHaveBeenCalledWith('store-1', created.id);
    });

    function mockExistingStore(): void {
      mockStoreService.getMyStore.mockReturnValue(of({
        id: 'store-1',
        name: 'Loja',
        slug: 'loja',
        phoneNumber: '11999999999',
        cuisineType: 'Hamburgueria',
        isOpen: true,
        supportsDelivery: true,
        supportsPickup: true,
        minimumOrderValue: 25,
      }));
      mockStoreService.getStoreAddress.mockReturnValue(of({
        street: 'Rua',
        number: '1',
        complement: '',
        neighborhood: 'Centro',
        city: 'Rio',
        state: 'RJ',
        zipCode: '20040-010',
      }));
    }

    it('keeps a category created after the store load started when that load resolves', () => {
      const loadSubject = new Subject<CuisineTypeDto[]>();
      mockStoreService.getStoreCuisineTypes.mockReturnValue(loadSubject.asObservable());
      mockExistingStore();

      component.ngOnInit();

      component.newCatName.set('Comida Vegana');
      component.addCategory();

      loadSubject.next([...DEFAULT_CUISINE_TYPES]);
      loadSubject.complete();

      expect(component.cuisineTypes().some((c) => c.name === 'Comida Vegana')).toBe(true);
    });

    it('keeps a category deleted after the store load started removed when that load resolves', () => {
      const loadSubject = new Subject<CuisineTypeDto[]>();
      const custom: CuisineTypeDto = {
        id: 'custom-9',
        name: 'Comida Vegana',
        isDefault: false,
        storeId: 'store-1',
      };
      mockStoreService.getStoreCuisineTypes.mockReturnValue(loadSubject.asObservable());
      mockExistingStore();

      component.ngOnInit();

      component.deleteCategory(custom);

      loadSubject.next([...DEFAULT_CUISINE_TYPES, custom]);
      loadSubject.complete();

      expect(component.cuisineTypes().some((c) => c.id === 'custom-9')).toBe(false);
    });

    it('clears the empty-category error when a category is selected', () => {
      component.cuisineTypeErrorVisible.set(true);
      component.onCuisineTypeChange('Pizzaria');

      expect(component.cuisineType()).toBe('Pizzaria');
      expect(component.cuisineTypeErrorVisible()).toBe(false);
    });
  });

  describe('image upload', () => {
    it('keeps the selected logo format for upload', async () => {
      const file = new File(['svg'], 'logo.svg', { type: 'image/svg+xml' });

      await component.onLogoSelected(file);

      expect(component.logoFile()).toBe(file);
    });

    it('keeps the logo pending while its preview is still being read', () => {
      const file = new File(['png'], 'logo.png', { type: 'image/png' });
      const { restore } = captureReaders();

      void component.onLogoSelected(file);

      expect(component.logoFile()).toBe(file);
      restore();
    });

    it('keeps the selected banner format for upload', async () => {
      const file = new File(['avif'], 'banner.avif', { type: 'image/avif' });

      await component.onBannerSelected(file);

      expect(component.bannerFile()).toBe(file);
    });

    it('keeps the banner pending while its preview is still being read', () => {
      const file = new File(['webp'], 'banner.webp', { type: 'image/webp' });
      const { restore } = captureReaders();

      void component.onBannerSelected(file);

      expect(component.bannerFile()).toBe(file);
      restore();
    });

    it('creates a preview for a selected file', async () => {
      const file = new File(['image'], 'logo.png', { type: 'image/png' });

      await component.onLogoSelected(file);

      expect(component.logoPreview()).toMatch(/^data:/);
    });

    it('clears the pending logo and shows an error when the preview fails', async () => {
      mockToastService.showError.mockClear();
      const readSpy = jest
        .spyOn(FileReader.prototype, 'readAsDataURL')
        .mockImplementation(function (this: FileReader) {
          (this.onerror as unknown as ((event: unknown) => void) | null)?.(new ProgressEvent('error'));
        });
      const file = new File(['bad'], 'logo.png', { type: 'image/png' });

      await expect(component.onLogoSelected(file)).resolves.toBeUndefined();

      expect(component.logoFile()).toBeNull();
      expect(component.logoPreview()).toBeNull();
      expect(mockToastService.showError).toHaveBeenCalledWith(
        'Não foi possível gerar a pré-visualização da imagem. Tente novamente.',
      );
      readSpy.mockRestore();
    });

    it('clears the pending banner and shows an error when the preview fails', async () => {
      mockToastService.showError.mockClear();
      const readSpy = jest
        .spyOn(FileReader.prototype, 'readAsDataURL')
        .mockImplementation(function (this: FileReader) {
          (this.onerror as unknown as ((event: unknown) => void) | null)?.(new ProgressEvent('error'));
        });
      const file = new File(['bad'], 'banner.avif', { type: 'image/avif' });

      await expect(component.onBannerSelected(file)).resolves.toBeUndefined();

      expect(component.bannerFile()).toBeNull();
      expect(component.bannerPreview()).toBeNull();
      expect(mockToastService.showError).toHaveBeenCalledWith(
        'Não foi possível gerar a pré-visualização da imagem. Tente novamente.',
      );
      readSpy.mockRestore();
    });

    it('preserves an existing logo preview when a newly selected logo fails', async () => {
      mockToastService.showError.mockClear();
      component.logoPreview.set('https://res.cloudinary.com/demo/logo-old.png');
      component.logoFile.set(null);
      const { readers, restore } = captureReaders();
      const file = new File(['bad'], 'logo.png', { type: 'image/png' });

      const pending = component.onLogoSelected(file);
      failReader(readers[0]);

      await pending;

      expect(component.logoPreview()).toBe('https://res.cloudinary.com/demo/logo-old.png');
      expect(component.logoFile()).toBeNull();
      expect(mockToastService.showError).toHaveBeenCalledWith(
        'Não foi possível gerar a pré-visualização da imagem. Tente novamente.',
      );
      restore();
    });

    it('preserves an existing banner preview when a newly selected banner fails', async () => {
      mockToastService.showError.mockClear();
      component.bannerPreview.set('https://res.cloudinary.com/demo/banner-old.png');
      component.bannerFile.set(null);
      const { readers, restore } = captureReaders();
      const file = new File(['bad'], 'banner.avif', { type: 'image/avif' });

      const pending = component.onBannerSelected(file);
      failReader(readers[0]);

      await pending;

      expect(component.bannerPreview()).toBe('https://res.cloudinary.com/demo/banner-old.png');
      expect(component.bannerFile()).toBeNull();
      expect(mockToastService.showError).toHaveBeenCalledWith(
        'Não foi possível gerar a pré-visualização da imagem. Tente novamente.',
      );
      restore();
    });

    it('keeps the prior pending logo and preview when a replacement selection fails', async () => {
      mockToastService.showError.mockClear();
      const { readers, restore } = captureReaders();
      const valid = new File(['good'], 'logo.png', { type: 'image/png' });
      const invalid = new File(['bad'], 'logo-bad.png', { type: 'image/png' });

      const first = component.onLogoSelected(valid);
      resolveReader(readers[0], 'data:image/png;base64,valid');
      await first;

      expect(component.logoFile()).toBe(valid);
      expect(component.logoPreview()).toBe('data:image/png;base64,valid');

      const second = component.onLogoSelected(invalid);
      failReader(readers[1]);
      await second;

      expect(component.logoFile()).toBe(valid);
      expect(component.logoPreview()).toBe('data:image/png;base64,valid');
      expect(mockToastService.showError).toHaveBeenCalledWith(
        'Não foi possível gerar a pré-visualização da imagem. Tente novamente.',
      );
      restore();
    });

    it('keeps the prior pending banner and preview when a replacement selection fails', async () => {
      mockToastService.showError.mockClear();
      const { readers, restore } = captureReaders();
      const valid = new File(['good'], 'banner.avif', { type: 'image/avif' });
      const invalid = new File(['bad'], 'banner-bad.avif', { type: 'image/avif' });

      const first = component.onBannerSelected(valid);
      resolveReader(readers[0], 'data:image/avif;base64,valid');
      await first;

      expect(component.bannerFile()).toBe(valid);
      expect(component.bannerPreview()).toBe('data:image/avif;base64,valid');

      const second = component.onBannerSelected(invalid);
      failReader(readers[1]);
      await second;

      expect(component.bannerFile()).toBe(valid);
      expect(component.bannerPreview()).toBe('data:image/avif;base64,valid');
      expect(mockToastService.showError).toHaveBeenCalledWith(
        'Não foi possível gerar a pré-visualização da imagem. Tente novamente.',
      );
      restore();
    });

    it('restores the immediately previous pending logo when the latest replacement fails before it resolves', async () => {
      mockToastService.showError.mockClear();
      const { readers, restore } = captureReaders();
      const first = new File(['first'], 'first.png', { type: 'image/png' });
      const second = new File(['second'], 'second.png', { type: 'image/png' });

      const firstPromise = component.onLogoSelected(first);
      const secondPromise = component.onLogoSelected(second);

      failReader(readers[1]);
      await secondPromise;

      expect(component.logoFile()).toBe(first);
      expect(mockToastService.showError).toHaveBeenCalledWith(
        'Não foi possível gerar a pré-visualização da imagem. Tente novamente.',
      );

      resolveReader(readers[0], 'data:image/png;base64,first');
      await firstPromise;

      expect(component.logoFile()).toBe(first);
      expect(component.logoPreview()).toBe('data:image/png;base64,first');
      restore();
    });

    it('clears the pending logo when the restored previous pending selection also fails', async () => {
      mockToastService.showError.mockClear();
      const { readers, restore } = captureReaders();
      const first = new File(['first'], 'first.png', { type: 'image/png' });
      const second = new File(['second'], 'second.png', { type: 'image/png' });

      const firstPromise = component.onLogoSelected(first);
      const secondPromise = component.onLogoSelected(second);

      failReader(readers[1]);
      await secondPromise;

      expect(component.logoFile()).toBe(first);

      failReader(readers[0]);
      await firstPromise;

      expect(component.logoFile()).toBeNull();
      expect(component.logoPreview()).toBeNull();
      restore();
    });

    it('clears the pending file without rejecting when readAsDataURL throws', async () => {
      mockToastService.showError.mockClear();
      const readSpy = jest
        .spyOn(FileReader.prototype, 'readAsDataURL')
        .mockImplementation(() => {
          throw new Error('read failed');
        });
      const file = new File(['bad'], 'logo.png', { type: 'image/png' });

      await expect(component.onLogoSelected(file)).resolves.toBeUndefined();

      expect(component.logoFile()).toBeNull();
      expect(component.logoPreview()).toBeNull();
      expect(mockToastService.showError).toHaveBeenCalledWith(
        'Não foi possível gerar a pré-visualização da imagem. Tente novamente.',
      );
      readSpy.mockRestore();
    });

    function captureReaders(): { readers: FileReader[]; restore: () => void } {
      const readers: FileReader[] = [];
      const readSpy = jest
        .spyOn(FileReader.prototype, 'readAsDataURL')
        .mockImplementation(function (this: FileReader) {
          readers.push(this);
        });
      return { readers, restore: () => readSpy.mockRestore() };
    }

    function resolveReader(reader: FileReader, result: string): void {
      Object.defineProperty(reader, 'result', { configurable: true, value: result });
      (reader.onload as unknown as ((event: unknown) => void) | null)?.(new ProgressEvent('load'));
    }

    function failReader(reader: FileReader): void {
      (reader.onerror as unknown as ((event: unknown) => void) | null)?.(new ProgressEvent('error'));
    }

    it('keeps the latest logo preview when a stale reader resolves after a newer selection', async () => {
      const { readers, restore } = captureReaders();
      const first = new File(['first'], 'first.png', { type: 'image/png' });
      const second = new File(['second'], 'second.png', { type: 'image/png' });

      const firstPromise = component.onLogoSelected(first);
      const secondPromise = component.onLogoSelected(second);

      resolveReader(readers[1], 'data:image/png;base64,second');
      resolveReader(readers[0], 'data:image/png;base64,first');

      await Promise.all([firstPromise, secondPromise]);

      expect(component.logoPreview()).toBe('data:image/png;base64,second');
      expect(component.logoFile()).toBe(second);
      restore();
    });

    it('ignores a stale logo failure that resolves after a newer successful selection', async () => {
      mockToastService.showError.mockClear();
      const { readers, restore } = captureReaders();
      const first = new File(['first'], 'first.png', { type: 'image/png' });
      const second = new File(['second'], 'second.png', { type: 'image/png' });

      const firstPromise = component.onLogoSelected(first);
      const secondPromise = component.onLogoSelected(second);

      resolveReader(readers[1], 'data:image/png;base64,second');
      failReader(readers[0]);

      await Promise.all([firstPromise, secondPromise]);

      expect(component.logoPreview()).toBe('data:image/png;base64,second');
      expect(component.logoFile()).toBe(second);
      expect(mockToastService.showError).not.toHaveBeenCalled();
      restore();
    });

    it('restores the older successful logo when the latest selection fails afterwards', async () => {
      mockToastService.showError.mockClear();
      const { readers, restore } = captureReaders();
      const first = new File(['first'], 'first.png', { type: 'image/png' });
      const second = new File(['second'], 'second.png', { type: 'image/png' });

      const firstPromise = component.onLogoSelected(first);
      const secondPromise = component.onLogoSelected(second);

      resolveReader(readers[0], 'data:image/png;base64,first');
      failReader(readers[1]);

      await Promise.all([firstPromise, secondPromise]);

      expect(component.logoFile()).toBe(first);
      expect(component.logoPreview()).toBe('data:image/png;base64,first');
      expect(mockToastService.showError).toHaveBeenCalledWith(
        'Não foi possível gerar a pré-visualização da imagem. Tente novamente.',
      );
      restore();
    });

    it('restores the older successful banner when the latest selection fails afterwards', async () => {
      mockToastService.showError.mockClear();
      const { readers, restore } = captureReaders();
      const first = new File(['first'], 'first.avif', { type: 'image/avif' });
      const second = new File(['second'], 'second.avif', { type: 'image/avif' });

      const firstPromise = component.onBannerSelected(first);
      const secondPromise = component.onBannerSelected(second);

      resolveReader(readers[0], 'data:image/avif;base64,first');
      failReader(readers[1]);

      await Promise.all([firstPromise, secondPromise]);

      expect(component.bannerFile()).toBe(first);
      expect(component.bannerPreview()).toBe('data:image/avif;base64,first');
      expect(mockToastService.showError).toHaveBeenCalledWith(
        'Não foi possível gerar a pré-visualização da imagem. Tente novamente.',
      );
      restore();
    });

    it('keeps the latest banner preview when a stale reader resolves after a newer selection', async () => {
      const { readers, restore } = captureReaders();
      const first = new File(['first'], 'first.avif', { type: 'image/avif' });
      const second = new File(['second'], 'second.avif', { type: 'image/avif' });

      const firstPromise = component.onBannerSelected(first);
      const secondPromise = component.onBannerSelected(second);

      resolveReader(readers[1], 'data:image/avif;base64,second');
      resolveReader(readers[0], 'data:image/avif;base64,first');

      await Promise.all([firstPromise, secondPromise]);

      expect(component.bannerPreview()).toBe('data:image/avif;base64,second');
      expect(component.bannerFile()).toBe(second);
      restore();
    });

    it('guards logo and banner previews independently', async () => {
      const { readers, restore } = captureReaders();
      const logo = new File(['logo'], 'logo.png', { type: 'image/png' });
      const banner = new File(['banner'], 'banner.avif', { type: 'image/avif' });

      const logoPromise = component.onLogoSelected(logo);
      const bannerPromise = component.onBannerSelected(banner);

      resolveReader(readers[1], 'data:image/avif;base64,banner');
      resolveReader(readers[0], 'data:image/png;base64,logo');

      await Promise.all([logoPromise, bannerPromise]);

      expect(component.logoPreview()).toBe('data:image/png;base64,logo');
      expect(component.bannerPreview()).toBe('data:image/avif;base64,banner');
      restore();
    });

    it('does not restore a removed logo when an in-flight reader resolves afterwards', async () => {
      const { readers, restore } = captureReaders();
      const file = new File(['logo'], 'logo.png', { type: 'image/png' });

      const pending = component.onLogoSelected(file);
      component.onLogoRemoved();

      resolveReader(readers[0], 'data:image/png;base64,logo');

      await pending;

      expect(component.logoFile()).toBeNull();
      expect(component.logoPreview()).toBeNull();
      restore();
    });

    it('does not restore a removed banner when an in-flight reader resolves afterwards', async () => {
      const { readers, restore } = captureReaders();
      const file = new File(['banner'], 'banner.avif', { type: 'image/avif' });

      const pending = component.onBannerSelected(file);
      component.onBannerRemoved();

      resolveReader(readers[0], 'data:image/avif;base64,banner');

      await pending;

      expect(component.bannerFile()).toBeNull();
      expect(component.bannerPreview()).toBeNull();
      restore();
    });
  });

  describe('store business identifiers', () => {
    it('masks CPF and validates its checksum', () => {
      component.onDocumentInput('52998224725');
      component.onDocumentBlur();

      expect(component.storeDocument()).toBe('529.982.247-25');
      expect(component.documentValid()).toBe(true);
    });

    it('rejects an invalid CNPJ or CPF', () => {
      component.onDocumentInput('11111111111');
      component.onDocumentBlur();

      expect(component.documentValid()).toBe(false);
    });

    it('limits Pix key to fifty characters', () => {
      component.onPixKeyInput('a'.repeat(60));

      expect(component.pixKey().length).toBe(50);
    });
  });

  describe('submit slug conflict', () => {
    function fillValidForm(): void {
      component.storeName.set('Minha Loja');
      component.contractorName.set('Contratante Teste');
      component.cuisineType.set('Hamburgueria');
      component.whatsapp.set('(11) 99999-9999');
      component.cep.set('20040-010');
      component.street.set('Rua Exemplo');
      component.number.set('10');
      component.neighborhood.set('Centro');
      component.city.set('Rio de Janeiro');
      component.state.set('RJ');
      component.initialMinute.set(30);
      component.finalMinute.set(60);
      component.maxDeliveryRadiusKm.set(10);
    }

    const slugConflictError = {
      status: 409,
      error: { error: 'O nome ou a URL da loja já está em uso. Escolha outro.' },
    };

    it('does not navigate and surfaces the URL conflict when create returns 409', async () => {
      mockStoreService.createStore.mockReturnValue(throwError(() => slugConflictError));
      const router = TestBed.inject(Router);
      const navigateSpy = jest.spyOn(router, 'navigate').mockResolvedValue(true);
      fillValidForm();
      component.storeUrl.set('minha-loja');

      await component.goNext();

      expect(component.storeUrlConflict()).toBe('O nome ou a URL da loja já está em uso. Escolha outro.');
      expect(component.sectionsExpanded().url).toBe(true);
      expect(mockToastService.showError).toHaveBeenCalledWith('O nome ou a URL da loja já está em uso. Escolha outro.');
      expect(navigateSpy).not.toHaveBeenCalled();
    });

    it('does not navigate and surfaces the URL conflict when update returns 409', async () => {
      mockStoreService.updateStore.mockReturnValue(throwError(() => slugConflictError));
      const router = TestBed.inject(Router);
      const navigateSpy = jest.spyOn(router, 'navigate').mockResolvedValue(true);
      component.existingStoreId.set('store-1');
      fillValidForm();
      component.storeUrl.set('minha-loja');

      await component.goNext();

      expect(component.storeUrlConflict()).toBe('O nome ou a URL da loja já está em uso. Escolha outro.');
      expect(component.sectionsExpanded().url).toBe(true);
      expect(navigateSpy).not.toHaveBeenCalled();
    });

    it('navigates when submit succeeds without a URL conflict', async () => {
      mockStoreService.createStore.mockReturnValue(of({ id: 'store-1' }));
      mockStoreService.upsertStoreAddress.mockReturnValue(of({}));
      mockStoreService.updateDeliveryConfig.mockReturnValue(of({}));
      const router = TestBed.inject(Router);
      const navigateSpy = jest.spyOn(router, 'navigate').mockResolvedValue(true);
      fillValidForm();
      component.storeUrl.set('minha-loja');

      await component.goNext();

      expect(component.storeUrlConflict()).toBeNull();
      expect(navigateSpy).toHaveBeenCalledWith(['/configurar-loja/horarios']);
    });

    it('omits deliveryAreas from the delivery-config payload when saving an existing store from page 1', async () => {
      mockStoreService.updateStore.mockReturnValue(of({ id: 'store-1' }));
      mockStoreService.upsertStoreAddress.mockReturnValue(of({}));
      mockStoreService.updateDeliveryConfig.mockReturnValue(of({}));
      const router = TestBed.inject(Router);
      const navigateSpy = jest.spyOn(router, 'navigate').mockResolvedValue(true);
      component.existingStoreId.set('store-1');
      fillValidForm();
      component.storeUrl.set('minha-loja');

      await component.goNext();

      const calls = mockStoreService.updateDeliveryConfig.mock.calls;
      const payload = calls[calls.length - 1][1];
      expect(payload).not.toHaveProperty('deliveryAreas');
      expect(navigateSpy).toHaveBeenCalledWith(['/configurar-loja/horarios']);
    });

    it('preserves an active freeShippingToday and the threshold when advancing an existing store', async () => {
      mockStoreService.getMyStore.mockReturnValue(of({
        id: 'store-1',
        name: 'Minha Loja',
        slug: 'minha-loja',
        phoneNumber: '11999999999',
        cuisineType: 'Hamburgueria',
        isOpen: true,
        supportsDelivery: true,
        supportsPickup: true,
        minimumOrderValue: 25,
        freeShippingToday: true,
        freeShippingThreshold: 50,
      }));
      mockStoreService.getStoreAddress.mockReturnValue(of({
        street: 'Rua Exemplo',
        number: '10',
        complement: '',
        neighborhood: 'Centro',
        city: 'Rio de Janeiro',
        state: 'RJ',
        zipCode: '20040-010',
      }));
      mockStoreService.updateStore.mockReturnValue(of({ id: 'store-1' }));
      mockStoreService.upsertStoreAddress.mockReturnValue(of({}));
      mockStoreService.updateDeliveryConfig.mockReturnValue(of({}));
      const router = TestBed.inject(Router);
      const navigateSpy = jest.spyOn(router, 'navigate').mockResolvedValue(true);

      component.ngOnInit();
      fillValidForm();
      component.storeUrl.set('minha-loja');

      await component.goNext();

      const calls = mockStoreService.updateDeliveryConfig.mock.calls;
      const payload = calls[calls.length - 1][1];
      expect(payload.freeShippingToday).toBe(true);
      expect(payload.freeShippingThreshold).toBe(50);
      expect(navigateSpy).toHaveBeenCalledWith(['/configurar-loja/horarios']);
    });
  });

  describe('store URL validation', () => {
    const requiredUrlMessage = 'A URL da loja é obrigatória.';
    const shortUrlMessage = 'A URL da loja deve ter pelo menos 3 caracteres.';
    const formatUrlMessage = 'Use apenas letras minúsculas, números e hífens, sem hífens consecutivos ou nas extremidades.';

    beforeEach(() => {
      jest.clearAllMocks();
    });

    function fillValidForm(): void {
      component.storeName.set('Minha Loja');
      component.contractorName.set('Contratante Teste');
      component.cuisineType.set('Hamburgueria');
      component.whatsapp.set('(11) 99999-9999');
      component.cep.set('20040-010');
      component.street.set('Rua Exemplo');
      component.number.set('10');
      component.neighborhood.set('Centro');
      component.city.set('Rio de Janeiro');
      component.state.set('RJ');
      component.initialMinute.set(30);
      component.finalMinute.set(60);
      component.maxDeliveryRadiusKm.set(10);
    }

    function mockSuccessfulSave(): void {
      mockStoreService.createStore.mockReturnValue(of({ id: 'store-1' }));
      mockStoreService.upsertStoreAddress.mockReturnValue(of({}));
      mockStoreService.updateDeliveryConfig.mockReturnValue(of({}));
    }

    function visibleErrorText(): string {
      fixture.detectChanges();
      const alertEls = fixture.nativeElement.querySelectorAll('[role="alert"]');
      return [...alertEls].map((el) => el.textContent ?? '').join(' ');
    }

    it('blocks advancing and shows an inline error when the URL is empty', async () => {
      fillValidForm();
      component.storeUrl.set('');
      const router = TestBed.inject(Router);
      const navigateSpy = jest.spyOn(router, 'navigate').mockResolvedValue(true);
      mockSuccessfulSave();

      await component.goNext();

      expect(component.storeUrlError()).toBe(requiredUrlMessage);
      expect(component.storeUrlErrorVisible()).toBe(true);
      expect(component.storeUrlConflict()).toBeNull();
      expect(component.sectionsExpanded().url).toBe(true);
      expect(mockToastService.showError).toHaveBeenCalledWith(requiredUrlMessage);
      expect(mockStoreService.createStore).not.toHaveBeenCalled();
      expect(navigateSpy).not.toHaveBeenCalled();
      expect(visibleErrorText()).toContain(requiredUrlMessage);
    });

    it('blocks advancing when the URL has fewer than 3 characters', async () => {
      fillValidForm();
      component.storeUrl.set('ab');
      const router = TestBed.inject(Router);
      const navigateSpy = jest.spyOn(router, 'navigate').mockResolvedValue(true);
      mockSuccessfulSave();

      await component.goNext();

      expect(component.storeUrlError()).toBe(shortUrlMessage);
      expect(mockToastService.showError).toHaveBeenCalledWith(shortUrlMessage);
      expect(mockStoreService.createStore).not.toHaveBeenCalled();
      expect(navigateSpy).not.toHaveBeenCalled();
      expect(visibleErrorText()).toContain(shortUrlMessage);
    });

    it('blocks advancing when the URL format is invalid', async () => {
      fillValidForm();
      component.storeUrl.set('loja--loja');
      const router = TestBed.inject(Router);
      const navigateSpy = jest.spyOn(router, 'navigate').mockResolvedValue(true);
      mockSuccessfulSave();

      await component.goNext();

      expect(component.storeUrlError()).toBe(formatUrlMessage);
      expect(mockToastService.showError).toHaveBeenCalledWith(formatUrlMessage);
      expect(mockStoreService.createStore).not.toHaveBeenCalled();
      expect(navigateSpy).not.toHaveBeenCalled();
      expect(visibleErrorText()).toContain(formatUrlMessage);
    });

    it.each([' minha-loja', 'minha-loja ', ' minha-loja ', 'minha loja'])(
      'blocks advancing and never strips spaces when the URL is "%s"',
      async (url: string) => {
        fillValidForm();
        component.storeUrl.set(url);
        const router = TestBed.inject(Router);
        const navigateSpy = jest.spyOn(router, 'navigate').mockResolvedValue(true);
        mockSuccessfulSave();

        await component.goNext();

        expect(component.storeUrlError()).toBe(formatUrlMessage);
        expect(component.storeUrlErrorVisible()).toBe(true);
        expect(mockToastService.showError).toHaveBeenCalledWith(formatUrlMessage);
        expect(mockStoreService.createStore).not.toHaveBeenCalled();
        expect(mockStoreService.updateStore).not.toHaveBeenCalled();
        expect(navigateSpy).not.toHaveBeenCalled();
        expect(visibleErrorText()).toContain(formatUrlMessage);
      },
    );

    it('sends the URL exactly as validated when it is a valid slug', async () => {
      fillValidForm();
      component.storeUrl.set('minha-loja');
      const router = TestBed.inject(Router);
      const navigateSpy = jest.spyOn(router, 'navigate').mockResolvedValue(true);
      mockSuccessfulSave();

      await component.goNext();

      expect(component.storeUrlError()).toBeNull();
      expect(mockStoreService.createStore).toHaveBeenCalledWith(
        expect.objectContaining({ slug: 'minha-loja' }),
      );
      expect(navigateSpy).toHaveBeenCalledWith(['/configurar-loja/horarios']);
    });

    it('normalizes typed URLs to lowercase and accepts a valid slug', async () => {
      fillValidForm();
      mockSuccessfulSave();
      const router = TestBed.inject(Router);
      const navigateSpy = jest.spyOn(router, 'navigate').mockResolvedValue(true);

      component.onStoreUrlInput('Loja-Demo');

      expect(component.storeUrl()).toBe('loja-demo');
      expect(component.storeUrlError()).toBeNull();

      await component.goNext();

      expect(navigateSpy).toHaveBeenCalledWith(['/configurar-loja/horarios']);
      expect(mockStoreService.createStore).toHaveBeenCalledWith(
        expect.objectContaining({ slug: 'loja-demo' }),
      );
    });

    it('clears the inline error when the user edits the URL to a valid value', async () => {
      fillValidForm();
      component.storeUrl.set('-invalida');
      const router = TestBed.inject(Router);
      const navigateSpy = jest.spyOn(router, 'navigate').mockResolvedValue(true);
      mockSuccessfulSave();

      await component.goNext();
      expect(component.storeUrlError()).toBe(formatUrlMessage);
      expect(component.storeUrlErrorVisible()).toBe(true);
      expect(visibleErrorText()).toContain(formatUrlMessage);

      component.onStoreUrlInput('minha-loja');

      expect(component.storeUrl()).toBe('minha-loja');
      expect(component.storeUrlError()).toBeNull();
      expect(component.storeUrlErrorVisible()).toBe(false);
      expect(component.storeUrlConflict()).toBeNull();
      expect(visibleErrorText()).not.toContain(formatUrlMessage);

      await component.goNext();

      expect(navigateSpy).toHaveBeenCalledWith(['/configurar-loja/horarios']);
      expect(mockStoreService.createStore).toHaveBeenCalledWith(
        expect.objectContaining({ slug: 'minha-loja' }),
      );
    });
  });

  describe('image upload failure after store save', () => {
    function fillValidForm(): void {
      component.storeName.set('Minha Loja');
      component.contractorName.set('Contratante Teste');
      component.cuisineType.set('Hamburgueria');
      component.whatsapp.set('(11) 99999-9999');
      component.cep.set('20040-010');
      component.street.set('Rua Exemplo');
      component.number.set('10');
      component.neighborhood.set('Centro');
      component.city.set('Rio de Janeiro');
      component.state.set('RJ');
      component.initialMinute.set(30);
      component.finalMinute.set(60);
      component.maxDeliveryRadiusKm.set(10);
    }

    function mockStoreCreation(): void {
      mockStoreService.createStore.mockReturnValue(of({ id: 'store-1' }));
      mockStoreService.upsertStoreAddress.mockReturnValue(of({}));
      mockStoreService.updateDeliveryConfig.mockReturnValue(of({}));
    }

    it('does not duplicate the interceptor 413 size message and stops the wizard when the logo upload fails with HTTP 413', async () => {
      jest.clearAllMocks();
      mockStoreCreation();
      mockStoreService.uploadImage.mockReturnValue(throwError(() => ({ status: 413, message: 'Payload Too Large' })));
      const router = TestBed.inject(Router);
      const navigateSpy = jest.spyOn(router, 'navigate').mockResolvedValue(true);
      fillValidForm();
      component.storeUrl.set('minha-loja');
      const file = new File(['logo'], 'logo.png', { type: 'image/png' });
      await component.onLogoSelected(file);

      await component.goNext();

      expect(navigateSpy).not.toHaveBeenCalled();
      expect(mockStoreService.updateStore).not.toHaveBeenCalled();
      expect(mockStoreService.upsertStoreAddress).not.toHaveBeenCalled();
      expect(mockStoreService.updateDeliveryConfig).not.toHaveBeenCalled();
      // The global error interceptor already shows the upload infrastructure/size
      // message (see error.interceptor.spec.ts), so the wizard must not toast it again.
      expect(mockToastService.showError).not.toHaveBeenCalled();
      expect(component.saveStatus()).toBe('error');
    });

    it('shows a generic failure and stops the wizard when the logo upload fails without HTTP 413', async () => {
      jest.clearAllMocks();
      mockStoreCreation();
      mockStoreService.uploadImage.mockReturnValue(throwError(() => new Error('Upload failed')));
      const router = TestBed.inject(Router);
      const navigateSpy = jest.spyOn(router, 'navigate').mockResolvedValue(true);
      fillValidForm();
      component.storeUrl.set('minha-loja');
      const file = new File(['logo'], 'logo.png', { type: 'image/png' });
      await component.onLogoSelected(file);

      await component.goNext();

      expect(navigateSpy).not.toHaveBeenCalled();
      expect(mockStoreService.updateStore).not.toHaveBeenCalled();
      expect(mockStoreService.upsertStoreAddress).not.toHaveBeenCalled();
      expect(mockStoreService.updateDeliveryConfig).not.toHaveBeenCalled();
      expect(mockToastService.showError).toHaveBeenCalledWith('Não foi possível enviar a imagem. Tente novamente.');
      expect(mockToastService.showError).not.toHaveBeenCalledWith('A logo deve ter no máximo 2 MB.');
      expect(component.saveStatus()).toBe('error');
    });

    it('preserves the existing media URL and advances when no new image is selected', async () => {
      jest.clearAllMocks();
      mockStoreService.updateStore.mockReturnValue(of({ id: 'store-1' }));
      mockStoreService.upsertStoreAddress.mockReturnValue(of({}));
      mockStoreService.updateDeliveryConfig.mockReturnValue(of({}));
      component.existingStoreId.set('store-1');
      component.existingLogoUrl.set('https://res.cloudinary.com/demo/logo-old.png');
      component.existingBannerUrl.set('https://res.cloudinary.com/demo/banner-old.png');
      component.logoPreview.set('https://res.cloudinary.com/demo/logo-old.png');
      component.bannerPreview.set('https://res.cloudinary.com/demo/banner-old.png');
      const router = TestBed.inject(Router);
      const navigateSpy = jest.spyOn(router, 'navigate').mockResolvedValue(true);
      fillValidForm();
      component.storeUrl.set('minha-loja');

      await component.goNext();

      expect(navigateSpy).toHaveBeenCalledWith(['/configurar-loja/horarios']);
      expect(mockStoreService.uploadImage).not.toHaveBeenCalled();
      expect(mockStoreService.upsertStoreAddress).toHaveBeenCalled();
      expect(mockStoreService.updateStore).toHaveBeenCalledTimes(1);
      expect(mockStoreService.updateStore).toHaveBeenCalledWith('store-1', expect.objectContaining({
        logoUrl: 'https://res.cloudinary.com/demo/logo-old.png',
        bannerUrl: 'https://res.cloudinary.com/demo/banner-old.png',
      }));
    });
  });

  describe('store and contractor identity', () => {
    function fillValidIdentityForm(): void {
      component.storeName.set('Minha Loja');
      component.contractorName.set('Contratante Teste');
      component.cuisineType.set('Hamburgueria');
      component.whatsapp.set('(11) 99999-9999');
      component.cep.set('20040-010');
      component.street.set('Rua Exemplo');
      component.number.set('10');
      component.neighborhood.set('Centro');
      component.city.set('Rio de Janeiro');
      component.state.set('RJ');
      component.initialMinute.set(30);
      component.finalMinute.set(60);
      component.maxDeliveryRadiusKm.set(10);
    }

    it('populates storeName from the store and contractorName from the seller profile for an existing store', () => {
      mockStoreService.getMyStore.mockReturnValue(of({
        id: 'store-1',
        name: 'Loja do Banco',
        slug: 'loja-do-banco',
        phoneNumber: '11999999999',
        cuisineType: 'Hamburgueria',
        isOpen: true,
        supportsDelivery: true,
        supportsPickup: true,
        minimumOrderValue: 25,
      }));
      mockStoreService.getStoreAddress.mockReturnValue(of({
        street: 'Rua Exemplo',
        number: '10',
        complement: '',
        neighborhood: 'Centro',
        city: 'Rio de Janeiro',
        state: 'RJ',
        zipCode: '20040-010',
      }));
      mockAuthService.getSellerProfile.mockReturnValue(of({
        fullName: 'Contratante do Banco',
        document: null,
        phoneNumber: null,
        email: 'contratante@example.com',
      }));

      component.ngOnInit();

      expect(component.storeName()).toBe('Loja do Banco');
      expect(component.contractorName()).toBe('Contratante do Banco');
    });

    it('sends the store name as name and persists the contractor name to the seller profile', async () => {
      mockStoreService.createStore.mockReturnValue(of({ id: 'store-1' }));
      mockStoreService.upsertStoreAddress.mockReturnValue(of({}));
      mockStoreService.updateDeliveryConfig.mockReturnValue(of({}));
      const router = TestBed.inject(Router);
      jest.spyOn(router, 'navigate').mockResolvedValue(true);
      fillValidIdentityForm();
      component.storeUrl.set('minha-loja');

      await component.goNext();

      expect(mockStoreService.createStore).toHaveBeenCalledWith(
        expect.objectContaining({ name: 'Minha Loja' }),
      );
      expect(mockAuthService.updateSellerProfile).toHaveBeenCalledWith({ fullName: 'Contratante Teste' });
    });

    it('does not advance and reports an error when the contractor name update fails', async () => {
      mockStoreService.createStore.mockReturnValue(of({ id: 'store-1' }));
      mockStoreService.upsertStoreAddress.mockReturnValue(of({}));
      mockStoreService.updateDeliveryConfig.mockReturnValue(of({}));
      mockAuthService.updateSellerProfile.mockReturnValue(throwError(() => new Error('profile failed')));
      const router = TestBed.inject(Router);
      const navigateSpy = jest.spyOn(router, 'navigate').mockResolvedValue(true);
      fillValidIdentityForm();
      component.storeUrl.set('minha-loja');

      await component.goNext();

      expect(navigateSpy).not.toHaveBeenCalled();
      expect(component.saveStatus()).toBe('error');
    });

    it('blocks advancing when the store name exceeds one hundred characters', async () => {
      jest.clearAllMocks();
      const router = TestBed.inject(Router);
      const navigateSpy = jest.spyOn(router, 'navigate').mockResolvedValue(true);
      fillValidIdentityForm();
      component.storeName.set('a'.repeat(101));
      component.storeUrl.set('minha-loja');

      await component.goNext();

      expect(mockStoreService.createStore).not.toHaveBeenCalled();
      expect(mockAuthService.updateSellerProfile).not.toHaveBeenCalled();
      expect(navigateSpy).not.toHaveBeenCalled();
    });
  });

  describe('seller shell store name sync', () => {
    function fillValidForm(): void {
      component.storeName.set('Minha Loja');
      component.contractorName.set('Contratante Teste');
      component.cuisineType.set('Hamburgueria');
      component.whatsapp.set('(11) 99999-9999');
      component.cep.set('20040-010');
      component.street.set('Rua Exemplo');
      component.number.set('10');
      component.neighborhood.set('Centro');
      component.city.set('Rio de Janeiro');
      component.state.set('RJ');
      component.initialMinute.set(30);
      component.finalMinute.set(60);
      component.maxDeliveryRadiusKm.set(10);
    }

    function savedStore(name: string) {
      return {
        id: 'store-1',
        ownerUserId: 'owner1',
        name,
        slug: 'minha-loja',
        phoneNumber: '11999999999',
        cuisineType: 'Hamburgueria',
        isOpen: true,
        isPublished: false,
        isSubscriptionBlocked: false,
        supportsDelivery: true,
        supportsPickup: true,
        minimumOrderValue: 25,
        deliveryAreas: [],
        averageRating: 0,
        totalReviews: 0,
      };
    }

    it('merges the final store into the seller shell facade only after a successful create', async () => {
      jest.clearAllMocks();
      const store = savedStore('Minha Loja');
      mockStoreService.createStore.mockReturnValue(of(store));
      mockStoreService.upsertStoreAddress.mockReturnValue(of({}));
      mockStoreService.updateDeliveryConfig.mockReturnValue(of({}));
      const router = TestBed.inject(Router);
      jest.spyOn(router, 'navigate').mockResolvedValue(true);
      fillValidForm();
      component.storeUrl.set('minha-loja');

      await component.goNext();

      expect(mockSellerShellFacade.mergeStore).toHaveBeenCalledTimes(1);
      expect(mockSellerShellFacade.mergeStore).toHaveBeenCalledWith(expect.objectContaining({
        id: 'store-1',
        name: 'Minha Loja',
      }));
    });

    it('merges the final store with the final name and uploaded media after a successful create', async () => {
      jest.clearAllMocks();
      const store = savedStore('Minha Loja');
      const logoUrl = 'https://res.cloudinary.com/demo/new-logo.png';
      const bannerUrl = 'https://res.cloudinary.com/demo/new-banner.png';
      mockStoreService.createStore.mockReturnValue(of(store));
      mockStoreService.uploadImage
        .mockReturnValueOnce(of({ url: logoUrl }))
        .mockReturnValueOnce(of({ url: bannerUrl }));
      mockStoreService.updateStore.mockReturnValue(of({ ...store, logoUrl, bannerUrl }));
      mockStoreService.upsertStoreAddress.mockReturnValue(of({}));
      mockStoreService.updateDeliveryConfig.mockReturnValue(of({}));
      const router = TestBed.inject(Router);
      jest.spyOn(router, 'navigate').mockResolvedValue(true);
      fillValidForm();
      component.storeUrl.set('minha-loja');
      component.logoFile.set(new File(['logo'], 'logo.png', { type: 'image/png' }));
      component.bannerFile.set(new File(['banner'], 'banner.avif', { type: 'image/avif' }));

      await component.goNext();

      expect(mockStoreService.uploadImage).toHaveBeenCalledTimes(2);
      expect(mockSellerShellFacade.mergeStore).toHaveBeenCalledTimes(1);
      expect(mockSellerShellFacade.mergeStore).toHaveBeenCalledWith(expect.objectContaining({
        id: 'store-1',
        name: 'Minha Loja',
        logoUrl,
        bannerUrl,
      }));
    });

    it('merges the final store with the final name and uploaded media after a successful update', async () => {
      jest.clearAllMocks();
      const store = savedStore('Loja Atualizada');
      const logoUrl = 'https://res.cloudinary.com/demo/updated-logo.png';
      mockStoreService.updateStore.mockReturnValue(of({ ...store, logoUrl }));
      mockStoreService.uploadImage.mockReturnValue(of({ url: logoUrl }));
      mockStoreService.upsertStoreAddress.mockReturnValue(of({}));
      mockStoreService.updateDeliveryConfig.mockReturnValue(of({}));
      const router = TestBed.inject(Router);
      jest.spyOn(router, 'navigate').mockResolvedValue(true);
      component.existingStoreId.set('store-1');
      fillValidForm();
      component.storeName.set('Loja Atualizada');
      component.storeUrl.set('minha-loja');
      component.logoFile.set(new File(['logo'], 'logo.png', { type: 'image/png' }));

      await component.goNext();

      expect(mockSellerShellFacade.mergeStore).toHaveBeenCalledTimes(1);
      expect(mockSellerShellFacade.mergeStore).toHaveBeenCalledWith(expect.objectContaining({
        id: 'store-1',
        name: 'Loja Atualizada',
        logoUrl,
      }));
    });

    it('resolves submit with an error, does not navigate, and does not merge when updateStore fails after a successful image upload', async () => {
      jest.clearAllMocks();
      const store = savedStore('Minha Loja');
      const logoUrl = 'https://res.cloudinary.com/demo/new-logo.png';
      mockStoreService.createStore.mockReturnValue(of(store));
      mockStoreService.uploadImage.mockReturnValue(of({ url: logoUrl }));
      mockStoreService.updateStore.mockReturnValue(throwError(() => new Error('update failed')));
      mockStoreService.upsertStoreAddress.mockReturnValue(of({}));
      mockStoreService.updateDeliveryConfig.mockReturnValue(of({}));
      const router = TestBed.inject(Router);
      const navigateSpy = jest.spyOn(router, 'navigate').mockResolvedValue(true);
      fillValidForm();
      component.storeUrl.set('minha-loja');
      component.logoFile.set(new File(['logo'], 'logo.png', { type: 'image/png' }));

      const result = await component.submit();

      expect(result).toBe(false);
      expect(mockStoreService.uploadImage).toHaveBeenCalledTimes(1);
      expect(mockStoreService.updateStore).toHaveBeenCalledTimes(1);
      expect(navigateSpy).not.toHaveBeenCalled();
      expect(component.saveStatus()).toBe('error');
      expect(component.loading()).toBe(false);
      expect(mockToastService.showError).toHaveBeenCalledWith(
        'Não foi possível atualizar as informações da loja. Verifique os dados.',
      );
      expect(mockSellerShellFacade.mergeStore).not.toHaveBeenCalled();
      expect(mockStoreService.upsertStoreAddress).not.toHaveBeenCalled();
      expect(mockStoreService.updateDeliveryConfig).not.toHaveBeenCalled();
    });

    it('does not merge the store when the create request fails', async () => {
      jest.clearAllMocks();
      mockStoreService.createStore.mockReturnValue(throwError(() => new Error('create failed')));
      const router = TestBed.inject(Router);
      jest.spyOn(router, 'navigate').mockResolvedValue(true);
      fillValidForm();
      component.storeUrl.set('minha-loja');

      await component.goNext();

      expect(mockSellerShellFacade.mergeStore).not.toHaveBeenCalled();
    });

    it('does not merge the store when the media upload fails', async () => {
      jest.clearAllMocks();
      mockStoreService.createStore.mockReturnValue(of(savedStore('Minha Loja')));
      mockStoreService.uploadImage.mockReturnValue(throwError(() => new Error('upload failed')));
      mockStoreService.upsertStoreAddress.mockReturnValue(of({}));
      mockStoreService.updateDeliveryConfig.mockReturnValue(of({}));
      const router = TestBed.inject(Router);
      jest.spyOn(router, 'navigate').mockResolvedValue(true);
      fillValidForm();
      component.storeUrl.set('minha-loja');
      component.logoFile.set(new File(['logo'], 'logo.png', { type: 'image/png' }));

      await component.goNext();

      expect(mockSellerShellFacade.mergeStore).not.toHaveBeenCalled();
    });

    it('does not merge the store when the address or delivery config save fails', async () => {
      jest.clearAllMocks();
      mockStoreService.createStore.mockReturnValue(of(savedStore('Minha Loja')));
      mockStoreService.upsertStoreAddress.mockReturnValue(of({}));
      mockStoreService.updateDeliveryConfig.mockReturnValue(throwError(() => new Error('delivery failed')));
      const router = TestBed.inject(Router);
      jest.spyOn(router, 'navigate').mockResolvedValue(true);
      fillValidForm();
      component.storeUrl.set('minha-loja');

      await component.goNext();

      expect(mockSellerShellFacade.mergeStore).not.toHaveBeenCalled();
    });

    it('does not merge the store when the contractor profile update fails', async () => {
      jest.clearAllMocks();
      mockStoreService.createStore.mockReturnValue(of(savedStore('Minha Loja')));
      mockStoreService.upsertStoreAddress.mockReturnValue(of({}));
      mockStoreService.updateDeliveryConfig.mockReturnValue(of({}));
      mockAuthService.updateSellerProfile.mockReturnValue(throwError(() => new Error('profile failed')));
      const router = TestBed.inject(Router);
      jest.spyOn(router, 'navigate').mockResolvedValue(true);
      fillValidForm();
      component.storeUrl.set('minha-loja');

      await component.goNext();

      expect(mockSellerShellFacade.mergeStore).not.toHaveBeenCalled();
    });
  });
});
