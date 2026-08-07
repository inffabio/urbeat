import { CanDeactivateFn } from '@angular/router';

export interface PendingChangesComponent {
  hasUnsavedChanges(): boolean;
}

export const pendingChangesGuard: CanDeactivateFn<PendingChangesComponent> = (component) => {
  if (!component.hasUnsavedChanges()) return true;

  return window.confirm('Voce tem alteracoes nao salvas. Deseja sair mesmo assim?');
};
