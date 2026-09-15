import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { ActivatedRoute } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { StoreDeliveryPageComponent } from './store-delivery-page.component';
import { StoreService } from '../../../core/services/store.service';
import { ToastService } from '../../../core/services/toast.service';
import { DeliveryNeighborhood } from '../../../shared/models/store.model';
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

  describe('persistConfig payload', () => {
    it('sends deliveryAreas when saving neighborhoods from the delivery screen', async () => {
      storeServiceMock.getMyStore!.mockReturnValue(of({
        ...mockStore,
        deliveryFee: 0,
        minimumOrderValue: 25,
        deliveryAreas: [
          { id: 'a1', neighborhood: 'Centro', deliveryFee: 5, isActive: true, notes: '' },
          { id: 'a2', neighborhood: 'Jardins', deliveryFee: 8, isActive: true, notes: '' },
        ]
      }));
      storeServiceMock.getStoreAddress!.mockReturnValue(of(mockAddress));
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValue(of([]));
      storeServiceMock.updateDeliveryConfig!.mockReturnValue(of({} as any));

      fixture.detectChanges();

      await component.saveDraft();

      expect(storeServiceMock.updateDeliveryConfig).toHaveBeenCalledTimes(1);
      const [storeId, payload] = storeServiceMock.updateDeliveryConfig!.mock.calls[0] as [string, any];
      expect(storeId).toBe('store-123');
      expect(Array.isArray(payload.deliveryAreas)).toBe(true);
      const names = (payload.deliveryAreas as { neighborhood: string }[]).map(a => a.neighborhood);
      expect(names).toEqual(['Centro', 'Jardins']);
    });
  });

  describe('free shipping card visibility', () => {
    it('does not render the free shipping card in the wizard', () => {
      storeServiceMock.getMyStore!.mockReturnValue(of(mockStore));
      storeServiceMock.getStoreAddress!.mockReturnValue(of(mockAddress));
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValue(of([]));

      fixture.detectChanges();

      expect(component.isDashboardView()).toBe(false);
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).not.toContain('Frete grátis hoje');
      expect(text).not.toContain('Frete grátis a partir de');
      expect(text).not.toContain('Valor mínimo para frete grátis');
      expect(fixture.nativeElement.querySelector('[formcontrolname="freeShippingThreshold"]')).toBeNull();
    });

    it('keeps the shared free shipping state and payload intact in wizard mode', () => {
      storeServiceMock.getMyStore!.mockReturnValue(of({ ...mockStore, freeShippingToday: true, freeShippingThreshold: 50 }));
      storeServiceMock.getStoreAddress!.mockReturnValue(of(mockAddress));
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValue(of([]));

      fixture.detectChanges();

      expect(component.freeShippingToday()).toBe(true);
      expect(component.form.get('freeShippingThreshold')?.value).toBe('50,00');
      expect(storeServiceMock.updateDeliveryConfig).not.toHaveBeenCalled();
    });
  });

  describe('free shipping today toggle', () => {
    const changeEvent = (checked: boolean) => ({ target: { checked } } as unknown as Event);

    function load(freeShippingToday: boolean) {
      storeServiceMock.getMyStore!.mockReturnValue(of({
        ...mockStore,
        freeShippingToday,
        deliveryFee: 0,
        minimumOrderValue: 0,
        deliveryAreas: []
      }));
      storeServiceMock.getStoreAddress!.mockReturnValue(of(mockAddress));
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValue(of([]));
      storeServiceMock.updateDeliveryConfig!.mockReturnValue(of({} as any));
      fixture.detectChanges();
    }

    it('follows the native input checked state when turning the promotion on', () => {
      load(false);

      component.toggleFreeShippingToday(changeEvent(true));

      expect(component.freeShippingToday()).toBe(true);
      expect(component.formDirty()).toBe(true);
    });

    it('follows the native input checked state when turning the promotion off', () => {
      load(true);

      component.toggleFreeShippingToday(changeEvent(false));

      expect(component.freeShippingToday()).toBe(false);
      expect(component.formDirty()).toBe(true);
    });

    it('does not invert the signal when the input state already matches it', () => {
      load(true);

      component.toggleFreeShippingToday(changeEvent(true));

      expect(component.freeShippingToday()).toBe(true);
    });

    it('sends the exact current boolean in the update payload', async () => {
      load(false);

      component.toggleFreeShippingToday(changeEvent(true));
      await component.saveDraft();
      let [, payload] = storeServiceMock.updateDeliveryConfig!.mock.calls[0] as [string, any];
      expect(payload.freeShippingToday).toBe(true);

      storeServiceMock.updateDeliveryConfig!.mockClear();
      component.toggleFreeShippingToday(changeEvent(false));
      await component.saveDraft();
      [, payload] = storeServiceMock.updateDeliveryConfig!.mock.calls[0] as [string, any];
      expect(payload.freeShippingToday).toBe(false);
    });

    it('reloads the persisted boolean when changes are discarded', () => {
      load(true);

      expect(component.freeShippingToday()).toBe(true);
      component.toggleFreeShippingToday(changeEvent(false));
      expect(component.freeShippingToday()).toBe(false);

      component.cancelChanges();

      expect(component.freeShippingToday()).toBe(true);
      expect(component.formDirty()).toBe(false);
    });
  });

  describe('delivery radius editing', () => {
    function loadStore(
      overrides: Record<string, unknown> = {},
      knownGlobalNeighborhoods: DeliveryNeighborhood[] = [],
    ) {
      storeServiceMock.getMyStore!.mockReturnValue(of({
        ...mockStore,
        deliveryFee: 0,
        minimumOrderValue: 0,
        ...overrides,
      }));
      storeServiceMock.getStoreAddress!.mockReturnValue(of(mockAddress));
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValue(of(knownGlobalNeighborhoods));
      storeServiceMock.updateDeliveryConfig!.mockReturnValue(of({} as any));
      fixture.detectChanges();
    }

    it('hydrates the radius from the store', () => {
      loadStore({ maxDeliveryRadiusKm: 7 });

      expect(component.maxDeliveryRadiusKm()).toBe(7);
      expect(component.radiusInputValue()).toBe(7);
    });

    it('queries with the edited radius and marks the form dirty', () => {
      loadStore({ maxDeliveryRadiusKm: 5 });
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValue(of([
        { id: 'nb-1', neighborhood: 'Pinheiros', city: 'Sao Paulo', latitude: -23.5667, longitude: -46.6833 },
      ]));

      component.onDeliveryRadiusChange(8);

      expect(storeServiceMock.getDeliveryNeighborhoodsByStore).toHaveBeenLastCalledWith('store-123', 8);
      expect(component.maxDeliveryRadiusKm()).toBe(8);
      expect(component.formDirty()).toBe(true);
      expect(component.deliveryNeighborhoods().map(n => n.neighborhood)).toContain('Pinheiros');
    });

    it('does not query or remove areas for an invalid radius', () => {
      loadStore({
        maxDeliveryRadiusKm: 5,
        deliveryAreas: [{ id: 'a1', neighborhood: 'Centro', deliveryFee: 5 }],
      });
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockClear();

      component.onDeliveryRadiusChange(0);

      expect(storeServiceMock.getDeliveryNeighborhoodsByStore).not.toHaveBeenCalled();
      expect(component.areas.length).toBe(1);
      expect(component.radiusValidationError()).toBe(true);
      expect(component.maxDeliveryRadiusKm()).toBe(5);
    });

    it('removes selected areas outside the reduced radius after a successful reload', () => {
      loadStore({
        maxDeliveryRadiusKm: 10,
        deliveryAreas: [
          { id: 'a1', neighborhood: 'Bela Vista', deliveryFee: 5 },
          { id: 'a2', neighborhood: 'Vila Mariana', deliveryFee: 8 },
        ],
      }, [
        { id: 'nb-bela', neighborhood: 'Bela Vista', city: 'Sao Paulo', latitude: -23.558, longitude: -46.642 },
        { id: 'nb-vila', neighborhood: 'Vila Mariana', city: 'Sao Paulo', latitude: -23.5505, longitude: -46.584 },
      ]);

      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValue(of([
        { id: 'nb-bela', neighborhood: 'Bela Vista', city: 'Sao Paulo', latitude: -23.558, longitude: -46.642 },
      ]));

      component.onDeliveryRadiusChange(3);

      const names = component.areas.controls.map(c => c.value.neighborhood);
      expect(names).toEqual(['Bela Vista']);
      expect(component.formDirty()).toBe(true);
    });

    it('retains an unknown store-only manual neighborhood while removing a known out-of-radius one', () => {
      loadStore({
        maxDeliveryRadiusKm: 10,
        deliveryAreas: [
          { id: 'a1', neighborhood: 'Bela Vista', deliveryFee: 5 },
          { id: 'a2', neighborhood: 'Vila Mariana', deliveryFee: 8 },
          { id: 'a3', neighborhood: 'Bairro Manual', deliveryFee: 4 },
        ],
      }, [
        { id: 'nb-bela', neighborhood: 'Bela Vista', city: 'Sao Paulo', latitude: -23.558, longitude: -46.642 },
        { id: 'nb-vila', neighborhood: 'Vila Mariana', city: 'Sao Paulo', latitude: -23.5505, longitude: -46.584 },
      ]);

      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValue(of([
        { id: 'nb-bela', neighborhood: 'Bela Vista', city: 'Sao Paulo', latitude: -23.558, longitude: -46.642 },
      ]));

      component.onDeliveryRadiusChange(3);

      expect(component.areas.controls.map(c => c.value.neighborhood)).toEqual(['Bairro Manual', 'Bela Vista']);
      expect(component.formDirty()).toBe(true);
    });

    it('removes only the store form-array areas and keeps the global eligible list available', () => {
      loadStore({
        maxDeliveryRadiusKm: 10,
        deliveryAreas: [
          { id: 'a1', neighborhood: 'Bela Vista', deliveryFee: 5 },
          { id: 'a2', neighborhood: 'Vila Mariana', deliveryFee: 8 },
        ],
      }, [
        { id: 'nb-bela', neighborhood: 'Bela Vista', city: 'Sao Paulo', latitude: -23.558, longitude: -46.642 },
        { id: 'nb-vila', neighborhood: 'Vila Mariana', city: 'Sao Paulo', latitude: -23.5505, longitude: -46.584 },
      ]);

      const eligible: DeliveryNeighborhood[] = [
        { id: 'nb-bela', neighborhood: 'Bela Vista', city: 'Sao Paulo', latitude: -23.558, longitude: -46.642 },
        { id: 'nb-manual', neighborhood: 'Bairro Manual', city: 'Sao Paulo' },
      ];
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValue(of(eligible));

      component.onDeliveryRadiusChange(3);

      expect(component.areas.controls.map(c => c.value.neighborhood)).toEqual(['Bela Vista']);
      expect(component.deliveryNeighborhoods().map(n => n.neighborhood)).toEqual(['Bela Vista', 'Bairro Manual']);
      expect(eligible.map(n => n.neighborhood)).toEqual(['Bela Vista', 'Bairro Manual']);
    });

    it('keeps selected store areas when the radius increases', () => {
      loadStore({
        maxDeliveryRadiusKm: 3,
        deliveryAreas: [
          { id: 'a1', neighborhood: 'Bela Vista', deliveryFee: 5 },
          { id: 'a2', neighborhood: 'Vila Mariana', deliveryFee: 8 },
        ],
      });

      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValue(of([
        { id: 'nb-bela', neighborhood: 'Bela Vista', city: 'Sao Paulo', latitude: -23.558, longitude: -46.642 },
        { id: 'nb-vila', neighborhood: 'Vila Mariana', city: 'Sao Paulo', latitude: -23.5505, longitude: -46.5840 },
      ]));

      component.onDeliveryRadiusChange(10);

      expect(component.areas.controls.map(c => c.value.neighborhood)).toEqual(['Bela Vista', 'Vila Mariana']);
      expect(component.deliveryNeighborhoods().map(n => n.neighborhood)).toEqual(['Bela Vista', 'Vila Mariana']);
    });

    it('keeps selected store areas when the radius stays the same', () => {
      loadStore({
        maxDeliveryRadiusKm: 5,
        deliveryAreas: [
          { id: 'a1', neighborhood: 'Bela Vista', deliveryFee: 5 },
          { id: 'a2', neighborhood: 'Vila Mariana', deliveryFee: 8 },
        ],
      });

      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValue(of([
        { id: 'nb-bela', neighborhood: 'Bela Vista', city: 'Sao Paulo', latitude: -23.558, longitude: -46.642 },
      ]));

      component.onDeliveryRadiusChange(5);

      expect(component.areas.controls.map(c => c.value.neighborhood)).toEqual(['Bela Vista', 'Vila Mariana']);
    });

    it('invalidates a pending preview when the input becomes invalid and never mutates the store FormArray', () => {
      loadStore({
        maxDeliveryRadiusKm: 10,
        deliveryAreas: [
          { id: 'a1', neighborhood: 'Bela Vista', deliveryFee: 5 },
          { id: 'a2', neighborhood: 'Vila Mariana', deliveryFee: 8 },
        ],
      });

      const pending$ = new Subject<DeliveryNeighborhood[]>();
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValueOnce(pending$);

      component.onDeliveryRadiusChange(3);
      expect(component.isRadiusUpdating()).toBe(true);

      component.onDeliveryRadiusChange('');
      expect(component.radiusValidationError()).toBe(true);
      expect(component.isRadiusUpdating()).toBe(false);
      expect(storeServiceMock.getDeliveryNeighborhoodsByStore).toHaveBeenCalledTimes(2);

      pending$.next([
        { id: 'nb-bela', neighborhood: 'Bela Vista', city: 'Sao Paulo', latitude: -23.558, longitude: -46.642 },
      ]);
      pending$.complete();

      expect(component.areas.controls.map(c => c.value.neighborhood)).toEqual(['Bela Vista', 'Vila Mariana']);
      expect(component.deliveryNeighborhoods()).toEqual([]);
    });

    it('persists only the remaining in-range areas after a radius reduction', async () => {
      loadStore({
        maxDeliveryRadiusKm: 10,
        deliveryAreas: [
          { id: 'a1', neighborhood: 'Bela Vista', deliveryFee: 5 },
          { id: 'a2', neighborhood: 'Vila Mariana', deliveryFee: 8 },
        ],
      }, [
        { id: 'nb-bela', neighborhood: 'Bela Vista', city: 'Sao Paulo', latitude: -23.558, longitude: -46.642 },
        { id: 'nb-vila', neighborhood: 'Vila Mariana', city: 'Sao Paulo', latitude: -23.5505, longitude: -46.584 },
      ]);
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValue(of([
        { id: 'nb-bela', neighborhood: 'Bela Vista', city: 'Sao Paulo', latitude: -23.558, longitude: -46.642 },
      ]));
      storeServiceMock.updateDeliveryConfig!.mockReturnValue(of({} as any));

      component.onDeliveryRadiusChange(3);
      await component.saveDraft();

      const [, payload] = storeServiceMock.updateDeliveryConfig!.mock.calls[0] as [string, any];
      expect((payload.deliveryAreas as { neighborhood: string }[]).map(a => a.neighborhood)).toEqual(['Bela Vista']);
    });

    it('retains areas and the previous list when the reload fails', () => {
      loadStore({
        maxDeliveryRadiusKm: 10,
        deliveryAreas: [
          { id: 'a1', neighborhood: 'Bela Vista', deliveryFee: 5 },
          { id: 'a2', neighborhood: 'Vila Mariana', deliveryFee: 8 },
        ],
      });
      const previous = component.deliveryNeighborhoods();
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValue(throwError(() => new Error('boom')));

      component.onDeliveryRadiusChange(3);

      expect(component.areas.length).toBe(2);
      expect(component.deliveryNeighborhoods()).toEqual(previous);
      expect(toastServiceMock.showError).toHaveBeenCalled();
    });

    it('exposes a distinct preview error and blocks saving after a failed valid-radius request', async () => {
      loadStore({
        maxDeliveryRadiusKm: 10,
        deliveryAreas: [
          { id: 'a1', neighborhood: 'Bela Vista', deliveryFee: 5 },
          { id: 'a2', neighborhood: 'Vila Mariana', deliveryFee: 8 },
        ],
      });
      const previous = component.deliveryNeighborhoods();
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValue(throwError(() => new Error('boom')));

      component.onDeliveryRadiusChange(3);

      expect(component.radiusPreviewError()).toBe(true);
      expect(component.radiusValidationError()).toBe(false);
      expect(component.isRadiusUpdating()).toBe(false);
      expect(component.maxDeliveryRadiusKm()).toBe(3);
      expect(component.radiusInputValue()).toBe(3);
      expect(component.formDirty()).toBe(true);
      expect(component.areas.length).toBe(2);
      expect(component.deliveryNeighborhoods()).toEqual(previous);

      await component.saveDraft();

      expect(storeServiceMock.updateDeliveryConfig).not.toHaveBeenCalled();
      expect(component.saveStatus()).toBe('idle');
    });

    it('clears the preview error after a successful retry and allows saving again', async () => {
      loadStore({
        maxDeliveryRadiusKm: 10,
        deliveryAreas: [
          { id: 'a1', neighborhood: 'Bela Vista', deliveryFee: 5 },
          { id: 'a2', neighborhood: 'Vila Mariana', deliveryFee: 8 },
        ],
      }, [
        { id: 'nb-bela', neighborhood: 'Bela Vista', city: 'Sao Paulo', latitude: -23.558, longitude: -46.642 },
        { id: 'nb-vila', neighborhood: 'Vila Mariana', city: 'Sao Paulo', latitude: -23.5505, longitude: -46.584 },
      ]);
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValueOnce(throwError(() => new Error('boom')));

      component.onDeliveryRadiusChange(3);
      expect(component.radiusPreviewError()).toBe(true);

      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValueOnce(of([
        { id: 'nb-bela', neighborhood: 'Bela Vista', city: 'Sao Paulo', latitude: -23.558, longitude: -46.642 },
      ]));

      component.onDeliveryRadiusChange(3);

      expect(component.radiusPreviewError()).toBe(false);
      expect(component.isRadiusUpdating()).toBe(false);
      expect(component.areas.controls.map(c => c.value.neighborhood)).toEqual(['Bela Vista']);

      await component.saveDraft();

      expect(storeServiceMock.updateDeliveryConfig).toHaveBeenCalledTimes(1);
      const [, payload] = storeServiceMock.updateDeliveryConfig!.mock.calls[0] as [string, any];
      expect(payload.maxDeliveryRadiusKm).toBe(3);
      expect((payload.deliveryAreas as { neighborhood: string }[]).map(a => a.neighborhood)).toEqual(['Bela Vista']);
    });

    it('ignores a stale preview response that resolves after a newer radius request', () => {
      loadStore({
        maxDeliveryRadiusKm: 10,
        deliveryAreas: [
          { id: 'a1', neighborhood: 'Bela Vista', deliveryFee: 5 },
          { id: 'a2', neighborhood: 'Vila Mariana', deliveryFee: 8 },
        ],
      }, [
        { id: 'nb-bela', neighborhood: 'Bela Vista', city: 'Sao Paulo', latitude: -23.558, longitude: -46.642 },
        { id: 'nb-vila', neighborhood: 'Vila Mariana', city: 'Sao Paulo', latitude: -23.5505, longitude: -46.584 },
      ]);

      const stale$ = new Subject<DeliveryNeighborhood[]>();
      const latest$ = new Subject<DeliveryNeighborhood[]>();
      storeServiceMock.getDeliveryNeighborhoodsByStore!
        .mockReturnValueOnce(stale$)
        .mockReturnValueOnce(latest$);

      component.onDeliveryRadiusChange(8);
      component.onDeliveryRadiusChange(3);

      latest$.next([
        { id: 'nb-bela', neighborhood: 'Bela Vista', city: 'Sao Paulo', latitude: -23.558, longitude: -46.642 },
      ]);
      latest$.complete();

      expect(component.deliveryNeighborhoods().map(n => n.neighborhood)).toEqual(['Bela Vista']);
      expect(component.areas.controls.map(c => c.value.neighborhood)).toEqual(['Bela Vista']);
      expect(component.isRadiusUpdating()).toBe(false);

      stale$.next([
        { id: 'nb-bela', neighborhood: 'Bela Vista', city: 'Sao Paulo', latitude: -23.558, longitude: -46.642 },
        { id: 'nb-vila', neighborhood: 'Vila Mariana', city: 'Sao Paulo', latitude: -23.55, longitude: -46.584 },
      ]);
      stale$.complete();

      expect(component.deliveryNeighborhoods().map(n => n.neighborhood)).toEqual(['Bela Vista']);
      expect(component.areas.controls.map(c => c.value.neighborhood)).toEqual(['Bela Vista']);
    });

    it('does not show an error when a stale preview request fails after a newer one succeeds', () => {
      loadStore({
        maxDeliveryRadiusKm: 10,
        deliveryAreas: [{ id: 'a1', neighborhood: 'Bela Vista', deliveryFee: 5 }],
      });

      const stale$ = new Subject<DeliveryNeighborhood[]>();
      const latest$ = new Subject<DeliveryNeighborhood[]>();
      storeServiceMock.getDeliveryNeighborhoodsByStore!
        .mockReturnValueOnce(stale$)
        .mockReturnValueOnce(latest$);

      component.onDeliveryRadiusChange(8);
      component.onDeliveryRadiusChange(3);

      latest$.next([
        { id: 'nb-bela', neighborhood: 'Bela Vista', city: 'Sao Paulo', latitude: -23.558, longitude: -46.642 },
      ]);
      latest$.complete();
      toastServiceMock.showError.mockClear();

      stale$.error(new Error('boom'));

      expect(toastServiceMock.showError).not.toHaveBeenCalled();
      expect(component.deliveryNeighborhoods().map(n => n.neighborhood)).toEqual(['Bela Vista']);
      expect(component.areas.length).toBe(1);
    });

    it('ignores the initial neighborhood load when a newer radius preview has already resolved', () => {
      const initial$ = new Subject<DeliveryNeighborhood[]>();
      storeServiceMock.getMyStore!.mockReturnValue(of({
        ...mockStore,
        deliveryFee: 0,
        minimumOrderValue: 0,
        maxDeliveryRadiusKm: 10,
      }));
      storeServiceMock.getStoreAddress!.mockReturnValue(of(mockAddress));
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValueOnce(initial$);

      fixture.detectChanges();

      const preview$ = new Subject<DeliveryNeighborhood[]>();
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValueOnce(preview$);

      component.onDeliveryRadiusChange(3);
      preview$.next([
        { id: 'nb-preview', neighborhood: 'Bela Vista', city: 'Sao Paulo', latitude: -23.558, longitude: -46.642 },
      ]);
      preview$.complete();

      expect(component.deliveryNeighborhoods().map(n => n.neighborhood)).toEqual(['Bela Vista']);

      initial$.next([
        { id: 'nb-initial', neighborhood: 'Centro', city: 'Sao Paulo', latitude: -23.55, longitude: -46.63 },
      ]);
      initial$.complete();

      expect(component.deliveryNeighborhoods().map(n => n.neighborhood)).toEqual(['Bela Vista']);
    });

    it('ignores the initial neighborhood load when the radius changed before the address response', () => {
      const address$ = new Subject<typeof mockAddress>();
      storeServiceMock.getMyStore!.mockReturnValue(of({
        ...mockStore,
        deliveryFee: 0,
        minimumOrderValue: 0,
        maxDeliveryRadiusKm: 10,
      }));
      storeServiceMock.getStoreAddress!.mockReturnValue(address$);
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValue(of([]));

      fixture.detectChanges();

      const preview$ = new Subject<DeliveryNeighborhood[]>();
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValueOnce(preview$);

      component.onDeliveryRadiusChange(3);
      preview$.next([
        { id: 'nb-preview', neighborhood: 'Bela Vista', city: 'Sao Paulo', latitude: -23.558, longitude: -46.642 },
      ]);
      preview$.complete();

      address$.next(mockAddress);
      address$.complete();

      expect(storeServiceMock.getDeliveryNeighborhoodsByStore).toHaveBeenCalledTimes(1);
      expect(component.deliveryNeighborhoods().map(n => n.neighborhood)).toEqual(['Bela Vista']);
    });

    it('restores the persisted neighborhood list for the persisted radius when changes are discarded', () => {
      const persistedList: DeliveryNeighborhood[] = [
        { id: 'nb-bela', neighborhood: 'Bela Vista', city: 'Sao Paulo', latitude: -23.558, longitude: -46.642 },
        { id: 'nb-vila', neighborhood: 'Vila Mariana', city: 'Sao Paulo', latitude: -23.55, longitude: -46.584 },
      ];
      storeServiceMock.getMyStore!.mockReturnValue(of({
        ...mockStore,
        deliveryFee: 0,
        minimumOrderValue: 0,
        maxDeliveryRadiusKm: 5,
        deliveryAreas: [{ id: 'a1', neighborhood: 'Bela Vista', deliveryFee: 5 }],
      }));
      storeServiceMock.getStoreAddress!.mockReturnValue(of(mockAddress));
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValue(of(persistedList));
      storeServiceMock.updateDeliveryConfig!.mockReturnValue(of({} as any));

      fixture.detectChanges();

      expect(component.deliveryNeighborhoods().map(n => n.neighborhood)).toEqual(['Bela Vista', 'Vila Mariana']);

      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValueOnce(of([
        { id: 'nb-bela', neighborhood: 'Bela Vista', city: 'Sao Paulo', latitude: -23.558, longitude: -46.642 },
      ]));
      component.onDeliveryRadiusChange(3);
      expect(component.deliveryNeighborhoods().map(n => n.neighborhood)).toEqual(['Bela Vista']);

      component.cancelChanges();

      expect(component.maxDeliveryRadiusKm()).toBe(5);
      expect(storeServiceMock.getDeliveryNeighborhoodsByStore).toHaveBeenLastCalledWith('store-123', 5);
      expect(component.deliveryNeighborhoods().map(n => n.neighborhood)).toEqual(['Bela Vista', 'Vila Mariana']);
      expect(component.areas.controls.map(c => c.value.neighborhood)).toEqual(['Bela Vista']);
    });

    it('does not restore the persisted config when the radius is edited after cancelChanges but before the restore response', () => {
      loadStore({
        maxDeliveryRadiusKm: 5,
        deliveryAreas: [{ id: 'a1', neighborhood: 'Bela Vista', deliveryFee: 5 }],
      });

      const restore$ = new Subject<typeof mockStore>();
      storeServiceMock.getMyStore!.mockReturnValue(restore$);
      storeServiceMock.getDeliveryNeighborhoodsByStore!
        .mockReturnValueOnce(of([
          { id: 'nb-vila', neighborhood: 'Vila Mariana', city: 'Sao Paulo', latitude: -23.55, longitude: -46.584 },
        ]))
        .mockReturnValue(of([
          { id: 'nb-bela', neighborhood: 'Bela Vista', city: 'Sao Paulo', latitude: -23.558, longitude: -46.642 },
        ]));

      component.cancelChanges();

      component.onDeliveryRadiusChange(8);

      expect(component.maxDeliveryRadiusKm()).toBe(8);
      expect(component.radiusInputValue()).toBe(8);
      expect(component.formDirty()).toBe(true);
      expect(component.deliveryNeighborhoods().map(n => n.neighborhood)).toEqual(['Vila Mariana']);

      restore$.next({
        ...mockStore,
        deliveryFee: 0,
        minimumOrderValue: 0,
        maxDeliveryRadiusKm: 5,
        deliveryAreas: [{ id: 'a1', neighborhood: 'Bela Vista', deliveryFee: 5 }],
      });
      restore$.complete();

      expect(component.maxDeliveryRadiusKm()).toBe(8);
      expect(component.radiusInputValue()).toBe(8);
      expect(component.formDirty()).toBe(true);
      expect(component.areas.controls.map(c => c.value.neighborhood)).toEqual(['Bela Vista']);
      expect(component.deliveryNeighborhoods().map(n => n.neighborhood)).toEqual(['Vila Mariana']);
    });

    it('keeps the restored persisted list when a pending preview resolves after cancelChanges', () => {
      const persistedList: DeliveryNeighborhood[] = [
        { id: 'nb-bela', neighborhood: 'Bela Vista', city: 'Sao Paulo', latitude: -23.558, longitude: -46.642 },
        { id: 'nb-vila', neighborhood: 'Vila Mariana', city: 'Sao Paulo', latitude: -23.55, longitude: -46.584 },
      ];
      storeServiceMock.getMyStore!.mockReturnValue(of({
        ...mockStore,
        deliveryFee: 0,
        minimumOrderValue: 0,
        maxDeliveryRadiusKm: 5,
        deliveryAreas: [{ id: 'a1', neighborhood: 'Bela Vista', deliveryFee: 5 }],
      }));
      storeServiceMock.getStoreAddress!.mockReturnValue(of(mockAddress));
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValue(of(persistedList));
      storeServiceMock.updateDeliveryConfig!.mockReturnValue(of({} as any));

      fixture.detectChanges();

      const pending$ = new Subject<DeliveryNeighborhood[]>();
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValueOnce(pending$);
      component.onDeliveryRadiusChange(3);
      expect(component.isRadiusUpdating()).toBe(true);

      component.cancelChanges();

      expect(component.deliveryNeighborhoods().map(n => n.neighborhood)).toEqual(['Bela Vista', 'Vila Mariana']);

      pending$.next([
        { id: 'nb-bela', neighborhood: 'Bela Vista', city: 'Sao Paulo', latitude: -23.558, longitude: -46.642 },
      ]);
      pending$.complete();

      expect(component.deliveryNeighborhoods().map(n => n.neighborhood)).toEqual(['Bela Vista', 'Vila Mariana']);
      expect(component.areas.controls.map(c => c.value.neighborhood)).toEqual(['Bela Vista']);
      expect(component.radiusPreviewError()).toBe(false);
      expect(component.isRadiusUpdating()).toBe(false);
    });

    it('ignores a pending preview response that resolves after cancelChanges', () => {
      loadStore({
        maxDeliveryRadiusKm: 10,
        deliveryAreas: [
          { id: 'a1', neighborhood: 'Bela Vista', deliveryFee: 5 },
          { id: 'a2', neighborhood: 'Vila Mariana', deliveryFee: 8 },
        ],
      });

      const pending$ = new Subject<DeliveryNeighborhood[]>();
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValueOnce(pending$);

      component.onDeliveryRadiusChange(3);
      expect(component.isRadiusUpdating()).toBe(true);

      component.cancelChanges();

      expect(component.isRadiusUpdating()).toBe(false);
      expect(component.areas.controls.map(c => c.value.neighborhood)).toEqual(['Bela Vista', 'Vila Mariana']);

      pending$.next([
        { id: 'nb-bela', neighborhood: 'Bela Vista', city: 'Sao Paulo', latitude: -23.558, longitude: -46.642 },
      ]);
      pending$.complete();

      expect(component.deliveryNeighborhoods()).toEqual([]);
      expect(component.areas.controls.map(c => c.value.neighborhood)).toEqual(['Bela Vista', 'Vila Mariana']);
      expect(component.radiusPreviewError()).toBe(false);
    });

    it('ignores a pending preview error that arrives after cancelChanges', () => {
      loadStore({
        maxDeliveryRadiusKm: 10,
        deliveryAreas: [{ id: 'a1', neighborhood: 'Bela Vista', deliveryFee: 5 }],
      });

      const pending$ = new Subject<DeliveryNeighborhood[]>();
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValueOnce(pending$);

      component.onDeliveryRadiusChange(3);
      component.cancelChanges();
      toastServiceMock.showError.mockClear();

      pending$.error(new Error('boom'));

      expect(component.radiusPreviewError()).toBe(false);
      expect(component.isRadiusUpdating()).toBe(false);
      expect(toastServiceMock.showError).not.toHaveBeenCalled();
      expect(component.areas.controls.map(c => c.value.neighborhood)).toEqual(['Bela Vista']);
    });

    it('blocks persistence while a radius preview is in flight', async () => {
      loadStore({
        maxDeliveryRadiusKm: 10,
        deliveryAreas: [{ id: 'a1', neighborhood: 'Centro', deliveryFee: 5 }],
      });

      const pending$ = new Subject<DeliveryNeighborhood[]>();
      storeServiceMock.getDeliveryNeighborhoodsByStore!.mockReturnValueOnce(pending$);

      component.onDeliveryRadiusChange(3);
      expect(component.isRadiusUpdating()).toBe(true);

      await component.saveDraft();

      expect(storeServiceMock.updateDeliveryConfig).not.toHaveBeenCalled();
      expect(component.saveStatus()).toBe('idle');
    });

    it('includes the radius in the save payload', async () => {
      loadStore({
        maxDeliveryRadiusKm: 6,
        deliveryAreas: [{ id: 'a1', neighborhood: 'Centro', deliveryFee: 5 }],
      });

      await component.saveDraft();

      const [, payload] = storeServiceMock.updateDeliveryConfig!.mock.calls[0] as [string, any];
      expect(payload.maxDeliveryRadiusKm).toBe(6);
    });
  });
});
