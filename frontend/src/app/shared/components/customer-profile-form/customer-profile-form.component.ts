import { CommonModule } from '@angular/common';
import { Component, OnDestroy, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IonIcon } from '@ionic/angular/standalone';
import { Subscription } from 'rxjs';

import { AddressService } from '../../../core/services/address.service';
import { CustomerAddress, UpsertCustomerAddress } from '../../models/address.model';
import {
  CustomerProfileField,
  CustomerProfileFields,
  addressSnapshot as buildAddressSnapshot,
  buildCustomerProfileValue,
  customerProfileFieldError,
  formatCepInput,
  formatPhoneInput,
  isValidAddress as isAddressValid,
  isValidProfile as isProfileValid,
  profileSnapshot as buildProfileSnapshot,
} from '../../utils/customer-profile.rules';

export type CustomerProfileFormMode = 'checkout' | 'edit';

export interface CustomerProfileFormProfile {
  fullName: string;
  email: string;
  phoneNumber: string;
}

export interface CustomerProfileFormValue {
  profile: CustomerProfileFormProfile;
  address: UpsertCustomerAddress;
  /** Indica que a conta deve persistir a seção de perfil neste envio. */
  saveProfile: boolean;
  /** Indica que a conta deve persistir a seção de endereço neste envio. */
  saveAddress: boolean;
}

type CustomerField = CustomerProfileField;

@Component({
  selector: 'app-customer-profile-form',
  standalone: true,
  imports: [CommonModule, FormsModule, IonIcon],
  templateUrl: './customer-profile-form.component.html',
  styleUrl: './customer-profile-form.component.scss',
})
export class CustomerProfileFormComponent implements OnDestroy {
  private readonly addressService = inject(AddressService);

  readonly mode = input<CustomerProfileFormMode>('checkout');
  readonly initialProfile = input<CustomerProfileFormProfile | null>(null);
  readonly initialAddress = input<CustomerAddress | null>(null);
  readonly saving = input(false);

  readonly validSubmit = output<CustomerProfileFormValue>();
  readonly dirtyChange = output<boolean>();

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

  private readonly profileBaseline = signal(buildProfileSnapshot(this.fields()));
  private readonly addressBaseline = signal(buildAddressSnapshot(this.fields()));
  private readonly subscriptions = new Subscription();
  private cepLookupGeneration = 0;
  private appliedKey = '';

  readonly isValidProfile = computed(() => isProfileValid(this.fields()));
  readonly isValidAddress = computed(() => isAddressValid(this.fields()));
  readonly isProfileDirty = computed(() => buildProfileSnapshot(this.fields()) !== this.profileBaseline());
  readonly isAddressDirty = computed(() => buildAddressSnapshot(this.fields()) !== this.addressBaseline());
  readonly isDirty = computed(() => this.isProfileDirty() || this.isAddressDirty());

  readonly canSubmit = computed(() => {
    if (this.cepLoading()) return false;
    if (this.mode() === 'checkout') {
      return this.isDirty() && this.isValidProfile() && this.isValidAddress();
    }
    const profileDirty = this.isProfileDirty();
    const addressDirty = this.isAddressDirty();
    if (!profileDirty && !addressDirty) return false;
    if (profileDirty && !this.isValidProfile()) return false;
    if (addressDirty && !this.isValidAddress()) return false;
    return true;
  });

  constructor() {
    effect(() => {
      const profile = this.initialProfile();
      const address = this.initialAddress();
      const key = `${this.mode()}|${profile?.fullName ?? ''}|${profile?.email ?? ''}|${profile?.phoneNumber ?? ''}|${address?.id ?? ''}|${address?.cep ?? ''}`;
      if (!profile && !address) return;
      if (key === this.appliedKey) return;
      this.appliedKey = key;

      // Never overwrite edits the user is still working on. This matters on a
      // partial save: the profile input refreshes after a successful profile
      // update, but a failed address save must keep its edits and dirty state
      // so the retry still submits them.
      const profileDirty = this.isProfileDirty();
      const addressDirty = this.isAddressDirty();

      if (profile && !profileDirty) {
        this.fullName.set(profile.fullName ?? '');
        this.email.set(profile.email ?? '');
        this.phone.set(profile.phoneNumber ?? '');
      }
      if (!profileDirty) {
        this.profileBaseline.set(buildProfileSnapshot(this.fields()));
      }
      if (address && !addressDirty) {
        this.applyAddress(address);
      }
      if (!addressDirty) {
        this.addressBaseline.set(buildAddressSnapshot(this.fields()));
      }
      this.dirtyChange.emit(this.canSubmit());
    });
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
  }

