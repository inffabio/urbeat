import { CommonModule } from '@angular/common';
import {
  Component,
  ElementRef,
  HostListener,
  OnInit,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { DeliveryDriverService } from '../../core/services/delivery-driver.service';
import { ToastService } from '../../core/services/toast.service';
import { formatSaoPauloDate, formatSaoPauloDateTime } from '../../core/utils/sao-paulo-date.helper';
import {
  InviteDeliveryDriverRequest,
  SellerDeliveryDriver,
  StoreDeliveryDriverStatus,
} from '../../shared/models/delivery-driver.model';

@Component({
  selector: 'app-seller-delivery-drivers-page',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './seller-delivery-drivers-page.component.html',
  styleUrl: './seller-delivery-drivers-page.component.scss',
})
export class SellerDeliveryDriversPageComponent implements OnInit {
  private readonly driversApi = inject(DeliveryDriverService);
  private readonly toast = inject(ToastService);
  private readonly fb = inject(FormBuilder);

  @ViewChild('inviteDialog') private inviteDialogRef?: ElementRef<HTMLElement>;
  @ViewChild('revokeDialog') private revokeDialogRef?: ElementRef<HTMLElement>;

  private inviteTrigger: HTMLElement | null = null;
  private revokeTrigger: HTMLElement | null = null;

  readonly StoreDeliveryDriverStatus = StoreDeliveryDriverStatus;
  readonly loading = signal(true);
  readonly error = signal(false);
  readonly submitting = signal(false);
  readonly inviteDialogOpen = signal(false);
  readonly revokeTarget = signal<SellerDeliveryDriver | null>(null);
  readonly actionDriverId = signal<string | null>(null);
  readonly drivers = signal<SellerDeliveryDriver[]>([]);

  readonly inviteForm = this.fb.nonNullable.group({
    fullName: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(120)]],
    email: ['', [Validators.required, Validators.email]],
    phoneNumber: ['', [Validators.required, Validators.minLength(8), Validators.maxLength(20)]],
  });

  readonly activeCount = computed(
    () => this.drivers().filter((driver) => driver.status === StoreDeliveryDriverStatus.Active).length,
  );
  readonly pendingCount = computed(
    () => this.drivers().filter((driver) => driver.status === StoreDeliveryDriverStatus.Pending).length,
  );
  readonly revokedCount = computed(
    () => this.drivers().filter((driver) => driver.status === StoreDeliveryDriverStatus.Revoked).length,
  );

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);

    this.driversApi.listStoreDrivers().subscribe({
      next: (drivers) => {
        this.drivers.set(drivers);
        this.loading.set(false);
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }

  openInviteDialog(event?: Event): void {
    this.inviteTrigger = this.resolveTrigger(event);
    this.inviteForm.reset();
    this.inviteDialogOpen.set(true);
    this.focusDialog('invite');
  }

  closeInviteDialog(): void {
    const trigger = this.inviteTrigger;
    this.inviteTrigger = null;
    this.inviteDialogOpen.set(false);
    this.submitting.set(false);
    this.restoreFocus(trigger);
  }

  submitInvite(): void {
    if (this.inviteForm.invalid || this.submitting()) {
      this.inviteForm.markAllAsTouched();
      return;
    }

    const request: InviteDeliveryDriverRequest = {
      fullName: this.inviteForm.controls.fullName.value.trim(),
      email: this.inviteForm.controls.email.value.trim(),
      phoneNumber: this.inviteForm.controls.phoneNumber.value.trim(),
    };

    this.submitting.set(true);
    this.driversApi.invite(request).subscribe({
      next: (driver) => {
        this.submitting.set(false);
        this.drivers.update((items) => [driver, ...items.filter((item) => item.id !== driver.id)]);
        this.closeInviteDialog();
        void this.toast.showSuccess('Convite enviado para o entregador.');
      },
      error: (err) => {
        this.submitting.set(false);
        const message = err?.error?.error ?? 'Não foi possível enviar o convite.';
        void this.toast.showError(message);
      },
    });
  }

  resendInvite(driver: SellerDeliveryDriver): void {
    if (this.actionDriverId()) return;
    this.actionDriverId.set(driver.id);

    this.driversApi.resendInvite(driver.id).subscribe({
      next: (updated) => {
        this.actionDriverId.set(null);
        this.replaceDriver(updated);
        void this.toast.showSuccess('Convite reenviado.');
      },
      error: () => {
        this.actionDriverId.set(null);
        void this.toast.showError('Não foi possível reenviar o convite.');
      },
    });
  }

  revoke(driver: SellerDeliveryDriver, event?: Event): void {
    if (this.actionDriverId()) return;
    this.revokeTrigger = this.resolveTrigger(event);
    this.revokeTarget.set(driver);
    this.focusDialog('revoke');
  }

  cancelRevoke(): void {
    this.closeRevoke();
  }

  confirmRevoke(): void {
    const driver = this.revokeTarget();
    if (!driver || this.actionDriverId()) return;

    this.actionDriverId.set(driver.id);
    this.driversApi.revoke(driver.id).subscribe({
      next: (updated) => {
        this.actionDriverId.set(null);
        this.closeRevoke();
        this.replaceDriver(updated);
        void this.toast.showSuccess('Acesso revogado.');
      },
      error: (err) => {
        this.actionDriverId.set(null);
        this.closeRevoke();
        void this.toast.showError(err?.error?.error ?? 'Não foi possível revogar o acesso.');
      },
    });
  }

  @HostListener('document:keydown.escape', ['$event'])
  onEscape(event?: Event): void {
    if (this.revokeTarget()) {
      event?.preventDefault();
      this.closeRevoke();
      return;
    }

    if (this.inviteDialogOpen()) {
      event?.preventDefault();
      this.closeInviteDialog();
    }
  }

  /** Keeps Tab/Shift+Tab inside the open dialog, matching the seller orders and driver dialogs. */
  onDialogKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      this.onEscape(event);
      return;
    }

    if (event.key !== 'Tab') return;

    const dialog = event.currentTarget as HTMLElement | null;
    if (!dialog) return;

    const focusable = Array.from(dialog.querySelectorAll<HTMLElement>(
      'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])',
    )).filter((element) => !element.hasAttribute('disabled'));

    if (focusable.length === 0) {
      event.preventDefault();
      return;
    }

    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    const active = document.activeElement as HTMLElement | null;
    const index = active ? focusable.indexOf(active) : -1;

    if (event.shiftKey) {
      if (index <= 0) {
        event.preventDefault();
        last.focus();
      }
      return;
    }

    if (index === -1 || index === focusable.length - 1) {
      event.preventDefault();
      first.focus();
    }
  }

  statusLabel(status: StoreDeliveryDriverStatus): string {
    switch (status) {
      case StoreDeliveryDriverStatus.Active:
        return 'Ativo';
      case StoreDeliveryDriverStatus.Pending:
        return 'Convite pendente';
      case StoreDeliveryDriverStatus.Revoked:
        return 'Revogado';
      default:
        return '';
    }
  }

  formatDateTime(value?: string | null): string {
    return value ? formatSaoPauloDateTime(value) : '-';
  }

  formatDate(value?: string | null): string {
    return value ? formatSaoPauloDate(value) : '-';
  }

  private replaceDriver(updated: SellerDeliveryDriver): void {
    this.drivers.update((items) => items.map((item) => (item.id === updated.id ? updated : item)));
  }

  private closeRevoke(): void {
    const trigger = this.revokeTrigger;
    this.revokeTrigger = null;
    this.revokeTarget.set(null);
    this.restoreFocus(trigger);
  }

  private resolveTrigger(event?: Event): HTMLElement | null {
    const target = event?.currentTarget;
    return target instanceof HTMLElement ? target : null;
  }

  private focusDialog(kind: 'invite' | 'revoke'): void {
    // The dialog is rendered by the signal update, so focus must wait for change detection.
    setTimeout(() => {
      const dialog = kind === 'invite'
        ? this.inviteDialogRef?.nativeElement
        : this.revokeDialogRef?.nativeElement;
      dialog?.focus();
    });
  }

  private restoreFocus(trigger: HTMLElement | null): void {
    if (!trigger) return;

    setTimeout(() => {
      if (trigger.isConnected) trigger.focus();
    });
  }
}
