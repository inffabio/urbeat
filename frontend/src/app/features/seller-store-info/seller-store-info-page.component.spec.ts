import { ComponentFixture, TestBed, NO_ERRORS_SCHEMA } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { SellerStoreInfoPageComponent } from './seller-store-info-page.component';
import { StoreService } from '../../core/services/store.service';
import { AddressService } from '../../core/services/address.service';
import { AuthService } from '../../core/services/auth.service';
import { ToastService } from '../../core/services/toast.service';
import { StoreResponse, StoreAddressResponse } from '../../shared/models/store.model';

describe('SellerStoreInfoPageComponent', () => {
  let component: SellerStoreInfoPageComponent;
  let fixture: ComponentFixture<SellerStoreInfoPageComponent>;

  const mockStoreService = {
    getCuisineTypes: jest.fn(),
    getMyStore: jest.fn(),
    getStoreAddress: jest.fn(),
    updateStore: jest.fn(),
    upsertStoreAddress: jest.fn(),
    updateDeliveryConfig: jest.fn(),
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
    showWarning: jest.fn(),
    showInfo: jest.fn(),
  };

  const buildStore = (overrides: Partial<StoreResponse> = {}): StoreResponse => ({
    id: 'store-123',
    ownerUserId: 'user-1',
    name: 'Minha Loja',
    slug: 'minha-loja',
    phoneNumber: '(21) 99999-9999',
    document: '',
    pixKey: '',
    websiteUrl: '',
    description: 'Descricao',
    cuisineType: 'Hamburgueria',
    isOpen: true,
    isSubscriptionBlocked: false,
    supportsDelivery: true,
    supportsPickup: true,
    initialMinute: 30,
    finalMinute: 60,
    maxDeliveryRadiusKm: 10,
    minimumOrderValue: 25,
    deliveryAreas: [],
    averageRating: 4.5,
    totalReviews: 0,
    ...overrides,
  });

  const buildAddress = (): StoreAddressResponse => ({
    storeId: 'store-123',
    street: 'Rua das Flores',
    number: '123',
    complement: '',
    neighborhood: 'Centro',
    city: 'Rio de Janeiro',
    state: 'RJ',
    zipCode: '20040-010',
  });

  const render = (): void => {
    fixture = TestBed.createComponent(SellerStoreInfoPageComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  };

  const options = (): NodeListOf<HTMLElement> =>
    fixture.nativeElement.querySelectorAll('[role="checkbox"]');

  const saveButton = (): HTMLElement =>
    fixture.nativeElement.querySelector('.establishment-save-bar button');

  beforeEach(async () => {
    jest.clearAllMocks();

    mockStoreService.getCuisineTypes.mockReturnValue(of([{ id: '1', name: 'Hamburgueria' }]));
    mockStoreService.getMyStore.mockReturnValue(throwError(() => new Error('Not found')));
    mockStoreService.getStoreAddress.mockReturnValue(of(buildAddress()));
    mockAuthService.getSellerProfile.mockReturnValue(of({}));
    mockStoreService.updateStore.mockReturnValue(of(buildStore()));
    mockStoreService.upsertStoreAddress.mockReturnValue(of(buildAddress()));
    mockStoreService.updateDeliveryConfig.mockReturnValue(of(buildStore()));

    await TestBed.configureTestingModule({
      imports: [FormsModule, SellerStoreInfoPageComponent],
      schemas: [NO_ERRORS_SCHEMA],
      providers: [
        provideRouter([]),
        { provide: StoreService, useValue: mockStoreService },
        { provide: AddressService, useValue: mockAddressService },
        { provide: AuthService, useValue: mockAuthService },
        { provide: ToastService, useValue: mockToastService },
      ],
    }).compileComponents();
  });

  it('should create', () => {
    render();
    expect(component).toBeTruthy();
  });

  describe('estado persistido', () => {
    it('carrega supportsDelivery/supportsPickup persistidos do getMyStore', async () => {
      mockStoreService.getMyStore.mockReturnValue(
        of(buildStore({ supportsDelivery: false, supportsPickup: true })),
      );
      render();
      await fixture.whenStable();

      expect(component.supportsDelivery()).toBe(false);
      expect(component.supportsPickup()).toBe(true);

      const [delivery, pickup] = options();
      expect(delivery.getAttribute('aria-checked')).toBe('false');
      expect(pickup.getAttribute('aria-checked')).toBe('true');
    });

    it('seleciona Entrega como fallback quando a API retorna false/false', async () => {
      mockStoreService.getMyStore.mockReturnValue(
        of(buildStore({ supportsDelivery: false, supportsPickup: false })),
      );
      render();
      await fixture.whenStable();

      expect(component.supportsDelivery()).toBe(true);
      expect(component.supportsPickup()).toBe(false);

      const [delivery, pickup] = options();
      expect(delivery.getAttribute('aria-checked')).toBe('true');
      expect(pickup.getAttribute('aria-checked')).toBe('false');
    });
  });

  describe('tipo de atendimento', () => {
    it('renders Entrega and Retirada options instead of the Redes sociais card', () => {
      render();
      const text = fixture.nativeElement.textContent;

      expect(text).toContain('Tipo de atendimento');
      expect(text).toContain('Entrega');
      expect(text).toContain('Retirada');
      expect(text).not.toContain('Redes sociais');
    });

    it('renders keyboard-focusable checkboxes with proper aria semantics', () => {
      render();
      const checked = fixture.nativeElement.querySelectorAll('[role="checkbox"][aria-checked="true"]');
      expect(options().length).toBe(2);
      expect(checked.length).toBe(2);
    });

    it('updates aria-checked through a real click interaction', async () => {
      mockStoreService.getMyStore.mockReturnValue(
        of(buildStore({ supportsDelivery: true, supportsPickup: true })),
      );
      render();
      await fixture.whenStable();

      const [delivery, pickup] = options();
      expect(delivery.getAttribute('aria-checked')).toBe('true');
      expect(pickup.getAttribute('aria-checked')).toBe('true');

      pickup.click();
      fixture.detectChanges();

      expect(component.supportsPickup()).toBe(false);
      expect(pickup.getAttribute('aria-checked')).toBe('false');
      expect(delivery.getAttribute('aria-checked')).toBe('true');
    });

    it('blocks unchecking the last selected option through click', async () => {
      mockStoreService.getMyStore.mockReturnValue(
        of(buildStore({ supportsDelivery: true, supportsPickup: false })),
      );
      render();
      await fixture.whenStable();

      const [delivery] = options();
      delivery.click();
      fixture.detectChanges();

      expect(component.supportsDelivery()).toBe(true);
      expect(delivery.getAttribute('aria-checked')).toBe('true');
    });

    it('allows toggling to one option or both, but never none', () => {
      render();
      component.supportsDelivery.set(true);
      component.supportsPickup.set(true);

      component.toggleDelivery();
      expect(component.supportsDelivery()).toBe(false);
      expect(component.supportsPickup()).toBe(true);

      component.toggleDelivery();
      expect(component.supportsDelivery()).toBe(true);
      expect(component.supportsPickup()).toBe(true);

      component.toggleDelivery();
      component.togglePickup();
      expect(component.supportsDelivery()).toBe(false);
      expect(component.supportsPickup()).toBe(true);

      component.supportsDelivery.set(true);
      component.supportsPickup.set(false);
      component.toggleDelivery();
      expect(component.supportsDelivery()).toBe(true);
      expect(component.supportsPickup()).toBe(false);
    });
  });

  describe('submit', () => {
    const fillValidStore = () => {
      component.existingStoreId.set('store-123');
      component.storeName.set('Minha Loja');
      component.cuisineType.set('Hamburgueria');
      component.whatsapp.set('(21) 99999-9999');
      component.cep.set('20040-010');
      component.street.set('Rua das Flores');
      component.number.set('123');
      component.neighborhood.set('Centro');
      component.city.set('Rio de Janeiro');
      component.state.set('RJ');
      component.initialMinute.set(30);
      component.finalMinute.set(60);
      component.maxDeliveryRadiusKm.set(10);
    };

    it('sends supportsDelivery and supportsPickup through updateStore', async () => {
      render();
      fillValidStore();
      component.supportsDelivery.set(true);
      component.supportsPickup.set(false);

      const result = await component.submit();

      expect(result).toBe(true);
      expect(mockStoreService.updateStore).toHaveBeenCalledWith(
        'store-123',
        expect.objectContaining({ supportsDelivery: true, supportsPickup: false }),
      );
    });

    it('Salvar informações button uses the real flow and sends supportsDelivery/supportsPickup', async () => {
      mockStoreService.getMyStore.mockReturnValue(
        of(buildStore({ supportsDelivery: true, supportsPickup: false })),
      );
      render();
      await fixture.whenStable();

      saveButton().click();
      await fixture.whenStable();

      expect(mockStoreService.updateStore).toHaveBeenCalledWith(
        'store-123',
        expect.objectContaining({ supportsDelivery: true, supportsPickup: false }),
      );
    });

    it('blocks submit and shows an error when no atendimento is selected', async () => {
      render();
      fillValidStore();
      component.supportsDelivery.set(false);
      component.supportsPickup.set(false);

      const result = await component.submit();

      expect(result).toBe(false);
      expect(mockToastService.showError).toHaveBeenCalledWith(
        'Selecione ao menos um tipo de atendimento: Delivery ou Retirada.',
      );
      expect(mockStoreService.updateStore).not.toHaveBeenCalled();
    });
  });
});
