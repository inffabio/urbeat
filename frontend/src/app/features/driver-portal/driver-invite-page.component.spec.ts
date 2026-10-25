import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { of, throwError } from 'rxjs';
import { DeliveryDriverService } from '../../core/services/delivery-driver.service';
import { DriverInvitePageComponent as InvitePage } from './driver-invite-page.component';

describe('DriverInvitePageComponent', () => {
  let serviceMock: { previewInvite: jest.Mock; acceptInvite: jest.Mock };
  let routerMock: { navigate: jest.Mock };
  let routeMock: { snapshot: { paramMap: ReturnType<typeof convertToParamMap> } };

  const preview = {
    valid: true,
    alreadyAccepted: false,
    storeName: 'Loja Teste',
    driverName: 'Ana',
    maskedEmail: 'an***@example.com',
    expiresAtUtc: '2026-10-31T12:00:00Z',
  };

  beforeEach(async () => {
    serviceMock = {
      previewInvite: jest.fn().mockReturnValue(of(preview)),
      acceptInvite: jest.fn().mockReturnValue(of({ succeeded: true, storeName: 'Loja Teste' })),
    };
    routerMock = { navigate: jest.fn().mockResolvedValue(true) };
    routeMock = { snapshot: { paramMap: convertToParamMap({ token: 'invite-token' }) } };

    await TestBed.configureTestingModule({
      imports: [InvitePage],
      providers: [
        { provide: DeliveryDriverService, useValue: serviceMock },
        { provide: ActivatedRoute, useValue: routeMock },
        { provide: Router, useValue: routerMock },
      ],
    }).compileComponents();
  });

  it('loads the invitation preview and prefills the driver name', () => {
    const fixture = TestBed.createComponent(InvitePage);
    fixture.detectChanges();

    expect(serviceMock.previewInvite).toHaveBeenCalledWith('invite-token');
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Loja Teste convidou você');
    expect(text).toContain('an***@example.com');
    expect(fixture.componentInstance.form.controls.fullName.value).toBe('Ana');
  });

  it('shows the expired state for an invalid token', () => {
    serviceMock.previewInvite.mockReturnValue(of({ ...preview, valid: false }));
    const fixture = TestBed.createComponent(InvitePage);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Convite expirado');
  });

  it('requires matching passwords before accepting', () => {
    const fixture = TestBed.createComponent(InvitePage);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.form.setValue({ fullName: 'Ana', password: 'SenhaForte123', confirmPassword: 'OutraSenha123' });
    component.submit();
    expect(serviceMock.acceptInvite).not.toHaveBeenCalled();
  });

  it('accepts the invite and shows the success state', () => {
    const fixture = TestBed.createComponent(InvitePage);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.form.setValue({ fullName: 'Ana', password: 'SenhaForte123', confirmPassword: 'SenhaForte123' });
    component.submit();
    fixture.detectChanges();

    expect(serviceMock.acceptInvite).toHaveBeenCalledWith('invite-token', {
      password: 'SenhaForte123',
      fullName: 'Ana',
    });
    expect(component.accepted()).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Convite aceito');
  });

  it('surfaces acceptance errors', () => {
    serviceMock.acceptInvite.mockReturnValue(throwError(() => ({ error: { error: 'Convite inválido ou expirado.' } })));
    const fixture = TestBed.createComponent(InvitePage);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.form.setValue({ fullName: 'Ana', password: 'SenhaForte123', confirmPassword: 'SenhaForte123' });
    component.submit();

    expect(component.error()).toBe('Convite inválido ou expirado.');
  });
});





