import { Component, OnDestroy, OnInit, ViewChild, inject, signal, computed } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { ActivatedRoute, Router } from '@angular/router';
import { IonContent, IonIcon } from '@ionic/angular/standalone';
import { BrlCurrencyPipe } from '../../shared/pipes/brl-currency.pipe';

import { CartService } from '../../core/services/cart.service';
import { CheckoutService } from '../../core/services/checkout.service';
import { AddressService } from '../../core/services/address.service';
import { StoreService } from '../../core/services/store.service';
import { ApiService } from '../../core/services/api.service';
import { SignalRService } from '../../core/services/signalr.service';
import { ToastService, ToastLine } from '../../core/services/toast.service';
import { AuthService } from '../../core/services/auth.service';
import { CustomerAddress } from '../../shared/models/address.model';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { getStorePathFromUrl } from '../../shared/utils/router.utils';
import { StickyActionBarComponent } from '../../shared/components/sticky-action-bar/sticky-action-bar.component';
import { DeliveryCoverageModalComponent } from '../../shared/components/delivery-coverage-modal/delivery-coverage-modal.component';

type CustomerField = 'fullName' | 'phone' | 'email' | 'cep' | 'city' | 'state' | 'neighborhood' | 'street' | 'number';

@Component({
  selector: 'app-customer-page',
  standalone: true,
  imports: [CommonModule, FormsModule, IonContent, IonIcon, BrlCurrencyPipe, StickyActionBarComponent, DeliveryCoverageModalComponent],
  templateUrl: './customer-page.component.html',
  styleUrl: './customer-page.component.scss',
})
export class CustomerPageComponent implements OnInit, OnDestroy {
  readonly cart = inject(CartService);
  readonly checkout = inject(CheckoutService);
  private readonly address = inject(AddressService);
  private readonly storeService = inject(StoreService);
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly location = inject(Location);
  private readonly toast = inject(ToastService);
  private readonly signalR = inject(SignalRService);
  private readonly auth = inject(AuthService);

  private deliveryAreaUpdatedCallback?: (data: { storeId: string }) => void;
  private customerHubGeneration = 0;
  private active = true;
  private joinedStoreId: string | null = null;
  private cepLookupGeneration = 0;
  private deliveryCheckGeneration = 0;
  private readonly subscriptions = new Subscription();
  readonly FulfillmentType = FulfillmentType;

  @ViewChild(IonContent) private content?: IonContent;

  // form
  readonly fullName = signal('');
  readonly phone = signal('');
  readonly email = signal('');
  readonly cep = signal('');
  readonly street = signal('');
  readonly number = signal('');
  readonly complement = signal('');
  readonly neighborhood = signal('');
  readonly city = signal('');
  readonly state = signal('');

  readonly cepLoading = signal(false);
  readonly cepError = signal(false);
  readonly cepValidated = signal(false);
  readonly authError = signal('');
  readonly deliveryNotCovered = signal(false);
  readonly deliveryCheckLoading = signal(false);
  readonly deliveryCoverageModalOpen = signal(false);
  readonly isAccountEdit = signal(false);
  readonly accountLoading = signal(false);
  readonly accountSaving = signal(false);
  readonly accountProfileDirty = signal(false);
  readonly accountAddressDirty = signal(false);
  private readonly accountPrimaryAddressId = signal<string | null>(null);
  private readonly accountProfileBaseline = signal('');
  private readonly accountAddressBaseline = signal('');
  readonly storeAddress = signal<{ city?: string; state?: string; zipCode?: string; neighborhood?: string } | null>(null);
  readonly attemptedSubmit = signal(false);
  readonly touchedFields = signal<Record<CustomerField, boolean>>({
    fullName: false,
    phone: false,
    email: false,
    cep: false,
    city: false,
    state: false,
    neighborhood: false,
    street: false,
    number: false,
  });

