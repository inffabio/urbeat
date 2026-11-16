import { TestBed } from '@angular/core/testing';
import { HttpParams } from '@angular/common/http';
import { of } from 'rxjs';
import { ApiService } from './api.service';
import { AdminAccountService } from './admin-account.service';
import { AdminAdministrator, PagedAdminAdministrators } from '../../shared/models/admin-account.model';

function administrator(overrides: Partial<AdminAdministrator> = {}): AdminAdministrator {
  return {
    id: 'admin-1',
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

describe('AdminAccountService', () => {
  let api: {
    get: jest.Mock;
    post: jest.Mock;
    put: jest.Mock;
    patch: jest.Mock;
    delete: jest.Mock;
  };
  let service: AdminAccountService;

  const page: PagedAdminAdministrators = {
    page: 1,
    pageSize: 20,
    totalItems: 1,
    totalPages: 1,
    items: [administrator()],
  };

  beforeEach(() => {
    api = {
      get: jest.fn(() => of(page)),
      post: jest.fn(() => of({})),
      put: jest.fn(() => of(administrator())),
      patch: jest.fn(() => of({})),
      delete: jest.fn(() => of(void 0)),
    };

    TestBed.configureTestingModule({
      providers: [{ provide: ApiService, useValue: api }],
    });

    service = TestBed.inject(AdminAccountService);
  });

  it('lists administrators with the documented query parameters', () => {
    service
      .list({ status: 'blocked', search: ' admin@ ', page: 2, pageSize: 10 })
      .subscribe((result) => expect(result).toBe(page));

    expect(api.get).toHaveBeenCalledWith(
      '/api/admin/administrators',
      expect.any(HttpParams),
    );

    const params = api.get.mock.calls[0][1] as HttpParams;
    expect(params.get('status')).toBe('blocked');
    expect(params.get('search')).toBe('admin@');
    expect(params.get('page')).toBe('2');
    expect(params.get('pageSize')).toBe('10');
  });

  it('omits the status filter when it is "all"', () => {
    service.list({ status: 'all', search: '   ' }).subscribe();

    const params = api.get.mock.calls[0][1] as HttpParams;
    expect(params.keys()).not.toContain('status');
    expect(params.keys()).not.toContain('search');
  });

  it('invites by e-mail', () => {
    service.invite('novo@exemplo.com').subscribe();

    expect(api.post).toHaveBeenCalledWith('/api/admin/administrators', {
      email: 'novo@exemplo.com',
    });
  });

  it('updates the e-mail, resends and cancels invitations', () => {
    service.updateEmail('id 1', 'novo@exemplo.com').subscribe();
    service.resendInvitation('id 1').subscribe();
    service.cancelInvitation('id 1').subscribe();

    expect(api.put).toHaveBeenCalledWith('/api/admin/administrators/id 1/email', {
      email: 'novo@exemplo.com',
    });
    expect(api.post).toHaveBeenCalledWith(
      '/api/admin/administrators/id 1/invitation/resend',
      {},
    );
    expect(api.delete).toHaveBeenCalledWith('/api/admin/administrators/id 1/invitation');
  });

  it('requests password reset, blocks, unblocks and removes', () => {
    service.requestPasswordReset('id 1').subscribe();
    service.block('id 1', ' motivo ').subscribe();
    service.unblock('id 1').subscribe();
    service.remove('id 1', 'removido por inatividade').subscribe();

    expect(api.post).toHaveBeenCalledWith('/api/admin/administrators/id 1/password-reset', {});
    expect(api.post).toHaveBeenCalledWith('/api/admin/administrators/id 1/block', {
      reason: 'motivo',
    });
    expect(api.post).toHaveBeenCalledWith('/api/admin/administrators/id 1/unblock', {});
    expect(api.delete).toHaveBeenCalledWith(
      '/api/admin/administrators/id 1?reason=removido%20por%20inatividade',
    );
  });

  it('validates and consumes public tokens', () => {
    service.validateInvitationToken('token-1').subscribe();
    service.acceptInvitation({
      token: 'token-1',
      newPassword: 'Senha1234',
      confirmPassword: 'Senha1234',
    }).subscribe();
    service.validatePasswordResetToken('token-2').subscribe();
    service.confirmPasswordReset({
      token: 'token-2',
      newPassword: 'Senha1234',
      confirmPassword: 'Senha1234',
    }).subscribe();

    expect(api.post).toHaveBeenCalledWith(
      '/api/auth/admin-account/invitation/validate',
      { token: 'token-1' },
    );
    expect(api.post).toHaveBeenCalledWith(
      '/api/auth/admin-account/invitation/accept',
      { token: 'token-1', newPassword: 'Senha1234', confirmPassword: 'Senha1234' },
    );
    expect(api.post).toHaveBeenCalledWith(
      '/api/auth/admin-account/password-reset/validate',
      { token: 'token-2' },
    );
    expect(api.post).toHaveBeenCalledWith(
      '/api/auth/admin-account/password-reset/confirm',
      { token: 'token-2', newPassword: 'Senha1234', confirmPassword: 'Senha1234' },
    );
  });
});
