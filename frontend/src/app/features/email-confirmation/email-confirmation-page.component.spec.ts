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
});
