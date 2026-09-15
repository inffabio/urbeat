import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { addIcons } from 'ionicons';
import { checkmarkCircle, closeCircle, logoWhatsapp, arrowBackOutline, arrowForwardOutline, chevronUpOutline, chevronDownOutline } from 'ionicons/icons';
import { IonContent, IonIcon, IonModal, IonHeader, IonToolbar, IonTitle, IonButtons, IonButton, IonSearchbar, IonList, IonItem, IonLabel, IonInput, IonSpinner } from '@ionic/angular/standalone';
import { StoreService } from '../../core/services/store.service';
import { AddressService } from '../../core/services/address.service';
import { AuthService } from '../../core/services/auth.service';
import {
  CreateStoreRequest,
  StoreResponse,
  UpdateStoreAddressRequest,
  UpdateDeliveryConfigRequest,
  CuisineTypeDto,
} from '../../shared/models/store.model';
import { SellerProfileResponse } from '../../shared/models/auth.model';

import { ToastService } from '../../core/services/toast.service';
import { SellerShellFacade } from '../seller-shell/seller-shell.facade';
import { createStepperSteps } from '../../shared/config/wizard-steps.config';
import { WizardHeaderComponent } from '../../shared/components/wizard-header/wizard-header.component';
import { WizardFooterComponent } from '../../shared/components/wizard-footer/wizard-footer.component';
import { MediaUploadComponent } from '../../shared/components/media-upload/media-upload.component';

const STORE_URL_SLUG_PATTERN = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

// Register icons to prevent Ionic standalone warnings
addIcons({
  'checkmark-circle': checkmarkCircle,
  'close-circle': closeCircle,
  'logo-whatsapp': logoWhatsapp,
  'arrow-back-outline': arrowBackOutline,
  'arrow-forward-outline': arrowForwardOutline,
  'chevron-up-outline': chevronUpOutline,
  'chevron-down-outline': chevronDownOutline,
});

@Component({
  selector: 'app-store-config-page',
  standalone: true,
  imports: [CommonModule, FormsModule, IonContent, IonIcon, IonModal, IonHeader, IonToolbar, IonTitle, IonButtons, IonButton, IonSearchbar, IonList, IonItem, IonLabel, IonInput, IonSpinner, WizardHeaderComponent, WizardFooterComponent, MediaUploadComponent],
  templateUrl: './store-config-page.component.html',
  styleUrl: './store-config-page.component.scss',
  host: { '[class.urbeat-onboarding]': '!isDashboardView()' },
})
export class StoreConfigPageComponent implements OnInit {
  private readonly storeService = inject(StoreService);
  private readonly addressService = inject(AddressService);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly sellerShell = inject(SellerShellFacade);
  readonly stepperSteps = createStepperSteps(0);
  readonly isDashboardView = computed(() => (this.router.url ?? '').startsWith('/app/'));
  readonly footerSaveStatus = computed(() => this.saveStatus() ?? 'idle');

  // ─── Form state ──────────────────────────────────────────
  readonly storeName = signal('');
  readonly contractorName = signal('');
  readonly cuisineType = signal('');
  readonly whatsapp = signal('');
  readonly storeDocument = signal('');
  readonly documentValid = signal<boolean | null>(null);
  readonly pixKey = signal('');
  readonly websiteUrl = signal('');
  readonly street = signal('');
  readonly number = signal('');
  readonly complement = signal('');
  readonly neighborhood = signal('');
  readonly city = signal('');
  readonly state = signal('RJ');
  readonly cep = signal('');
  readonly cepLoading = signal(false);
  readonly cepValid = signal<boolean | null>(null);

  // Section 2 – media
  readonly logoFile = signal<File | null>(null);
  readonly logoPreview = signal<string | null>(null);
  readonly bannerFile = signal<File | null>(null);
  readonly bannerPreview = signal<string | null>(null);
  private logoPreviewGeneration = 0;
  private bannerPreviewGeneration = 0;
  private logoPendingSelections: { generation: number; file: File; preview?: string }[] = [];
  private bannerPendingSelections: { generation: number; file: File; preview?: string }[] = [];
  private logoCommittedState: { file: File | null; preview: string | null } = { file: null, preview: null };
  private bannerCommittedState: { file: File | null; preview: string | null } = { file: null, preview: null };

  // Section 3 – quick config
  readonly supportsDelivery = signal(true);
  readonly supportsPickup = signal(true);
  readonly initialMinute = signal<number | null>(null);
  readonly finalMinute = signal<number | null>(null);
  readonly maxDeliveryRadiusKm = signal<number>(10);
  readonly minimumOrderValue = signal('25,00');

  // Section 4 – URL
  readonly storeUrl = signal('');
  readonly storeUrlConflict = signal<string | null>(null);
  readonly storeUrlErrorVisible = signal(false);
  readonly storeUrlError = computed<string | null>(() => {
    const value = this.storeUrl();
    if (!value.trim()) return 'A URL da loja é obrigatória.';
    if (value.length < 3) return 'A URL da loja deve ter pelo menos 3 caracteres.';
    if (!STORE_URL_SLUG_PATTERN.test(value)) {
      return 'Use apenas letras minúsculas, números e hífens, sem hífens consecutivos ou nas extremidades.';
    }
    return null;
  });

