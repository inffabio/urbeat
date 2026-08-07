import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { ActivatedRoute } from '@angular/router';
import { of } from 'rxjs';
import { StoreDeliveryPageComponent } from './store-delivery-page.component';
import { StoreService } from '../../../core/services/store.service';
import { ToastService } from '../../../core/services/toast.service';
import { AlertController, ToastController } from '@ionic/angular';
import { NO_ERRORS_SCHEMA } from '@angular/core';

describe('StoreDeliveryPageComponent', () => {
  let component: StoreDeliveryPageComponent;
  let fixture: ComponentFixture<StoreDeliveryPageComponent>;
  let storeServiceMock: jest.Mocked<Partial<StoreService>>;
  let toastServiceMock: jest.Mocked<ToastService>;
  let routerMock: jest.Mocked<Partial<Router>>;

  const mockStore = {
    id: 'store-123',
    name: 'Loja Teste',
    slug: 'loja-teste',
    storePath: 'loja_teste',
    cuisineType: 'Pizza',
    phoneNumber: '11999999999',
    maxDeliveryRadiusKm: 5,
    lastImportedRadiusKm: null,
    deliveryAreas: [],
    isActive: true,
    isPublished: false
  };

  const mockAddress = {
    street: 'Rua A',
    neighborhood: 'Centro',
    city: 'Sao Paulo',
    state: 'SP',
    postalCode: '01001000',
    latitude: -23.5505,
    longitude: -46.6333
  };

  beforeEach(async () => {
    storeServiceMock = {
      getMyStore: jest.fn(),
      getStoreAddress: jest.fn(),
      getDeliveryNeighborhoodsByStore: jest.fn(),
      updateDeliveryConfig: jest.fn(),
    };

    toastServiceMock = {
      showError: jest.fn().mockResolvedValue(undefined),
      showSuccess: jest.fn().mockResolvedValue(undefined),
      showWarning: jest.fn().mockResolvedValue(undefined),
      showInfo: jest.fn().mockResolvedValue(undefined),
    };

    routerMock = {
      navigate: jest.fn().mockResolvedValue(true),
    };

    await TestBed.configureTestingModule({
      imports: [StoreDeliveryPageComponent],
      schemas: [NO_ERRORS_SCHEMA],
      providers: [
        provideRouter([]),
        { provide: StoreService, useValue: storeServiceMock },
        { provide: ToastService, useValue: toastServiceMock },
        { provide: AlertController, useValue: { create: jest.fn().mockResolvedValue({ present: jest.fn().mockResolvedValue(undefined) }) } },
        { provide: ToastController, useValue: { create: jest.fn().mockResolvedValue({ present: jest.fn().mockResolvedValue(undefined) }) } },
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(StoreDeliveryPageComponent);
    component = fixture.componentInstance;
  });

  describe('neighborhood loading', () => {
    it('should show empty modal list when no neighborhoods exist', () => {
      storeServiceMock.getMyStore!.mockReturnValue(of(mockStore));
      storeServiceMock.getStoreAddress!.mockReturnValue(of(mockAddress));
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValue(of([]));

      fixture.detectChanges();

      expect(storeServiceMock.getDeliveryNeighborhoodsByStore).toHaveBeenCalledTimes(1);
      expect(component.deliveryNeighborhoods().length).toBe(0);
    });

    it('should not auto-import when neighborhoods already exist', () => {
      storeServiceMock.getMyStore!.mockReturnValue(of(mockStore));
      storeServiceMock.getStoreAddress!.mockReturnValue(of(mockAddress));
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValue(of([
        { id: 'nb-1', neighborhood: 'Pinheiros', city: 'Sao Paulo', latitude: -23.5667, longitude: -46.6833 }
      ]));

      fixture.detectChanges();

      expect(component.deliveryNeighborhoods().length).toBe(1);
    });

    it('should load neighborhoods when modal is opened', () => {
      storeServiceMock.getMyStore!.mockReturnValue(of(mockStore));
      storeServiceMock.getStoreAddress!.mockReturnValue(of(mockAddress));
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValue(of([]));

      fixture.detectChanges();
      component.openNeighborhoodModal(-1);

      expect(storeServiceMock.getDeliveryNeighborhoodsByStore).toHaveBeenCalledTimes(1);
    });
  });

  describe('alphabetical sorting', () => {
    it('should sort delivery areas alphabetically on load', () => {
      storeServiceMock.getMyStore!.mockReturnValue(of({
        ...mockStore,
        deliveryAreas: [
          { id: 'a1', neighborhood: 'Centro', deliveryFee: 5 },
          { id: 'a2', neighborhood: 'Jardins', deliveryFee: 8 },
          { id: 'a3', neighborhood: 'Bela Vista', deliveryFee: 6 },
        ]
      }));
      storeServiceMock.getStoreAddress!.mockReturnValue(of(mockAddress));
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValue(of([]));

      fixture.detectChanges();

      const areas = component.areas.controls;
      expect(areas.length).toBe(3);
      expect(areas.at(0).value.neighborhood).toBe('Bela Vista');
      expect(areas.at(1).value.neighborhood).toBe('Centro');
      expect(areas.at(2).value.neighborhood).toBe('Jardins');
    });

    it('should sort alphabetically after inline add', () => {
      storeServiceMock.getMyStore!.mockReturnValue(of({
        ...mockStore,
        deliveryAreas: [
          { id: 'a1', neighborhood: 'Centro', deliveryFee: 5 },
          { id: 'a2', neighborhood: 'Jardins', deliveryFee: 8 },
        ]
      }));
      storeServiceMock.getStoreAddress!.mockReturnValue(of(mockAddress));
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValue(of([]));

      fixture.detectChanges();

      component.inlineForm.setValue({ neighborhood: 'Bela Vista', deliveryFee: '6,00' });
      component.addInline();

      const areas = component.areas.controls;
      expect(areas.at(0).value.neighborhood).toBe('Bela Vista');
      expect(areas.at(1).value.neighborhood).toBe('Centro');
      expect(areas.at(2).value.neighborhood).toBe('Jardins');
    });
  });

  describe('filter bar', () => {
    beforeEach(() => {
      storeServiceMock.getMyStore!.mockReturnValue(of({
        ...mockStore,
        deliveryAreas: [
          { id: 'a1', neighborhood: 'Centro', deliveryFee: 5 },
          { id: 'a2', neighborhood: 'Jardins', deliveryFee: 8 },
          { id: 'a3', neighborhood: 'Bela Vista', deliveryFee: 6 },
          { id: 'a4', neighborhood: 'Pinheiros', deliveryFee: 10 },
        ]
      }));
      storeServiceMock.getStoreAddress!.mockReturnValue(of(mockAddress));
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValue(of([]));
      fixture.detectChanges();
    });

    it('should show all areas when filter is empty', () => {
      expect(component.filteredAreaIndices().length).toBe(4);
    });

    it('should filter areas by partial name match', () => {
      component.areaSearchFilter.set('jar');

      const indices = component.filteredAreaIndices();
      expect(indices.length).toBe(1);
      const name = component.areas.at(indices[0]).value.neighborhood;
      expect(name).toBe('Jardins');
    });

    it('should filter case-insensitively', () => {
      component.areaSearchFilter.set('centro');

      const indices = component.filteredAreaIndices();
      expect(indices.length).toBe(1);
      const name = component.areas.at(indices[0]).value.neighborhood;
      expect(name).toBe('Centro');
    });

    it('should return empty when filter matches nothing', () => {
      component.areaSearchFilter.set('zzz');

      expect(component.filteredAreaIndices().length).toBe(0);
    });

    it('should clear filter and show all areas again', () => {
      component.areaSearchFilter.set('jar');
      component.areaSearchFilter.set('');

      expect(component.filteredAreaIndices().length).toBe(4);
    });

    it('should maintain alphabetical order when filtered', () => {
      component.areaSearchFilter.set('a');

      const indices = component.filteredAreaIndices();
      const names = indices.map(i => component.areas.at(i).value.neighborhood);
      expect(names).toEqual(['Bela Vista', 'Jardins']);
    });
  });
});