  readonly canContinue = computed(() => {
    return (
      this.fullName().trim().length >= 3 &&
      this.phone().replace(/\D/g, '').length >= 10 &&
      this.isValidEmail(this.email()) &&
      this.cep().replace(/\D/g, '').length === 8 &&
      this.street().trim().length > 0 &&
      this.number().trim().length > 0 &&
      this.city().trim().length > 0 &&
      this.neighborhood().trim().length > 0 &&
      this.state().trim().length === 2 &&
      !this.cepLoading() &&
      !this.deliveryCheckLoading() &&
      !this.deliveryNotCovered()
    );
  });

  readonly canSaveProfile = computed(() =>
    this.fullName().trim().length >= 3 &&
    this.phone().replace(/\D/g, '').length >= 10 &&
    this.isValidEmail(this.email())
  );

  readonly canSaveAddress = computed(() =>
    this.cep().replace(/\D/g, '').length === 8 &&
    this.street().trim().length > 0 &&
    this.number().trim().length > 0 &&
    this.city().trim().length > 0 &&
    this.neighborhood().trim().length > 0 &&
    this.state().trim().length === 2
  );

  readonly canSaveAccount = computed(() => {
    if (this.accountSaving() || this.accountLoading()) return false;
    const profileDirty = this.accountProfileDirty();
    const addressDirty = this.accountAddressDirty();
    if (!profileDirty && !addressDirty) return false;
    if (profileDirty && !this.canSaveProfile()) return false;
    if (addressDirty && !this.canSaveAddress()) return false;
    return true;
  });

