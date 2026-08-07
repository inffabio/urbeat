import { Component, OnInit, inject, signal, ViewChild, ElementRef, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, FormArray, Validators, ReactiveFormsModule, FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AlertController, ToastController } from '@ionic/angular';
import { addIcons } from 'ionicons';
import L from 'leaflet';
import { copyOutline, arrowBackOutline, arrowForwardOutline, lockClosed, mapOutline, add, searchOutline, locationOutline, createOutline, checkmarkCircle } from 'ionicons/icons';
import {
  IonContent, IonHeader, IonToolbar, IonTitle, IonButtons, IonButton,
  IonIcon, IonModal,
  IonSearchbar, IonList, IonItem, IonLabel, IonProgressBar, IonNote
} from '@ionic/angular/standalone';
import { StoreService } from '../../../core/services/store.service';
import { ToastService } from '../../../core/services/toast.service';
import { StoreDeliveryArea, DeliveryNeighborhood, UpdateDeliveryConfigRequest } from '../../../shared/models/store.model';
import { WizardFooterComponent } from '../../../shared/components/wizard-footer/wizard-footer.component';
import { WizardHeaderComponent } from '../../../shared/components/wizard-header/wizard-header.component';
import { ConfigSubnavComponent } from '../../seller-shell/config-subnav.component';
import { createStepperSteps } from '../../../shared/config/wizard-steps.config';

addIcons({
  'copy-outline': copyOutline,
  'arrow-back-outline': arrowBackOutline,
  'arrow-forward-outline': arrowForwardOutline,
  'lock-closed': lockClosed,
  'map-outline': mapOutline,
  'add': add,
  'search-outline': searchOutline,
  'location-outline': locationOutline,
  'create-outline': createOutline,
  'checkmark-circle': checkmarkCircle
});

@Component({
  selector: 'app-store-delivery-page',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule, FormsModule,
    IonContent, IonHeader, IonToolbar, IonTitle, IonButtons, IonButton,
    IonIcon, IonModal,
    IonSearchbar, IonList, IonItem, IonLabel, IonNote, WizardHeaderComponent, WizardFooterComponent, ConfigSubnavComponent
  ],
  templateUrl: './store-delivery-page.component.html',
  styleUrl: './store-delivery-page.component.scss'
})
export class StoreDeliveryPageComponent implements OnInit {
  private readonly storeService = inject(StoreService);
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly alertCtrl = inject(AlertController);
  private readonly toastCtrl = inject(ToastController);
  readonly stepperSteps = createStepperSteps(2);
  readonly isDashboardView = computed(() => (this.router.url ?? '').startsWith('/app/'));
  private removedArea: { neighborhood: string; deliveryFee: string } | null = null;

  @ViewChild('inlineNeighborhood') inlineNeighborhoodInput!: ElementRef;

  readonly storeId = signal<string | null>(null);
  readonly isSaving = signal(false);
  readonly saveStatus = signal<'idle' | 'saving' | 'saved' | 'error'>('idle');
  readonly formDirty = signal(false);
  loading = signal(true);
  private existingMinimumOrder = 0;
  private existingDeliveryFee = 0;

  readonly deliveryNeighborhoods = signal<DeliveryNeighborhood[]>([]);
  readonly storeCity = signal<string>('');
  readonly storeUf = signal<string>('');
  readonly storeLat = signal<number | null>(null);
  readonly storeLon = signal<number | null>(null);
  readonly maxDeliveryRadiusKm = signal<number>(0);
  readonly isNeighborhoodModalOpen = signal(false);
  readonly isMapModalOpen = signal(false);
  private leafletMap: any = null;
  readonly newNeighborhoodName = signal('');
  readonly neighborhoodSearch = signal('');
  readonly activeRowIndex = signal<number | null>(null);
  readonly bulkFeeValue = signal('');
  readonly isAddingNeighborhood = signal(false);
  readonly freeShippingToday = signal(false);
  readonly areaSearchFilter = signal('');
  readonly areaStatusFilter = signal<'all' | 'active' | 'paused'>('all');
  readonly invalidRowIndices = signal<Set<number>>(new Set());
  private areaVersion = signal(0);

  /** Bairros marcados no modal OSM (set de ids). */
  readonly checkedNeighborhoodIds = signal<Set<string>>(new Set());

