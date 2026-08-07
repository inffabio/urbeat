import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { addIcons } from 'ionicons';
import { checkmarkCircle, closeCircle, logoWhatsapp, arrowBackOutline, arrowForwardOutline, chevronUpOutline, chevronDownOutline } from 'ionicons/icons';
import { IonContent, IonIcon, IonModal, IonHeader, IonToolbar, IonTitle, IonButtons, IonButton, IonSearchbar, IonList, IonItem, IonLabel, IonInput, IonSpinner } from '@ionic/angular/standalone';
import { StoreService } from '../../core/services/store.service';
import { AddressService } from '../../core/services/address.service';
import {
  CreateStoreRequest,
  UpdateStoreAddressRequest,
  UpdateDeliveryConfigRequest,
  CuisineTypeDto,
} from '../../shared/models/store.model';

import { ToastService } from '../../core/services/toast.service';
import { createStepperSteps } from '../../shared/config/wizard-steps.config';
import { WizardHeaderComponent } from '../../shared/components/wizard-header/wizard-header.component';
import { WizardFooterComponent } from '../../shared/components/wizard-footer/wizard-footer.component';
import { ConfigSubnavComponent } from '../seller-shell/config-subnav.component';

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
  imports: [CommonModule, FormsModule, IonContent, IonIcon, IonModal, IonHeader, IonToolbar, IonTitle, IonButtons, IonButton, IonSearchbar, IonList, IonItem, IonLabel, IonInput, IonSpinner, WizardHeaderComponent, WizardFooterComponent, ConfigSubnavComponent],
  templateUrl: './store-config-page.component.html',
  styleUrl: './store-config-page.component.scss',
})
export class StoreConfigPageComponent implements OnInit {
  private readonly storeService = inject(StoreService);
  private readonly addressService = inject(AddressService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  readonly stepperSteps = createStepperSteps(0);
  readonly isDashboardView = computed(() => (this.router.url ?? '').startsWith('/app/'));
  readonly footerSaveStatus = computed(() => this.saveStatus() ?? 'idle');

  // ─── Form state ──────────────────────────────────────────
  readonly storeName = signal('');
  readonly cuisineType = signal('');
  readonly whatsapp = signal('');
  readonly storeDocument = signal('');
  readonly documentValid = signal<boolean | null>(null);
  readonly pixKey = signal('');
  readonly instagramUrl = signal('');
  readonly facebookUrl = signal('');
  readonly tikTokUrl = signal('');
  readonly websiteUrl = signal('');
  readonly description = signal('');
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

  // Section 3 – quick config
  readonly supportsDelivery = signal(true);
  readonly supportsPickup = signal(true);
  readonly initialMinute = signal<number | null>(null);
  readonly finalMinute = signal<number | null>(null);
  readonly maxDeliveryRadiusKm = signal<number>(10);
  readonly minimumOrderValue = signal('25,00');

  // Section 4 – URL
  readonly storeUrl = signal('');

  // ─── UI state ────────────────────────────────────────────
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly saveStatus = signal<'saved' | 'saving' | 'error' | null>(null);
  readonly cuisineTypes = signal<CuisineTypeDto[]>([]);
  readonly existingStoreId = signal<string | null>(null);
  readonly existingDeliveryAreas = signal<any[]>([]);
  readonly existingLogoUrl = signal<string | undefined>(undefined);
  readonly existingBannerUrl = signal<string | undefined>(undefined);
  readonly storeIsOpen = signal(false);
  private existingFreeShippingThreshold: number | undefined = undefined;

  readonly sectionsExpanded = signal({
    info: true, desc: false, visual: false, config: true, url: false,
  });

  toggleSection(key: 'info' | 'desc' | 'visual' | 'config' | 'url'): void {
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

  readonly filteredCategories = computed(() => {
    const s = this.catSearch().toLowerCase().trim();
    return this.cuisineTypes()
      .filter(c => c.name.toLowerCase().includes(s))
      .sort((a, b) => a.name.localeCompare(b.name, 'pt-BR'));
  });

  readonly sortedCuisineTypes = computed(() => {
    return [...this.cuisineTypes()].sort((a, b) => a.name.localeCompare(b.name, 'pt-BR'));
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

    const normalizedName = name
      .toLowerCase()
      .normalize('NFD')
      .replace(/[\u0300-\u036f]/g, '')
      .trim();

    const isDuplicate = this.cuisineTypes().some(c => 
      c.name.toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '').trim() === normalizedName
    );

    if (isDuplicate) {
      this.toast.showError('Já existe uma categoria com esse nome.');
      return;
    }

    this.storeService.createCuisineType(name).subscribe({
      next: (created) => {
        this.cuisineTypes.update(cats => [...cats, created]);
        this.cuisineType.set(created.name);
        this.newCatName.set('');
        this.isCatModalOpen.set(false);
        this.toast.showSuccess('Categoria adicionada com sucesso!');
      },
      error: () => {
        this.toast.showError('Não foi possível adicionar a categoria. Tente novamente.');
      },
    });
  }

  deleteCategory(cat: CuisineTypeDto) {
    if (confirm('Tem certeza que deseja apagar essa categoria?')) {
      this.cuisineTypes.update(cats => cats.filter(c => c.id !== cat.id));
      if (this.cuisineType() === cat.name) this.cuisineType.set('');
    }
  }

  selectCategory(cat: CuisineTypeDto) {
    this.cuisineType.set(cat.name);
    this.isCatModalOpen.set(false);
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
      next: (types) => this.cuisineTypes.set(types),
      error: () => this.toast.showError('Não foi possível carregar os tipos de cozinha.'),
    });

    this.storeService.getMyStore().subscribe({
      next: (store) => {
        this.populateFromExisting(store);
      },
      error: () => {},
    });
  }