  private isValidEmail(value: string): boolean {
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value.trim());
  }

  markTouched(field: CustomerField): void {
    this.touchedFields.update((fields) => ({ ...fields, [field]: true }));
  }

  showFieldError(field: CustomerField): boolean {
    if (field === 'cep' && this.cepError()) return true;
    return (this.attemptedSubmit() || this.touchedFields()[field]) && this.fieldError(field).length > 0;
  }

  fieldError(field: CustomerField): string {
    switch (field) {
      case 'fullName':
        return this.fullName().trim().length >= 3 ? '' : 'Informe seu nome completo.';
      case 'phone':
        return this.phone().replace(/\D/g, '').length >= 10 ? '' : 'Informe um telefone com DDD.';
      case 'email':
        return this.isValidEmail(this.email()) ? '' : 'Informe um e-mail válido.';
      case 'cep':
        if (this.cepError()) return 'CEP não encontrado. Preencha o endereço manualmente.';
        return this.cep().replace(/\D/g, '').length === 8 ? '' : 'Informe um CEP válido com 8 dígitos.';
      case 'city':
        return this.city().trim().length > 0 ? '' : 'Informe a cidade.';
      case 'state':
        return this.state().trim().length === 2 ? '' : 'Informe a UF com 2 letras.';
      case 'neighborhood':
        return this.neighborhood().trim().length > 0 ? '' : 'Informe o bairro.';
      case 'street':
        return this.street().trim().length > 0 ? '' : 'Informe a rua.';
      case 'number':
        return this.number().trim().length > 0 ? '' : 'Informe o número.';
    }
  }

  /** Reúne TODOS os problemas do formulário para exibir numa única mensagem. */
  private validate(): ToastLine[] {
    const problems: ToastLine[] = [];

    if (this.fullName().trim().length < 3) {
      problems.push({ type: 'error', text: 'Informe seu nome completo.' });
    }
    if (this.phone().replace(/\D/g, '').length < 10) {
      problems.push({ type: 'error', text: 'Informe um telefone com DDD.' });
    }
    if (!this.isValidEmail(this.email())) {
      problems.push({ type: 'error', text: 'Informe um e-mail válido.' });
    }

    if (this.cep().replace(/\D/g, '').length !== 8) {
      problems.push({ type: 'error', text: 'Informe um CEP válido (8 dígitos).' });
    } else if (this.cepError()) {
      problems.push({ type: 'warning', text: 'CEP não localizado: confira cidade, bairro e rua.' });
    }

    if (this.city().trim().length === 0) {
      problems.push({ type: 'error', text: 'Informe a cidade.' });
    }
    if (this.state().trim().length === 0) {
      problems.push({ type: 'error', text: 'Informe o estado (UF).' });
    }
    if (this.neighborhood().trim().length === 0) {
      problems.push({ type: 'error', text: 'Informe o bairro.' });
    }
    if (this.street().trim().length === 0) {
      problems.push({ type: 'error', text: 'Informe a rua.' });
    }
    if (this.number().trim().length === 0) {
      problems.push({ type: 'error', text: 'Informe o número.' });
    }

    return problems;
  }

  ngOnInit(): void {
    this.isAccountEdit.set(this.route.snapshot.routeConfig?.path === 'conta/cadastro');
    if (this.isAccountEdit()) {
      this.loadAccountData();
      return;
    }

    this.loadStoreAddress();
    // restaura se voltou
    const info = this.checkout.customerInfo();
    if (info) {
      this.fullName.set(info.fullName);
      this.phone.set(info.phoneNumber);
      this.email.set(info.email);
    }
    const addr = this.checkout.customerAddress();
    if (addr) {
      this.cep.set(addr.cep);
      this.street.set(addr.street);
      this.number.set(addr.number);
      this.complement.set(addr.complement ?? '');
      this.neighborhood.set(addr.neighborhood);
      this.city.set(addr.city);
      this.state.set(addr.state);
    }
  }

  private loadAccountData(): void {
    this.accountLoading.set(true);
    const profile = this.auth.customerProfile();
    const profileRequest = profile ? undefined : this.auth.restoreCustomerSession();

    if (profileRequest) {
      this.subscriptions.add(profileRequest.subscribe({
        next: (loadedProfile) => this.loadAccountAddress(loadedProfile),
        error: () => this.redirectFromAccount(),
      }));
      return;
    }

    this.loadAccountAddress(profile!);
  }

  private loadAccountAddress(profile: NonNullable<ReturnType<AuthService['customerProfile']>>): void {
    this.fullName.set(profile.fullName ?? '');
    this.email.set(profile.email ?? '');
    this.phone.set(profile.phoneNumber ?? '');
    this.subscriptions.add(this.address.list().subscribe({
      next: (addresses) => {
        const primary = addresses.find((address) => address.isPrimary) ?? addresses[0];
        if (primary) this.applyAddress(primary);
        this.accountPrimaryAddressId.set(primary?.id ?? null);
        this.accountProfileBaseline.set(this.accountProfileSnapshot());
        this.accountAddressBaseline.set(this.accountAddressSnapshot());
        this.accountLoading.set(false);
        this.scrollToTopAfterRender();
      },
      error: () => this.redirectFromAccount(),
    }));
  }

  private scrollToTopAfterRender(): void {
    setTimeout(() => this.content?.scrollToTop(300), 0);
  }

  private applyAddress(address: CustomerAddress): void {
    this.cep.set(address.cep);
    this.street.set(address.street);
    this.number.set(address.number);
    this.complement.set(address.complement ?? '');
    this.neighborhood.set(address.neighborhood);
    this.city.set(address.city);
    this.state.set(address.state);
    this.cepValidated.set(true);
  }

  private accountProfileSnapshot(): string {
    return JSON.stringify({
      fullName: this.fullName().trim(),
      email: this.email().trim().toLowerCase(),
      phone: this.phone().replace(/\D/g, ''),
    });
  }

  private accountAddressSnapshot(): string {
    return JSON.stringify({
      cep: this.cep().replace(/\D/g, ''),
      street: this.street().trim(),
      number: this.number().trim(),
      complement: this.complement().trim(),
      neighborhood: this.neighborhood().trim(),
      city: this.city().trim(),
      state: this.state().trim().toUpperCase(),
    });
  }

  updateAccountDirty(): void {
    if (this.isAccountEdit() && !this.accountLoading()) {
      this.accountProfileDirty.set(this.accountProfileSnapshot() !== this.accountProfileBaseline());
      this.accountAddressDirty.set(this.accountAddressSnapshot() !== this.accountAddressBaseline());
    }
  }

  private saveAccount(): void {
    this.attemptedSubmit.set(true);
    if (!this.canSaveAccount()) return;

    this.accountSaving.set(true);
    const saveProfile = this.accountProfileDirty();
    const saveAddress = this.accountAddressDirty();

    const syncProfileBaseline = (): void => {
      this.accountProfileBaseline.set(this.accountProfileSnapshot());
      this.accountProfileDirty.set(false);
    };

    const syncAddressBaseline = (): void => {
      this.accountAddressBaseline.set(this.accountAddressSnapshot());
      this.accountAddressDirty.set(false);
    };

    const saveAddressRequest = (): void => {
      if (!saveAddress) {
        this.accountSaving.set(false);
        this.toast.showSuccess('Cadastro atualizado com sucesso.');
        return;
      }

      const addressPayload = {
        cep: this.cep().replace(/\D/g, ''),
        street: this.street().trim(),
        number: this.number().trim(),
        complement: this.complement().trim() || undefined,
        neighborhood: this.neighborhood().trim(),
        city: this.city().trim(),
        state: this.state().trim().toUpperCase(),
        isPrimary: true,
      };

      const addressId = this.accountPrimaryAddressId();
      const addressRequest = addressId
        ? this.address.update(addressId, addressPayload)
        : this.address.create(addressPayload);
      this.subscriptions.add(addressRequest.subscribe({
        next: (address) => {
          this.accountPrimaryAddressId.set(address.id);
          syncAddressBaseline();
          this.accountSaving.set(false);
          this.toast.showSuccess('Cadastro atualizado com sucesso.');
        },
        error: () => {
          // A profile-first failure keeps the address dirty and editable. When the profile was
          // already persisted before the address call failed, report the partial update honestly
          // instead of pretending a rollback.
          this.accountSaving.set(false);
          if (saveProfile) {
            this.toast.showWarning('Seus dados foram salvos, mas não foi possível salvar o endereço. Revise e tente novamente.');
          } else {
            this.toast.showError('Não foi possível salvar seu endereço. Tente novamente.');
          }
        },
      }));
    };

    if (saveProfile) {
      this.subscriptions.add(this.auth.updateCustomerProfile({
        fullName: this.fullName().trim(),
        email: this.email().trim(),
        phoneNumber: this.phone().replace(/\D/g, ''),
      }).subscribe({
        next: () => {
          syncProfileBaseline();
          saveAddressRequest();
        },
        error: () => {
          this.accountSaving.set(false);
          this.toast.showError('Não foi possível salvar seus dados. Tente novamente.');
        },
      }));
      return;
    }

    saveAddressRequest();
  }

  private redirectFromAccount(): void {
    this.accountLoading.set(false);
    const storePath = getStorePathFromUrl(this.router);
    this.router.navigate(storePath ? ['/', storePath] : ['/']);
  }

  private loadStoreAddress(): void {
    const storeId = this.cart.storeId();
    if (!storeId) return;

    this.subscriptions.add(this.storeService.getStoreById(storeId).subscribe({
      next: (store) => this.storeAddress.set(store.address ?? null),
      error: () => this.storeAddress.set(null),
    }));
  }

  onPhoneInput(v: string): void {
    const digits = v.replace(/\D/g, '').slice(0, 11);
    let formatted = digits;
    if (digits.length > 2) formatted = `(${digits.slice(0, 2)}) ${digits.slice(2)}`;
    if (digits.length > 6) formatted = `(${digits.slice(0, 2)}) ${digits.slice(2, 7)}-${digits.slice(7)}`;
    this.phone.set(formatted);
    this.updateAccountDirty();
  }

  onCepInput(v: string): void {
    const digits = v.replace(/\D/g, '').slice(0, 8);
    const formatted = digits.length > 5 ? `${digits.slice(0, 5)}-${digits.slice(5)}` : digits;
    this.cep.set(formatted);
    this.cepError.set(false);
    this.cepValidated.set(false);
    this.updateAccountDirty();
    this.invalidateDeliveryCoverage();
    this.cepLookupGeneration++;
    if (digits.length === 8) {
      this.lookupCep(digits);
    } else {
      this.cepLoading.set(false);
    }
  }

  onAddressFieldChange(field: 'cep' | 'city' | 'state' | 'neighborhood' | 'street' | 'number', value: string): void {
    if (field === 'cep') {
      this.onCepInput(value);
      return;
    }
    switch (field) {
      case 'city':
        this.city.set(value);
        break;
      case 'state':
        this.state.set(value.toUpperCase());
        break;
      case 'neighborhood':
        this.neighborhood.set(value);
        break;
      case 'street':
        this.street.set(value);
        break;
      case 'number':
        this.number.set(value);
        break;
    }
    this.updateAccountDirty();
    this.invalidateDeliveryCoverage();
  }

  private invalidateDeliveryCoverage(): void {
    this.deliveryCheckGeneration++;
    this.deliveryCheckLoading.set(false);
    this.deliveryNotCovered.set(false);
  }

  private lookupCep(cep: string): void {
    const generation = this.cepLookupGeneration;
    this.cepLoading.set(true);
    this.subscriptions.add(this.address.lookupCep(cep).subscribe({
      next: (res) => {
        if (generation !== this.cepLookupGeneration) return;
        this.street.set(res.street);
        this.neighborhood.set(res.neighborhood);
        this.city.set(res.city);
        this.state.set(res.state);
        this.cepValidated.set(true);
        this.cepLoading.set(false);
        this.updateAccountDirty();
        // Verifica cobertura de entrega após preencher o bairro
        this.checkDeliveryCoverage();
        this.subscribeStoreDeliveryUpdates();
      },
      error: () => {
        if (generation !== this.cepLookupGeneration) return;
        this.cepError.set(true);
        this.cepValidated.set(false);
        this.cepLoading.set(false);
      },
    }));
  }

  private subscribeStoreDeliveryUpdates(): void {
    const storeId = this.cart.storeId();
    if (!storeId) return;

    this.removeDeliveryAreaUpdatedListener();

    const callback = (data: { storeId: string }) => {
      if (data?.storeId === storeId) {
        this.checkDeliveryCoverage();
      }
    };
    this.deliveryAreaUpdatedCallback = callback;

    // Register before starting the hub so the callback is never lost while offline.
    this.signalR.onCustomerEvent('DeliveryAreaUpdated', callback);

    const generation = ++this.customerHubGeneration;
    this.signalR.startCustomerHub().then(() => {
      if (generation !== this.customerHubGeneration || !this.active) return;
      if (this.joinedStoreId === storeId) return;
      this.joinedStoreId = storeId;
      void this.signalR.joinStore(storeId).catch(() => {/* offline — ok */});
    }).catch(() => {/* offline — ok */});
  }

  private removeDeliveryAreaUpdatedListener(): void {
    if (this.deliveryAreaUpdatedCallback) {
      this.signalR.removeCustomerListener('DeliveryAreaUpdated', this.deliveryAreaUpdatedCallback);
      this.deliveryAreaUpdatedCallback = undefined;
    }
  }

  ngOnDestroy(): void {
    this.active = false;
    this.customerHubGeneration++;
    this.subscriptions.unsubscribe();
    this.removeDeliveryAreaUpdatedListener();
    if (this.joinedStoreId) {
      const storeId = this.joinedStoreId;
      this.joinedStoreId = null;
      void this.signalR.leaveStore(storeId).catch(() => {/* ignore */});
    }
  }

  /** Verifica se o bairro atual está coberto pelas áreas de entrega da loja. */
  private checkDeliveryCoverage(): void {
    const neighborhood = this.neighborhood().trim();
    const storeId = this.cart.storeId();
    if (!neighborhood || !storeId) return;
    if (this.checkout.fulfillmentType() !== this.FulfillmentType.Delivery) {
      this.deliveryNotCovered.set(false);
      this.deliveryCheckLoading.set(false);
      return;
    }

    const generation = ++this.deliveryCheckGeneration;
    this.deliveryCheckLoading.set(true);
    this.subscriptions.add(this.api.get<{ covered: boolean; deliveryFee: number }>(
      `/api/public/stores/${storeId}/delivery-check?neighborhood=${encodeURIComponent(neighborhood)}`,
    ).subscribe({
      next: (res) => {
        if (generation !== this.deliveryCheckGeneration) return;
        this.deliveryCheckLoading.set(false);
        if (this.neighborhood().trim() !== neighborhood) return;
        this.deliveryNotCovered.set(!res.covered);
      },
      error: () => {
        if (generation !== this.deliveryCheckGeneration) return;
        this.deliveryCheckLoading.set(false);
      },
    }));
  }

  onBack(): void {
    this.location.back();
  }

  continue(): void {
    if (this.isAccountEdit()) {
      this.saveAccount();
      return;
    }
    this.attemptedSubmit.set(true);
    if (this.cepLoading()) return;

    // Bairro não coberto para entrega? Exige que o lojista cadastre a área.
    if (this.deliveryNotCovered()) {
      this.deliveryCoverageModalOpen.set(true);
      return;
    }

    const problems = this.validate();
    if (problems.length > 0) {
      return;
    }

    this.cepLoading.set(true);
    this.cepError.set(false);

    this.checkout.customerInfo.set({
      fullName: this.fullName().trim(),
      email: this.email().trim(),
      phoneNumber: this.phone(),
    });
    this.checkout.customerAddress.set({
      cep: this.cep().replace(/\D/g, ''),
      street: this.street(),
      number: this.number(),
      complement: this.complement() || undefined,
      neighborhood: this.neighborhood(),
      city: this.city(),
      state: this.state(),
      isPrimary: true,
    });

    const storeId = this.cart.storeId();
    if (!storeId) {
      this.cepLoading.set(false);
      this.toast.showError('Não foi possível identificar a loja. Volte ao cardápio e tente novamente.');
      return;
    }

    this.subscriptions.add(this.checkout.createCustomerSession({
      storeId,
      customer: {
        fullName: this.fullName().trim(),
        email: this.email().trim(),
        phoneNumber: this.phone().replace(/\D/g, ''),
      },
      address: {
        cep: this.cep().replace(/\D/g, ''),
        street: this.street().trim(),
        number: this.number().trim(),
        complement: this.complement().trim() || undefined,
        neighborhood: this.neighborhood().trim(),
        city: this.city().trim(),
        state: this.state().trim().toUpperCase(),
      },
    }).subscribe({
      next: (session) => {
        if (!this.active) return;
        if (!session.succeeded || !session.accessToken) {
          this.cepLoading.set(false);
          this.toast.showError(session.error ?? 'Não foi possível continuar. Tente novamente.');
          return;
        }

        this.auth.saveToken({
          accessToken: session.accessToken,
          expiresAtUtc: session.expiresAtUtc ?? '',
        });
        this.checkout.customerAddressId.set(session.customerAddressId ?? null);
        this.cepLoading.set(false);
        this.router.navigate(['/', getStorePathFromUrl(this.router), 'checkout', 'pagamento']);
      },
      error: () => {
        if (!this.active) return;
        this.cepLoading.set(false);
        this.toast.showError('Não foi possível criar sua sessão segura. Tente novamente.');
      },
    }));
  }

  closeDeliveryCoverageModal(): void {
    this.deliveryCoverageModalOpen.set(false);
  }

  goToMenu(): void {
    this.router.navigate(['/', getStorePathFromUrl(this.router)]);
  }
}