  readonly checkedCount = computed(() => this.checkedNeighborhoodIds().size);

  readonly allFilteredChecked = computed(() => {
    const filtered = this.filteredNeighborhoods();
    if (filtered.length === 0) return false;
    const ids = this.checkedNeighborhoodIds();
    return filtered.every(n => ids.has(n.id));
  });

  readonly filteredNeighborhoods = computed(() => {
    const s = this.neighborhoodSearch().toLowerCase();
    const radius = this.maxDeliveryRadiusKm();
    const lat = this.storeLat();
    const lon = this.storeLon();
    const existing = new Set(
      this.areas.controls.map(ctrl => (ctrl.value.neighborhood || '').trim().toLowerCase())
    );
    let list = this.deliveryNeighborhoods()
      .filter(n => n.neighborhood.toLowerCase().includes(s))
      .filter(n => !existing.has(n.neighborhood.toLowerCase()));
    if (radius > 0 && lat != null && lon != null) {
      list = list.filter(n => {
        if (!n.latitude || !n.longitude) return false;
        return this.calcDistanceRaw(lat, lon, n.latitude, n.longitude) <= radius;
      });
    }
    return list.sort((a, b) => a.neighborhood.localeCompare(b.neighborhood, 'pt-BR'));
  });

  readonly filteredAreaIndices = computed(() => {
    this.areaVersion();
    const filter = this.areaSearchFilter().toLowerCase();
    const entries: { index: number; name: string }[] = [];
    for (let i = 0; i < this.areas.length; i++) {
      const name = (this.areas.at(i).value.neighborhood || '').toLowerCase();
      entries.push({ index: i, name });
    }
    entries.sort((a, b) => a.name.localeCompare(b.name, 'pt-BR'));
      return entries
      .filter(e => !filter || e.name.includes(filter))
      .filter(e => {
        const active = this.areas.at(e.index).value.isActive !== false;
        return this.areaStatusFilter() === 'all'
          || (this.areaStatusFilter() === 'active' && active)
          || (this.areaStatusFilter() === 'paused' && !active);
      })
      .map(e => e.index);
  });

  form!: FormGroup;
  inlineForm!: FormGroup;

  constructor() {
    this.initForms();
    this.form.valueChanges.subscribe(() => this.formDirty.set(true));
  }

  hasUnsavedChanges(): boolean {
    return this.formDirty() && !this.isSaving();
  }