  // ─── UI state ────────────────────────────────────────────
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly saveStatus = signal<'saved' | 'saving' | 'error' | null>(null);
  readonly cuisineTypes = signal<CuisineTypeDto[]>([]);
  readonly pendingCuisineTypes = signal<CuisineTypeDto[]>([]);
  readonly cuisineTypeErrorVisible = signal(false);
  readonly cuisineTypeError = computed<string | null>(() =>
    this.cuisineType().trim() ? null : 'Selecione uma categoria para a loja.',
  );
  private pendingCuisineId = 0;
  // Store-scoped category loads are asynchronous, so local creations/deletions
  // that happen after a load starts must not be overwritten by its response.
  // The generation discards responses from superseded loads (e.g. store switch)
  // and the local overrides are reconciled onto the authoritative server list.
  private cuisineTypesLoadGeneration = 0;
  private lastCuisineStoreId: string | null = null;
  private readonly localCuisineAdds = new Map<string, CuisineTypeDto>();
  private readonly localCuisineDeletes = new Set<string>();
  readonly existingStoreId = signal<string | null>(null);
  readonly existingLogoUrl = signal<string | null | undefined>(undefined);
  readonly existingBannerUrl = signal<string | null | undefined>(undefined);
  readonly storeIsOpen = signal(false);
  private existingFreeShippingThreshold: number | undefined = undefined;
  private existingFreeShippingToday: boolean | undefined = undefined;
  private storeLoaded = false;

  readonly sectionsExpanded = signal({
    info: true, visual: false, config: true, url: false,
  });

  toggleSection(key: 'info' | 'visual' | 'config' | 'url'): void {
    this.sectionsExpanded.update((s) => ({ ...s, [key]: !s[key] }));
  }

  // ─── Preview computed ────────────────────────────────────
  readonly previewStoreName = computed(() => this.storeName() || 'Nome da loja');
  readonly previewCuisine = computed(() => this.cuisineType() || 'Categoria');
  readonly previewDeliveryInfo = computed(() => {
    const methods = [];
    if (this.supportsPickup()) methods.push('Retirada');
    if (this.supportsDelivery()) {
      const deliveryPart = this.previewDeliveryTime();
      methods.push('Entrega' + (deliveryPart ? ` • ${deliveryPart}` : ''));
    }
    return methods.join(' ou ');
  });
  readonly previewDeliveryTime = computed(() => {
    const ini = this.initialMinute();
    const fim = this.finalMinute();
    if (ini != null && fim != null) return `${ini}-${fim} min`;
    if (ini != null) return `${ini} min`;
    return '';
  });
  readonly previewMinOrder = computed(() => {
    const v = this.minimumOrderValue().replace(',', '.');
    const n = parseFloat(v);
    if (isNaN(n) || n <= 0) return null;
    return `Pedido mínimo: R$ ${this.minimumOrderValue()}`;
  });
  readonly previewAddressLine = computed(() => {
    if (!this.street()) return '';
    let addr = `${this.street()}`;
    if (this.number()) addr += `, ${this.number()}`;
    if (this.neighborhood()) addr += ` - ${this.neighborhood()}`;
    return addr;
  });

  readonly stateOptions = [
    'AC', 'AL', 'AP', 'AM', 'BA', 'CE', 'DF', 'ES', 'GO',
    'MA', 'MT', 'MS', 'MG', 'PA', 'PB', 'PR', 'PE', 'PI',
    'RJ', 'RN', 'RS', 'RO', 'RR', 'SC', 'SP', 'SE', 'TO',
  ];

  // ─── Modals State ─────────────────────────────────────────
  readonly isCatModalOpen = signal(false);

  readonly catSearch = signal('');
  readonly newCatName = signal('');

  readonly allCuisineTypes = computed(() => [...this.cuisineTypes(), ...this.pendingCuisineTypes()]);

  readonly filteredCategories = computed(() => {
    const s = this.catSearch().toLowerCase().trim();
    return this.allCuisineTypes()
      .filter(c => c.name.toLowerCase().includes(s))
      .sort((a, b) => a.name.localeCompare(b.name, 'pt-BR'));
  });

  readonly sortedCuisineTypes = computed(() => {
    return [...this.allCuisineTypes()].sort((a, b) => a.name.localeCompare(b.name, 'pt-BR'));
  });

  readonly timeRangeError = computed(() => {
    const ini = this.initialMinute();
    const fim = this.finalMinute();
    if (ini != null && fim != null && ini > fim) {
      return 'O valor inicial não pode ser maior que o final.';
    }
    return null;
  });