  private populateFromExisting(store: import('../../shared/models/store.model').StoreResponse): void {
    this.existingStoreId.set(store.id);
    this.storeIsOpen.set(store.isOpen);
    this.existingDeliveryAreas.set(store.deliveryAreas || []);
    this.existingFreeShippingThreshold = store.freeShippingThreshold ?? undefined;
    this.storeName.set(store.name);
    this.cuisineType.set(store.cuisineType);
    this.whatsapp.set(store.phoneNumber);
    this.onWhatsappInput(store.phoneNumber);
    this.onDocumentInput(store.document ?? '');
    this.pixKey.set(store.pixKey ?? '');
    this.instagramUrl.set(store.instagramUrl ?? '');
    this.facebookUrl.set(store.facebookUrl ?? '');
    this.tikTokUrl.set(store.tikTokUrl ?? '');
    this.websiteUrl.set(store.websiteUrl ?? '');
    this.description.set(store.description ?? '');
    this.storeUrl.set(store.slug);
    this.supportsDelivery.set(store.supportsDelivery ?? true);
    this.supportsPickup.set(store.supportsPickup ?? true);
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

  // ─── File uploads (logo / banner) ───────────────────────
  private async compressImage(file: File, maxWidth: number = 1920, quality: number = 0.8): Promise<File> {
    return new Promise((resolve) => {
      const reader = new FileReader();
      reader.readAsDataURL(file);
      reader.onload = (event) => {
        const img = new Image();
        img.src = event.target?.result as string;
        img.onload = () => {
          const canvas = document.createElement('canvas');
          let width = img.width;
          let height = img.height;

          if (width > maxWidth) {
            height = Math.round((height * maxWidth) / width);
            width = maxWidth;
          }

          canvas.width = width;
          canvas.height = height;
          const ctx = canvas.getContext('2d');
          if (ctx) {
            ctx.drawImage(img, 0, 0, width, height);
          }

          canvas.toBlob(
            (blob) => {
              if (blob) {
                const compressedFile = new File([blob], file.name, {
                  type: 'image/jpeg',
                  lastModified: Date.now(),
                });
                resolve(compressedFile);
              } else {
                resolve(file); // Fallback to original if compression fails
              }
            },
            'image/jpeg',
            quality
          );
        };
      };
    });
  }

  async onLogoSelected(event: Event): Promise<void> {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;

    try {
      const compressedFile = await this.compressImage(file, 800, 0.85); // Logo: max 800px
      this.logoFile.set(compressedFile);
      const reader = new FileReader();
      reader.onload = () => this.logoPreview.set(reader.result as string);
      reader.readAsDataURL(compressedFile);
    } catch (err) {
      console.error('Error compressing logo', err);
      this.toast.showError('Erro ao processar a imagem da logo.');
    }
  }

  async onBannerSelected(event: Event): Promise<void> {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;

    try {
      const compressedFile = await this.compressImage(file, 1920, 0.8); // Banner: max 1920px
      this.bannerFile.set(compressedFile);
      const reader = new FileReader();
      reader.onload = () => this.bannerPreview.set(reader.result as string);
      reader.readAsDataURL(compressedFile);
    } catch (err) {
      console.error('Error compressing banner', err);
      this.toast.showError('Erro ao processar a imagem do banner.');
    }
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
    if (!this.cuisineType()) {
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

    this.loading.set(true);
    this.saveStatus.set('saving');

    const rawSlug = this.storeUrl().trim() || this.storeName();
    const slug = rawSlug.toLowerCase()
      .normalize('NFD').replace(/[\u0300-\u036f]/g, '')
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-|-$/g, '')
      .substring(0, 80);

    const buildReq = (logoUrl?: string, bannerUrl?: string): CreateStoreRequest => ({
      name: this.storeName().trim(),
      slug,
      phoneNumber: this.whatsapp().replace(/\D/g, ''),
      document: this.storeDocument().replace(/\D/g, '') || undefined,
      pixKey: this.pixKey().trim() || undefined,
      instagramUrl: this.instagramUrl().trim() || undefined,
      facebookUrl: this.facebookUrl().trim() || undefined,
      tikTokUrl: this.tikTokUrl().trim() || undefined,
      websiteUrl: this.websiteUrl().trim() || undefined,
      cuisineType: this.cuisineType(),
      description: this.description().trim(),
      supportsDelivery: this.supportsDelivery(),
      supportsPickup: this.supportsPickup(),
      initialMinute: this.initialMinute() ?? undefined,
      finalMinute: this.finalMinute() ?? undefined,
      maxDeliveryRadiusKm: this.maxDeliveryRadiusKm(),
      ...(logoUrl != null && { logoUrl }),
      ...(bannerUrl != null && { bannerUrl }),
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
          deliveryAreas: this.existingDeliveryAreas()
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

    const afterStoreSaved = async (storeId: string): Promise<boolean> => {
      const existingLogo = this.existingLogoUrl();
      const existingBanner = this.existingBannerUrl();

      let logoUrl: string | undefined = existingLogo;
      let bannerUrl: string | undefined = existingBanner;

      try {
        const newLogo = await uploadImageIfNew('logo', this.logoFile());
        if (newLogo) {
          logoUrl = newLogo;
          this.logoFile.set(null);
          this.existingLogoUrl.set(newLogo);
        }
        const newBanner = await uploadImageIfNew('banner', this.bannerFile());
        if (newBanner) {
          bannerUrl = newBanner;
          this.bannerFile.set(null);
          this.existingBannerUrl.set(newBanner);
        }
      } catch (err) {
        console.error('Failed to upload images after store save', err);
      }

      if (logoUrl !== existingLogo || bannerUrl !== existingBanner) {
        const patchReq = buildReq(logoUrl, bannerUrl);
        await import('rxjs').then(x => x.firstValueFrom(this.storeService.updateStore(storeId, patchReq)));
      }

      const result = await saveAddressAndConfig(storeId);
      return result;
    };

    const req = buildReq(this.existingLogoUrl(), this.existingBannerUrl());

    console.log('PAYLOAD BEING SENT TO BACKEND:', JSON.stringify(req, null, 2));

    return new Promise((resolve) => {
      if (this.existingStoreId()) {
        this.storeService.updateStore(this.existingStoreId()!, req).subscribe({
          next: (res) => {
            afterStoreSaved(res.id).then((result) => {
              this.loading.set(false);
              this.saveStatus.set(result ? 'saved' : 'error');
              if (!result) this.toast.showError('Não foi possível salvar as configurações de entrega.');
              resolve(result);
            });
          },
          error: (err) => {
            this.loading.set(false);
            this.saveStatus.set('error');
            let backendError = 'Não foi possível atualizar as informações da loja. Verifique os dados.';
            if (err.error?.error) backendError = err.error.error;
            else if (err.error?.detail) backendError = err.error.detail;
            else if (err.error?.errors) backendError = Object.values(err.error.errors).flat().join('\n');
            else if (err.error?.message) backendError = err.error.message;
            this.toast.showError(backendError);
            resolve(false);
          },
        });
      } else {
        this.storeService.createStore(req).subscribe({
          next: (res) => {
            this.existingStoreId.set(res.id);
            afterStoreSaved(res.id).then((result) => {
              this.loading.set(false);
              if (result) {
                this.saveStatus.set('saved');
                this.toast.showSuccess('Configurações salvas com sucesso!');
              } else {
                this.saveStatus.set('error');
                this.toast.showError('Não foi possível salvar as configurações de entrega.');
              }
              resolve(result);
            });
          },
          error: (err) => {
            this.loading.set(false);
            this.saveStatus.set('error');
            let backendError = 'Não foi possível criar a loja. Verifique os dados.';
            if (err.error?.error) backendError = err.error.error;
            else if (err.error?.detail) backendError = err.error.detail;
            else if (err.error?.errors) backendError = Object.values(err.error.errors).flat().join('\n');
            else if (err.error?.message) backendError = err.error.message;
            this.toast.showError(backendError);
            resolve(false);
          },
        });
      }
    });
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
