import { Component, OnDestroy, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { IonContent, IonIcon } from '@ionic/angular/standalone';
import { BrlCurrencyPipe } from '../../shared/pipes/brl-currency.pipe';
import { Subscription } from 'rxjs';

import { CartService } from '../../core/services/cart.service';
import { CheckoutService } from '../../core/services/checkout.service';
import { AddressService } from '../../core/services/address.service';
import { ApiService } from '../../core/services/api.service';
import { SignalRService } from '../../core/services/signalr.service';
import { ToastService, ToastLine } from '../../core/services/toast.service';
import { AuthService } from '../../core/services/auth.service';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { getStorePathFromUrl } from '../../shared/utils/router.utils';

type CustomerField = 'fullName' | 'phone' | 'email' | 'cep' | 'city' | 'state' | 'neighborhood' | 'street' | 'number';

@Component({
  selector: 'app-customer-page',
  standalone: true,
  imports: [CommonModule, FormsModule, IonContent, IonIcon, BrlCurrencyPipe],
  templateUrl: './customer-page.component.html',
  styleUrl: './customer-page.component.scss',
})
export class CustomerPageComponent implements OnInit, OnDestroy {
  readonly cart = inject(CartService);
  readonly checkout = inject(CheckoutService);
  private readonly address = inject(AddressService);
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  private readonly toast = inject(ToastService);
  private readonly signalR = inject(SignalRService);
  private readonly auth = inject(AuthService);

  private signalRSub?: Subscription;
  readonly FulfillmentType = FulfillmentType;

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

  onPhoneInput(v: string): void {
    const digits = v.replace(/\D/g, '').slice(0, 11);
    let formatted = digits;
    if (digits.length > 2) formatted = `(${digits.slice(0, 2)}) ${digits.slice(2)}`;
    if (digits.length > 6) formatted = `(${digits.slice(0, 2)}) ${digits.slice(2, 7)}-${digits.slice(7)}`;
    this.phone.set(formatted);
  }

  onCepInput(v: string): void {
    const digits = v.replace(/\D/g, '').slice(0, 8);
    const formatted = digits.length > 5 ? `${digits.slice(0, 5)}-${digits.slice(5)}` : digits;
    this.cep.set(formatted);
    this.cepError.set(false);
    this.cepValidated.set(false);
    if (digits.length === 8) {
      this.lookupCep(digits);
    }
  }

  private lookupCep(cep: string): void {
    this.cepLoading.set(true);
    this.address.lookupCep(cep).subscribe({
      next: (res) => {
        this.street.set(res.street);
        this.neighborhood.set(res.neighborhood);
        this.city.set(res.city);
        this.state.set(res.state);
        this.cepValidated.set(true);
        this.cepLoading.set(false);
        // Verifica cobertura de entrega após preencher o bairro
        this.checkDeliveryCoverage();
        this.subscribeStoreDeliveryUpdates();
      },
      error: () => {
        this.cepError.set(true);
        this.cepValidated.set(false);
        this.cepLoading.set(false);
      },
    });
  }

  private subscribeStoreDeliveryUpdates(): void {
    const storeId = this.cart.storeId();
    if (!storeId) return;

    // Conecta ao hub do cliente e entra no grupo da loja para receber DeliveryAreaUpdated
    this.signalR.startCustomerHub().then(() => {
      try {
        this.signalR.invokeCustomerMethod('JoinStore', storeId);
      } catch { /* best-effort */ }
    }).catch(() => {/* offline — ok */});

    this.signalRSub?.unsubscribe();
    this.signalR.onCustomerEvent('DeliveryAreaUpdated', (data: { storeId: string }) => {
      if (data?.storeId === storeId) {
        this.checkDeliveryCoverage();
      }
    });
  }

  ngOnDestroy(): void {
    this.signalRSub?.unsubscribe();
    const storeId = this.cart.storeId();
    if (storeId) {
      try { this.signalR.invokeCustomerMethod('LeaveStore', storeId); } catch { /* ignore */ }
    }
  }

  /** Verifica se o bairro atual está coberto pelas áreas de entrega da loja. */
  private checkDeliveryCoverage(): void {
    const neighborhood = this.neighborhood().trim();
    const storeId = this.cart.storeId();
    if (!neighborhood || !storeId) return;
    if (this.checkout.fulfillmentType() !== this.FulfillmentType.Delivery) {
      this.deliveryNotCovered.set(false);
      return;
    }

    this.deliveryCheckLoading.set(true);
    this.api.get<{ covered: boolean; deliveryFee: number }>(
      `/api/public/stores/${storeId}/delivery-check?neighborhood=${encodeURIComponent(neighborhood)}`,
    ).subscribe({
      next: (res) => {
        this.deliveryNotCovered.set(!res.covered);
        this.deliveryCheckLoading.set(false);
      },
      error: () => {
        this.deliveryCheckLoading.set(false);
      },
    });
  }

  onBack(): void {
    this.location.back();
  }

  continue(): void {
    this.attemptedSubmit.set(true);
    if (this.cepLoading()) return;

    // Bairro não coberto para entrega? Exige que o lojista cadastre a área.
    if (this.deliveryNotCovered()) {
      this.toast.showWarning('Ainda não entregamos no seu bairro. Entre em contato com a loja pelo chat.');
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

    this.checkout.createCustomerSession({
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
        if (!session.succeeded || !session.accessToken || !session.refreshToken) {
          this.cepLoading.set(false);
          this.toast.showError(session.error ?? 'Não foi possível continuar. Tente novamente.');
          return;
        }

        this.auth.saveToken({
          accessToken: session.accessToken,
          refreshToken: session.refreshToken,
          expiresAtUtc: session.expiresAtUtc ?? '',
          refreshTokenExpiresAtUtc: session.refreshTokenExpiresAtUtc ?? '',
        });
        this.checkout.customerAddressId.set(session.customerAddressId ?? null);
        this.cepLoading.set(false);
        this.router.navigate(['/', getStorePathFromUrl(this.router), 'checkout', 'pagamento']);
      },
      error: () => {
        this.cepLoading.set(false);
        this.toast.showError('Não foi possível criar sua sessão segura. Tente novamente.');
      },
    });
  }
}
