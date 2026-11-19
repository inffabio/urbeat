import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { AdminAccountService } from '../../core/services/admin-account.service';
import { AuthService } from '../../core/services/auth.service';
import {
  AdminAdministrator,
  InviteAdminAccountResponse,
  PagedAdminAdministrators,
} from '../../shared/models/admin-account.model';
import { AdminAdministratorsPageComponent } from './admin-administrators-page.component';

function administrator(overrides: Partial<AdminAdministrator> = {}): AdminAdministrator {
  return {
    id: 'id-1',
    email: 'admin@urbeat.local',
    status: 'Active',
    isEmailConfirmed: true,
    isBlocked: false,
    isCurrentUser: false,
    isLastActiveAdmin: false,
    createdAtUtc: '2026-01-01T00:00:00Z',
    invitation: null,
    ...overrides,
  };
}

function page(
  items: AdminAdministrator[],
  overrides: Partial<PagedAdminAdministrators> = {},
): PagedAdminAdministrators {
  return {
    page: 1,
    pageSize: 20,
    totalItems: items.length,
    totalPages: items.length > 0 ? 1 : 0,
    items,
    ...overrides,
  };
}

describe('AdminAdministratorsPageComponent', () => {
  let fixture: ComponentFixture<AdminAdministratorsPageComponent>;
  let service: {
    list: jest.Mock;
    invite: jest.Mock;
    updateEmail: jest.Mock;
    resendInvitation: jest.Mock;
    cancelInvitation: jest.Mock;
    requestPasswordReset: jest.Mock;
    block: jest.Mock;
    unblock: jest.Mock;
    remove: jest.Mock;
  };
  let router: { navigate: jest.Mock; navigateByUrl: jest.Mock };
  let queryParams: Record<string, string>;

  function create(items: AdminAdministrator[], overrides: Partial<PagedAdminAdministrators> = {}): void {
    service.list.mockReturnValue(of(page(items, overrides)));
    fixture = TestBed.createComponent(AdminAdministratorsPageComponent);
    fixture.detectChanges();
  }

  function buttonByText(text: string): HTMLButtonElement | undefined {
    return Array.from(fixture.nativeElement.querySelectorAll('button')).find(
      (button) => (button as HTMLButtonElement).textContent?.trim() === text,
    ) as HTMLButtonElement | undefined;
  }

  beforeEach(() => {
    queryParams = {};
    router = {
      navigate: jest.fn(() => Promise.resolve(true)),
      navigateByUrl: jest.fn(() => Promise.resolve(true)),
    };
    service = {
      list: jest.fn(() => of(page([]))),
      invite: jest.fn(() => of({ administratorId: 'id-2', email: 'novo@exemplo.com' })),
      updateEmail: jest.fn(() => of(administrator())),
      resendInvitation: jest.fn(() => of({})),
      cancelInvitation: jest.fn(() => of(void 0)),
      requestPasswordReset: jest.fn(() => of({})),
      block: jest.fn(() => of(administrator({ isBlocked: true }))),
      unblock: jest.fn(() => of(administrator())),
      remove: jest.fn(() => of(void 0)),
    };

    TestBed.configureTestingModule({
      imports: [AdminAdministratorsPageComponent],
      providers: [
        { provide: AdminAccountService, useValue: service },
        { provide: AuthService, useValue: { logout: jest.fn() } },
        { provide: Router, useValue: router },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              get queryParamMap() {
                return convertToParamMap(queryParams);
              },
            },
          },
        },
      ],
    });
  });

  it('renders the dedicated administrators screen instead of a settings tab', () => {
    create([administrator()]);

    const heading = fixture.nativeElement.querySelector(
      '#admin-administrators-title',
    ) as HTMLElement | null;

    expect(heading).not.toBeNull();
    expect(heading?.textContent?.trim()).toBe('Administradores');
    expect(fixture.nativeElement.querySelector('app-admin-administrators-section')).toBeNull();
  });

  it('loads the list on creation and shows badges', () => {
    create([
      administrator(),
      administrator({
        id: 'id-2',
        email: 'novo@exemplo.com',
        status: 'Invited',
        isEmailConfirmed: false,
        invitation: {
          id: 'inv-1',
          purpose: 'AccountInvitation',
          state: 'Pending',
          expiresAtUtc: new Date(Date.now() + 23 * 3600_000).toISOString(),
          emailSentAtUtc: null,
          createdAtUtc: new Date().toISOString(),
        },
      }),
    ]);

    expect(service.list).toHaveBeenCalledWith({
      status: 'all',
      search: '',
      page: 1,
      pageSize: 20,
    });
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('admin@urbeat.local');
    expect(text).toContain('novo@exemplo.com');
    expect(text).toContain('Ativo');
    expect(text).toContain('Convidado');
    expect(text).toContain('Convite em envio');
    expect(text).toContain('expira em 23h');
  });

  it('shows the empty state with guidance to invite', () => {
    create([]);

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Nenhum administrador encontrado.');
    expect(text).toContain('Convide um administrador');
  });

  it('shows the load error with a retry action', () => {
    service.list.mockReturnValue(throwError(() => ({ error: { detail: 'Falha de conexão.' } })));
    fixture = TestBed.createComponent(AdminAdministratorsPageComponent);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Não foi possível carregar os administradores.');
    expect(text).toContain('Falha de conexão.');

    service.list.mockReturnValue(of(page([administrator()])));
    buttonByText('Tentar novamente')?.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('admin@urbeat.local');
  });

  it('debounces the search and resets the page', fakeAsync(() => {
    create([administrator()]);
    service.list.mockClear();

    const input = fixture.nativeElement.querySelector('#admins-search') as HTMLInputElement;
    input.value = 'novo';
    input.dispatchEvent(new Event('input'));
    tick(299);
    expect(service.list).not.toHaveBeenCalled();
    tick(1);

    expect(service.list).toHaveBeenCalledWith(
      expect.objectContaining({ search: 'novo', page: 1 }),
    );
    expect(router.navigate).toHaveBeenCalled();
  }));

  it('restores filters and page from the query string', () => {
    queryParams = { page: '2', status: 'blocked', search: 'adm' };

    fixture = TestBed.createComponent(AdminAdministratorsPageComponent);
    fixture.detectChanges();

    expect(service.list).toHaveBeenCalledWith({
      status: 'blocked',
      search: 'adm',
      page: 2,
      pageSize: 20,
    });

    const select = fixture.nativeElement.querySelector('#admins-status') as HTMLSelectElement;
    expect(select.value).toBe('blocked');
  });

  it('paginates with the expected query parameters and keeps the URL shareable', () => {
    create([administrator()], { totalItems: 42, totalPages: 3 });
    service.list.mockClear();

    fixture.componentInstance.goToPage(2);
    fixture.detectChanges();

    expect(service.list).toHaveBeenCalledWith(expect.objectContaining({ page: 2 }));
    expect(router.navigate).toHaveBeenCalledWith(
      [],
      expect.objectContaining({
        replaceUrl: true,
        queryParams: expect.objectContaining({ page: 2 }),
      }),
    );
  });

  it('returns to the last valid page when the current page becomes empty', () => {
    queryParams = { page: '3' };
    service.list
      .mockReturnValueOnce(
        of(page([administrator({ id: 'id-3' })], { page: 3, totalItems: 41, totalPages: 3 })),
      )
      .mockReturnValueOnce(of(page([], { page: 3, totalItems: 40, totalPages: 2 })))
      .mockReturnValueOnce(
        of(
          page([administrator({ id: 'id-2', email: 'pagina2@urbeat.local' })], {
            page: 2,
            totalItems: 40,
            totalPages: 2,
          }),
        ),
      );

    fixture = TestBed.createComponent(AdminAdministratorsPageComponent);
    fixture.detectChanges();

    buttonByText('Remover')!.click();
    fixture.detectChanges();
    buttonByText('Confirmar remoção')!.click();
    fixture.detectChanges();

    expect(service.remove).toHaveBeenCalledWith('id-3', undefined);
    expect(fixture.componentInstance.page()).toBe(2);
    expect(service.list).toHaveBeenLastCalledWith(expect.objectContaining({ page: 2 }));
    expect(fixture.nativeElement.textContent).toContain('pagina2@urbeat.local');
  });

  it('invites an administrator and announces the result', () => {
    create([]);

    const input = fixture.nativeElement.querySelector('#invite-email') as HTMLInputElement;
    input.value = 'novo@exemplo.com';
    input.dispatchEvent(new Event('input'));
    fixture.componentInstance.invite();
    fixture.detectChanges();

    expect(service.invite).toHaveBeenCalledWith('novo@exemplo.com');
    expect(fixture.nativeElement.textContent).toContain('Convite criado para novo@exemplo.com');
  });

  it('submits the invite form through the DOM instead of letting the browser reload the page', () => {
    create([]);

    const input = fixture.nativeElement.querySelector('#invite-email') as HTMLInputElement;
    input.value = 'inffabio@yahoo.com.br';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const form = fixture.nativeElement.querySelector('form.invite-form') as HTMLFormElement;
    const submit = new Event('submit', { bubbles: true, cancelable: true });
    form.dispatchEvent(submit);
    fixture.detectChanges();

    expect(service.invite).toHaveBeenCalledWith('inffabio@yahoo.com.br');
    expect(submit.defaultPrevented).toBe(true);
  });

  it('ignores a repeated submit while the invitation request is still in flight', () => {
    const pending = new Subject<InviteAdminAccountResponse>();
    service.invite.mockReturnValue(pending.asObservable());
    create([]);

    const input = fixture.nativeElement.querySelector('#invite-email') as HTMLInputElement;
    input.value = 'novo@exemplo.com';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    // Two activations can reach the handler before change detection disables the button
    // (production runs with eventCoalescing: true, so the disabled binding is deferred).
    const form = fixture.nativeElement.querySelector('form.invite-form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    fixture.detectChanges();

    expect(service.invite).toHaveBeenCalledTimes(1);

    pending.next({
      administratorId: 'id-2',
      email: 'novo@exemplo.com',
      status: 'Invited',
      invitation: {
        id: 'inv-2',
        purpose: 'AccountInvitation',
        state: 'Pending',
        expiresAtUtc: new Date(Date.now() + 3600_000).toISOString(),
        emailSentAtUtc: null,
        createdAtUtc: new Date().toISOString(),
      },
    });
    pending.complete();
  });

  it('submits the e-mail edit form through the DOM instead of letting the browser reload the page', () => {
    create([administrator({ id: 'id-1', email: 'admin@urbeat.local' })]);

    buttonByText('Editar e-mail')!.click();
    fixture.detectChanges();

    const input = fixture.nativeElement.querySelector(
      '#edit-email-id-1',
    ) as HTMLInputElement;
    input.value = 'novo.admin@urbeat.local';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const form = fixture.nativeElement.querySelector('form.edit-form') as HTMLFormElement;
    const submit = new Event('submit', { bubbles: true, cancelable: true });
    form.dispatchEvent(submit);
    fixture.detectChanges();

    expect(service.updateEmail).toHaveBeenCalledWith('id-1', 'novo.admin@urbeat.local');
    expect(submit.defaultPrevented).toBe(true);
  });

  it('alerts the operator when the invite was created but the e-mail could not be queued', () => {
    service.invite.mockReturnValue(
      of({
        administratorId: 'id-2',
        email: 'inffabio@yahoo.com.br',
        status: 'Invited',
        invitation: {
          id: 'inv-2',
          purpose: 'AccountInvitation',
          state: 'Pending',
          expiresAtUtc: new Date(Date.now() + 24 * 3600_000).toISOString(),
          emailSentAtUtc: null,
          createdAtUtc: new Date().toISOString(),
        },
        emailQueued: false,
      }),
    );
    create([]);

    const input = fixture.nativeElement.querySelector('#invite-email') as HTMLInputElement;
    input.value = 'inffabio@yahoo.com.br';
    input.dispatchEvent(new Event('input'));
    fixture.componentInstance.invite();
    fixture.detectChanges();

    const feedback = fixture.nativeElement.querySelector('.feedback') as HTMLElement;
    expect(feedback.getAttribute('role')).toBe('alert');
    expect(feedback.classList).toContain('is-error');
    expect(feedback.textContent).toContain('não foi enfileirado');
    expect(feedback.textContent).toContain('Reenviar convite');
  });

  it('alerts the operator when a resend could not be queued', () => {
    service.resendInvitation.mockReturnValue(
      of({
        invitationId: 'inv-4',
        expiresAtUtc: new Date(Date.now() + 3600_000).toISOString(),
        emailDeliveryPending: true,
        emailQueued: false,
      }),
    );
    create([
      administrator({
        id: 'id-4',
        email: 'pendente@exemplo.com',
        status: 'Invited',
        isEmailConfirmed: false,
        invitation: {
          id: 'inv-4',
          purpose: 'AccountInvitation',
          state: 'Pending',
          expiresAtUtc: new Date(Date.now() + 3600_000).toISOString(),
          emailSentAtUtc: null,
          createdAtUtc: new Date().toISOString(),
        },
      }),
    ]);

    buttonByText('Reenviar convite')!.click();
    fixture.detectChanges();

    const feedback = fixture.nativeElement.querySelector('.feedback') as HTMLElement;
    expect(feedback.getAttribute('role')).toBe('alert');
    expect(feedback.textContent).toContain('não foi enfileirado');
  });

  it('shows the specific seller e-mail conflict returned by the API as an alert in the invite form', () => {
    service.invite.mockReturnValue(
      throwError(() => ({
        error: {
          code: 'SELLER_EMAIL_ALREADY_IN_USE',
          message:
            'Este e-mail já pertence a uma conta de lojista. O convite de administrador não pode ser enviado.',
        },
      })),
    );
    create([]);

    const input = fixture.nativeElement.querySelector('#invite-email') as HTMLInputElement;
    input.value = 'lojista@exemplo.com';
    input.dispatchEvent(new Event('input'));
    fixture.componentInstance.invite();
    fixture.detectChanges();

    const feedback = fixture.nativeElement.querySelector('.feedback') as HTMLElement;
    expect(feedback).not.toBeNull();
    expect(feedback.getAttribute('role')).toBe('alert');
    expect(feedback.getAttribute('aria-live')).toBe('assertive');
    expect(feedback.classList).toContain('is-error');
    expect(feedback.textContent).toContain(
      'Este e-mail já pertence a uma conta de lojista. O convite de administrador não pode ser enviado.',
    );
    expect(fixture.componentInstance.inviteEmail()).toBe('lojista@exemplo.com');
  });

  it('disables block and removal for the current user and the last active administrator', () => {
    create([
      administrator({ isCurrentUser: true }),
      administrator({ id: 'id-2', email: 'ultimo@urbeat.local', isLastActiveAdmin: true }),
    ]);

    const blockButtons = Array.from(
      fixture.nativeElement.querySelectorAll('button'),
    ).filter((button) => (button as HTMLButtonElement).textContent?.trim() === 'Bloquear') as HTMLButtonElement[];
    const removeButtons = Array.from(
      fixture.nativeElement.querySelectorAll('button'),
    ).filter((button) => (button as HTMLButtonElement).textContent?.trim() === 'Remover') as HTMLButtonElement[];

    expect(blockButtons).toHaveLength(2);
    expect(removeButtons).toHaveLength(2);
    expect(blockButtons.every((button) => button.disabled)).toBe(true);
    expect(removeButtons.every((button) => button.disabled)).toBe(true);
    expect(blockButtons[0].title).toContain('própria conta');
    expect(blockButtons[1].title).toContain('pelo menos um administrador ativo');
  });

  it('shows a server 409 message without inventing authorization in the client', () => {
    service.block.mockReturnValue(
      throwError(() => ({
        error: { code: 'LAST_ACTIVE_ADMIN', message: 'A plataforma precisa de um administrador.' },
      })),
    );
    create([administrator({ id: 'id-2', email: 'outro@urbeat.local' })]);

    buttonByText('Bloquear')!.click();
    fixture.detectChanges();
    buttonByText('Confirmar bloqueio')!.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(
      'A plataforma precisa de um administrador.',
    );
  });

  it('confirms before cancelling an invitation', () => {
    create([
      administrator({
        id: 'id-3',
        email: 'pendente@exemplo.com',
        status: 'Invited',
        isEmailConfirmed: false,
        invitation: {
          id: 'inv-3',
          purpose: 'AccountInvitation',
          state: 'Pending',
          expiresAtUtc: new Date(Date.now() + 3600_000).toISOString(),
          emailSentAtUtc: null,
          createdAtUtc: new Date().toISOString(),
        },
      }),
    ]);

    buttonByText('Cancelar convite')!.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('O link enviado para pendente@exemplo.com');

    buttonByText('Confirmar cancelamento')!.click();
    fixture.detectChanges();

    expect(service.cancelInvitation).toHaveBeenCalledWith('id-3');
  });

  it('opens a destructive confirmation as an alertdialog with initial focus on cancel', fakeAsync(() => {
    create([administrator({ id: 'id-2', email: 'outro@urbeat.local' })]);

    buttonByText('Bloquear')!.click();
    fixture.detectChanges();
    tick();

    const dialog = fixture.nativeElement.querySelector('[role="alertdialog"]') as HTMLElement;
    expect(dialog).not.toBeNull();
    expect(dialog.textContent).toContain('As sessões de outro@urbeat.local serão encerradas');
    expect(document.activeElement).toBe(buttonByText('Cancelar'));

    dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alertdialog"]')).toBeNull();
  }));
});