  addCategory() {
    const name = this.newCatName().trim();
    if (!name) {
      this.toast.showError('O nome da categoria é obrigatório.');
      return;
    }

    const normalizedName = this.normalizeCategoryName(name);
    const isDuplicate = this.allCuisineTypes().some(
      c => this.normalizeCategoryName(c.name) === normalizedName,
    );

    if (isDuplicate) {
      this.toast.showError('Já existe uma categoria com esse nome.');
      return;
    }

    const storeId = this.existingStoreId();
    if (storeId) {
      this.storeService.createStoreCuisineType(storeId, name).subscribe({
        next: (created) => {
          this.localCuisineAdds.set(created.id, created);
          this.localCuisineDeletes.delete(created.id);
          this.cuisineTypes.update(cats =>
            cats.some(c => c.id === created.id) ? cats : [...cats, created],
          );
          this.finishCategoryAdd(created.name);
        },
        error: () => {
          this.toast.showError('Não foi possível adicionar a categoria. Tente novamente.');
        },
      });
      return;
    }

    const pending: CuisineTypeDto = {
      id: `pending-${++this.pendingCuisineId}`,
      name,
      isDefault: false,
      storeId: null,
    };
    this.pendingCuisineTypes.update(cats => [...cats, pending]);
    this.finishCategoryAdd(name);
  }

  private finishCategoryAdd(name: string): void {
    this.cuisineType.set(name);
    this.cuisineTypeErrorVisible.set(false);
    this.newCatName.set('');
    this.isCatModalOpen.set(false);
    this.toast.showSuccess('Categoria adicionada com sucesso!');
  }

  private normalizeCategoryName(name: string): string {
    return name
      .toLowerCase()
      .normalize('NFD')
      .replace(/[\u0300-\u036f]/g, '')
      .trim();
  }

  deleteCategory(cat: CuisineTypeDto) {
    if (cat.isDefault) {
      this.toast.showError('Categorias padrão não podem ser excluídas.');
      return;
    }

    if (!confirm('Tem certeza que deseja apagar essa categoria?')) {
      return;
    }

    const storeId = this.existingStoreId();
    if (storeId && !cat.id.startsWith('pending-')) {
      this.storeService.deleteStoreCuisineType(storeId, cat.id).subscribe({
        next: () => this.removeCategory(cat),
        error: () => this.toast.showError('Não foi possível excluir a categoria. Tente novamente.'),
      });
      return;
    }

    this.removeCategory(cat);
  }

  private removeCategory(cat: CuisineTypeDto): void {
    this.localCuisineDeletes.add(cat.id);
    this.localCuisineAdds.delete(cat.id);
    this.cuisineTypes.update(cats => cats.filter(c => c.id !== cat.id));
    this.pendingCuisineTypes.update(cats => cats.filter(c => c.id !== cat.id));
    if (this.cuisineType() === cat.name) this.cuisineType.set('');
  }

  selectCategory(cat: CuisineTypeDto) {
    this.cuisineType.set(cat.name);
    this.cuisineTypeErrorVisible.set(false);
    this.isCatModalOpen.set(false);
  }

  onCuisineTypeChange(value: string): void {
    this.cuisineType.set(value);
    this.cuisineTypeErrorVisible.set(false);
  }

  onCepBlur() {
    const cepVal = this.cep().replace(/\D/g, '');
    if (cepVal.length === 8) {
      this.cepLoading.set(true);
      this.cepValid.set(null);
      this.addressService.lookupCep(cepVal).subscribe({
        next: (data) => {
          this.street.set(data.street || '');
          this.neighborhood.set(data.neighborhood || '');
          this.city.set(data.city || '');
          this.state.set(data.state || 'RJ');
          this.cepValid.set(true);
          this.cepLoading.set(false);
        },
        error: () => {
          this.cepValid.set(false);
          this.cepLoading.set(false);
          this.toast.showWarning('CEP inexistente.');
        }
      });
    }
  }

  ngOnInit(): void {
    this.storeService.getCuisineTypes().subscribe({
      next: (types) => {
        // The store-scoped request already returns the defaults plus private
        // categories; never let this older defaults-only response overwrite it.
        if (!this.existingStoreId()) this.cuisineTypes.set(types);
      },
      error: () => this.toast.showError('Não foi possível carregar os tipos de cozinha.'),
    });

    this.authService.getSellerProfile().subscribe({
      next: (profile) => this.applySellerProfile(profile),
      error: () => {},
    });

    this.storeService.getMyStore().subscribe({
      next: (store) => {
        this.storeLoaded = true;
        this.populateFromExisting(store);
      },
      error: () => {},
    });
  }

  private applySellerProfile(profile: SellerProfileResponse): void {
    if (profile.fullName) {
      this.contractorName.set(profile.fullName);
    }
    if (!this.storeLoaded) {
      if (profile.phoneNumber) {
        this.whatsapp.set(profile.phoneNumber);
        this.onWhatsappInput(profile.phoneNumber);
      }
      if (profile.document) {
        this.onDocumentInput(profile.document);
      }
    }
  }

