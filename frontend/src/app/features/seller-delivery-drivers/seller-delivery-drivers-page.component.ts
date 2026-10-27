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
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { DeliveryDriverService } from '../../core/services/delivery-driver.service';
import { ToastService } from '../../core/services/toast.service';
import {
  SellerDeliveryDriver,
  UpsertStoreDeliveryDriverRequest,
} from '../../shared/models/delivery-driver.model';
import {
  formatBrazilianPhone,
  isValidBrazilianMobile,
  normalizeBrazilianMobile,
  onlyPhoneDigits,
  phoneCaretPosition,
} from '../../shared/utils/brazilian-phone.utils';

type PhoneField = 'phoneNumber' | 'whatsAppPhoneNumber';

/** Optional-friendly mobile validator; requiredness is enforced by the control itself. */
function brazilianMobileValidator(control: AbstractControl): ValidationErrors | null {
  const value = control.value;
  if (typeof value !== 'string' || value.trim().length === 0) return null;

  return isValidBrazilianMobile(value) ? null : { mobilePhone: true };
}

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

  @ViewChild('driverDialog') private driverDialogRef?: ElementRef<HTMLElement>;
  @ViewChild('statusDialog') private statusDialogRef?: ElementRef<HTMLElement>;
  @ViewChild('deleteDialog') private deleteDialogRef?: ElementRef<HTMLElement>;

  private driverTrigger: HTMLElement | null = null;
  private statusTrigger: HTMLElement | null = null;
  private deleteTrigger: HTMLElement | null = null;

  readonly loading = signal(true);
  readonly error = signal(false);
  readonly submitting = signal(false);
  readonly driverDialogOpen = signal(false);
  readonly editingDriver = signal<SellerDeliveryDriver | null>(null);
  readonly statusTarget = signal<SellerDeliveryDriver | null>(null);
  readonly deleteTarget = signal<SellerDeliveryDriver | null>(null);
  readonly actionDriverId = signal<string | null>(null);
  readonly drivers = signal<SellerDeliveryDriver[]>([]);

  readonly driverForm = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(120)]],
    phoneNumber: ['', [Validators.required, brazilianMobileValidator]],
    whatsAppPhoneNumber: ['', [brazilianMobileValidator]],
    isActive: [true],
  });

  readonly activeCount = computed(() => this.drivers().filter((driver) => driver.isActive).length);
  readonly inactiveCount = computed(() => this.drivers().filter((driver) => !driver.isActive).length);
  readonly dialogTitle = computed(() => (this.editingDriver() ? 'Editar motoboy' : 'Novo motoboy'));

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);

    this.driversApi.listStoreDrivers().subscribe({
      next: (drivers) => {
        this.drivers.set(this.sortDrivers(drivers));
        this.loading.set(false);
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }

  openCreateDialog(event?: Event): void {
    this.driverTrigger = this.resolveTrigger(event);
    this.editingDriver.set(null);
    this.driverForm.reset({ isActive: true });
    this.driverDialogOpen.set(true);
    this.focusDialog('driver');
  }

  openEditDialog(driver: SellerDeliveryDriver, event?: Event): void {
    this.driverTrigger = this.resolveTrigger(event);
    this.editingDriver.set(driver);
    this.driverForm.reset({
      name: driver.name,
      phoneNumber: formatBrazilianPhone(driver.phoneNumber),
      whatsAppPhoneNumber: formatBrazilianPhone(driver.whatsAppPhoneNumber),
      isActive: driver.isActive,
    });
    this.driverDialogOpen.set(true);
    this.focusDialog('driver');
  }

  closeDriverDialog(): void {
    const trigger = this.driverTrigger;
    this.driverTrigger = null;
    this.driverDialogOpen.set(false);
    this.editingDriver.set(null);
    this.submitting.set(false);
    this.restoreFocus(trigger);
  }

  submitDriver(): void {
    if (this.driverForm.invalid || this.submitting()) {
      this.driverForm.markAllAsTouched();
      return;
    }

    const value = this.driverForm.getRawValue();
    const whatsApp = normalizeBrazilianMobile(value.whatsAppPhoneNumber);
    const request: UpsertStoreDeliveryDriverRequest = {
      name: value.name.trim(),
      phoneNumber: normalizeBrazilianMobile(value.phoneNumber),
      whatsAppPhoneNumber: whatsApp || null,
      isActive: value.isActive,
    };
    const editing = this.editingDriver();

    this.submitting.set(true);
    const operation = editing
      ? this.driversApi.updateDriver(editing.id, request)
      : this.driversApi.createDriver(request);

    operation.subscribe({
      next: (driver) => {
        this.submitting.set(false);
        this.drivers.update((items) => this.sortDrivers([
          driver,
          ...items.filter((item) => item.id !== driver.id),
        ]));
        this.closeDriverDialog();
        void this.toast.showSuccess(editing ? 'Motoboy atualizado.' : 'Motoboy cadastrado.');
      },
      error: (err) => {
        this.submitting.set(false);
        void this.toast.showError(err?.error?.error ?? 'Não foi possível salvar o motoboy.');
      },
    });
  }

  /** Applies the visual mobile mask while preserving the caret position. */
  onPhoneInput(field: PhoneField, event: Event): void {
    const input = event.target;
    if (!(input instanceof HTMLInputElement)) return;

    const caret = input.selectionStart ?? input.value.length;
    const typedDigits = onlyPhoneDigits(input.value.slice(0, caret)).length;
    const formatted = formatBrazilianPhone(input.value);

    const control = this.driverForm.controls[field];
    control.setValue(formatted, { emitEvent: false });
    control.markAsDirty();

    input.value = formatted;
    const position = phoneCaretPosition(formatted, typedDigits);
    input.setSelectionRange(position, position);
  }

  requestStatusChange(driver: SellerDeliveryDriver, event?: Event): void {
    if (this.actionDriverId()) return;
    this.statusTrigger = this.resolveTrigger(event);
    this.statusTarget.set(driver);
    this.focusDialog('status');
  }

  cancelStatusChange(): void {
    this.closeStatus();
  }

  confirmStatusChange(): void {
    const driver = this.statusTarget();
    if (!driver || this.actionDriverId()) return;

    this.actionDriverId.set(driver.id);
    this.driversApi.setDriverActive(driver.id, !driver.isActive).subscribe({
      next: (updated) => {
        this.actionDriverId.set(null);
        this.closeStatus();
        this.drivers.update((items) => this.sortDrivers(
          items.map((item) => (item.id === updated.id ? updated : item)),
        ));
        void this.toast.showSuccess(updated.isActive ? 'Motoboy ativado.' : 'Motoboy inativado.');
      },
      error: (err) => {
        this.actionDriverId.set(null);
        this.closeStatus();
        void this.toast.showError(err?.error?.error ?? 'Não foi possível alterar o status.');
      },
    });
  }

  requestDelete(driver: SellerDeliveryDriver, event?: Event): void {
    if (this.actionDriverId()) return;
    this.deleteTrigger = this.resolveTrigger(event);
    this.deleteTarget.set(driver);
    this.focusDialog('delete');
  }

  cancelDelete(): void {
    this.closeDelete();
  }

  confirmDelete(): void {
    const driver = this.deleteTarget();
    if (!driver || this.actionDriverId()) return;

    this.actionDriverId.set(driver.id);
    this.driversApi.deleteDriver(driver.id).subscribe({
      next: () => {
        this.actionDriverId.set(null);
        this.closeDelete();
        this.drivers.update((items) => items.filter((item) => item.id !== driver.id));
        void this.toast.showSuccess('Motoboy excluído.');
      },
      error: (err) => {
        this.actionDriverId.set(null);
        this.closeDelete();
        void this.toast.showError(err?.error?.error ?? 'Não foi possível excluir o motoboy.');
      },
    });
  }

  formatPhone(value: string | null | undefined): string {
    return formatBrazilianPhone(value);
  }

  whatsAppUrl(driver: SellerDeliveryDriver): string | null {
    const phone = normalizeBrazilianMobile(driver.whatsAppPhoneNumber || driver.phoneNumber);
    if (!phone) return null;

    const withCountry = phone.startsWith('55') ? phone : `55${phone}`;
    return `https://wa.me/${withCountry}`;
  }

  phoneHref(driver: SellerDeliveryDriver): string {
    return `tel:${normalizeBrazilianMobile(driver.phoneNumber)}`;
  }

  statusLabel(driver: SellerDeliveryDriver): string {
    return driver.isActive ? 'Ativo' : 'Inativo';
  }

  @HostListener('document:keydown.escape', ['$event'])
  onEscape(event?: Event): void {
    if (this.deleteTarget()) {
      event?.preventDefault();
      this.closeDelete();
      return;
    }

    if (this.statusTarget()) {
      event?.preventDefault();
      this.closeStatus();
      return;
    }

    if (this.driverDialogOpen()) {
      event?.preventDefault();
      this.closeDriverDialog();
    }
  }

  /** Keeps Tab/Shift+Tab inside the open dialog. */
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

  private closeStatus(): void {
    const trigger = this.statusTrigger;
    this.statusTrigger = null;
    this.statusTarget.set(null);
    this.restoreFocus(trigger);
  }

  private closeDelete(): void {
    const trigger = this.deleteTrigger;
    this.deleteTrigger = null;
    this.deleteTarget.set(null);
    this.restoreFocus(trigger);
  }

  private resolveTrigger(event?: Event): HTMLElement | null {
    const target = event?.currentTarget;
    return target instanceof HTMLElement ? target : null;
  }

  private focusDialog(kind: 'driver' | 'status' | 'delete'): void {
    setTimeout(() => {
      const dialog = kind === 'driver'
        ? this.driverDialogRef?.nativeElement
        : kind === 'status'
          ? this.statusDialogRef?.nativeElement
          : this.deleteDialogRef?.nativeElement;
      dialog?.focus();
    });
  }

  private restoreFocus(trigger: HTMLElement | null): void {
    if (!trigger) return;

    setTimeout(() => {
      if (trigger.isConnected) trigger.focus();
    });
  }

  private sortDrivers(drivers: SellerDeliveryDriver[]): SellerDeliveryDriver[] {
    return [...drivers].sort((a, b) => {
      if (a.isActive !== b.isActive) return a.isActive ? -1 : 1;
      return a.name.localeCompare(b.name, 'pt-BR');
    });
  }
}