  ngOnInit() {
    this.storeService.getMyStore().subscribe({
      next: (store) => {
        this.storeId.set(store.id);
        this.maxDeliveryRadiusKm.set(store.maxDeliveryRadiusKm ?? 0);
        this.loadExistingConfig(store);
        this.loadStoreCity(store.id);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  private initForms() {
    this.form = this.fb.group({
      freeShippingThreshold: [null],
      areas: this.fb.array([])
    });

    this.inlineForm = this.fb.group({
      neighborhood: ['', Validators.required],
      deliveryFee: ['']
    });
  }

  get areas(): FormArray {
    return this.form.get('areas') as FormArray;
  }

  private loadStoreCity(storeId: string) {
    this.storeService.getStoreAddress(storeId).subscribe({
      next: (addr) => {
        this.storeCity.set(addr.city || '');
        this.storeUf.set(addr.state || '');
        this.storeLat.set(addr.latitude ?? null);
        this.storeLon.set(addr.longitude ?? null);
          if (addr.city) {
            this.loadExistingNeighborhoods(addr.city);
          }
      },
      error: () => this.loading.set(false)
    });
  }

  private loadExistingNeighborhoods(city: string) {
    const id = this.storeId();
    if (!id) return;
    this.storeService.getDeliveryNeighborhoodsByStore(id).subscribe({
      next: (list) => {
        this.deliveryNeighborhoods.set(list);
      },
      error: () => {
        this.toast.showError('Erro ao carregar bairros.');
      },
    });
  }

  private loadExistingConfig(store: any) {
    this.existingMinimumOrder = store.minimumOrderValue ?? 0;
    this.existingDeliveryFee = store.deliveryFee ?? 0;
    this.freeShippingToday.set(store.freeShippingToday ?? false);

    if (store.freeShippingThreshold !== undefined && store.freeShippingThreshold !== null) {
      this.form.patchValue({
        freeShippingThreshold: this.formatNumberToBRL(store.freeShippingThreshold)
      });
    }

    this.areas.clear();

    if (store.deliveryAreas && Array.isArray(store.deliveryAreas)) {
      store.deliveryAreas.forEach((area: StoreDeliveryArea) => {
        this.areas.push(this.fb.group({
          id: [area.id],
          neighborhood: [area.neighborhood, Validators.required],
          deliveryFee: [this.formatNumberToBRL(area.deliveryFee)],
          minimumOrderValue: [this.formatNumberToBRL(area.minimumOrderValue ?? 0)],
          freeShippingThreshold: [area.freeShippingThreshold == null ? '' : this.formatNumberToBRL(area.freeShippingThreshold)],
          isActive: [area.isActive !== false],
          notes: [area.notes ?? '']
        }));
      });
      this.sortAreas();
    }

    this.formDirty.set(false);
  }


  addInline() {
    this.formDirty.set(true);
    const neighborhood = this.inlineForm.value.neighborhood?.trim();
    let feeStr = this.inlineForm.value.deliveryFee?.trim();

    if (!neighborhood) {
      if (this.inlineNeighborhoodInput) {
        this.inlineNeighborhoodInput.nativeElement.focus();
      }
      return;
    }

    const duplicate = this.areas.controls.some(
      ctrl => ctrl.value.neighborhood?.trim().toLowerCase() === neighborhood.toLowerCase()
    );
    if (duplicate) {
      this.toast.showError('Este bairro já foi adicionado.');
      return;
    }

    if (!feeStr) {
      feeStr = '5,00';
      this.toast.showInfo('Valor padrão de R$ 5,00 aplicado. Altere se necessário.');
    } else {
      feeStr = this.formatCurrencyString(feeStr);
    }

      this.areas.push(this.fb.group({
        neighborhood: [neighborhood, Validators.required],
        deliveryFee: [feeStr],
        minimumOrderValue: ['0,00'],
        freeShippingThreshold: [''],
        isActive: [true],
        notes: ['']
    }));

    this.sortAreas();
    this.inlineForm.reset({ neighborhood: '', deliveryFee: '' });

    if (this.inlineNeighborhoodInput) {
      this.inlineNeighborhoodInput.nativeElement.focus();
    }
  }

  openNeighborhoodModal(rowIndex: number) {
    this.activeRowIndex.set(rowIndex);
    this.newNeighborhoodName.set('');
    this.neighborhoodSearch.set('');
    this.bulkFeeValue.set('');
    this.checkedNeighborhoodIds.set(new Set());
    this.isNeighborhoodModalOpen.set(true);
  }

  async closeNeighborhoodModal() {
    if (this.checkedCount() > 0) {
      const alert = await this.alertCtrl.create({
        header: 'Descartar seleções',
        message: 'Tem certeza? As seleções serão perdidas.',
        buttons: [
          { text: 'Cancelar', role: 'cancel' },
          { text: 'Descartar', role: 'destructive', handler: () => {
            this.isNeighborhoodModalOpen.set(false);
            this.activeRowIndex.set(null);
          }}
        ]
      });
      await alert.present();
      return;
    }
    this.isNeighborhoodModalOpen.set(false);
    this.activeRowIndex.set(null);
  }

  openMap() {
    this.isMapModalOpen.set(true);
  }

  initLeafletMap() {
    if (this.leafletMap) {
      this.leafletMap.remove();
      this.leafletMap = null;
    }

    const container = document.getElementById('delivery-map-container');
    if (!container) return;

    const neighborhoods = this.deliveryNeighborhoods();
    const neighborMap = new Map<string, { lat: number | null; lon: number | null }>();
    for (const nb of neighborhoods) {
      const key = nb.neighborhood.toLowerCase();
      neighborMap.set(key, { lat: nb.latitude ?? null, lon: nb.longitude ?? null });
    }

    const markers: L.Marker[] = [];

    for (const area of this.areas.controls) {
      const name = (area.value.neighborhood || '').trim();
      const feeStr = area.value.deliveryFee || '';
      if (!name) continue;

      const key = name.toLowerCase();
      const coord = neighborMap.get(key);
      if (!coord || !coord.lat || !coord.lon) continue;

      const fee = parseFloat(feeStr.replace(',', '.')) || 0;
      const tier = fee <= 5 ? 'barata' : fee <= 12 ? 'média' : 'cara';
      const color = fee <= 5 ? '#119441' : fee <= 12 ? '#6c4634' : '#D54A51';

      const icon = L.divIcon({
        className: 'custom-pin',
        html: `<div style="background:${color};width:14px;height:14px;border-radius:50%;border:2px solid white;box-shadow:0 1px 4px rgba(0,0,0,0.4)" aria-label="Entrega ${tier}"></div>`,
        iconSize: [18, 18],
        iconAnchor: [9, 9],
      });

      const marker = L.marker([coord.lat, coord.lon], { icon })
        .bindPopup(`<strong>${name}</strong><br>Taxa: R$ ${fee.toFixed(2).replace('.', ',')}`);

      markers.push(marker);
    }

    const center: [number, number] = markers.length > 0
      ? [markers[0].getLatLng().lat, markers[0].getLatLng().lng]
      : [-14.235, -51.9253];

    const map = L.map('delivery-map-container').setView(center, 12);
    this.leafletMap = map;

    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19,
    }).addTo(map);

    const radius = this.maxDeliveryRadiusKm();
    const lat = this.storeLat();
    const lon = this.storeLon();

    if (radius > 0 && lat != null && lon != null) {
      L.circle([lat, lon], {
        radius: radius * 1000,
        color: '#D54A51',
        fillColor: '#FDECEE',
        fillOpacity: 0.25,
        weight: 2
      }).addTo(map);

      L.circleMarker([lat, lon], {
        radius: 6,
        color: '#B63A41',
        fillColor: '#D54A51',
        fillOpacity: 1,
        weight: 2
      }).addTo(map).bindPopup('Sua loja');
    }

    for (const m of markers) m.addTo(map);

    if (markers.length > 0) {
      const group = L.featureGroup(markers);
      map.fitBounds(group.getBounds(), { padding: [40, 40] });
    } else if (radius > 0 && lat != null && lon != null) {
      map.setView([lat, lon], 12);
    }
  }

  closeMap() {
    if (this.leafletMap) {
      this.leafletMap.remove();
      this.leafletMap = null;
    }
    this.isMapModalOpen.set(false);
  }

  calcDistance(lat1: number, lon1: number, lat2: number, lon2: number): string {
    return this.calcDistanceRaw(lat1, lon1, lat2, lon2).toFixed(1);
  }

  getNeighborhoodDistance(neighborhoodName: string): string {
    const name = (neighborhoodName || '').trim().toLowerCase();
    const nb = this.deliveryNeighborhoods().find(n => n.neighborhood.toLowerCase() === name);
    const storeLat = this.storeLat();
    const storeLon = this.storeLon();
    if (!nb?.latitude || !nb?.longitude || storeLat == null || storeLon == null) return '—';
    return this.calcDistance(storeLat, storeLon, nb.latitude, nb.longitude) + ' km';
  }

  calcDistanceRaw(lat1: number, lon1: number, lat2: number, lon2: number): number {
    const R = 6371;
    const dLat = (lat2 - lat1) * Math.PI / 180;
    const dLon = (lon2 - lon1) * Math.PI / 180;
    const a = Math.sin(dLat / 2) * Math.sin(dLat / 2) +
      Math.cos(lat1 * Math.PI / 180) * Math.cos(lat2 * Math.PI / 180) *
      Math.sin(dLon / 2) * Math.sin(dLon / 2);
    const c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
    return R * c;
  }

  addNeighborhood() {
    const name = this.newNeighborhoodName().trim();
    if (!name) {
      this.toast.showError('O nome do bairro é obrigatório.');
      return;
    }

    if (name.length > 80) {
      this.toast.showError('O nome do bairro deve ter no máximo 80 caracteres.');
      return;
    }

    const city = this.storeCity();
    if (!city) {
      this.toast.showError('Cidade da loja não encontrada.');
      return;
    }

    const exists = this.deliveryNeighborhoods().some(
      n => n.neighborhood.toLowerCase() === name.toLowerCase()
    );
    if (exists) {
      this.toast.showError('Este bairro já existe nesta cidade.');
      return;
    }

    this.isAddingNeighborhood.set(true);
    this.storeService.createDeliveryNeighborhood(name, city).subscribe({
      next: (created) => {
        this.isAddingNeighborhood.set(false);
        this.deliveryNeighborhoods.update(list =>
          [...list, created].sort((a, b) => a.neighborhood.localeCompare(b.neighborhood, 'pt-BR'))
        );
        this.checkedNeighborhoodIds.update(set => new Set([...set, created.id]));
        this.newNeighborhoodName.set('');
      },
      error: (err) => {
        this.isAddingNeighborhood.set(false);
        if (err.status === 409) {
          this.toast.showError('Este bairro já existe nesta cidade.');
        } else {
          this.toast.showError('Erro ao criar bairro.');
        }
      },
    });
  }

  selectNeighborhood(nb: DeliveryNeighborhood) {
    const rowIndex = this.activeRowIndex();
    if (rowIndex === null) return;
    const area = this.areas.at(rowIndex);
    if (!area) return;
    area.patchValue({ neighborhood: nb.neighborhood });
    this.closeNeighborhoodModal();
  }

  toggleNeighborhood(id: string): void {
    this.checkedNeighborhoodIds.update(set => {
      const copy = new Set(set);
      if (copy.has(id)) copy.delete(id);
      else copy.add(id);
      return copy;
    });
  }

  toggleAllNeighborhoods(): void {
    const filtered = this.filteredNeighborhoods();
    const allChecked = filtered.every(n => this.checkedNeighborhoodIds().has(n.id));
    if (allChecked) {
      this.checkedNeighborhoodIds.set(new Set());
    } else {
      this.checkedNeighborhoodIds.set(new Set(filtered.map(n => n.id)));
    }
  }

  addSelectedNeighborhoods(): void {
    const selected = this.filteredNeighborhoods().filter(n => this.checkedNeighborhoodIds().has(n.id));
    const feeVal = this.parseBRL(this.bulkFeeValue());

    for (const nb of selected) {
      const feeStr = this.bulkFeeValue().trim()
        ? feeVal.toFixed(2).replace('.', ',')
        : '';
      this.areas.push(this.fb.group({
        neighborhood: [nb.neighborhood, Validators.required],
        deliveryFee: [feeStr],
        minimumOrderValue: ['0,00'],
        freeShippingThreshold: [''],
        isActive: [true],
        notes: ['']
      }));
    }

    this.checkedNeighborhoodIds.set(new Set());
    this.bulkFeeValue.set('');
    this.formDirty.set(true);
    this.sortAreas();
    this.closeNeighborhoodModal();
  }

  private sortAreas(): void {
    const sortedControls = [...this.areas.controls].sort((a, b) => {
      const nameA = (a.value.neighborhood || '').toLowerCase();
      const nameB = (b.value.neighborhood || '').toLowerCase();
      return nameA.localeCompare(nameB, 'pt-BR');
    });

    this.areas.clear();
    sortedControls.forEach(control => this.areas.push(control));
    this.areaVersion.update(v => v + 1);
  }

  onMoneyInputBulk(value: string): void {
    const digits = value.replace(/\D/g, '');
    const num = parseFloat(digits) / 100;
    if (isNaN(num)) { this.bulkFeeValue.set(''); return; }
    this.bulkFeeValue.set(num.toFixed(2).replace('.', ','));
  }

  async applyBulkFeeToAll(): Promise<void> {
    const alert = await this.alertCtrl.create({
      header: 'Aplicar valor a todos os bairros',
      message: 'Defina um valor de entrega que será aplicado a todas as áreas cadastradas.',
      inputs: [{ name: 'fee', type: 'text', placeholder: '0,00' }],
      buttons: [
        { text: 'Cancelar', role: 'cancel' },
        { text: 'Aplicar', handler: (data) => { const digits = (data.fee || '').replace(/\D/g, ''); const num = parseFloat(digits) / 100; if (!isNaN(num)) { const formatted = num.toFixed(2).replace('.', ','); for (let i = 0; i < this.areas.length; i++) { this.areas.at(i).patchValue({ deliveryFee: formatted }); } } } }
      ]
    });
    await alert.present();
  }

  toggleFreeShippingToday(): void {
    this.freeShippingToday.update(v => !v);
    this.formDirty.set(true);
  }

  async removeArea(index: number) {
    const area = this.areas.at(index);
    const name = area?.value?.neighborhood?.trim() || `Bairro #${index + 1}`;
    const fee = area?.value?.deliveryFee || '';

    this.removedArea = { neighborhood: name, deliveryFee: fee };
    this.areas.removeAt(index);
    this.formDirty.set(true);
    this.areaVersion.update(v => v + 1);

    const toast = await this.toastCtrl.create({
      message: `"${name}" removido.`,
      duration: 6000,
      position: 'top',
      cssClass: 'urbeat-toast urbeat-toast-info',
      buttons: [
        { text: 'Desfazer', handler: () => this.undoRemove() },
        { icon: 'close', role: 'cancel' },
      ],
    });
    await toast.present();
  }

  private undoRemove(): void {
    if (!this.removedArea) return;
    const { neighborhood, deliveryFee } = this.removedArea;
    this.removedArea = null;
    this.areas.push(this.fb.group({
          neighborhood: [neighborhood, Validators.required],
          deliveryFee: [deliveryFee],
          minimumOrderValue: ['0,00'],
          freeShippingThreshold: [''],
          isActive: [true],
          notes: ['']
    }));
    this.sortAreas();
    this.formDirty.set(true);
    this.toast.showInfo('Bairro restaurado.');
  }

  async clearAllAreas(): Promise<void> {
    if (this.areas.length === 0) return;

    const alert = await this.alertCtrl.create({
      header: 'Excluir todos os bairros',
      message: 'Esta ação removerá todos os bairros da lista. As alterações só serão aplicadas quando você salvar.',
      buttons: [
        { text: 'Cancelar', role: 'cancel' },
        { text: 'Excluir todos', role: 'destructive', handler: () => {
          this.formDirty.set(true);
          while (this.areas.length > 0) this.areas.removeAt(0);
          this.areaVersion.update(v => v + 1);
        }}
      ]
    });
    await alert.present();
  }

  private persistConfig(): Promise<boolean> {
    return new Promise((resolve) => {
      const id = this.storeId();
      if (!id) {
        resolve(false);
        return;
      }

      const rawFreeShipping = this.form.value.freeShippingThreshold;
      const numFreeShipping = this.parseBRLToNumber(rawFreeShipping);

      const areasDto: StoreDeliveryArea[] = this.areas.controls.map(ctrl => {
        const val = ctrl.value;
        const numFee = this.parseBRLToNumber(val.deliveryFee) || 0;
        return {
          id: val.id || undefined,
          neighborhood: val.neighborhood || '',
          deliveryFee: numFee,
          minimumOrderValue: this.parseBRLToNumber(val.minimumOrderValue) || 0,
          freeShippingThreshold: this.parseBRLToNumber(val.freeShippingThreshold),
          isActive: val.isActive !== false,
          notes: String(val.notes || '').slice(0, 100)
        };
      });

      const req: UpdateDeliveryConfigRequest = {
        deliveryFee: this.existingDeliveryFee,
        minimumOrderValue: this.existingMinimumOrder,
        freeShippingThreshold: (numFreeShipping !== null && !isNaN(numFreeShipping)) ? numFreeShipping : undefined,
        freeShippingToday: this.freeShippingToday(),
        deliveryAreas: areasDto.filter(a => a.neighborhood.trim() !== '')
      };

      this.isSaving.set(true);
      this.storeService.updateDeliveryConfig(id, req).subscribe({
        next: () => {
          this.isSaving.set(false);
          this.toast.showSuccess('Configurações de entrega salvas com sucesso!');
          resolve(true);
        },
        error: () => {
          this.isSaving.set(false);
          this.toast.showError('Não foi possível salvar as áreas de entrega.');
          resolve(false);
        }
      });
    });
  }

  formatMoneyGroup(index: number) {
    const ctrl = this.areas.at(index).get('deliveryFee');
    if (ctrl && ctrl.value) {
      ctrl.setValue(this.formatCurrencyString(ctrl.value));
    }
  }

  formatMoneyInline() {
    const ctrl = this.inlineForm.get('deliveryFee');
    if (ctrl && ctrl.value) {
      ctrl.setValue(this.formatCurrencyString(ctrl.value));
    }
  }

  formatMoneyFreeShipping() {
    const ctrl = this.form.get('freeShippingThreshold');
    if (ctrl && ctrl.value) {
      ctrl.setValue(this.formatCurrencyString(ctrl.value));
    }
  }

  maskMoneyInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    const digits = input.value.replace(/\D/g, '');
    if (!digits) {
      input.value = '';
      return;
    }
    const num = parseFloat(digits) / 100;
    input.value = num.toFixed(2).replace('.', ',');
  }

  clearRowError(index: number): void {
    if (this.invalidRowIndices().has(index)) {
      this.invalidRowIndices.update(set => {
        const next = new Set(set);
        next.delete(index);
        return next;
      });
    }
  }

  validateRowFee(index: number): void {
    const area = this.areas.at(index);
    if (!area) return;
    const fee = this.parseBRLToNumber(area.value.deliveryFee);
    if (fee !== null && fee < 0) {
      this.invalidRowIndices.update(set => new Set([...set, index]));
    } else {
      this.invalidRowIndices.update(set => {
        const next = new Set(set);
        next.delete(index);
        return next;
      });
    }
  }

  protected formatCurrencyString(val: string): string {
    if (!val) return '';
    const digits = val.replace(/\D/g, '');
    if (!digits) return '';
    const num = parseFloat(digits) / 100;
    if (isNaN(num)) return '';
    return this.formatNumberToBRL(num);
  }

  private formatNumberToBRL(num: number): string {
    return num.toFixed(2).replace('.', ',');
  }

  private parseBRLToNumber(val: string): number | null {
    if (!val) return null;
    if (typeof val === 'number') return val;
    let clean = val.toString().replace(/[^0-9,-]/g, '');
    clean = clean.replace(',', '.');
    const num = parseFloat(clean);
    return isNaN(num) ? null : num;
  }

  private parseBRL(val: string): number {
    const clean = val.replace(/[^0-9,-]/g, '').replace(',', '.');
    const num = parseFloat(clean);
    return isNaN(num) ? 0 : num;
  }

  goBack() {
    this.router.navigate(['/configurar-loja/horarios']);
  }

  async goNext() {
    if (this.areas.length === 0) {
      this.toast.showError('Adicione pelo menos um bairro para continuar.');
      return;
    }

    // Valida bairros com valor de frete negativo
    const negativeFees: string[] = [];
    const invalidSet = new Set<number>();
    for (let i = 0; i < this.areas.length; i++) {
      const area = this.areas.at(i);
      const fee = this.parseBRLToNumber(area.value.deliveryFee);
      if (fee !== null && fee < 0) {
        const name = area.value.neighborhood || '';
        negativeFees.push(name ? `${name}` : `Bairro #${i + 1}`);
        invalidSet.add(i);
      }
    }
    if (negativeFees.length > 0) {
      this.invalidRowIndices.set(invalidSet);
      this.toast.showGrouped(negativeFees.map(name => ({
        type: 'error' as const,
        text: `${name} está com valor de frete inválido.`,
      })));
      return;
    }
    this.invalidRowIndices.set(new Set());

    // Verifica frete grátis
    const threshold = this.parseBRLToNumber(this.form.value.freeShippingThreshold as string);
    if (threshold === null || threshold === 0) {
      this.toast.showWarning('Nenhum valor mínimo definido para frete grátis. Pedidos de qualquer valor terão entrega gratuita.');
    }

    const success = await this.persistConfig();
    if (success) {
      this.router.navigate(['/configurar-loja/produtos']);
    }
  }

  async saveDraft(): Promise<void> {
    this.saveStatus.set('saving');
    const success = await this.persistConfig();
    this.saveStatus.set(success ? 'saved' : 'error');
    if (success) this.formDirty.set(false);
    setTimeout(() => {
      if (this.saveStatus() === 'saved') this.saveStatus.set('idle');
    }, 2000);
  }

  cancelChanges(): void {
    const id = this.storeId();
    if (!id || this.isSaving()) return;

    this.storeService.getMyStore().subscribe({
      next: store => {
        this.loadExistingConfig(store);
        this.formDirty.set(false);
        this.activeRowIndex.set(null);
        this.saveStatus.set('idle');
        this.toast.showInfo('Alterações descartadas.');
      },
      error: () => this.toast.showError('Não foi possível restaurar a configuração.'),
    });
  }
}
