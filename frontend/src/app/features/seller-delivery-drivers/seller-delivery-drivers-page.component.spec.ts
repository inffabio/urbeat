import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { DeliveryDriverService } from '../../core/services/delivery-driver.service';
import { ToastService } from '../../core/services/toast.service';
import { SellerDeliveryDriver, StoreDeliveryDriverStatus } from '../../shared/models/delivery-driver.model';
import { SellerDeliveryDriversPageComponent } from './seller-delivery-drivers-page.component';

const pendingDriver: SellerDeliveryDriver = {
  id: 'link-1',
  name: 'Ana Entregadora',
  email: 'ana@example.com',
  phoneNumber: '11999999999',
  status: StoreDeliveryDriverStatus.Pending,
  inviteSentAtUtc: '2026-10-24T12:00:00Z',
  inviteExpiresAtUtc: '2026-10-31T12:00:00Z',
  canResendInvite: true,
};

describe('SellerDeliveryDriversPageComponent', () => {
  let serviceMock: {
    listStoreDrivers: jest.Mock;
    invite: jest.Mock;
    resendInvite: jest.Mock;
    revoke: jest.Mock;
  };
  let toastMock: { showSuccess: jest.Mock; showError: jest.Mock };

  beforeEach(async () => {
    serviceMock = {
      listStoreDrivers: jest.fn().mockReturnValue(of([pendingDriver])),
      invite: jest.fn().mockReturnValue(of(pendingDriver)),
      resendInvite: jest.fn().mockReturnValue(of(pendingDriver)),
      revoke: jest.fn().mockReturnValue(of({ ...pendingDriver, status: StoreDeliveryDriverStatus.Revoked, canResendInvite: false })),
    };
    toastMock = {
      showSuccess: jest.fn().mockResolvedValue(undefined),
      showError: jest.fn().mockResolvedValue(undefined),
    };

    await TestBed.configureTestingModule({
      imports: [SellerDeliveryDriversPageComponent],
      providers: [
        { provide: DeliveryDriverService, useValue: serviceMock },
        { provide: ToastService, useValue: toastMock },
      ],
    }).compileComponents();
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('lists store drivers with status and invite actions', () => {
    const fixture = TestBed.createComponent(SellerDeliveryDriversPageComponent);
    fixture.detectChanges();

    expect(serviceMock.listStoreDrivers).toHaveBeenCalled();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Ana Entregadora');
    expect(text).toContain('ana@example.com');
    expect(text).toContain('Convite pendente');
    expect(text).toContain('Reenviar convite');
    expect(text).toContain('Revogar');
  });

  it('shows the empty state and opens the invite dialog', () => {
    serviceMock.listStoreDrivers.mockReturnValue(of([]));
    const fixture = TestBed.createComponent(SellerDeliveryDriversPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Nenhum entregador cadastrado');
    fixture.componentInstance.openInviteDialog();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Convidar entregador');
    expect(fixture.nativeElement.querySelector('#invite-driver-title')).not.toBeNull();
  });

  it('validates and submits the invite form', () => {
    const fixture = TestBed.createComponent(SellerDeliveryDriversPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.openInviteDialog();
    component.submitInvite();
    expect(serviceMock.invite).not.toHaveBeenCalled();
    expect(component.inviteForm.controls.email.touched).toBe(true);

    component.inviteForm.setValue({
      fullName: 'Novo Entregador',
      email: 'novo@example.com',
      phoneNumber: '11988887777',
    });
    component.submitInvite();

    expect(serviceMock.invite).toHaveBeenCalledWith({
      fullName: 'Novo Entregador',
      email: 'novo@example.com',
      phoneNumber: '11988887777',
    });
    expect(toastMock.showSuccess).toHaveBeenCalled();
  });

  it('resends an invite', () => {
    const fixture = TestBed.createComponent(SellerDeliveryDriversPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.resendInvite(pendingDriver);
    expect(serviceMock.resendInvite).toHaveBeenCalledWith('link-1');
    expect(toastMock.showSuccess).toHaveBeenCalled();
  });

  it('revokes access after confirmation', () => {
    const fixture = TestBed.createComponent(SellerDeliveryDriversPageComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.revoke(pendingDriver);
    expect(component.revokeTarget()?.id).toBe('link-1');
    component.confirmRevoke();

    expect(serviceMock.revoke).toHaveBeenCalledWith('link-1');
    expect(component.drivers()[0].status).toBe(StoreDeliveryDriverStatus.Revoked);
  });

  it('shows the error state when loading fails', () => {
    serviceMock.listStoreDrivers.mockReturnValue(throwError(() => new Error('network')));
    const fixture = TestBed.createComponent(SellerDeliveryDriversPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Não foi possível carregar os entregadores');
  });

  it('focuses the invite dialog, wires aria-describedby and restores focus on cancel', () => {
    jest.useFakeTimers();
    const fixture = TestBed.createComponent(SellerDeliveryDriversPageComponent);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const trigger = element.querySelector<HTMLButtonElement>('.btn-invite');
    expect(trigger).not.toBeNull();

    trigger!.focus();
    trigger!.click();
    fixture.detectChanges();
    jest.advanceTimersByTime(0);

    const dialog = element.querySelector<HTMLElement>('#invite-driver-dialog');
    expect(dialog).not.toBeNull();
    expect(dialog!.getAttribute('tabindex')).toBe('-1');
    expect(document.activeElement).toBe(dialog);
    expect(dialog!.getAttribute('aria-labelledby')).toBe('invite-driver-title');
    expect(dialog!.getAttribute('aria-describedby')).toBe('invite-driver-description');
    expect(element.querySelector('#invite-driver-description')?.textContent).toContain('7 dias');

    fixture.componentInstance.closeInviteDialog();
    fixture.detectChanges();
    jest.advanceTimersByTime(0);

    expect(fixture.componentInstance.inviteDialogOpen()).toBe(false);
    expect(document.activeElement).toBe(trigger);

    fixture.destroy();
  });

  it('traps Tab and Shift+Tab inside the invite dialog', () => {
    jest.useFakeTimers();
    const fixture = TestBed.createComponent(SellerDeliveryDriversPageComponent);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    fixture.componentInstance.openInviteDialog();
    fixture.detectChanges();
    jest.advanceTimersByTime(0);

    const dialog = element.querySelector<HTMLElement>('#invite-driver-dialog')!;
    const focusable = Array.from(
      dialog.querySelectorAll<HTMLElement>('button, input, [tabindex]:not([tabindex="-1"])'),
    ).filter((node) => !node.hasAttribute('disabled'));
    const first = focusable[0];
    const last = focusable[focusable.length - 1];

    last.focus();
    const tabForward = {
      key: 'Tab',
      shiftKey: false,
      currentTarget: dialog,
      preventDefault: jest.fn(),
    } as unknown as KeyboardEvent;
    fixture.componentInstance.onDialogKeydown(tabForward);
    expect(tabForward.preventDefault).toHaveBeenCalled();
    expect(document.activeElement).toBe(first);

    first.focus();
    const tabBackward = {
      key: 'Tab',
      shiftKey: true,
      currentTarget: dialog,
      preventDefault: jest.fn(),
    } as unknown as KeyboardEvent;
    fixture.componentInstance.onDialogKeydown(tabBackward);
    expect(tabBackward.preventDefault).toHaveBeenCalled();
    expect(document.activeElement).toBe(last);

    fixture.destroy();
  });

  it('closes the revoke dialog with Escape and restores focus to its trigger', () => {
    jest.useFakeTimers();
    const fixture = TestBed.createComponent(SellerDeliveryDriversPageComponent);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const trigger = Array.from(element.querySelectorAll<HTMLButtonElement>('button'))
      .find((button) => button.textContent?.trim() === 'Revogar');
    expect(trigger).toBeDefined();

    trigger!.focus();
    trigger!.click();
    fixture.detectChanges();
    jest.advanceTimersByTime(0);

    const dialog = element.querySelector<HTMLElement>('#revoke-driver-dialog');
    expect(dialog).not.toBeNull();
    expect(dialog!.getAttribute('tabindex')).toBe('-1');
    expect(document.activeElement).toBe(dialog);
    expect(dialog!.getAttribute('aria-describedby')).toBe('revoke-driver-description');

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    jest.advanceTimersByTime(0);

    expect(fixture.componentInstance.revokeTarget()).toBeNull();
    expect(document.activeElement).toBe(trigger);

    fixture.destroy();
  });
});
