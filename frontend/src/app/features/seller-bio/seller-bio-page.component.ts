import { CommonModule } from '@angular/common';
import { Component, OnInit, OnDestroy, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { IonIcon } from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import { callOutline, logoInstagram, logoWhatsapp, shareSocialOutline, cloudUploadOutline, trashOutline, bulbOutline, imageOutline, checkmarkOutline, checkmarkCircle } from 'ionicons/icons';
import { StoreService } from '../../core/services/store.service';
import { ToastService } from '../../core/services/toast.service';
import { PendingChangesComponent } from '../../core/guards/pending-changes.guard';
import { ConfigSubnavComponent } from '../seller-shell/config-subnav.component';
import { StoreResponse } from '../../shared/models/store.model';

addIcons({
  'call-outline': callOutline,
  'logo-instagram': logoInstagram,
  'logo-whatsapp': logoWhatsapp,
  'share-social-outline': shareSocialOutline,
  'cloud-upload-outline': cloudUploadOutline,
  'trash-outline': trashOutline,
  'bulb-outline': bulbOutline,
  'image-outline': imageOutline,
  'checkmark-outline': checkmarkOutline,
  'checkmark-circle': checkmarkCircle,
});

@Component({
  selector: 'app-seller-bio-page',
  standalone: true,
  imports: [CommonModule, FormsModule, IonIcon, ConfigSubnavComponent],
  templateUrl: './seller-bio-page.component.html',
  styleUrls: ['./seller-bio-page.component.scss'],
})
export class SellerBioPageComponent implements OnInit, OnDestroy, PendingChangesComponent {
  private readonly stores = inject(StoreService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly loading = signal(true);
  readonly error = signal(false);
  readonly saving = signal(false);
  readonly dirty = signal(false);
  readonly store = signal<StoreResponse | null>(null);
  readonly name = signal('');
  readonly description = signal('');
  readonly logoPreview = signal('');
  readonly bannerPreview = signal('');
  readonly bannerFile = signal<File | null>(null);
  readonly logoFile = signal<File | null>(null);
  readonly headerStatus = computed(() => {
    if (this.saving()) return 'Salvando bio';
    if (this.dirty()) return 'Alteracoes pendentes';
    return 'Apresentacao da loja';
  });
  readonly storeStatusLabel = computed(() => this.store()?.isOpen ? 'Loja aberta' : 'Loja fechada');

  ngOnInit(): void {
    this.load();
  }

  ngOnDestroy(): void {
    this.revokePreviewUrls();
  }

  hasUnsavedChanges(): boolean {
    return this.dirty() && !this.saving();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.stores.getMyStore().subscribe({
      next: (store) => {
        this.store.set(store);
        this.name.set(store.name);
        this.description.set(store.description ?? '');
        this.logoPreview.set(store.logoUrl ?? '');
        this.bannerPreview.set(store.bannerUrl ?? '');
        this.dirty.set(false);
        this.loading.set(false);
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }

  updateName(value: string): void {
    this.name.set(value);
    this.dirty.set(true);
  }

  updateDescription(value: string): void {
    this.description.set(value.slice(0, 160));
    this.dirty.set(true);
  }

  onLogoFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    if (!['image/png', 'image/webp'].includes(file.type)) {
      this.toast.showError('A logo deve ser PNG ou WebP.');
      return;
    }
    if (file.size > 2 * 1024 * 1024) {
      this.toast.showError('A logo deve ter no maximo 2 MB.');
      return;
    }
    this.revokeUrl(this.logoPreview());
    this.logoFile.set(file);
    this.logoPreview.set(URL.createObjectURL(file));
    this.dirty.set(true);
  }

  onBannerFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type)) {
      this.toast.showError('O banner deve ser PNG, JPG ou WebP.');
      return;
    }
    if (file.size > 5 * 1024 * 1024) {
      this.toast.showError('O banner deve ter no maximo 5 MB.');
      return;
    }
    this.revokeUrl(this.bannerPreview());
    this.bannerFile.set(file);
    this.bannerPreview.set(URL.createObjectURL(file));
    this.dirty.set(true);
  }

  removeLogo(): void {
    this.revokeUrl(this.logoPreview());
    this.logoPreview.set('');
    this.logoFile.set(null);
    this.dirty.set(true);
  }

  removeBanner(): void {
    this.revokeUrl(this.bannerPreview());
    this.bannerPreview.set('');
    this.bannerFile.set(null);
    this.dirty.set(true);
  }

  private revokeUrl(url: string): void {
    if (url && url.startsWith('blob:')) {
      URL.revokeObjectURL(url);
    }
  }

  private revokePreviewUrls(): void {
    this.revokeUrl(this.logoPreview());
    this.revokeUrl(this.bannerPreview());
  }

  onImageError(event: Event): void {
    (event.target as HTMLImageElement).style.display = 'none';
  }

  cancel(): void {
    this.load();
    this.toast.showWarning('Alteracoes descartadas.');
  }

  openStorefront(): void {
    const storeSlug = this.store()?.slug;
    if (!storeSlug) return;

    const storefrontUrl = this.router.serializeUrl(this.router.createUrlTree([storeSlug]));
    window.open(storefrontUrl, '_blank', 'noopener,noreferrer');
  }

  save(): void {
    const store = this.store();
    if (!store || this.saving()) return;

    this.saving.set(true);

    const upload = (file: File | null, type: 'logo' | 'banner', message: string): Promise<string | null> => {
      if (!file) return Promise.resolve(null);
      return new Promise((resolve) => {
        this.stores.uploadImage(file, type).subscribe({
          next: (r) => resolve(r.url),
          error: () => { this.toast.showError(message); resolve(null); },
        });
      });
    };

    const doSave = (logoUrl: string | null, bannerUrl: string | null) => {
      this.stores.updateStore(store.id, {
        name: this.name().trim(),
        slug: store.slug,
        phoneNumber: store.phoneNumber,
        description: this.description().trim(),
        cuisineType: store.cuisineType,
        bannerUrl,
        logoUrl,
        supportsDelivery: store.supportsDelivery,
        supportsPickup: store.supportsPickup,
        initialMinute: store.initialMinute,
        finalMinute: store.finalMinute,
        maxDeliveryRadiusKm: store.maxDeliveryRadiusKm ?? 0,
      }).subscribe({
        next: (updated) => {
          this.store.set(updated);
          this.dirty.set(false);
          this.saving.set(false);
          this.toast.showSuccess('Bio atualizada.');
        },
        error: () => {
          this.saving.set(false);
          this.toast.showError('Nao foi possivel salvar a bio.');
        },
      });
    };

    Promise.all([
      upload(this.logoFile(), 'logo', 'Falha ao enviar logo.'),
      upload(this.bannerFile(), 'banner', 'Falha ao enviar banner.'),
    ]).then(([uploadedLogoUrl, uploadedBannerUrl]) => {
      doSave(
        uploadedLogoUrl ?? (this.logoPreview() ? this.store()?.logoUrl ?? null : null),
        uploadedBannerUrl ?? (this.bannerPreview() ? this.store()?.bannerUrl ?? null : null),
      );
    });
  }
}
