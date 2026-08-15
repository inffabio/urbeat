import { ComponentFixture, TestBed, NO_ERRORS_SCHEMA } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { provideRouter, Router } from '@angular/router';
import { StoreConfigPageComponent } from './store-config-page.component';
import { StoreService } from '../../core/services/store.service';
import { AddressService } from '../../core/services/address.service';
import { AuthService } from '../../core/services/auth.service';
import { ToastService } from '../../core/services/toast.service';
import { of, throwError } from 'rxjs';
import { CuisineTypeDto } from '../../shared/models/store.model';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

// Mocks
const mockStoreService = {
  getCuisineTypes: jest.fn(),
  getMyStore: jest.fn(),
  getDeliveryTimeOptions: jest.fn(),
  createCuisineType: jest.fn(),
};

const mockAddressService = {
  lookupCep: jest.fn(),
};

const mockAuthService = {
  getSellerProfile: jest.fn(),
};

const mockToastService = {
  showError: jest.fn(),
  showSuccess: jest.fn(),
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
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(StoreConfigPageComponent);
    component = fixture.componentInstance;
    
    // Initialize default state
    mockStoreService.getCuisineTypes.mockReturnValue(of([
      { id: '1', name: 'Hamburgueria' },
      { id: '2', name: 'Pizzaria' }
    ]));
    mockStoreService.getMyStore.mockReturnValue(throwError(() => new Error('Not found'))); // Simulate new user
    mockStoreService.getDeliveryTimeOptions.mockReturnValue(of([]));
    mockAuthService.getSellerProfile.mockReturnValue(of({}));
    mockStoreService.createCuisineType.mockImplementation((name: string) =>
      of({ id: 'new-id', name } as CuisineTypeDto),
    );

    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('does not render dashboard-only configuration navigation in the wizard', () => {
    expect(fixture.nativeElement.querySelector('app-config-subnav')).toBeNull();
  });

  it('keeps vertical scrolling and footer spacing scoped to wizard surfaces', () => {
    const globalStyles = readFileSync(resolve(__dirname, '../../../theme/global.scss'), 'utf8');
    const componentStyles = readFileSync(resolve(__dirname, 'store-config-page.component.scss'), 'utf8');

    expect(globalStyles).toContain('.app-shell:has(.urbeat-onboarding)');
    expect(globalStyles).toContain('ion-app:has(.urbeat-onboarding)');
    expect(globalStyles).toContain('overflow-y: auto;');
    expect(componentStyles).toContain('padding-bottom: calc(96px + env(safe-area-inset-bottom, 0px));');
    expect(globalStyles).not.toMatch(/\.seller-main\s*\{[^}]*overflow-y\s*:/s);
  });

  describe('addCategory', () => {
    it('should show error toast if category name is empty', () => {
      component.newCatName.set('   ');
      component.addCategory();
      
      expect(mockToastService.showError).toHaveBeenCalledWith('O nome da categoria é obrigatório.');
      expect(component.cuisineTypes().length).toBe(2);
    });

    it('should show error toast if category already exists (exact match)', () => {
      component.newCatName.set('Hamburgueria');
      component.addCategory();
      
      expect(mockToastService.showError).toHaveBeenCalledWith('Já existe uma categoria com esse nome.');
      expect(component.cuisineTypes().length).toBe(2);
    });

    it('should show error toast if category already exists (case insensitive)', () => {
      component.newCatName.set('hamburgueria');
      component.addCategory();
      
      expect(mockToastService.showError).toHaveBeenCalledWith('Já existe uma categoria com esse nome.');
      expect(component.cuisineTypes().length).toBe(2);
    });

    it('should show error toast if category already exists (with accents)', () => {
      component.newCatName.set('Hambúrgueria');
      component.addCategory();
      
      expect(mockToastService.showError).toHaveBeenCalledWith('Já existe uma categoria com esse nome.');
      expect(component.cuisineTypes().length).toBe(2);
    });

    it('should add new category, select it, and show success toast when valid', () => {
      component.newCatName.set('Doceria');
      component.addCategory();
      
      expect(component.cuisineTypes().length).toBe(3);
      expect(component.cuisineTypes().some(c => c.name === 'Doceria')).toBe(true);
      expect(component.cuisineType()).toBe('Doceria');
      expect(component.newCatName()).toBe('');
      expect(component.isCatModalOpen()).toBe(false);
      expect(mockToastService.showSuccess).toHaveBeenCalledWith('Categoria adicionada com sucesso!');
    });
  });

  describe('deleteCategory', () => {
    beforeEach(() => {
      // Mock window.confirm
      global.confirm = jest.fn(() => true) as any;
    });

    it('should remove category from list', () => {
      const catToDelete = component.cuisineTypes()[0]; // 'Hamburgueria'
      component.deleteCategory(catToDelete);

      expect(component.cuisineTypes().length).toBe(1);
      expect(component.cuisineTypes().some(c => c.name === 'Hamburgueria')).toBe(false);
    });

    it('should clear selected cuisineType if deleted category was selected', () => {
      const catToDelete = component.cuisineTypes()[0]; // 'Hamburgueria'
      component.cuisineType.set('Hamburgueria');

      component.deleteCategory(catToDelete);

      expect(component.cuisineType()).toBe('');
    });
  });

  describe('image upload', () => {
    it('should call compressImage and set logoFile when a valid file is selected', async () => {
      const file = new File(['dummy content'], 'logo.png', { type: 'image/png' });
      const event = { target: { files: [file] } } as unknown as Event;

      // Mock the compressImage method to return the original file for testing
      jest.spyOn(component as any, 'compressImage').mockResolvedValue(file);

      await component.onLogoSelected(event);

      expect(component.logoFile()).toBe(file);
    });

    it('should call compressImage and set bannerFile when a valid file is selected', async () => {
      const file = new File(['dummy content'], 'banner.jpg', { type: 'image/jpeg' });
      const event = { target: { files: [file] } } as unknown as Event;

      // Mock the compressImage method to return the original file for testing
      jest.spyOn(component as any, 'compressImage').mockResolvedValue(file);

      await component.onBannerSelected(event);

      expect(component.bannerFile()).toBe(file);
    });

    it('should not crash if no file is selected', async () => {
      const event = { target: { files: null } } as unknown as Event;

      await expect(component.onLogoSelected(event)).resolves.not.toThrow();
      await expect(component.onBannerSelected(event)).resolves.not.toThrow();
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
});