  private populateFromExisting(store: import('../../shared/models/store.model').StoreResponse): void {
    this.existingStoreId.set(store.id);
    this.storeIsOpen.set(store.isOpen);
    this.existingFreeShippingThreshold = store.freeShippingThreshold ?? undefined;
    this.existingFreeShippingToday = store.freeShippingToday ?? undefined;
    this.storeName.set(store.name);
    // A category created while the store was still unknown is the user's
    // explicit selection, so it must win over the store's saved category.
    const selectedPendingName = this.pendingCuisineTypes().some((c) => c.name === this.cuisineType())
      ? this.cuisineType()
      : null;
    this.cuisineType.set(selectedPendingName ?? store.cuisineType);
    this.loadStoreCuisineTypes(store.id);
    this.promotePendingCuisineTypes(store.id);
    this.whatsapp.set(store.phoneNumber);
    this.onWhatsappInput(store.phoneNumber);
    this.onDocumentInput(store.document ?? '');
    this.pixKey.set(store.pixKey ?? '');
    this.websiteUrl.set(store.websiteUrl ?? '');
    this.storeUrl.set(store.slug);
    const atendimento = this.resolveAtendimentoDefaults(store.supportsDelivery, store.supportsPickup);
    this.supportsDelivery.set(atendimento.delivery);
    this.supportsPickup.set(atendimento.pickup);
    this.initialMinute.set(store.initialMinute ?? null);
    this.finalMinute.set(store.finalMinute ?? null);
    this.maxDeliveryRadiusKm.set(store.maxDeliveryRadiusKm ?? 10);

    if (store.bannerUrl) {
      this.bannerPreview.set(store.bannerUrl);
      this.existingBannerUrl.set(store.bannerUrl);
    }
    if (store.logoUrl) {
      this.logoPreview.set(store.logoUrl);
      this.existingLogoUrl.set(store.logoUrl);
    }
    
    // Formata corretamente com 2 casas decimais (ex: 25 -> "25,00")
    if (store.minimumOrderValue !== undefined && store.minimumOrderValue !== null) {
      this.minimumOrderValue.set(Number(store.minimumOrderValue).toFixed(2).replace('.', ','));
    }

    this.storeService.getStoreAddress(store.id).subscribe({
      next: (addr) => {
        this.street.set(addr.street);
        this.number.set(addr.number);
        this.complement.set(addr.complement ?? '');
        this.neighborhood.set(addr.neighborhood);
        this.city.set(addr.city);
        this.state.set(addr.state);
        this.cep.set(addr.zipCode);
        this.onCepInput(addr.zipCode);
      },
    });
  }

  // A category added while the store was still unknown lives only locally. Once
  // getMyStore identifies an existing store it must be persisted through that
  // store's scoped endpoint, selected, and dropped from the local pending list.
  // On failure the pending entry is kept so the user can retry, with an error.
  private promotePendingCuisineTypes(storeId: string): void {
    const pending = [...this.pendingCuisineTypes()];
    for (const pendingCat of pending) {
      this.storeService.createStoreCuisineType(storeId, pendingCat.name).subscribe({
        next: (created) => {
          this.pendingCuisineTypes.update((cats) => cats.filter((c) => c.id !== pendingCat.id));
          this.localCuisineAdds.set(created.id, created);
          this.localCuisineDeletes.delete(created.id);
          this.cuisineTypes.update((cats) =>
            cats.some((c) => c.id === created.id) ? cats : [...cats, created],
          );
          if (this.cuisineType() === pendingCat.name) this.cuisineType.set(created.name);
        },
        error: () => {
          this.toast.showError('Não foi possível adicionar a categoria. Tente novamente.');
        },
      });
    }
  }

  private loadStoreCuisineTypes(storeId: string): void {
    if (this.lastCuisineStoreId !== storeId) {
      // Local overrides belong to the previous store; never leak them into
      // another store's category list.
      this.localCuisineAdds.clear();
      this.localCuisineDeletes.clear();
      this.lastCuisineStoreId = storeId;
    }

    const generation = ++this.cuisineTypesLoadGeneration;
    this.storeService.getStoreCuisineTypes(storeId).subscribe({
      next: (types) => {
        // A newer load (e.g. after a store switch) superseded this one.
        if (generation !== this.cuisineTypesLoadGeneration) return;
        this.cuisineTypes.set(this.reconcileCuisineTypes(types));
      },
      error: () => {},
    });
  }

  // The server response is authoritative, but a creation/deletion that happened
  // after the request started must survive it: drop deleted ids and re-add
  // locally created categories that the response does not know about yet.
  private reconcileCuisineTypes(types: CuisineTypeDto[]): CuisineTypeDto[] {
    const merged = types.filter((type) => !this.localCuisineDeletes.has(type.id));
    for (const added of this.localCuisineAdds.values()) {
      if (!merged.some((type) => type.id === added.id)) {
        merged.push(added);
      }
    }
    return merged;
  }

  // ─── WhatsApp mask ───────────────────────────────────────
  onWhatsappInput(value: string): void {
    const digits = value.replace(/\D/g, '').slice(0, 11);
    let formatted = digits;
    if (digits.length > 6) {
      formatted = `(${digits.slice(0, 2)}) ${digits.slice(2, 7)}-${digits.slice(7)}`;
    } else if (digits.length > 2) {
      formatted = `(${digits.slice(0, 2)}) ${digits.slice(2)}`;
    } else if (digits.length > 0) {
      formatted = `(${digits}`;
    }
    this.whatsapp.set(formatted);
  }

