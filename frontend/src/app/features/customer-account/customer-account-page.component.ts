import { CommonModule, Location } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { IonContent, IonIcon } from '@ionic/angular/standalone';
import { Subscription } from 'rxjs';

import { AddressService } from '../../core/services/address.service';
import { AuthService } from '../../core/services/auth.service';
import { ToastService } from '../../core/services/toast.service';
import { getStorePathFromUrl } from '../../shared/utils/router.utils';
import { CustomerAddress } from '../../shared/models/address.model';
import { CustomerProfileResponse } from '../../shared/models/auth.model';
import {
  CustomerProfileFormComponent,
  CustomerProfileFormProfile,
  CustomerProfileFormValue,
} from '../../shared/components/customer-profile-form/customer-profile-form.component';
import { Router } from '@angular/router';

@Component({
  selector: 'app-customer-account-page',
  standalone: true,
  imports: [CommonModule, IonContent, IonIcon, CustomerProfileFormComponent],
  templateUrl: './customer-account-page.component.html',
  styleUrl: './customer-account-page.component.scss',
})
export class CustomerAccountPageComponent implements OnInit, OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly address = inject(AddressService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly location = inject(Location);

  private readonly subscriptions = new Subscription();
  private readonly form = viewChild(CustomerProfileFormComponent);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly profile = signal<CustomerProfileResponse | null>(null);
  readonly primaryAddress = signal<CustomerAddress | null>(null);
  readonly canSave = signal(false);

  readonly formProfile = computed<CustomerProfileFormProfile | null>(() => {
    const profile = this.profile();
    if (!profile) return null;
    return {
      fullName: profile.fullName ?? '',
      email: profile.email ?? '',
      phoneNumber: profile.phoneNumber ?? '',
    };
  });

  ngOnInit(): void {
    const current = this.auth.customerProfile();
    if (current) {
      this.loadAddress(current);
      return;
    }

    this.subscriptions.add(this.auth.restoreCustomerSession().subscribe({
      next: (profile) => this.loadAddress(profile),
      error: () => this.redirectToStorefront(),
    }));
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
  }

  private loadAddress(profile: CustomerProfileResponse): void {
    this.profile.set(profile);
    this.subscriptions.add(this.address.list().subscribe({
      next: (addresses) => {
        const primary = addresses.find((item) => item.isPrimary) ?? addresses[0] ?? null;
        this.primaryAddress.set(primary);
        this.loading.set(false);
      },
      error: () => this.redirectToStorefront(),
    }));
  }

  onDirtyChange(dirty: boolean): void {
    this.canSave.set(dirty);
  }

  onValidSubmit(value: CustomerProfileFormValue): void {
    if (this.saving()) return;
    this.saving.set(true);

    const finish = (): void => {
      this.saving.set(false);
      this.form()?.acceptChanges();
      this.toast.showSuccess('Cadastro atualizado com sucesso.');
    };

    const saveAddress = (): void => {
      if (!value.saveAddress) {
        finish();
        return;
      }

      const current = this.primaryAddress();
      const request = current
        ? this.address.update(current.id, value.address)
        : this.address.create(value.address);

      this.subscriptions.add(request.subscribe({
        next: (address) => {
          this.primaryAddress.set(address);
          finish();
        },
        error: () => {
          this.saving.set(false);
          if (value.saveProfile) {
            this.toast.showWarning('Seus dados foram salvos, mas não foi possível salvar o endereço. Revise e tente novamente.');
          } else {
            this.toast.showError('Não foi possível salvar seu endereço. Tente novamente.');
          }
        },
      }));
    };

    if (!value.saveProfile) {
      saveAddress();
      return;
    }

    // AuthService already updates its own customerProfile signal; keep the
    // form's baseline until the address is also persisted so a partial
    // failure can be retried.
    this.subscriptions.add(this.auth.updateCustomerProfile(value.profile).subscribe({
      next: () => saveAddress(),
      error: () => {
        this.saving.set(false);
        this.toast.showError('Não foi possível salvar seus dados. Tente novamente.');
      },
    }));
  }

  onBack(): void {
    this.location.back();
  }

  private redirectToStorefront(): void {
    this.loading.set(false);
    const storePath = getStorePathFromUrl(this.router);
    this.router.navigate(storePath ? ['/', storePath] : ['/']);
  }
}
