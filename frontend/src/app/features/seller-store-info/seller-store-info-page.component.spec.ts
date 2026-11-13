import { ComponentFixture, TestBed, NO_ERRORS_SCHEMA } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { SellerStoreInfoPageComponent } from './seller-store-info-page.component';
import { StoreService } from '../../core/services/store.service';
import { AddressService } from '../../core/services/address.service';
import { AuthService } from '../../core/services/auth.service';
import { ToastService } from '../../core/services/toast.service';
import { SellerShellFacade } from '../seller-shell/seller-shell.facade';
import { StoreResponse, StoreAddressResponse } from '../../shared/models/store.model';

describe('SellerStoreInfoPageComponent', () => {
  let component: SellerStoreInfoPageComponent;
  let fixture: ComponentFixture<SellerStoreInfoPageComponent>;

  const mockStoreService = {
    getCuisineTypes: jest.fn(),
    getStoreCuisineTypes: jest.fn(),
    createStoreCuisineType: jest.fn(),
    deleteStoreCuisineType: jest.fn(),
    getMyStore: jest.fn(),
    getStoreAddress: jest.fn(),
    updateStore: jest.fn(),
    upsertStoreAddress: jest.fn(),
    updateDeliveryConfig: jest.fn(),
    deleteStore: jest.fn(),
  };

  const mockAddressService = {
    lookupCep: jest.fn(),
  };

  const mockAuthService = {
    getSellerProfile: jest.fn(),
    updateSellerProfile: jest.fn(),
    logout: jest.fn(),
  };

  const mockToastService = {
    showError: jest.fn(),
    showSuccess: jest.fn(),
    showWarning: jest.fn(),
    showInfo: jest.fn(),
  };

  const mockSellerShellFacade = {
    mergeStore: jest.fn(),
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
    cuisineType: 'Hamburgueria',
    isOpen: true,
    isPublished: false,
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
    mockStoreService.getStoreCuisineTypes.mockReturnValue(of([{ id: '1', name: 'Hamburgueria', isDefault: true, storeId: null }]));
    mockStoreService.createStoreCuisineType.mockImplementation((storeId: string, name: string) =>
      of({ id: 'custom-new', name, isDefault: false, storeId }),
    );
    mockStoreService.deleteStoreCuisineType.mockReturnValue(of(undefined));
    mockStoreService.getMyStore.mockReturnValue(throwError(() => new Error('Not found')));
    mockStoreService.getStoreAddress.mockReturnValue(of(buildAddress()));
    mockAuthService.getSellerProfile.mockReturnValue(of({
      fullName: 'Contratante Teste',
      document: null,
      phoneNumber: null,
      email: 'contratante@urbeat.local',
    }));
    mockAuthService.updateSellerProfile.mockReturnValue(of({
      fullName: 'Contratante Teste',
      document: null,
      phoneNumber: null,
      email: 'contratante@urbeat.local',
    }));
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
        { provide: SellerShellFacade, useValue: mockSellerShellFacade },
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
      component.contractorName.set('Contratante Teste');
      component.storeUrl.set('minha-loja');
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

  describe('zona destrutiva', () => {
    let navigate: jest.SpyInstance;

    const renderWithStore = async (): Promise<void> => {
      mockStoreService.getMyStore.mockReturnValue(of(buildStore()));
      render();
      await fixture.whenStable();
    };

    const dangerZone = (): HTMLElement | null =>
      fixture.nativeElement.querySelector('.danger-zone');

    const openDeleteButton = (): HTMLButtonElement | null =>
      fixture.nativeElement.querySelector('.danger-zone > .btn-danger-app');

    const deleteConfirmationInput = (): HTMLInputElement | null =>
      fixture.nativeElement.querySelector('#deleteConfirmation');

    const cancelDeleteButton = (): HTMLButtonElement | null =>
      fixture.nativeElement.querySelector('.danger-actions .btn-outline-app');

    const confirmDeleteButton = (): HTMLButtonElement | null =>
      fixture.nativeElement.querySelector('.danger-actions .btn-danger-app');

    const typeConfirmation = (value: string): void => {
      const input = deleteConfirmationInput();
      expect(input).not.toBeNull();
      input!.value = value;
      input!.dispatchEvent(new Event('input'));
      fixture.detectChanges();
    };

    beforeEach(() => {
      navigate = jest.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      mockStoreService.deleteStore.mockReturnValue(of(undefined));
    });

    it('hides the destructive zone when the seller has no store', async () => {
      render();
      await fixture.whenStable();

      expect(dangerZone()).toBeNull();
    });

    it('shows the destructive zone when a store is loaded', async () => {
      await renderWithStore();

      expect(dangerZone()?.textContent).toContain('Zona destrutiva');
      expect(dangerZone()?.textContent).toContain('Esta ação não pode ser desfeita');
      expect(openDeleteButton()?.textContent?.trim()).toBe('Excluir loja');
    });

    it('keeps the destructive zone separated from the store information cards', async () => {
      await renderWithStore();

      const zone = dangerZone()!;
      const infoLayout = fixture.nativeElement.querySelector('.info-layout') as HTMLElement;
      expect(infoLayout.contains(zone)).toBe(false);
      expect(zone.closest('.content-card')).toBeNull();
    });

    it('requires the confirmation word before calling the API', async () => {
      await renderWithStore();

      openDeleteButton()!.click();
      fixture.detectChanges();

      expect(deleteConfirmationInput()).not.toBeNull();
      expect(confirmDeleteButton()!.disabled).toBe(true);

      confirmDeleteButton()!.click();
      fixture.detectChanges();
      expect(mockStoreService.deleteStore).not.toHaveBeenCalled();

      typeConfirmation('excluir');
      expect(confirmDeleteButton()!.disabled).toBe(false);

      confirmDeleteButton()!.click();
      expect(mockStoreService.deleteStore).toHaveBeenCalledWith('store-123');
    });

    it('accepts the confirmation word regardless of case and spacing', async () => {
      await renderWithStore();

      component.openDeletePanel();
      fixture.detectChanges();

      typeConfirmation('  Excluir  ');

      expect(component.canSubmitDeletion()).toBe(true);
    });

    it('cancels the confirmation without calling the API', async () => {
      await renderWithStore();

      component.openDeletePanel();
      fixture.detectChanges();
      cancelDeleteButton()!.click();
      fixture.detectChanges();

      expect(mockStoreService.deleteStore).not.toHaveBeenCalled();
      expect(deleteConfirmationInput()).toBeNull();
      expect(openDeleteButton()).not.toBeNull();
    });

    it('navigates to the store creation wizard after a successful deletion', async () => {
      await renderWithStore();

      component.openDeletePanel();
      fixture.detectChanges();
      typeConfirmation('excluir');
      confirmDeleteButton()!.click();

      expect(mockStoreService.deleteStore).toHaveBeenCalledWith('store-123');
      expect(mockToastService.showSuccess).toHaveBeenCalledWith(
        'Loja excluída. Crie uma nova loja quando quiser voltar a vender.',
      );
      expect(navigate).toHaveBeenCalledWith(['/configurar-loja']);
      expect(component.deleting()).toBe(false);
    });

    it('logs out and redirects to the seller login on 401', async () => {
      mockStoreService.deleteStore.mockReturnValue(throwError(() => ({ status: 401, error: {} })));
      await renderWithStore();

      component.openDeletePanel();
      fixture.detectChanges();
      typeConfirmation('excluir');
      confirmDeleteButton()!.click();

      expect(mockAuthService.logout).toHaveBeenCalled();
      expect(navigate).toHaveBeenCalledWith(['/login-vendedor']);
      expect(mockToastService.showError).toHaveBeenCalledWith(
        'Sua sessão expirou. Entre novamente para continuar.',
      );
      expect(component.deleting()).toBe(false);
    });

    it('reports a permission error on 403 without navigating', async () => {
      mockStoreService.deleteStore.mockReturnValue(throwError(() => ({ status: 403, error: {} })));
      await renderWithStore();

      component.openDeletePanel();
      fixture.detectChanges();
      typeConfirmation('excluir');
      confirmDeleteButton()!.click();

      expect(mockToastService.showError).toHaveBeenCalledWith(
        'Você não tem permissão para excluir esta loja.',
      );
      expect(navigate).not.toHaveBeenCalled();
      expect(mockAuthService.logout).not.toHaveBeenCalled();
      expect(component.deleting()).toBe(false);
      expect(component.deletePanelOpen()).toBe(false);
    });

    it('treats 404 as an already deleted store and offers the store creation wizard', async () => {
      mockStoreService.deleteStore.mockReturnValue(throwError(() => ({ status: 404, error: {} })));
      await renderWithStore();

      component.openDeletePanel();
      fixture.detectChanges();
      typeConfirmation('excluir');
      confirmDeleteButton()!.click();

      expect(mockToastService.showInfo).toHaveBeenCalledWith(
        'Esta loja já não existe. Crie uma nova loja para voltar a vender.',
      );
      expect(navigate).toHaveBeenCalledWith(['/configurar-loja']);
      expect(mockToastService.showSuccess).not.toHaveBeenCalled();
    });

    it('keeps the panel open with an inline error when the request fails unexpectedly', async () => {
      mockStoreService.deleteStore.mockReturnValue(throwError(() => ({ status: 500, error: {} })));
      await renderWithStore();

      component.openDeletePanel();
      fixture.detectChanges();
      typeConfirmation('excluir');
      confirmDeleteButton()!.click();
      fixture.detectChanges();

      expect(mockToastService.showError).toHaveBeenCalledWith(
        'Não foi possível excluir a loja. Tente novamente.',
      );
      expect(component.deletePanelOpen()).toBe(true);
      expect(component.deleting()).toBe(false);
      expect(component.deleteError()).toBe('Não foi possível excluir a loja. Tente novamente.');
      expect(fixture.nativeElement.querySelector('#deleteConfirmationError')?.textContent)
        .toContain('Não foi possível excluir a loja');
      expect(navigate).not.toHaveBeenCalled();
    });
  });
});