  private fields(): CustomerProfileFields {
    return {
      fullName: this.fullName(),
      phone: this.phone(),
      email: this.email(),
      cep: this.cep(),
      street: this.street(),
      number: this.number(),
      complement: this.complement(),
      neighborhood: this.neighborhood(),
      city: this.city(),
      state: this.state(),
    };
  }

  markTouched(field: CustomerField): void {
    this.touchedFields.update((fields) => ({ ...fields, [field]: true }));
  }

  showFieldError(field: CustomerField): boolean {
    if (field === 'cep' && this.cepError()) return true;
    return (this.attemptedSubmit() || this.touchedFields()[field]) && this.fieldError(field).length > 0;
  }

  fieldError(field: CustomerField): string {
    return customerProfileFieldError(field, this.fields(), { cepError: this.cepError() });
  }

  onPhoneInput(value: string): void {
    this.phone.set(formatPhoneInput(value));
    this.emitDirty();
  }

  onCepInput(value: string): void {
    const formatted = formatCepInput(value);
    this.cep.set(formatted);
    this.cepError.set(false);
    this.cepValidated.set(false);
    this.emitDirty();
    this.cepLookupGeneration++;
    if (formatted.replace(/\D/g, '').length === 8) {
      this.lookupCep(formatted.replace(/\D/g, ''));
    } else {
      this.cepLoading.set(false);
    }
  }

  onAddressFieldChange(
    field: 'cep' | 'city' | 'state' | 'neighborhood' | 'street' | 'number',
    value: string,
  ): void {
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
    this.emitDirty();
  }

  onComplementInput(value: string): void {
    this.complement.set(value);
    this.emitDirty();
  }

  onFieldInput(field: 'fullName' | 'email' | 'complement', value: string): void {
    switch (field) {
      case 'fullName':
        this.fullName.set(value);
        break;
      case 'email':
        this.email.set(value);
        break;
      case 'complement':
        this.complement.set(value);
        break;
    }
    this.emitDirty();
  }

  submit(): void {
    this.attemptedSubmit.set(true);
    if (!this.canSubmit()) return;
    const value = buildCustomerProfileValue(this.fields());
    const isCheckout = this.mode() === 'checkout';
    this.validSubmit.emit({
      ...value,
      saveProfile: isCheckout ? true : this.isProfileDirty(),
      saveAddress: isCheckout ? true : this.isAddressDirty(),
    });
  }

  /** Re-syncs the dirty baselines after a successful save. */
  acceptChanges(): void {
    this.profileBaseline.set(buildProfileSnapshot(this.fields()));
    this.addressBaseline.set(buildAddressSnapshot(this.fields()));
    this.dirtyChange.emit(false);
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

  private lookupCep(cep: string): void {
    const generation = this.cepLookupGeneration;
    this.cepLoading.set(true);
    this.subscriptions.add(this.addressService.lookupCep(cep).subscribe({
      next: (res) => {
        if (generation !== this.cepLookupGeneration) return;
        this.street.set(res.street);
        this.neighborhood.set(res.neighborhood);
        this.city.set(res.city);
        this.state.set(res.state);
        this.cepValidated.set(true);
        this.cepLoading.set(false);
        this.emitDirty();
      },
      error: () => {
        if (generation !== this.cepLookupGeneration) return;
        this.cepError.set(true);
        this.cepValidated.set(false);
        this.cepLoading.set(false);
      },
    }));
  }

  private emitDirty(): void {
    this.dirtyChange.emit(this.canSubmit());
  }
}
