import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AdminAccountService } from '../../../core/services/admin-account.service';
import { AuthService } from '../../../core/services/auth.service';
import {
  AdminAdministrator,
  PagedAdminAdministrators,
} from '../../../shared/models/admin-account.model';
import { AdminAdministratorsSectionComponent } from './admin-administrators-section.component';

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

function page(items: AdminAdministrator[], total = items.length): PagedAdminAdministrators {
  return { page: 1, pageSize: 20, totalItems: total, totalPages: 1, items };
}

describe('AdminAdministratorsSectionComponent', () => {
  let fixture: ComponentFixture<AdminAdministratorsSectionComponent>;
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

  function create(items: AdminAdministrator[]): void {
    service.list.mockReturnValue(of(page(items)));
    fixture = TestBed.createComponent(AdminAdministratorsSectionComponent);
    fixture.detectChanges();
  }

  function buttonByText(text: string): HTMLButtonElement | undefined {
    return Array.from(fixture.nativeElement.querySelectorAll('button')).find(
      (button) => (button as HTMLButtonElement).textContent?.trim() === text,
    ) as HTMLButtonElement | undefined;
  }

  beforeEach(() => {
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
      imports: [AdminAdministratorsSectionComponent],
      providers: [
        provideRouter([]),
        { provide: AdminAccountService, useValue: service },
        { provide: AuthService, useValue: { logout: jest.fn() } },
      ],
    });
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
    fixture = TestBed.createComponent(AdminAdministratorsSectionComponent);
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
  }));

  it('invites an administrator and announces the result', () => {
    create([]);

    const input = fixture.nativeElement.querySelector('#invite-email') as HTMLInputElement;
    input.value = 'novo@exemplo.com';
    input.dispatchEvent(new Event('input'));
    fixture.componentInstance.invite();
    fixture.detectChanges();

    expect(service.invite).toHaveBeenCalledWith('novo@exemplo.com');
    expect(fixture.nativeElement.textContent).toContain('Convite enviado para novo@exemplo.com');
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
