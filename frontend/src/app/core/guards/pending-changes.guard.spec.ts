import { TestBed } from '@angular/core/testing';
import { pendingChangesGuard } from './pending-changes.guard';

describe('pendingChangesGuard', () => {
  const originalConfirm = window.confirm;

  afterEach(() => {
    window.confirm = originalConfirm;
  });

  it('allows navigation when component has no pending changes', () => {
    const result = TestBed.runInInjectionContext(() => pendingChangesGuard({ hasUnsavedChanges: () => false }, null as never, null as never, null as never));

    expect(result).toBe(true);
  });

  it('asks confirmation when component has pending changes', () => {
    window.confirm = jest.fn(() => false);

    const result = TestBed.runInInjectionContext(() => pendingChangesGuard({ hasUnsavedChanges: () => true }, null as never, null as never, null as never));

    expect(window.confirm).toHaveBeenCalledWith('Voce tem alteracoes nao salvas. Deseja sair mesmo assim?');
    expect(result).toBe(false);
  });
});
