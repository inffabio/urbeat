import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';
import { ToastService } from '../../core/services/toast.service';
import { EmailConfirmationPageComponent } from './email-confirmation-page.component';

describe('EmailConfirmationPageComponent', () => {
  let fixture: ComponentFixture<EmailConfirmationPageComponent>;
  let component: EmailConfirmationPageComponent;
  let authServiceMock: {
    confirmRolePromotion: jest.Mock;
    resendConfirmation: jest.Mock;
    updateEmail: jest.Mock;
  };
  let toastMock: { showSuccess: jest.Mock; showError: jest.Mock; showWarning: jest.Mock };

  // Public resend contract: unknown, pending and already-confirmed addresses must be
  // indistinguishable, so the response exposes no `alreadyConfirmed` field.
  type ResendResponse = {
    succeeded: boolean;
    message: string;
    resendAvailableAtUtc?: string | null;
  };

  function useFakeClock(): void {
    jest.useFakeTimers();
    jest.setSystemTime(new Date('2026-01-01T12:00:00.000Z'));
  }

  function configure(
    queryParams: Record<string, string>,
    routeFragment: string | null = null,
    useUrlFragment = true,
  ): Promise<void> {
    authServiceMock = {
      confirmRolePromotion: jest.fn(),
      resendConfirmation: jest.fn(),
      updateEmail: jest.fn(),
    };
    toastMock = {
      showSuccess: jest.fn(),
      showError: jest.fn(),
      showWarning: jest.fn(),
    };

    // Model the real address bar for the promotion flow: the token arrives in the URL fragment
    // (never the query string) and must disappear from the URL as soon as it is read.
    if (useUrlFragment && queryParams['promotionToken']) {
      const params = new URLSearchParams(queryParams);
      window.history.replaceState({}, '', `/confirmacao-email#${params.toString()}`);
    }

    return TestBed.configureTestingModule({
      imports: [EmailConfirmationPageComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authServiceMock },
        { provide: ToastService, useValue: toastMock },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              queryParamMap: convertToParamMap(queryParams),
              fragment: routeFragment,
            },
          },
        },
      ],
    })
      .compileComponents()
      .then(() => {
        fixture = TestBed.createComponent(EmailConfirmationPageComponent);
        component = fixture.componentInstance;
      });
  }

  afterEach(() => {
    fixture?.destroy();
    jest.useRealTimers();
    sessionStorage.clear();
    delete (window as unknown as { __urbeatPromotionFragment?: unknown }).__urbeatPromotionFragment;
    window.history.replaceState({}, '', '/');
  });

  it('removes the promotion fragment from the URL before the confirmation request completes', async () => {
    const pending = new Subject<{
      succeeded: boolean;
      alreadyApplied: boolean;
      role: 'Seller';
      emailConfirmationPending: boolean;
      message: string;
    }>();

    await configure({ promotionToken: 'opaque-token', role: 'Seller' });
    authServiceMock.confirmRolePromotion.mockReturnValue(pending.asObservable());

    fixture.detectChanges();

    // Read once into memory, then stripped immediately: the token is not kept in the address bar,
    // history, storage or logs while the POST is still in flight.
    expect(authServiceMock.confirmRolePromotion).toHaveBeenCalledWith('opaque-token');
    expect(window.location.search).not.toContain('promotionToken');
    expect(window.location.hash).not.toContain('promotionToken');
    expect(window.location.hash).toBe('');
    expect(JSON.stringify({ ...localStorage })).not.toContain('opaque-token');
    expect(JSON.stringify({ ...sessionStorage })).not.toContain('opaque-token');
    expect(component.promotionState()).toBe('confirming');

    pending.next({
      succeeded: true,
      alreadyApplied: false,
      role: 'Seller',
      emailConfirmationPending: false,
      message: 'ok',
    });
    pending.complete();
    fixture.detectChanges();

    expect(component.promotionState()).toBe('succeeded');
    expect(window.location.search).not.toContain('promotionToken');
    expect(window.location.hash).not.toContain('promotionToken');
  });

  it('uses the in-memory fragment captured before Angular boots and clears it after reading', async () => {
    (window as unknown as { __urbeatPromotionFragment?: unknown }).__urbeatPromotionFragment = {
      token: 'captured-token',
      role: 'Seller',
    };
    await configure({});
    authServiceMock.confirmRolePromotion.mockReturnValue(
      of({
        succeeded: true,
        alreadyApplied: false,
        role: 'Seller',
        emailConfirmationPending: false,
        message: 'ok',
      }),
    );

    fixture.detectChanges();

    expect(authServiceMock.confirmRolePromotion).toHaveBeenCalledWith('captured-token');
    expect(component.promotionRole()).toBe('Seller');
    expect(
      (window as unknown as { __urbeatPromotionFragment?: unknown }).__urbeatPromotionFragment,
    ).toBeUndefined();
    expect(JSON.stringify({ ...localStorage })).not.toContain('captured-token');
    expect(JSON.stringify({ ...sessionStorage })).not.toContain('captured-token');
  });

  it('does not accept a legacy query-string promotion token', async () => {
    await configure({ promotionToken: 'query-token', role: 'Seller' }, null, false);

    fixture.detectChanges();

    expect(authServiceMock.confirmRolePromotion).not.toHaveBeenCalled();
    expect(component.promotionMode()).toBe(false);
  });

  it('shows a promotion-pending message without asking for the existing password', async () => {
    await configure({
      promotionPending: 'true',
      role: 'Seller',
      email: 'cliente@urbeat.local',
    });

    fixture.detectChanges();

    expect(component.promotionMode()).toBe(true);
    expect(component.promotionPending()).toBe(true);
    expect(component.promotionRole()).toBe('Seller');
    expect(component.email()).toBe('cliente@urbeat.local');

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Confirme seu novo acesso de lojista');
    expect(text).toContain('30 minutos');
    expect(text).toContain('apenas uma vez');
    expect(text).toContain('nunca solicita sua senha por e-mail');
    expect(authServiceMock.confirmRolePromotion).not.toHaveBeenCalled();
  });

  it('confirms an explicit promotion token instead of treating it as an e-mail change', async () => {
    await configure({ promotionToken: 'opaque-token', role: 'Seller' });
    authServiceMock.confirmRolePromotion.mockReturnValue(
      of({
        succeeded: true,
        alreadyApplied: false,
        role: 'Seller',
        emailConfirmationPending: false,
        message: 'ok',
      }),
    );

    fixture.detectChanges();

    expect(authServiceMock.confirmRolePromotion).toHaveBeenCalledWith('opaque-token');
    expect(component.promotionState()).toBe('succeeded');
    expect(component.promotionRole()).toBe('Seller');

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Novo acesso de lojista ativado!');
    expect(text).toContain('Sua senha do outro acesso continua a mesma.');

    // Seller has a real login route: the CTA must navigate there, not to the landing page.
    const loginLink = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>('a.btn-go');
    expect(loginLink).not.toBeNull();
    expect(loginLink!.getAttribute('href')).toBe('/login-vendedor');
    expect(loginLink!.textContent).toContain('Ir para o login');
  });

  it('does not pretend the landing page is a customer login when a customer access is confirmed', async () => {
    await configure({ promotionToken: 'opaque-token', role: 'Customer' });
    authServiceMock.confirmRolePromotion.mockReturnValue(
      of({
        succeeded: true,
        alreadyApplied: false,
        role: 'Customer',
        emailConfirmationPending: false,
        message: 'ok',
      }),
    );

    fixture.detectChanges();

    expect(component.promotionState()).toBe('succeeded');
    const host = fixture.nativeElement as HTMLElement;
    // There is no customer login page in the app; the page must not link to the landing page as if
    // it were one. It gives explicit guidance for the store where the person orders instead.
    expect(host.querySelector('a.btn-go')).toBeNull();
    expect(host.textContent).toContain('entre na loja em que você faz seus pedidos');
  });

  it('reports a replayed promotion as already applied', async () => {
    await configure({ promotionToken: 'opaque-token', role: 'Customer' });
    authServiceMock.confirmRolePromotion.mockReturnValue(
      of({
        succeeded: true,
        alreadyApplied: true,
        role: 'Customer',
        emailConfirmationPending: false,
        message: 'ok',
      }),
    );

    fixture.detectChanges();

    expect(component.promotionState()).toBe('already');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Acesso de cliente já confirmado',
    );
  });

  it('shows a safe error for an expired or replayed promotion link', async () => {
    await configure({ promotionToken: 'expired-token', role: 'Seller' });
    authServiceMock.confirmRolePromotion.mockReturnValue(
      throwError(() => ({ status: 400 })),
    );

    fixture.detectChanges();

    expect(component.promotionState()).toBe('error');
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Não foi possível confirmar o novo acesso');
    expect(text).toContain('sua senha atual permanece a mesma');

    const retryLink = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>(
      'a.btn-go.outline',
    );
    expect(retryLink!.getAttribute('href')).toBe('/cadastro');
  });

  it('guides a customer retry without linking to a seller-only registration page', async () => {
    await configure({ promotionToken: 'expired-token', role: 'Customer' });
    authServiceMock.confirmRolePromotion.mockReturnValue(
      throwError(() => ({ status: 400 })),
    );

    fixture.detectChanges();

    expect(component.promotionState()).toBe('error');
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('a.btn-go.outline')).toBeNull();
    expect(host.textContent).toContain('pela loja em que você faz seus pedidos');
  });

  it('keeps the regular e-mail confirmation flow when no promotion parameters are present', async () => {
    await configure({ email: 'novo@urbeat.local', userId: 'user-1' });

    fixture.detectChanges();

    expect(component.promotionMode()).toBe(false);
    expect(component.email()).toBe('novo@urbeat.local');
    expect(component.userId()).toBe('user-1');
    expect(authServiceMock.confirmRolePromotion).not.toHaveBeenCalled();
  });

  describe('resend cooldown', () => {
    it('leaves the first resend immediately available on normal entry', async () => {
      await configure({ email: 'novo@urbeat.local', userId: 'user-1' });
      useFakeClock();
      authServiceMock.resendConfirmation.mockReturnValue(
        of<ResendResponse>({ succeeded: true, message: 'ok' }),
      );

      fixture.detectChanges();

      // Opening the page is not a resend: there is no accepted request, so a cooldown would block
      // the one action the person needs when the confirmation e-mail never arrived.
      expect(component.resendCooldownActive()).toBe(false);
      expect(component.secondsLeft()).toBe(0);
      expect(component.showChangeEmail()).toBe(false);

      const host = fixture.nativeElement as HTMLElement;
      expect(host.textContent).not.toContain('Você poderá solicitar um novo e-mail em');

      const resendButton = host.querySelector<HTMLButtonElement>('.btn-resend');
      expect(resendButton).not.toBeNull();
      expect(resendButton!.disabled).toBe(false);
      expect(resendButton!.textContent).toContain('Reenviar e-mail');

      resendButton!.click();
      fixture.detectChanges();

      expect(authServiceMock.resendConfirmation).toHaveBeenCalledTimes(1);
      expect(authServiceMock.resendConfirmation).toHaveBeenCalledWith({
        email: 'novo@urbeat.local',
      });
    });

    it('unlocks the change-email section and keeps the resend available when the cooldown expires', async () => {
      await configure({ email: 'novo@urbeat.local', userId: 'user-1' });
      useFakeClock();
      authServiceMock.resendConfirmation.mockReturnValue(
        of<ResendResponse>({ succeeded: true, message: 'ok' }),
      );

      fixture.detectChanges();
      component.resend();
      fixture.detectChanges();
      expect(component.secondsLeft()).toBe(120);

      jest.advanceTimersByTime(120_000);
      fixture.detectChanges();

      expect(component.secondsLeft()).toBe(0);
      expect(component.resendCooldownActive()).toBe(false);
      expect(component.showChangeEmail()).toBe(true);

      // The interval must stop at zero: further ticks cannot make the value negative or
      // re-trigger the change-email state.
      jest.advanceTimersByTime(10_000);
      fixture.detectChanges();
      expect(component.secondsLeft()).toBe(0);
      expect(component.showChangeEmail()).toBe(true);

      const host = fixture.nativeElement as HTMLElement;
      expect(host.querySelector('.change-email-section')).not.toBeNull();
      const resendButton = host.querySelector<HTMLButtonElement>(
        '.change-email-section .btn-resend',
      );
      expect(resendButton).not.toBeNull();
      expect(resendButton!.disabled).toBe(false);
      expect(resendButton!.textContent).toContain('Reenviar e-mail');
    });

    it('starts the cooldown only after an accepted resend and never claims delivery', async () => {
      await configure({ email: 'novo@urbeat.local', userId: 'user-1' });
      useFakeClock();
      authServiceMock.resendConfirmation.mockReturnValue(
        of<ResendResponse>({
          succeeded: true,
          message: 'Se o e-mail estiver cadastrado, um link de confirmação foi enviado.',
          resendAvailableAtUtc: new Date(Date.now() + 120_000).toISOString(),
        }),
      );

      fixture.detectChanges();
      expect(component.secondsLeft()).toBe(0);

      component.resend();
      fixture.detectChanges();

      expect(component.secondsLeft()).toBe(120);
      expect(component.showChangeEmail()).toBe(false);
      expect(component.resending()).toBe(false);
      expect(toastMock.showSuccess).toHaveBeenCalledWith(
        expect.stringContaining('Se o e-mail estiver cadastrado'),
      );
      expect(toastMock.showSuccess.mock.calls[0][0]).not.toContain('reenviado');

      jest.advanceTimersByTime(1000);
      fixture.detectChanges();
      expect(component.secondsLeft()).toBe(119);

      // A duplicated interval would decrement twice per second.
      jest.advanceTimersByTime(1000);
      fixture.detectChanges();
      expect(component.secondsLeft()).toBe(118);
    });

    it('restarts the cooldown when a new resend is accepted after the previous one expired', async () => {
      await configure({ email: 'novo@urbeat.local', userId: 'user-1' });
      useFakeClock();
      authServiceMock.resendConfirmation.mockReturnValue(
        of<ResendResponse>({ succeeded: true, message: 'ok' }),
      );

      fixture.detectChanges();
      component.resend();
      fixture.detectChanges();
      jest.advanceTimersByTime(120_000);
      fixture.detectChanges();
      expect(component.secondsLeft()).toBe(0);
      expect(component.showChangeEmail()).toBe(true);

      component.resend();
      fixture.detectChanges();

      expect(authServiceMock.resendConfirmation).toHaveBeenCalledTimes(2);
      expect(component.secondsLeft()).toBe(120);
      expect(component.showChangeEmail()).toBe(false);

      jest.advanceTimersByTime(1000);
      fixture.detectChanges();
      expect(component.secondsLeft()).toBe(119);
    });

    it('rejects a direct resend call during the cooldown and accepts it again after it expires', async () => {
      await configure({ email: 'novo@urbeat.local', userId: 'user-1' });
      useFakeClock();
      authServiceMock.resendConfirmation.mockReturnValue(
        of<ResendResponse>({ succeeded: true, message: 'ok' }),
      );

      fixture.detectChanges();
      component.resend();
      fixture.detectChanges();

      expect(authServiceMock.resendConfirmation).toHaveBeenCalledTimes(1);
      expect(component.resendCooldownActive()).toBe(true);

      // The template disables the button, but the accepted-request cooldown must also block a
      // direct call (programmatic click, stale binding or a second surface) from issuing another
      // request; the guard cannot live only in `[disabled]`.
      component.resend();
      fixture.detectChanges();
      expect(authServiceMock.resendConfirmation).toHaveBeenCalledTimes(1);
      expect(component.resending()).toBe(false);

      jest.advanceTimersByTime(120_000);
      fixture.detectChanges();
      expect(component.resendCooldownActive()).toBe(false);

      component.resend();
      fixture.detectChanges();
      expect(authServiceMock.resendConfirmation).toHaveBeenCalledTimes(2);
      expect(component.secondsLeft()).toBe(120);
    });

    it('uses the deadline returned by the server instead of the local fallback', async () => {
      await configure({ email: 'novo@urbeat.local', userId: 'user-1' });
      useFakeClock();
      authServiceMock.resendConfirmation.mockReturnValue(
        of<ResendResponse>({
          succeeded: true,
          message: 'ok',
          resendAvailableAtUtc: new Date(Date.now() + 30_000).toISOString(),
        }),
      );

      fixture.detectChanges();
      component.resend();
      fixture.detectChanges();

      expect(component.secondsLeft()).toBe(30);

      jest.advanceTimersByTime(1000);
      fixture.detectChanges();
      expect(component.secondsLeft()).toBe(29);
    });

    it('falls back to 120 seconds when an older server response omits the deadline', async () => {
      await configure({ email: 'novo@urbeat.local', userId: 'user-1' });
      useFakeClock();
      authServiceMock.resendConfirmation.mockReturnValue(
        of<ResendResponse>({ succeeded: true, message: 'ok' }),
      );

      fixture.detectChanges();
      component.resend();
      fixture.detectChanges();

      expect(component.secondsLeft()).toBe(120);
    });

    it('does not start the cooldown when the resend request fails', async () => {
      await configure({ email: 'novo@urbeat.local', userId: 'user-1' });
      useFakeClock();
      authServiceMock.resendConfirmation.mockReturnValue(
        throwError(() => ({ status: 500 })),
      );

      fixture.detectChanges();
      component.resend();
      fixture.detectChanges();

      expect(component.secondsLeft()).toBe(0);
      expect(component.resendCooldownActive()).toBe(false);
      // A failed request was not accepted, so the change-email section is not unlocked and the
      // regular resend button stays available for an immediate retry.
      expect(component.showChangeEmail()).toBe(false);
      expect(component.resending()).toBe(false);
      expect(toastMock.showError).toHaveBeenCalledWith('Erro ao reenviar. Tente novamente.');

      jest.advanceTimersByTime(10_000);
      fixture.detectChanges();
      expect(component.secondsLeft()).toBe(0);
      expect(component.showChangeEmail()).toBe(false);
    });

    it('allows retrying after an error and starts the cooldown only on success', async () => {
      await configure({ email: 'novo@urbeat.local', userId: 'user-1' });
      useFakeClock();
      authServiceMock.resendConfirmation
        .mockReturnValueOnce(throwError(() => ({ status: 500 })))
        .mockReturnValueOnce(
          of<ResendResponse>({ succeeded: true, message: 'ok' }),
        );

      fixture.detectChanges();

      component.resend();
      fixture.detectChanges();

      expect(component.secondsLeft()).toBe(0);
      expect(component.resending()).toBe(false);

      component.resend();
      fixture.detectChanges();

      expect(authServiceMock.resendConfirmation).toHaveBeenCalledTimes(2);
      expect(component.secondsLeft()).toBe(120);
      expect(component.showChangeEmail()).toBe(false);

      jest.advanceTimersByTime(1000);
      fixture.detectChanges();
      expect(component.secondsLeft()).toBe(119);
    });

    it('releases the button and shows a specific message on HTTP 429 without starting a timer', async () => {
      await configure({ email: 'novo@urbeat.local', userId: 'user-1' });
      useFakeClock();
      authServiceMock.resendConfirmation.mockReturnValue(
        throwError(() => ({ status: 429 })),
      );

      fixture.detectChanges();
      component.resend();
      fixture.detectChanges();

      expect(component.secondsLeft()).toBe(0);
      expect(component.resendCooldownActive()).toBe(false);
      expect(component.resending()).toBe(false);
      expect(toastMock.showWarning).toHaveBeenCalledWith(
        expect.stringContaining('Muitas solicitações'),
      );
      expect(toastMock.showError).not.toHaveBeenCalled();

      jest.advanceTimersByTime(10_000);
      fixture.detectChanges();
      expect(component.secondsLeft()).toBe(0);
    });

    it('ignores repeated clicks while the resend request is in flight', async () => {
      await configure({ email: 'novo@urbeat.local', userId: 'user-1' });
      useFakeClock();
      const pending = new Subject<ResendResponse>();
      authServiceMock.resendConfirmation.mockReturnValue(pending.asObservable());

      fixture.detectChanges();
      component.resend();
      component.resend();

      expect(authServiceMock.resendConfirmation).toHaveBeenCalledTimes(1);
      expect(component.resending()).toBe(true);

      pending.next({ succeeded: true, message: 'ok' });
      pending.complete();
      fixture.detectChanges();

      expect(component.resending()).toBe(false);
      expect(component.secondsLeft()).toBe(120);

      jest.advanceTimersByTime(1000);
      fixture.detectChanges();
      expect(component.secondsLeft()).toBe(119);
    });

    it('restarts the cooldown after a successful e-mail change', async () => {
      sessionStorage.setItem('urbeat.emailChangeChallenge:old@urbeat.local', 'challenge');
      await configure({ email: 'old@urbeat.local', userId: 'user-1' });
      useFakeClock();
      authServiceMock.resendConfirmation.mockReturnValue(
        of<ResendResponse>({ succeeded: true, message: 'ok' }),
      );
      authServiceMock.updateEmail.mockReturnValue(of({ message: 'ok' }));

      fixture.detectChanges();
      component.resend();
      fixture.detectChanges();
      jest.advanceTimersByTime(120_000);
      fixture.detectChanges();
      expect(component.showChangeEmail()).toBe(true);

      component.newEmail.set('new@urbeat.local');
      component.changeEmail();
      fixture.detectChanges();

      expect(component.email()).toBe('new@urbeat.local');
      expect(component.showChangeEmail()).toBe(false);
      expect(component.secondsLeft()).toBe(120);

      jest.advanceTimersByTime(1000);
      fixture.detectChanges();
      expect(component.secondsLeft()).toBe(119);
    });

    it('clears the interval on destroy and leaves the first resend available when recreated', async () => {
      await configure({ email: 'novo@urbeat.local', userId: 'user-1' });
      useFakeClock();
      authServiceMock.resendConfirmation.mockReturnValue(
        of<ResendResponse>({ succeeded: true, message: 'ok' }),
      );

      fixture.detectChanges();
      component.resend();
      fixture.detectChanges();
      expect(component.secondsLeft()).toBe(120);

      jest.advanceTimersByTime(5000);
      fixture.detectChanges();
      expect(component.secondsLeft()).toBe(115);

      fixture.destroy();

      // If ngOnDestroy did not clear the interval, the destroyed component would keep ticking.
      jest.advanceTimersByTime(10_000);
      expect(component.secondsLeft()).toBe(115);

      fixture = TestBed.createComponent(EmailConfirmationPageComponent);
      component = fixture.componentInstance;
      fixture.detectChanges();

      // Reload/navigation must not persist or resurrect the previous cooldown: the server rate
      // limiter stays authoritative and the first resend is available again.
      expect(component.secondsLeft()).toBe(0);
      expect(component.resendCooldownActive()).toBe(false);
      jest.advanceTimersByTime(1000);
      fixture.detectChanges();
      expect(component.secondsLeft()).toBe(0);
    });

    it('does not start the regular-flow timer while confirming a promotion', async () => {
      await configure({ promotionToken: 'opaque-token', role: 'Seller' });
      useFakeClock();
      authServiceMock.confirmRolePromotion.mockReturnValue(
        of({
          succeeded: true,
          alreadyApplied: false,
          role: 'Seller',
          emailConfirmationPending: false,
          message: 'ok',
        }),
      );

      fixture.detectChanges();

      expect(component.promotionMode()).toBe(true);
      expect(component.secondsLeft()).toBe(0);

      jest.advanceTimersByTime(10_000);
      fixture.detectChanges();
      expect(component.secondsLeft()).toBe(0);
    });

    it('does not start the regular-flow timer for a promotion-pending message', async () => {
      await configure({
        promotionPending: 'true',
        role: 'Seller',
        email: 'cliente@urbeat.local',
      });
      useFakeClock();

      fixture.detectChanges();

      expect(component.promotionPending()).toBe(true);
      expect(component.secondsLeft()).toBe(0);

      jest.advanceTimersByTime(10_000);
      fixture.detectChanges();
      expect(component.secondsLeft()).toBe(0);
    });
  });
});
