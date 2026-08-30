import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { ActivatedRoute } from '@angular/router';
import { of } from 'rxjs';
import { StoreDeliveryPageComponent } from './store-delivery-page.component';
import { StoreService } from '../../../core/services/store.service';
import { ToastService } from '../../../core/services/toast.service';
import { AlertController, ToastController } from '@ionic/angular';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

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

  it('provides a centered wizard surface with a vertical Ionic scroller', () => {
    const template = readFileSync(resolve(__dirname, 'store-delivery-page.component.html'), 'utf8');
    const styles = readFileSync(resolve(__dirname, 'store-delivery-page.component.scss'), 'utf8');

    expect(template).toContain('<ion-content class="wizard-content delivery-content"');
    expect(styles).toContain('margin: 0 auto;');
    expect(styles).toContain('--background: var(--app-wizard-bg);');
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

    it('should keep a manual neighborhood (no coordinates) visible after reload when radius is active', () => {
      storeServiceMock.getMyStore!.mockReturnValue(of(mockStore));
      storeServiceMock.getStoreAddress!.mockReturnValue(of(mockAddress));
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValue(of([
        { id: 'nb-manual', neighborhood: 'Bairro Manual', city: 'Sao Paulo' },
        { id: 'nb-near', neighborhood: 'Bela Vista', city: 'Sao Paulo', latitude: -23.558, longitude: -46.642 },
        { id: 'nb-far', neighborhood: 'Itaquera', city: 'Sao Paulo', latitude: -23.54, longitude: -46.46 },
      ]));

      fixture.detectChanges();

      const names = component.filteredNeighborhoods().map(n => n.neighborhood);
      expect(names).toContain('Bairro Manual');
      expect(names).toContain('Bela Vista');
      expect(names).not.toContain('Itaquera');
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

  describe('selection modal accessibility and interaction', () => {
    const readTemplate = () => readFileSync(resolve(__dirname, 'store-delivery-page.component.html'), 'utf8');
    const readStyles = () => readFileSync(resolve(__dirname, 'store-delivery-page.component.scss'), 'utf8');

    it('gives the search bar an explicit accessible name', () => {
      expect(readTemplate()).toContain('aria-label="Buscar bairro na modal"');
    });

    it('keeps each row clickable without being an interactive button or nesting a checkbox button', () => {
      const template = readTemplate();

      expect(template).toContain('(click)="toggleNeighborhood(nb.id)"');
      expect(template).toContain('<label class="nb-check-wrap" slot="start"');
      expect(template).toContain('(change)="toggleNeighborhood(nb.id)"');
      expect(template).not.toContain('lines="none" button');
    });

    it('stops the checkbox from bubbling so the row and checkbox do not double-toggle', () => {
      const template = readTemplate();

      expect(template).toContain('<label class="nb-check-wrap" slot="start" (click)="$event.stopPropagation()"');
      expect(template).toContain('(click)="$event.stopPropagation()"');
      expect(template).toContain('(change)="toggleNeighborhood(nb.id)"');
    });

    it('gives the checkbox a real 44x44 touch target while keeping the native input', () => {
      const template = readTemplate();
      const styles = readStyles();

      expect(template).toContain('class="nb-checkbox"');
      expect(template).toContain('type="checkbox"');
      expect(styles).toContain('.nb-check-wrap');
      expect(styles).toContain('width: 44px');
      expect(styles).toContain('min-width: 44px');
      expect(styles).toContain('height: 44px');
      expect(styles).toContain('min-height: 44px');
      expect(styles).toContain('place-items: center');
    });

    it('toggles a neighborhood exactly once per call', () => {
      storeServiceMock.getMyStore!.mockReturnValue(of(mockStore));
      storeServiceMock.getStoreAddress!.mockReturnValue(of(mockAddress));
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValue(of([]));
      fixture.detectChanges();

      expect(component.checkedNeighborhoodIds().size).toBe(0);

      component.toggleNeighborhood('nb-1');
      expect(component.checkedNeighborhoodIds().has('nb-1')).toBe(true);

      component.toggleNeighborhood('nb-1');
      expect(component.checkedNeighborhoodIds().has('nb-1')).toBe(false);
    });
  });

  describe('select-all control accessibility', () => {
    const readTemplate = () => readFileSync(resolve(__dirname, 'store-delivery-page.component.html'), 'utf8');
    const readStyles = () => readFileSync(resolve(__dirname, 'store-delivery-page.component.scss'), 'utf8');

    it('gives the "Selecionar todos" control a native checkbox, an accessible name and a 44px touch target', () => {
      const template = readTemplate();
      const styles = readStyles();

      expect(template).toContain('aria-label="Selecionar todos os bairros"');
      expect(template).toContain('type="checkbox"');
      expect(styles).toContain('.nb-check-label');
      expect(styles).toContain('min-height: 44px');
      expect(styles).toContain('display: flex; align-items: center');
    });
  });

  describe('modal accessible names', () => {
    it('gives accessible names to the close buttons and modal inputs', () => {
      const template = readFileSync(resolve(__dirname, 'store-delivery-page.component.html'), 'utf8');

      expect(template).toContain('aria-label="Fechar modal de bairros"');
      expect(template).toContain('aria-label="Nome do novo bairro"');
      expect(template).toContain('aria-label="Taxa em lote"');
      expect(template).toContain('aria-label="Fechar mapa"');
    });
  });

  describe('toggleAllNeighborhoods', () => {
    beforeEach(() => {
      storeServiceMock.getMyStore!.mockReturnValue(of({ ...mockStore, deliveryAreas: [] }));
      storeServiceMock.getStoreAddress!.mockReturnValue(of(mockAddress));
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValue(of([
        { id: 'nb-a', neighborhood: 'Bairro A', city: 'Sao Paulo' },
        { id: 'nb-b', neighborhood: 'Bairro B', city: 'Sao Paulo' },
      ]));
      fixture.detectChanges();
      component.neighborhoodSearch.set('Bairro');
    });

    it('removes only the filtered ids and preserves selections outside the filter when all filtered are checked', () => {
      component.checkedNeighborhoodIds.set(new Set(['outside', 'nb-a', 'nb-b']));

      component.toggleAllNeighborhoods();

      expect(component.checkedNeighborhoodIds().has('outside')).toBe(true);
      expect(component.checkedNeighborhoodIds().has('nb-a')).toBe(false);
      expect(component.checkedNeighborhoodIds().has('nb-b')).toBe(false);
    });

    it('adds the filtered ids to the existing set without clearing other selections when not all are checked', () => {
      component.checkedNeighborhoodIds.set(new Set(['outside']));

      component.toggleAllNeighborhoods();

      expect(component.checkedNeighborhoodIds().has('outside')).toBe(true);
      expect(component.checkedNeighborhoodIds().has('nb-a')).toBe(true);
      expect(component.checkedNeighborhoodIds().has('nb-b')).toBe(true);
    });
  });
});
