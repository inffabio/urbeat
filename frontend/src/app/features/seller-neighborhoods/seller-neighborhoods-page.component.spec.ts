import { TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { FormGroup } from '@angular/forms';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AlertController, ToastController } from '@ionic/angular';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { SellerNeighborhoodsPageComponent } from './seller-neighborhoods-page.component';
import { StoreService } from '../../core/services/store.service';
import { ToastService } from '../../core/services/toast.service';

describe('SellerNeighborhoodsPageComponent', () => {
  const storeServiceMock = {
    getMyStore: jest.fn(),
    getStoreAddress: jest.fn(),
    getDeliveryNeighborhoodsByStore: jest.fn(),
    createDeliveryNeighborhood: jest.fn(),
    updateDeliveryConfig: jest.fn(),
  };

  const toastServiceMock = {
    showError: jest.fn(),
    showSuccess: jest.fn(),
    showWarning: jest.fn(),
    showInfo: jest.fn(),
  };

  beforeEach(async () => {
    jest.clearAllMocks();
    jest.spyOn(window, 'confirm').mockReturnValue(true);

    storeServiceMock.getMyStore.mockReturnValue(of({
      id: 'store-123',
      deliveryAreas: [{ id: 'a1', neighborhood: 'Centro', deliveryFee: 5 }],
    }));
    storeServiceMock.getStoreAddress.mockReturnValue(of({ city: 'Sao Paulo', state: 'SP' }));
    storeServiceMock.getDeliveryNeighborhoodsByStore.mockReturnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [SellerNeighborhoodsPageComponent],
      schemas: [NO_ERRORS_SCHEMA],
      providers: [
        provideRouter([]),
        { provide: StoreService, useValue: storeServiceMock },
        { provide: ToastService, useValue: toastServiceMock },
        { provide: AlertController, useValue: { create: jest.fn() } },
        { provide: ToastController, useValue: { create: jest.fn().mockResolvedValue({ present: jest.fn().mockResolvedValue(undefined) }) } },
      ],
    }).compileComponents();
  });

  it('asks for confirmation before removing a neighborhood', async () => {
    const confirmSpy = jest.spyOn(window, 'confirm').mockReturnValue(false);
    const fixture = TestBed.createComponent(SellerNeighborhoodsPageComponent);
    fixture.detectChanges();

    await fixture.componentInstance.removeArea(0);

    expect(confirmSpy).toHaveBeenCalledWith('Excluir o bairro "Centro"?');
    expect(fixture.componentInstance.areas.length).toBe(1);
  });

  it('restores the persisted neighborhood configuration when cancelling changes', () => {
    const fixture = TestBed.createComponent(SellerNeighborhoodsPageComponent);
    fixture.detectChanges();
    fixture.componentInstance.formDirty.set(true);
    fixture.componentInstance.areas.at(0).get('deliveryFee')?.setValue('99,00');

    fixture.componentInstance.cancelChanges();

    expect(fixture.componentInstance.formDirty()).toBe(false);
    expect(fixture.componentInstance.areas.at(0).value.deliveryFee).toBe('5,00');
  });

  it('restores the persisted available neighborhood list when cancelling changes', () => {
    storeServiceMock.getMyStore.mockReturnValue(of({
      id: 'store-123',
      maxDeliveryRadiusKm: 5,
      deliveryAreas: [{ id: 'a1', neighborhood: 'Bela Vista', deliveryFee: 5 }],
    }));
    storeServiceMock.getStoreAddress.mockReturnValue(of({
      city: 'Sao Paulo',
      state: 'SP',
      latitude: -23.5505,
      longitude: -46.6333,
    }));
    const persisted = [
      { id: 'nb-bela', neighborhood: 'Bela Vista', city: 'Sao Paulo', latitude: -23.558, longitude: -46.642 },
      { id: 'nb-vila', neighborhood: 'Vila Mariana', city: 'Sao Paulo', latitude: -23.55, longitude: -46.584 },
    ];
    storeServiceMock.getDeliveryNeighborhoodsByStore.mockReturnValue(of(persisted));

    const fixture = TestBed.createComponent(SellerNeighborhoodsPageComponent);
    fixture.detectChanges();

    storeServiceMock.getDeliveryNeighborhoodsByStore.mockReturnValueOnce(of([
      { id: 'nb-bela', neighborhood: 'Bela Vista', city: 'Sao Paulo', latitude: -23.558, longitude: -46.642 },
    ]));
    fixture.componentInstance.onDeliveryRadiusChange(3);
    expect(fixture.componentInstance.deliveryNeighborhoods().map(n => n.neighborhood)).toEqual(['Bela Vista']);

    fixture.componentInstance.cancelChanges();

    expect(fixture.componentInstance.maxDeliveryRadiusKm()).toBe(5);
    expect(storeServiceMock.getDeliveryNeighborhoodsByStore).toHaveBeenLastCalledWith('store-123', 5);
    expect(fixture.componentInstance.deliveryNeighborhoods().map(n => n.neighborhood)).toEqual(['Bela Vista', 'Vila Mariana']);
  });

  describe('manual neighborhood in the selection modal', () => {
    function createComponent() {
      const fixture = TestBed.createComponent(SellerNeighborhoodsPageComponent);
      fixture.detectChanges();
      return fixture.componentInstance;
    }

    it('keeps a manually-created neighborhood without coordinates visible and selected when the radius filter is active', () => {
      storeServiceMock.getMyStore.mockReturnValue(of({
        id: 'store-123',
        maxDeliveryRadiusKm: 5,
        deliveryAreas: [],
      }));
      storeServiceMock.getStoreAddress.mockReturnValue(of({
        city: 'Sao Paulo',
        state: 'SP',
        latitude: -23.5505,
        longitude: -46.6333,
      }));
      storeServiceMock.getDeliveryNeighborhoodsByStore.mockReturnValue(of([
        { id: 'nb-far', neighborhood: 'Far Away', city: 'Sao Paulo', latitude: -24.0, longitude: -47.0 },
      ]));
      storeServiceMock.createDeliveryNeighborhood.mockReturnValue(of({
        id: 'nb-manual',
        neighborhood: 'Novo Bairro',
        city: 'Sao Paulo',
      }));

      const component = createComponent();

      component.newNeighborhoodName.set('Novo Bairro');
      component.addNeighborhood();

      const filtered = component.filteredNeighborhoods();
      expect(filtered.map(n => n.neighborhood)).toContain('Novo Bairro');
      expect(filtered.map(n => n.neighborhood)).not.toContain('Far Away');
      expect(component.checkedNeighborhoodIds().has('nb-manual')).toBe(true);
    });

    it('adds the manual neighborhood to the areas FormArray only after "Adicionar bairros"', () => {
      storeServiceMock.getMyStore.mockReturnValue(of({ id: 'store-123', deliveryAreas: [] }));
      storeServiceMock.getStoreAddress.mockReturnValue(of({ city: 'Sao Paulo', state: 'SP' }));
      storeServiceMock.getDeliveryNeighborhoodsByStore.mockReturnValue(of([]));
      storeServiceMock.createDeliveryNeighborhood.mockReturnValue(of({
        id: 'nb-manual',
        neighborhood: 'Novo Bairro',
        city: 'Sao Paulo',
      }));

      const component = createComponent();

      component.newNeighborhoodName.set('Novo Bairro');
      component.addNeighborhood();

      expect(component.areas.length).toBe(0);

      component.addSelectedNeighborhoods();

      expect(component.areas.length).toBe(1);
      expect(component.areas.at(0).value.neighborhood).toBe('Novo Bairro');
    });

    it('removes an added neighborhood from the modal after it is added to the areas list', () => {
      storeServiceMock.getMyStore.mockReturnValue(of({ id: 'store-123', deliveryAreas: [] }));
      storeServiceMock.getStoreAddress.mockReturnValue(of({ city: 'Sao Paulo', state: 'SP' }));
      storeServiceMock.getDeliveryNeighborhoodsByStore.mockReturnValue(of([]));
      storeServiceMock.createDeliveryNeighborhood.mockReturnValue(of({
        id: 'nb-manual',
        neighborhood: 'Novo Bairro',
        city: 'Sao Paulo',
      }));

      const component = createComponent();

      component.openNeighborhoodModal(-1);
      component.newNeighborhoodName.set('Novo Bairro');
      component.addNeighborhood();

      expect(component.filteredNeighborhoods().map(n => n.neighborhood)).toContain('Novo Bairro');

      component.addSelectedNeighborhoods();
      expect(component.areas.length).toBe(1);

      component.openNeighborhoodModal(-1);
      expect(component.filteredNeighborhoods().map(n => n.neighborhood)).not.toContain('Novo Bairro');
    });

    it('styles the final "Adicionar bairros" button with an orange background and white text on hover', () => {
      const template = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.html'), 'utf8');
      const styles = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.scss'), 'utf8');

      expect(template).toContain('Adicionar bairros');
      expect(styles).toContain('.nb-footer .btn-orange:hover');
      expect(styles).toContain('var(--dash-orange');
      expect(styles).toContain('color: #fff !important');
    });
  });

  describe('selection modal spacing and manual add button', () => {
    it('lays out each neighborhood with explicit spacing classes for checkbox, name and city', () => {
      const template = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.html'), 'utf8');
      const styles = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.scss'), 'utf8');

      expect(template).toContain('class="nb-item"');
      expect(template).toContain('class="nb-checkbox"');
      expect(template).toContain('class="nb-label"');
      expect(styles).toContain('.nb-list ion-item.nb-item');
      expect(styles).toContain('--min-height: 64px');
      expect(styles).toContain('.nb-checkbox');
      expect(styles).toContain('accent-color');
      expect(styles).toContain('.nb-label');
      expect(styles).toContain('.nb-name');
      expect(styles).toContain('.nb-city');
    });

    it('gives each neighborhood checkbox a real 44x44 touch target while keeping the native input and row click', () => {
      const template = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.html'), 'utf8');
      const styles = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.scss'), 'utf8');

      expect(template).toContain('class="nb-check-wrap"');
      expect(template).toContain('type="checkbox"');
      expect(template).toContain('slot="start"');
      expect(template).toContain('(click)="toggleNeighborhood(nb.id)"');
      expect(styles).toContain('.nb-check-wrap');
      expect(styles).toContain('width: 44px');
      expect(styles).toContain('min-width: 44px');
      expect(styles).toContain('height: 44px');
      expect(styles).toContain('min-height: 44px');
      expect(styles).toContain('place-items: center');
      expect(styles).toContain('cursor: pointer');
    });

    it('darkens the modal auxiliary texts to AA contrast with a darker muted token fallback', () => {
      const styles = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.scss'), 'utf8');

      const modalSelectors = ['.nb-modal-copy', '.nb-select-all', '.nb-city', '.nb-add-new p', '.nb-footer'];
      for (const selector of modalSelectors) {
        const rule = styles.match(new RegExp(selector.replace(/\./g, '\\.') + '\\s*\\{[^}]*color:[^}]*\\}'));
        expect(rule).toBeTruthy();
        expect(rule?.[0]).toContain('--dash-muted-strong');
        expect(rule?.[0]).not.toContain('var(--dash-muted, #7d8298)');
      }
    });

    it('styles the manual "Adicionar" button with a dark orange hover/focus that reaches AA contrast', () => {
      const template = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.html'), 'utf8');
      const styles = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.scss'), 'utf8');

      expect(template).toContain('nb-add-btn');
      expect(styles).toContain('.nb-add-row .nb-add-btn:hover');
      expect(styles).toContain('.nb-add-row .nb-add-btn:focus-visible');
      expect(styles).toContain('var(--dash-orange-strong, #c2410c)');
      expect(styles).toContain('color: #fff');
    });

    it('keeps the manual "Adicionar" button focus visibly explicit', () => {
      const styles = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.scss'), 'utf8');

      expect(styles).toContain('.nb-add-row .nb-add-btn:focus-visible');
      expect(styles).toContain('outline: 3px solid');
      expect(styles).toContain('outline-offset: 2px');
    });

    it('gives the "Selecionar todos" control a native checkbox, an accessible name and a 44px touch target', () => {
      const template = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.html'), 'utf8');
      const styles = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.scss'), 'utf8');

      expect(template).toContain('aria-label="Selecionar todos os bairros"');
      expect(template).toContain('class="nb-check-label"');
      expect(template).toContain('type="checkbox"');
      expect(styles).toContain('.nb-check-label');
      expect(styles).toContain('min-height: 44px');
      expect(styles).toContain('display: flex; align-items: center');
    });
  });

  describe('per-area minimum order removal', () => {
    it('does not render a minimum order column, field or per-area free shipping editor', () => {
      const template = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.html'), 'utf8');

      expect(template).not.toContain('Pedido mínimo');
      expect(template).not.toContain('formControlName="minimumOrderValue"');
      expect(template).not.toContain('Frete grátis acima de');
    });

    it('keeps the global free shipping threshold control', () => {
      const template = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.html'), 'utf8');

      expect(template).toContain('Frete grátis a partir de');
      expect(template).toContain('formControlName="freeShippingThreshold"');
    });

    it('does not create a minimumOrderValue control per area but keeps the delivery fee', () => {
      const fixture = TestBed.createComponent(SellerNeighborhoodsPageComponent);
      fixture.detectChanges();

      const area = fixture.componentInstance.areas.at(0) as FormGroup;
      expect(area.get('minimumOrderValue')).toBeNull();
      expect(area.get('deliveryFee')).toBeTruthy();
    });

    it('does not create a freeShippingThreshold control per area', () => {
      const fixture = TestBed.createComponent(SellerNeighborhoodsPageComponent);
      fixture.detectChanges();

      const area = fixture.componentInstance.areas.at(0) as FormGroup;
      expect(area.get('freeShippingThreshold')).toBeNull();
    });
  });

  describe('delivery distance column', () => {
    it('renders a Distância header, data-label and the distance helper in the grid', () => {
      const template = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.html'), 'utf8');

      expect(template).toContain('<th>Distância</th>');
      expect(template).toContain('data-label="Distância"');
      expect(template).toContain('{{ getNeighborhoodDistance(areas.at(i).value.neighborhood) }}');
    });

    it('shows the empty colspan matching the five-column layout', () => {
      const template = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.html'), 'utf8');

      expect(template).toContain('<td colspan="5" class="table-empty">Nenhum bairro encontrado.</td>');
    });

    it('returns "—" for a neighborhood without coordinates', () => {
      const fixture = TestBed.createComponent(SellerNeighborhoodsPageComponent);
      fixture.detectChanges();

      expect(fixture.componentInstance.getNeighborhoodDistance('Centro')).toBe('—');
    });

    it('returns the distance in km for a neighborhood with coordinates', () => {
      storeServiceMock.getStoreAddress.mockReturnValue(of({
        city: 'Sao Paulo',
        state: 'SP',
        latitude: -23.5505,
        longitude: -46.6333,
      }));
      storeServiceMock.getDeliveryNeighborhoodsByStore.mockReturnValue(of([
        { id: 'nb-centro', neighborhood: 'Centro', city: 'Sao Paulo', latitude: -23.56, longitude: -46.64 },
      ]));

      const fixture = TestBed.createComponent(SellerNeighborhoodsPageComponent);
      fixture.detectChanges();

      const distance = fixture.componentInstance.getNeighborhoodDistance('Centro');
      expect(distance).toContain('km');
      expect(distance).not.toBe('—');
    });
  });

  describe('row selection and editor feedback', () => {
    function createComponent() {
      const fixture = TestBed.createComponent(SellerNeighborhoodsPageComponent);
      fixture.detectChanges();
      return fixture;
    }

    it('starts with no selected row and selects one persistently', () => {
      const fixture = createComponent();
      const component = fixture.componentInstance;

      expect(component.activeRowIndex()).toBeNull();
      expect(component.selectedAreaIndex()).toBeNull();

      component.selectArea(0);

      expect(component.activeRowIndex()).toBe(0);
      expect(component.selectedAreaIndex()).toBe(0);
    });

    it('moves the selection to the newly clicked neighborhood', () => {
      storeServiceMock.getMyStore.mockReturnValue(of({
        id: 'store-123',
        deliveryAreas: [
          { id: 'a1', neighborhood: 'Centro', deliveryFee: 5 },
          { id: 'a2', neighborhood: 'Bela Vista', deliveryFee: 8 },
        ],
      }));

      const fixture = createComponent();
      const component = fixture.componentInstance;

      component.selectArea(0);
      expect(component.selectedAreaIndex()).toBe(0);
      expect(component.editorEntry()[0].group).toBe(component.areas.at(0));

      component.selectArea(1);
      expect(component.activeRowIndex()).toBe(1);
      expect(component.selectedAreaIndex()).toBe(1);
      expect(component.editorEntry()[0].index).toBe(1);
      expect(component.editorEntry()[0].group).toBe(component.areas.at(1));
      expect(component.editorEntry()[0].group.value.neighborhood).toBe(component.areas.at(1).value.neighborhood);
    });

    it('clears the selection, highlight and editor when cancelling', () => {
      const fixture = createComponent();
      const component = fixture.componentInstance;

      component.selectArea(0);
      expect(component.selectedAreaIndex()).toBe(0);
      expect(component.editorEntry().length).toBe(1);

      component.cancelChanges();

      expect(component.activeRowIndex()).toBeNull();
      expect(component.selectedAreaIndex()).toBeNull();
      expect(component.editorEntry().length).toBe(0);
    });

    it('marks the selected row with the selected-row class', () => {
      const template = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.html'), 'utf8');

      expect(template).toContain('[class.selected-row]="selectedAreaIndex() === i"');
    });

    it('shows an "Editando" indication in the editor', () => {
      const template = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.html'), 'utf8');

      expect(template).toContain('Editando:');
      expect(template).toContain('area-editor-context');
    });

    it('renders the editor with the entrance animation class and a reduced-motion fallback', () => {
      const template = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.html'), 'utf8');
      const styles = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.scss'), 'utf8');

      expect(template).toContain('area-enter');
      expect(template).toContain('track entry.index');
      expect(styles).toContain('area-editor-enter');
      expect(styles).toContain('prefers-reduced-motion');
      expect(styles).toContain('animation: none');
    });

    it('highlights the selected row with the system tone', () => {
      const styles = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.scss'), 'utf8');

      expect(styles).toContain('.neighborhood-table tbody tr.selected-row');
      expect(styles).toContain('var(--dash-primary-soft');
      expect(styles).toContain('var(--dash-primary');
    });

    it('focuses the neighborhood name input when a neighborhood is selected', async () => {
      const fixture = createComponent();
      const component = fixture.componentInstance;

      component.selectArea(0);
      fixture.detectChanges();
      await new Promise((resolve) => setTimeout(resolve, 0));

      const input = fixture.nativeElement.querySelector('#neighborhood-name') as HTMLInputElement;
      expect(input).toBeTruthy();
      expect(document.activeElement).toBe(input);
    });

    it('selects, highlights and focuses the editor when the Editar button is clicked', async () => {
      const fixture = createComponent();
      fixture.detectChanges();

      const editButton = fixture.nativeElement.querySelector('button[aria-label="Editar bairro Centro"]');
      expect(editButton).toBeTruthy();

      editButton.click();
      fixture.detectChanges();
      await new Promise((resolve) => setTimeout(resolve, 0));

      expect(fixture.componentInstance.selectedAreaIndex()).toBe(0);
      expect(fixture.componentInstance.editorEntry().length).toBe(1);
      expect(fixture.nativeElement.querySelector('tr.selected-row')).toBeTruthy();

      const input = fixture.nativeElement.querySelector('#neighborhood-name') as HTMLInputElement;
      expect(input).toBeTruthy();
      expect(document.activeElement).toBe(input);
    });

    it('moves the highlight and editor to another neighborhood when its Editar button is clicked', async () => {
      storeServiceMock.getMyStore.mockReturnValue(of({
        id: 'store-123',
        deliveryAreas: [
          { id: 'a1', neighborhood: 'Centro', deliveryFee: 5 },
          { id: 'a2', neighborhood: 'Bela Vista', deliveryFee: 8 },
        ],
      }));

      const fixture = createComponent();
      fixture.detectChanges();

      const editButtons = fixture.nativeElement.querySelectorAll('button[aria-label^="Editar bairro "]');
      expect(editButtons.length).toBe(2);

      editButtons[0].click();
      fixture.detectChanges();
      const firstSelected = fixture.componentInstance.selectedAreaIndex();
      const firstNeighborhood = fixture.componentInstance.editorEntry()[0].group.value.neighborhood;

      editButtons[1].click();
      fixture.detectChanges();

      const component = fixture.componentInstance;
      expect(component.selectedAreaIndex()).not.toBe(firstSelected);
      expect(component.editorEntry()[0].group.value.neighborhood).not.toBe(firstNeighborhood);
      expect(component.editorEntry()[0].group.value.neighborhood).toBe(component.areas.at(component.selectedAreaIndex()!).value.neighborhood);
    });

    it('clears the highlight and editor when the Cancel button is clicked', async () => {
      const fixture = createComponent();
      fixture.detectChanges();

      const editButton = fixture.nativeElement.querySelector('button[aria-label="Editar bairro Centro"]');
      editButton.click();
      fixture.detectChanges();
      expect(fixture.componentInstance.editorEntry().length).toBe(1);

      const buttons = Array.from(fixture.nativeElement.querySelectorAll('.editor-actions button')) as HTMLElement[];
      const cancelButton = buttons.find((button) => button.textContent?.trim() === 'Cancelar');
      expect(cancelButton).toBeTruthy();

      cancelButton!.click();
      fixture.detectChanges();
      await new Promise((resolve) => setTimeout(resolve, 0));

      expect(fixture.componentInstance.activeRowIndex()).toBeNull();
      expect(fixture.componentInstance.selectedAreaIndex()).toBeNull();
      expect(fixture.componentInstance.editorEntry().length).toBe(0);
      expect(fixture.nativeElement.querySelector('tr.selected-row')).toBeNull();
    });

    it('associates the editor labels to their inputs and gives the hidden checkbox a visible focus', () => {
      const template = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.html'), 'utf8');
      const styles = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.scss'), 'utf8');

      expect(template).toContain('for="delivery-fee"');
      expect(template).toContain('id="delivery-fee"');
      expect(template).toContain('for="area-active"');
      expect(template).toContain('id="area-active"');
      expect(template).toContain('for="area-notes"');
      expect(template).toContain('id="area-notes"');
      expect(styles).toContain('.area-active-row input:focus-visible + .area-switch');
    });
  });

  describe('keyboard access to neighborhood rows', () => {
    function createComponent() {
      const fixture = TestBed.createComponent(SellerNeighborhoodsPageComponent);
      fixture.detectChanges();
      return fixture;
    }

    it('makes each neighborhood row focusable and exposes selection state and an accessible name', () => {
      const fixture = createComponent();

      const row = fixture.nativeElement.querySelector('tbody tr[tabindex="0"]') as HTMLElement;
      expect(row).toBeTruthy();
      expect(row.getAttribute('aria-selected')).toBe('false');
      expect(row.getAttribute('aria-label')).toBe('Selecionar bairro Centro');
    });

    it('gives the Editar button a specific accessible name that includes the neighborhood', () => {
      const fixture = createComponent();

      expect(fixture.nativeElement.querySelector('button[aria-label="Editar bairro Centro"]')).toBeTruthy();
      expect(fixture.nativeElement.querySelector('button[aria-label="Editar bairro"]')).toBeNull();
    });

    it('selects and edits the neighborhood when Enter is pressed on the row', async () => {
      const fixture = createComponent();

      const row = fixture.nativeElement.querySelector('tbody tr[tabindex="0"]') as HTMLElement;
      row.focus();
      row.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
      fixture.detectChanges();
      await new Promise((resolve) => setTimeout(resolve, 0));

      expect(fixture.componentInstance.selectedAreaIndex()).toBe(0);
      expect(fixture.componentInstance.editorEntry().length).toBe(1);
      expect(fixture.nativeElement.querySelector('tr.selected-row')).toBeTruthy();
    });

    it('selects and edits the neighborhood when Space is pressed on the row and prevents scrolling', async () => {
      const fixture = createComponent();

      const row = fixture.nativeElement.querySelector('tbody tr[tabindex="0"]') as HTMLElement;
      const event = new KeyboardEvent('keydown', { key: ' ', bubbles: true, cancelable: true });
      row.dispatchEvent(event);
      fixture.detectChanges();
      await new Promise((resolve) => setTimeout(resolve, 0));

      expect(event.defaultPrevented).toBe(true);
      expect(fixture.componentInstance.selectedAreaIndex()).toBe(0);
      expect(fixture.componentInstance.editorEntry().length).toBe(1);
    });

    it('does not re-select the row when Enter is pressed on an action button', async () => {
      const fixture = createComponent();

      const editButton = fixture.nativeElement.querySelector('button[aria-label="Editar bairro Centro"]') as HTMLElement;
      editButton.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
      fixture.detectChanges();

      expect(fixture.componentInstance.selectedAreaIndex()).toBeNull();
    });

    it('keeps the focus on the neighborhood name input after keyboard selection', async () => {
      const fixture = createComponent();

      const row = fixture.nativeElement.querySelector('tbody tr[tabindex="0"]') as HTMLElement;
      row.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
      fixture.detectChanges();
      await new Promise((resolve) => setTimeout(resolve, 0));

      const input = fixture.nativeElement.querySelector('#neighborhood-name') as HTMLInputElement;
      expect(input).toBeTruthy();
      expect(document.activeElement).toBe(input);
    });

    it('sizes the action buttons to a 44px touch target', () => {
      const styles = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.scss'), 'utf8');

      expect(styles).toContain('.small-icon-btn { display: inline-grid; place-items: center; width: 44px; min-width: 44px; height: 44px; min-height: 44px;');
      expect(styles).toContain('.small-icon-btn:focus-visible');
      expect(styles).toContain('.neighborhood-table tbody tr:focus-visible');
    });
  });

  describe('selection modal interaction and reduced motion', () => {
    function createComponent() {
      const fixture = TestBed.createComponent(SellerNeighborhoodsPageComponent);
      fixture.detectChanges();
      return fixture;
    }

    it('keeps each neighborhood row clickable without being an interactive button or duplicating the checkbox focus control', () => {
      const template = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.html'), 'utf8');

      expect(template).toContain('class="nb-item"');
      expect(template).toContain('(click)="toggleNeighborhood(nb.id)"');
      expect(template).toContain('<label class="nb-check-wrap"');
      expect(template).toContain('(change)="toggleNeighborhood(nb.id)"');
      expect(template).not.toContain('lines="none" button');
      expect(template).not.toContain('onNeighborhoodItemKeydown');
      expect(template).not.toContain('<ion-item class="nb-item" lines="none" tabindex=');
    });

    it('stops the checkbox from bubbling so the row and checkbox do not double-toggle', () => {
      const template = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.html'), 'utf8');

      expect(template).toContain('class="nb-checkbox"');
      expect(template).toContain('<label class="nb-check-wrap" slot="start" (click)="$event.stopPropagation()"');
      expect(template).toContain('(change)="toggleNeighborhood(nb.id)"');
      expect(template).toContain('[attr.aria-label]="\'Selecionar \' + nb.neighborhood"');
    });

    it('toggles a neighborhood exactly once per call', () => {
      const component = createComponent().componentInstance;

      expect(component.checkedNeighborhoodIds().size).toBe(0);

      component.toggleNeighborhood('nb-1');
      expect(component.checkedNeighborhoodIds().has('nb-1')).toBe(true);

      component.toggleNeighborhood('nb-1');
      expect(component.checkedNeighborhoodIds().has('nb-1')).toBe(false);
    });

    it('toggles a neighborhood exactly once when the native checkbox changes', () => {
      const component = createComponent().componentInstance;

      expect(component.checkedNeighborhoodIds().has('nb-2')).toBe(false);

      component.toggleNeighborhood('nb-2');
      expect(component.checkedNeighborhoodIds().has('nb-2')).toBe(true);
      expect(component.checkedNeighborhoodIds().size).toBe(1);
    });

    it('gives persistent accessible names to close, search, free shipping and new neighborhood fields', () => {
      const template = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.html'), 'utf8');

      expect(template).toContain('aria-label="Fechar modal"');
      expect(template).toContain('aria-label="Buscar bairro"');
      expect(template).toContain('aria-label="Buscar bairro na modal"');
      expect(template).toContain('aria-label="Valor mínimo para frete grátis"');
      expect(template).toContain('aria-label="Nome do novo bairro"');
    });

    it('sizes pagination buttons to a 44px touch target while keeping the footer layout', () => {
      const styles = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.scss'), 'utf8');

      expect(styles).toContain('.pagination-controls');
      const pageButtonRule = styles.match(/\.page-button\s*\{[^}]*\}/);
      expect(pageButtonRule).toBeTruthy();
      expect(pageButtonRule?.[0]).toContain('width: 44px');
      expect(pageButtonRule?.[0]).toContain('min-width: 44px');
      expect(pageButtonRule?.[0]).toContain('height: 44px');
      expect(pageButtonRule?.[0]).toContain('min-height: 44px');
    });

    it('removes row, editor and switch transitions under prefers-reduced-motion', () => {
      const styles = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.scss'), 'utf8');

      expect(styles).toContain('prefers-reduced-motion');
      expect(styles).toContain('.neighborhood-table tbody tr { transition: none; }');
      expect(styles).toContain('.area-switch::after, .free-switch::after { transition: none; }');
      expect(styles).toContain('.area-editor.area-enter { animation: none; }');
    });
  });

  describe('grid pagination', () => {
    function createComponent() {
      const fixture = TestBed.createComponent(SellerNeighborhoodsPageComponent);
      fixture.detectChanges();
      return fixture.componentInstance;
    }

    function areaList(count: number) {
      return Array.from({ length: count }, (_, i) => ({
        id: `a${i + 1}`,
        neighborhood: `Bairro ${String(i + 1).padStart(2, '0')}`,
        deliveryFee: 5,
      }));
    }

    it('resets the grid to page 1 when the search filter changes', () => {
      storeServiceMock.getMyStore.mockReturnValue(of({ id: 'store-123', deliveryAreas: areaList(12) }));
      const component = createComponent();

      component.setNeighborhoodPage(2);
      expect(component.neighborhoodPage()).toBe(2);

      component.setAreaSearchFilter('Bairro 0');
      expect(component.neighborhoodPage()).toBe(1);
    });

    it('resets the grid to page 1 when the status filter changes', () => {
      storeServiceMock.getMyStore.mockReturnValue(of({ id: 'store-123', deliveryAreas: areaList(12) }));
      const component = createComponent();

      component.setNeighborhoodPage(2);
      expect(component.neighborhoodPage()).toBe(2);

      component.setAreaStatusFilter('active');
      expect(component.neighborhoodPage()).toBe(1);
    });

    it('binds the search and status filters through the page-resetting setters', () => {
      const template = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.html'), 'utf8');

      expect(template).toContain('(ngModelChange)="setAreaSearchFilter($event)"');
      expect(template).toContain('(ngModelChange)="setAreaStatusFilter($event)"');
    });
  });

  describe('dashboard free shipping card', () => {
    it('keeps the complete free shipping card in the dashboard template', () => {
      const template = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.html'), 'utf8');

      expect(template).toContain('Frete grátis hoje');
      expect(template).toContain('[checked]="freeShippingToday()"');
      expect(template).toContain('(change)="toggleFreeShippingToday($event)"');
      expect(template).toContain('Frete grátis a partir de');
      expect(template).toContain('formControlName="freeShippingThreshold"');
      expect(template).toContain('(blur)="formatMoneyFreeShipping()"');
    });

    it('renders the free shipping controls through the dashboard component', () => {
      const fixture = TestBed.createComponent(SellerNeighborhoodsPageComponent);
      fixture.detectChanges();

      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('Frete grátis hoje');
      expect(text).toContain('Frete grátis a partir de');
      expect(fixture.nativeElement.querySelector('[formcontrolname="freeShippingThreshold"]')).toBeTruthy();
    });

    it('turns the promotion on from the native checkbox checked state', () => {
      storeServiceMock.getMyStore.mockReturnValue(of({ id: 'store-123', deliveryAreas: [], freeShippingToday: false }));
      const fixture = TestBed.createComponent(SellerNeighborhoodsPageComponent);
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('.free-shipping-toggle input') as HTMLInputElement;
      input.checked = true;
      input.dispatchEvent(new Event('change'));
      fixture.detectChanges();

      expect(fixture.componentInstance.freeShippingToday()).toBe(true);
      expect(fixture.nativeElement.querySelector('.free-active-banner')).toBeTruthy();
    });

    it('turns the promotion off from the native checkbox checked state', () => {
      storeServiceMock.getMyStore.mockReturnValue(of({ id: 'store-123', deliveryAreas: [], freeShippingToday: true }));
      const fixture = TestBed.createComponent(SellerNeighborhoodsPageComponent);
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('.free-shipping-toggle input') as HTMLInputElement;
      input.checked = false;
      input.dispatchEvent(new Event('change'));
      fixture.detectChanges();

      expect(fixture.componentInstance.freeShippingToday()).toBe(false);
      expect(fixture.nativeElement.querySelector('.free-active-banner')).toBeNull();
    });

    it('sends the exact current boolean when saving the free shipping card', async () => {
      storeServiceMock.getMyStore.mockReturnValue(of({ id: 'store-123', deliveryAreas: [], freeShippingToday: true }));
      storeServiceMock.updateDeliveryConfig.mockReturnValue(of({} as any));
      const fixture = TestBed.createComponent(SellerNeighborhoodsPageComponent);
      fixture.detectChanges();

      await fixture.componentInstance.saveDraft();

      const [, payload] = storeServiceMock.updateDeliveryConfig.mock.calls[0] as [string, any];
      expect(payload.freeShippingToday).toBe(true);
    });
  });

  describe('delivery radius card', () => {
    it('renders the Alcance de entrega container with a numeric km input', () => {
      const template = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.html'), 'utf8');

      expect(template).toContain('Alcance de entrega');
      expect(template).toContain('type="number"');
      expect(template).toContain('min="1"');
      expect(template).toContain('step="1"');
      expect(template).toContain('radius-suffix');
      expect(template).toContain('(ngModelChange)="onDeliveryRadiusChange($event)"');
    });

    it('renders the radius card and the available neighborhood count through the dashboard component', () => {
      storeServiceMock.getMyStore.mockReturnValue(of({ id: 'store-123', maxDeliveryRadiusKm: 5, deliveryAreas: [] }));
      storeServiceMock.getStoreAddress.mockReturnValue(of({ city: 'Sao Paulo', state: 'SP' }));
      storeServiceMock.getDeliveryNeighborhoodsByStore.mockReturnValue(of([
        { id: 'nb-a', neighborhood: 'Centro', city: 'Sao Paulo' },
      ]));

      const fixture = TestBed.createComponent(SellerNeighborhoodsPageComponent);
      fixture.detectChanges();

      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('Alcance de entrega');
      expect(text).toContain('bairros disponíveis');
    });

    it('gives the radius input a 44px-plus touch target and responsive width', () => {
      const styles = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.scss'), 'utf8');

      expect(styles).toContain('.radius-input-wrap');
      expect(styles).toContain('.radius-input');
      expect(styles).toContain('min-height: 44px');
      expect(styles).toContain('minmax(0, 1fr)');
    });

    it('points aria-describedby to an element that exists in both the hint and error states', () => {
      const template = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.html'), 'utf8');

      expect(template).toContain('[attr.aria-describedby]="radiusDescribedBy()"');
      expect(template).toContain('id="delivery-radius-error"');
      expect(template).toContain('id="delivery-radius-hint"');
      expect(template).toContain('id="delivery-radius-preview-error"');
      expect(template).not.toContain('aria-describedby="delivery-radius-hint"');
    });

    it('describes the radius input with the preview-error element when a valid preview fails', () => {
      storeServiceMock.getMyStore.mockReturnValue(of({ id: 'store-123', maxDeliveryRadiusKm: 5, deliveryAreas: [] }));
      storeServiceMock.getStoreAddress.mockReturnValue(of({ city: 'Sao Paulo', state: 'SP' }));
      storeServiceMock.getDeliveryNeighborhoodsByStore.mockReturnValue(of([]));

      const fixture = TestBed.createComponent(SellerNeighborhoodsPageComponent);
      fixture.detectChanges();

      storeServiceMock.getDeliveryNeighborhoodsByStore.mockReturnValue(throwError(() => new Error('boom')));
      fixture.componentInstance.onDeliveryRadiusChange(3);
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('#delivery-radius') as HTMLInputElement;
      const describedBy = input.getAttribute('aria-describedby') ?? '';
      expect(describedBy).toContain('delivery-radius-preview-error');
      expect(describedBy).toContain('delivery-radius-hint');
      expect(fixture.nativeElement.querySelector('#delivery-radius-preview-error')).toBeTruthy();
      expect(fixture.nativeElement.querySelector('#delivery-radius-hint')).toBeTruthy();
      expect(fixture.nativeElement.querySelector('#delivery-radius-error')).toBeNull();
    });

    it('describes the radius input with the invalid-radius element that exists when the value is invalid', () => {
      storeServiceMock.getMyStore.mockReturnValue(of({ id: 'store-123', maxDeliveryRadiusKm: 5, deliveryAreas: [] }));
      storeServiceMock.getStoreAddress.mockReturnValue(of({ city: 'Sao Paulo', state: 'SP' }));
      storeServiceMock.getDeliveryNeighborhoodsByStore.mockReturnValue(of([]));

      const fixture = TestBed.createComponent(SellerNeighborhoodsPageComponent);
      fixture.detectChanges();

      fixture.componentInstance.onDeliveryRadiusChange(0);
      fixture.detectChanges();

      const input = fixture.nativeElement.querySelector('#delivery-radius') as HTMLInputElement;
      expect(input.getAttribute('aria-describedby')).toBe('delivery-radius-error');
      expect(fixture.nativeElement.querySelector('#delivery-radius-error')).toBeTruthy();
      expect(fixture.nativeElement.querySelector('#delivery-radius-hint')).toBeNull();
    });

    it('disables the dashboard save buttons while the radius preview is updating or failed', () => {
      const template = readFileSync(resolve(__dirname, 'seller-neighborhoods-page.component.html'), 'utf8');

      const disabledBindings = template.match(/\[disabled\]="isSaving\(\) \|\| isRadiusUpdating\(\) \|\| radiusPreviewError\(\) \|\| radiusValidationError\(\) \|\| !formDirty\(\)"/g) ?? [];
      expect(disabledBindings.length).toBe(2);
    });

    it('blocks saving and disables the dashboard save buttons when the radius is invalid', async () => {
      storeServiceMock.getMyStore.mockReturnValue(of({
        id: 'store-123',
        maxDeliveryRadiusKm: 5,
        deliveryAreas: [{ id: 'a1', neighborhood: 'Centro', deliveryFee: 5 }],
      }));
      storeServiceMock.getStoreAddress.mockReturnValue(of({ city: 'Sao Paulo', state: 'SP' }));
      storeServiceMock.getDeliveryNeighborhoodsByStore.mockReturnValue(of([]));
      storeServiceMock.updateDeliveryConfig.mockReturnValue(of({} as any));

      const fixture = TestBed.createComponent(SellerNeighborhoodsPageComponent);
      fixture.detectChanges();

      fixture.componentInstance.toggleFreeShippingToday({ target: { checked: true } } as unknown as Event);
      fixture.componentInstance.onDeliveryRadiusChange(0);
      fixture.detectChanges();

      expect(fixture.componentInstance.radiusValidationError()).toBe(true);
      expect(fixture.componentInstance.formDirty()).toBe(true);

      const buttons = Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];
      const saveButtons = buttons.filter(button => button.textContent?.includes('Salvar'));
      expect(saveButtons.length).toBeGreaterThan(0);
      expect(saveButtons.every(button => button.disabled)).toBe(true);

      await fixture.componentInstance.saveDraft();

      expect(storeServiceMock.updateDeliveryConfig).not.toHaveBeenCalled();
    });

    it('surfaces a distinct preview-error message when a valid radius request fails', () => {
      storeServiceMock.getMyStore.mockReturnValue(of({ id: 'store-123', maxDeliveryRadiusKm: 5, deliveryAreas: [] }));
      storeServiceMock.getStoreAddress.mockReturnValue(of({ city: 'Sao Paulo', state: 'SP' }));
      storeServiceMock.getDeliveryNeighborhoodsByStore.mockReturnValue(of([]));
      storeServiceMock.updateDeliveryConfig.mockReturnValue(of({} as any));

      const fixture = TestBed.createComponent(SellerNeighborhoodsPageComponent);
      fixture.detectChanges();

      storeServiceMock.getDeliveryNeighborhoodsByStore.mockReturnValue(throwError(() => new Error('boom')));
      fixture.componentInstance.onDeliveryRadiusChange(3);
      fixture.detectChanges();

      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(fixture.componentInstance.radiusPreviewError()).toBe(true);
      expect(fixture.componentInstance.radiusValidationError()).toBe(false);
      expect(text).toContain('Não foi possível atualizar os bairros para o novo raio. Tente novamente.');

      const buttons = Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];
      const saveButtons = buttons.filter(button => button.textContent?.includes('Salvar'));
      expect(saveButtons.length).toBeGreaterThan(0);
      expect(saveButtons.every(button => button.disabled)).toBe(true);
    });
  });
});