  onDocumentInput(value: string): void {
    const digits = value.replace(/\D/g, '').slice(0, 14);
    let formatted = digits;
    if (digits.length <= 11) {
      if (digits.length > 9) formatted = `${digits.slice(0, 3)}.${digits.slice(3, 6)}.${digits.slice(6, 9)}-${digits.slice(9)}`;
      else if (digits.length > 6) formatted = `${digits.slice(0, 3)}.${digits.slice(3, 6)}.${digits.slice(6)}`;
      else if (digits.length > 3) formatted = `${digits.slice(0, 3)}.${digits.slice(3)}`;
    } else {
      if (digits.length > 12) formatted = `${digits.slice(0, 2)}.${digits.slice(2, 5)}.${digits.slice(5, 8)}/${digits.slice(8, 12)}-${digits.slice(12)}`;
      else if (digits.length > 8) formatted = `${digits.slice(0, 2)}.${digits.slice(2, 5)}.${digits.slice(5, 8)}/${digits.slice(8)}`;
      else if (digits.length > 5) formatted = `${digits.slice(0, 2)}.${digits.slice(2, 5)}.${digits.slice(5)}`;
      else if (digits.length > 2) formatted = `${digits.slice(0, 2)}.${digits.slice(2)}`;
    }
    this.storeDocument.set(formatted);
    this.documentValid.set(null);
  }

  onDocumentBlur(): void {
    const digits = this.storeDocument().replace(/\D/g, '');
    this.documentValid.set(!digits || this.isValidDocument(digits));
  }

  onPixKeyInput(value: string): void {
    this.pixKey.set(value.slice(0, 50));
  }

