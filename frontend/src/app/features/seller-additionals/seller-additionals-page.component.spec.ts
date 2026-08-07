import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { StoreService } from '../../core/services/store.service';
import { ToastService } from '../../core/services/toast.service';
import { SellerAdditionalsPageComponent } from './seller-additionals-page.component';

describe('SellerAdditionalsPageComponent', () => {
  let storeServiceMock: Record<string, jest.Mock>;
  const additional = { id: 'additional-1', storeId: 'store-1', groupId: 'group-1', groupName: 'Extras', name: 'Bacon extra', description: 'Fatias crocantes', price: 5, isActive: true, displayOrder: 1, productCount: 0 };

  beforeEach(async () => {
    storeServiceMock = {
      getMyStore: jest.fn().mockReturnValue(of({ id: 'store-1' })),
      getStoreAdditionals: jest.fn().mockReturnValue(of([additional])),
      getStoreAdditionalGroups: jest.fn().mockReturnValue(of([{ id: 'group-1', name: 'Extras', isActive: true }])),
      createStoreAdditional: jest.fn(),
      updateStoreAdditional: jest.fn(),
      toggleStoreAdditional: jest.fn(),
      deleteStoreAdditional: jest.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [SellerAdditionalsPageComponent],
      providers: [
        provideRouter([]),
        { provide: StoreService, useValue: storeServiceMock },
        { provide: ToastService, useValue: { showSuccess: jest.fn(), showError: jest.fn() } },
      ],
    }).compileComponents();
  });

  it('renders the documented catalog layout without a grid-level new button', () => {
    const fixture = TestBed.createComponent(SellerAdditionalsPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Adicionais cadastrados');
    expect(fixture.nativeElement.textContent).toContain('Novo adicional');
    expect(fixture.nativeElement.textContent).toContain('Extras');
    expect(fixture.nativeElement.textContent).toContain('Bacon extra');
    expect(fixture.nativeElement.querySelector('.additionals-table')).not.toBeNull();
    expect(fixture.nativeElement.querySelectorAll('.content-head .btn-primary-app').length).toBe(0);
  });

  it('loads an additional into edit mode and returns to new mode after saving', () => {
    storeServiceMock.updateStoreAdditional.mockReturnValue(of({ ...additional, name: 'Bacon premium' }));
    const fixture = TestBed.createComponent(SellerAdditionalsPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.startEdit(additional);
    expect(fixture.componentInstance.formTitle()).toBe('Editar adicional');
    fixture.componentInstance.saveAdditional();

    expect(storeServiceMock.updateStoreAdditional).toHaveBeenCalledWith('store-1', 'additional-1', expect.objectContaining({ name: 'Bacon extra', groupId: 'group-1', price: 5 }));
    expect(fixture.componentInstance.formTitle()).toBe('Novo adicional');
  });

  it('accepts zero price when creating an additional', () => {
    storeServiceMock.createStoreAdditional.mockReturnValue(of({ ...additional, id: 'additional-2', price: 0 }));
    const fixture = TestBed.createComponent(SellerAdditionalsPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.formName.set('Molho grátis');
    fixture.componentInstance.formGroupId.set('group-1');
    fixture.componentInstance.formPrice.set(0);
    fixture.componentInstance.saveAdditional();

    expect(storeServiceMock.createStoreAdditional).toHaveBeenCalledWith('store-1', expect.objectContaining({ price: 0 }));
  });

  it('persists status toggle and removes an unassigned additional after confirmation', () => {
    storeServiceMock.toggleStoreAdditional.mockReturnValue(of({ ...additional, isActive: false }));
    storeServiceMock.deleteStoreAdditional.mockReturnValue(of(void 0));
    const confirmSpy = jest.spyOn(window, 'confirm').mockReturnValue(true);
    const fixture = TestBed.createComponent(SellerAdditionalsPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.toggleAdditional(additional);
    fixture.componentInstance.deleteAdditional(additional);

    expect(storeServiceMock.toggleStoreAdditional).toHaveBeenCalledWith('store-1', 'additional-1', false);
    expect(storeServiceMock.deleteStoreAdditional).toHaveBeenCalledWith('store-1', 'additional-1');
    expect(fixture.componentInstance.additionals()).toHaveLength(0);
    confirmSpy.mockRestore();
  });

  it('blocks deletion when an additional is associated with products', () => {
    const fixture = TestBed.createComponent(SellerAdditionalsPageComponent);
    fixture.detectChanges();
    const confirmSpy = jest.spyOn(window, 'confirm');

    fixture.componentInstance.deleteAdditional({ ...additional, productCount: 1 });

    expect(confirmSpy).not.toHaveBeenCalled();
    expect(storeServiceMock.deleteStoreAdditional).not.toHaveBeenCalled();
    confirmSpy.mockRestore();
  });

  it('renders the shared retry state when additionals fail to load', () => {
    storeServiceMock.getMyStore.mockReturnValue(throwError(() => new Error('network')));
    const fixture = TestBed.createComponent(SellerAdditionalsPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Não foi possível carregar os adicionais.');
    expect(fixture.nativeElement.querySelector('.seller-state-card.is-error')).not.toBeNull();
  });
});