  private isValidDocument(document: string): boolean {
    if (![11, 14].includes(document.length) || /^([0-9])\1+$/.test(document)) return false;
    const size = document.length === 11 ? 9 : 12;
    const firstWeights = document.length === 11 ? [10, 9, 8, 7, 6, 5, 4, 3, 2] : [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    const secondWeights = document.length === 11 ? [11, 10, 9, 8, 7, 6, 5, 4, 3, 2] : [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    const calculate = (length: number, weights: number[]) => {
      const total = document.slice(0, length).split('').reduce((sum, digit, index) => sum + Number(digit) * weights[index], 0);
      const remainder = total % 11;
      return remainder < 2 ? 0 : 11 - remainder;
    };
    const first = calculate(size, firstWeights);
    const second = calculate(size + 1, secondWeights);
    return Number(document[size]) === first && Number(document[size + 1]) === second;
  }

  // ─── CEP mask ───────────────────────────────────────────
  onCepInput(value: string): void {
    const digits = value.replace(/\D/g, '').slice(0, 8);
    const formatted = digits.length > 5 ? `${digits.slice(0, 5)}-${digits.slice(5)}` : digits;
    this.cep.set(formatted);
  }

  // ─── Store URL slug generation ──────────────────────────
  onStoreNameChange(value: string): void {
    this.storeName.set(value);
    this.storeUrlConflict.set(null);

    if (!this.storeUrl()) {
      const slug = value
        .toLowerCase()
        .normalize('NFD')
        .replace(/[\u0300-\u036f]/g, '')
        .replace(/[^a-z0-9]+/g, '-')
        .replace(/^-|-$/g, '')
        .substring(0, 80);
      this.storeUrl.set(slug);
    }
  }

  onStoreUrlInput(value: string): void {
    this.storeUrl.set(value.toLowerCase());
    this.storeUrlConflict.set(null);
    this.storeUrlErrorVisible.set(false);
  }

  onStoreUrlBlur(): void {
    this.storeUrlErrorVisible.set(true);
  }

  // ─── File uploads (logo / banner) ───────────────────────
  async onLogoSelected(file: File): Promise<void> {
    await this.applyMediaPreview('logo', file);
  }

  async onBannerSelected(file: File): Promise<void> {
    await this.applyMediaPreview('banner', file);
  }

  private async applyMediaPreview(kind: 'logo' | 'banner', file: File): Promise<void> {
    const isLogo = kind === 'logo';
    const pending = isLogo ? this.logoPendingSelections : this.bannerPendingSelections;

    // When no read is in flight the current file/preview are the last committed
    // state, so snapshot them before this selection replaces them. While another
    // read is still pending the current file is only optimistic and must not be
    // treated as the fallback for a failed replacement.
    if (pending.length === 0) {
      const committed = {
        file: isLogo ? this.logoFile() : this.bannerFile(),
        preview: isLogo ? this.logoPreview() : this.bannerPreview(),
      };
      if (isLogo) this.logoCommittedState = committed;
      else this.bannerCommittedState = committed;
    }

    // Each selection bumps a per-kind generation. A FileReader is asynchronous, so
    // when the user picks a second logo/banner before the first read completes the
    // stale read must not overwrite the latest preview, pending file or error state.
    const generation = isLogo
      ? ++this.logoPreviewGeneration
      : ++this.bannerPreviewGeneration;

    // Track every in-flight selection so a failed replacement can fall back to the
    // immediately previous pending selection instead of losing it.
    pending.push({ generation, file });

    // Expose the pending file synchronously so submitting immediately after
    // selecting an image still uploads it, even though its preview is not ready.
    if (isLogo) this.logoFile.set(file);
    else this.bannerFile.set(file);

    const preview = await this.readFilePreview(file);

    const index = pending.findIndex((selection) => selection.generation === generation);
    if (index === -1) {
      return;
    }

    if (index !== pending.length - 1) {
      // A newer selection superseded this read. A successful stale read still
      // retains its preview on the pending entry so a later failure of the newest
      // selection can restore it; a failed stale read is dropped without touching
      // state.
      if (preview !== null) {
        pending[index].preview = preview;
      } else {
        pending.splice(index, 1);
      }
      return;
    }

    pending.pop();

    if (preview === null) {
      // A failed replacement must not drop the immediately previous pending
      // selection: restore its file and, when its read already completed, its
      // preview so submit still uploads the last valid selection. When no pending
      // selection remains, fall back to the last committed state (which may be a
      // server preview) or clear the invalid selection.
      if (pending.length > 0) {
        const previous = pending[pending.length - 1];
        if (isLogo) {
          this.logoFile.set(previous.file);
          if (previous.preview) this.logoPreview.set(previous.preview);
        } else {
          this.bannerFile.set(previous.file);
          if (previous.preview) this.bannerPreview.set(previous.preview);
        }
      } else {
        const committed = isLogo ? this.logoCommittedState : this.bannerCommittedState;
        if (isLogo) {
          this.logoFile.set(committed.file);
          this.logoPreview.set(committed.preview);
        } else {
          this.bannerFile.set(committed.file);
          this.bannerPreview.set(committed.preview);
        }
      }
      this.toast.showError('Não foi possível gerar a pré-visualização da imagem. Tente novamente.');
      return;
    }

    // The latest selection succeeded: discard older in-flight selections so their
    // stale callbacks cannot overwrite this preview.
    pending.length = 0;

    if (isLogo) {
      this.logoFile.set(file);
      this.logoPreview.set(preview);
    } else {
      this.bannerFile.set(file);
      this.bannerPreview.set(preview);
    }
  }

  onLogoRemoved(): void {
    // Bump the generation before clearing so an in-flight FileReader from a prior
    // selection cannot resolve afterwards and restore the removed logo. Clear the
    // pending selections in place and drop the committed fallback too so a later
    // failed selection cannot resurrect it.
    this.logoPreviewGeneration++;
    this.logoPendingSelections.length = 0;
    this.logoFile.set(null);
    this.logoPreview.set(null);
    this.existingLogoUrl.set(null);
    this.logoCommittedState = { file: null, preview: null };
  }

  onBannerRemoved(): void {
    // Bump the generation before clearing so an in-flight FileReader from a prior
    // selection cannot resolve afterwards and restore the removed banner. Clear the
    // pending selections in place and drop the committed fallback too so a later
    // failed selection cannot resurrect it.
    this.bannerPreviewGeneration++;
    this.bannerPendingSelections.length = 0;
    this.bannerFile.set(null);
    this.bannerPreview.set(null);
    this.existingBannerUrl.set(null);
    this.bannerCommittedState = { file: null, preview: null };
  }

  // Never rejects: FileReader failures (constructor, read error, abort, empty
  // result) resolve to null so callers can surface a clear message and clear the
  // pending file instead of producing an unhandled promise rejection.
  private readFilePreview(file: File): Promise<string | null> {
    return new Promise((resolve) => {
      let reader: FileReader;
      try {
        reader = new FileReader();
      } catch {
        resolve(null);
        return;
      }

      reader.onload = () => {
        const result = reader.result;
        resolve(typeof result === 'string' && result.length > 0 ? result : null);
      };
      reader.onerror = () => resolve(null);
      reader.onabort = () => resolve(null);

      try {
        reader.readAsDataURL(file);
      } catch {
        resolve(null);
      }
    });
  }

  protected resolveAtendimentoDefaults(
    delivery: boolean | undefined,
    pickup: boolean | undefined,
  ): { delivery: boolean; pickup: boolean } {
    return { delivery: delivery ?? true, pickup: pickup ?? true };
  }

  // ─── Toggle atendimento ─────────────────────────────────
  toggleDelivery(): void {
    if (this.supportsDelivery() && !this.supportsPickup()) return;
    this.supportsDelivery.update(v => !v);
  }
  togglePickup(): void {
    if (this.supportsPickup() && !this.supportsDelivery()) return;
    this.supportsPickup.update(v => !v);
  }

  // ─── Money mask ─────────────────────────────────────────
  onMoneyInput(value: string): void {
    const digits = value.replace(/\D/g, '');
    const num = parseFloat(digits) / 100;
    if (isNaN(num)) {
      this.minimumOrderValue.set('');
      return;
    }
    this.minimumOrderValue.set(num.toFixed(2).replace('.', ','));
  }

  // ─── Submit ─────────────────────────────────────────────
  async submit(): Promise<boolean> {
    // 1. Basic Validation
    if (!this.storeName().trim()) {
      this.toast.showError('Por favor, informe o nome da loja.');
      return false;
    }
    if (this.storeName().trim().length > 100) {
      this.toast.showError('O nome da loja deve ter no máximo 100 caracteres.');
      return false;
    }
    if (!this.contractorName().trim()) {
      this.toast.showError('Por favor, informe o nome do contratante/lojista.');
      return false;
    }
    if (!this.cuisineType().trim()) {
      this.cuisineTypeErrorVisible.set(true);
      this.toast.showError('Por favor, selecione uma categoria para a loja.');
      return false;
    }
    if (!this.whatsapp().trim() || this.whatsapp().replace(/\D/g, '').length < 10) {
      this.toast.showError('Por favor, informe um WhatsApp válido.');
      return false;
    }
    if (this.storeDocument().trim()) this.onDocumentBlur();
    if (this.documentValid() === false) {
      this.toast.showError('Informe um CNPJ/CPF válido.');
      return false;
    }
    if (!this.supportsDelivery() && !this.supportsPickup()) {
      this.toast.showError('Selecione ao menos um tipo de atendimento: Delivery ou Retirada.');
      return false;
    }
    if (!this.cep().trim() || !this.street().trim() || !this.number().trim() || !this.neighborhood().trim() || !this.city().trim() || !this.state().trim()) {
      this.toast.showError('Por favor, preencha todos os campos de endereço corretamente.');
      return false;
    }
    if (this.cepValid() === false) {
      this.toast.showError('CEP inexistente. Corrija o CEP para continuar.');
      return false;
    }
    if (this.initialMinute() == null || this.finalMinute() == null) {
      this.toast.showError('Preencha o tempo médio de entrega (início e fim).');
      return false;
    }
    if (this.initialMinute()! > this.finalMinute()!) {
      this.toast.showError('O tempo inicial não pode ser maior que o tempo final.');
      return false;
    }
    if (!this.maxDeliveryRadiusKm() || this.maxDeliveryRadiusKm() <= 0) {
      this.toast.showError('Informe o raio máximo de entrega em km.');
      return false;
    }
    const storeUrlValidationError = this.storeUrlError();
    if (storeUrlValidationError) {
      this.storeUrlErrorVisible.set(true);
      this.sectionsExpanded.update((s) => ({ ...s, url: true }));
      this.toast.showError(storeUrlValidationError);
      return false;
    }

    this.loading.set(true);
    this.saveStatus.set('saving');

    const slug = this.storeUrl().toLowerCase();

    const buildReq = (logoUrl?: string | null, bannerUrl?: string | null): CreateStoreRequest => ({
      name: this.storeName().trim(),
      slug,
      phoneNumber: this.whatsapp().replace(/\D/g, ''),
      document: this.storeDocument().replace(/\D/g, '') || undefined,
      pixKey: this.pixKey().trim() || undefined,
      websiteUrl: this.websiteUrl().trim() || undefined,
      cuisineType: this.cuisineType(),
      supportsDelivery: this.supportsDelivery(),
      supportsPickup: this.supportsPickup(),
      initialMinute: this.initialMinute() ?? undefined,
      finalMinute: this.finalMinute() ?? undefined,
      maxDeliveryRadiusKm: this.maxDeliveryRadiusKm(),
      ...(logoUrl !== undefined && { logoUrl }),
      ...(bannerUrl !== undefined && { bannerUrl }),
    });

    const saveAddressAndConfig = (storeId: string): Promise<boolean> => {
      return new Promise((resolve) => {
        const addrReq: UpdateStoreAddressRequest = {
          street: this.street(),
          number: this.number(),
          complement: this.complement() || undefined,
          neighborhood: this.neighborhood(),
          city: this.city(),
          state: this.state(),
          zipCode: this.cep().replace(/\D/g, ''),
        };

        const deliveryReq: UpdateDeliveryConfigRequest = {
          deliveryFee: 0,
          minimumOrderValue: parseFloat(this.minimumOrderValue().replace(',', '.')) || 0,
          freeShippingThreshold: this.existingFreeShippingThreshold,
          freeShippingToday: this.existingFreeShippingToday,
        };

        this.storeService.upsertStoreAddress(storeId, addrReq).subscribe({
          next: () => {
            this.storeService.updateDeliveryConfig(storeId, deliveryReq).subscribe({
              next: () => resolve(true),
              error: () => resolve(false),
            });
          },
          error: () => resolve(false),
        });
      });
    };

    const uploadImageIfNew = async (type: string, file: File | null): Promise<string | undefined> => {
      if (!file) return undefined;
      const res = await import('rxjs').then(x => x.firstValueFrom(this.storeService.uploadImage(file, type)));
      return res.url;
    };

    const afterStoreSaved = async (savedStore: StoreResponse): Promise<StoreResponse | null> => {
      const storeId = savedStore.id;
      const existingLogo = this.existingLogoUrl();
      const existingBanner = this.existingBannerUrl();

      let logoUrl: string | null | undefined = existingLogo;
      let bannerUrl: string | null | undefined = existingBanner;
      let finalStore = savedStore;

      const uploadFailureMessage = (err: unknown): string => {
        const backendMessage = (err as { error?: { error?: string } } | null)?.error?.error;
        return typeof backendMessage === 'string' && backendMessage.trim()
          ? backendMessage
          : 'Não foi possível enviar a imagem. Tente novamente.';
      };
      const handleUploadError = (err: unknown): null => {
        console.error('Failed to upload image after store save', err);
        this.loading.set(false);
        this.saveStatus.set('error');
        // For HTTP 413 the global interceptor already surfaces the upload
        // infrastructure/size message, so showing it here would duplicate it.
        if ((err as { status?: number } | null)?.status !== 413) {
          this.toast.showError(uploadFailureMessage(err));
        }
        return null;
      };

      if (this.logoFile()) {
        try {
          const newLogo = await uploadImageIfNew('logo', this.logoFile());
          if (newLogo) {
            logoUrl = newLogo;
            this.logoFile.set(null);
            this.existingLogoUrl.set(newLogo);
          }
        } catch (err) {
          return handleUploadError(err);
        }
      }

      if (this.bannerFile()) {
        try {
          const newBanner = await uploadImageIfNew('banner', this.bannerFile());
          if (newBanner) {
            bannerUrl = newBanner;
            this.bannerFile.set(null);
            this.existingBannerUrl.set(newBanner);
          }
        } catch (err) {
          return handleUploadError(err);
        }
      }

      if (logoUrl !== existingLogo || bannerUrl !== existingBanner) {
        const patchReq = buildReq(logoUrl, bannerUrl);
        try {
          finalStore = await import('rxjs').then(x => x.firstValueFrom(this.storeService.updateStore(storeId, patchReq)));
        } catch (err) {
          this.handleSubmitError(
            err as { status?: number; error?: { error?: unknown; detail?: unknown; errors?: unknown; message?: unknown } },
            'Não foi possível atualizar as informações da loja. Verifique os dados.',
          );
          return null;
        }
      }

      const result = await saveAddressAndConfig(storeId);
      if (!result) {
        this.toast.showError('Não foi possível salvar as configurações de entrega.');
        return null;
      }

      return {
        ...finalStore,
        name: this.storeName().trim(),
        logoUrl: logoUrl ?? undefined,
        bannerUrl: bannerUrl ?? undefined,
      };
    };

    const req = buildReq(this.existingLogoUrl(), this.existingBannerUrl());

    const finishSave = async (savedStore: StoreResponse, isNewStore: boolean): Promise<boolean> => {
      const finalStore = await afterStoreSaved(savedStore);
      if (!finalStore) {
        this.loading.set(false);
        this.saveStatus.set('error');
        return false;
      }

      const profileSaved = await this.saveContractorName();
      this.loading.set(false);
      this.saveStatus.set(profileSaved ? 'saved' : 'error');
      if (profileSaved) {
        this.sellerShell.mergeStore(finalStore);
      }
      if (profileSaved && isNewStore) {
        this.toast.showSuccess('Configurações salvas com sucesso!');
      }
      return profileSaved;
    };

    return new Promise((resolve) => {
      if (this.existingStoreId()) {
        this.storeService.updateStore(this.existingStoreId()!, req).subscribe({
          next: (res) => {
            finishSave(res, false).then(resolve);
          },
          error: (err) => {
            this.handleSubmitError(err, 'Não foi possível atualizar as informações da loja. Verifique os dados.');
            resolve(false);
          },
        });
      } else {
        this.storeService.createStore(req).subscribe({
          next: (res) => {
            this.existingStoreId.set(res.id);
            finishSave(res, true).then(resolve);
          },
          error: (err) => {
            this.handleSubmitError(err, 'Não foi possível criar a loja. Verifique os dados.');
            resolve(false);
          },
        });
      }
    });
  }

  private async saveContractorName(): Promise<boolean> {
    try {
      await import('rxjs').then(x => x.firstValueFrom(
        this.authService.updateSellerProfile({ fullName: this.contractorName().trim() }),
      ));
      return true;
    } catch (err) {
      console.error('Failed to update seller profile', err);
      this.toast.showError('Não foi possível salvar o nome do contratante/lojista.');
      return false;
    }
  }

  private handleSubmitError(
    err: { status?: number; error?: { error?: unknown; detail?: unknown; errors?: unknown; message?: unknown } },
    fallbackMessage: string,
  ): void {
    this.loading.set(false);
    this.saveStatus.set('error');

    let backendError = fallbackMessage;
    const errorBody = err?.error;
    if (errorBody?.error) backendError = String(errorBody.error);
    else if (errorBody?.detail) backendError = String(errorBody.detail);
    else if (errorBody?.errors) backendError = Object.values(errorBody.errors as object).flat().join('\n');
    else if (errorBody?.message) backendError = String(errorBody.message);

    if (err?.status === 409 && backendError.includes('URL')) {
      this.storeUrlConflict.set(backendError);
      this.sectionsExpanded.update((s) => ({ ...s, url: true }));
    }

    this.toast.showError(backendError);
  }

  async goNext(): Promise<void> {
    const success = await this.submit();
    if (success) {
      this.router.navigate(['/configurar-loja/horarios']);
    }
  }

  async saveDraft(): Promise<void> {
    await this.submit();
  }

  onBack(): void {
    this.router.navigate(['/']);
  }

  onImageError(event: Event): void {
    (event.target as HTMLImageElement).style.display = 'none';
  }
}
