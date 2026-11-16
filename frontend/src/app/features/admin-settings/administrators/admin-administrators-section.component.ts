import { CommonModule } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  OnInit,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, Subject, debounceTime, distinctUntilChanged } from 'rxjs';
import { AdminAccountService } from '../../../core/services/admin-account.service';
import { AuthService } from '../../../core/services/auth.service';
import { extractApiErrorMessage } from '../../../core/utils/api-error.helper';
import {
  AdminAccountStatus,
  AdminAdministrator,
  AdminInvitation,
} from '../../../shared/models/admin-account.model';

type SectionStatus = 'all' | 'active' | 'blocked' | 'invited';
type ConfirmKind = 'block' | 'remove' | 'cancel-invitation';

interface ConfirmAction {
  kind: ConfirmKind;
  item: AdminAdministrator;
}

interface Feedback {
  type: 'success' | 'error';
  message: string;
}

/**
 * Administrator management section rendered inside /admin/configuracoes. The list is loaded only
 * when the section is activated, so the parameters page keeps its original request profile.
 */
@Component({
  selector: 'app-admin-administrators-section',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './admin-administrators-section.component.html',
  styleUrl: './admin-administrators-section.component.scss',
})
export class AdminAdministratorsSectionComponent implements OnInit {
  private readonly service = inject(AdminAccountService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly searchChanges = new Subject<string>();

  @ViewChild('confirmCancel') private confirmCancel?: ElementRef<HTMLButtonElement>;

  readonly items = signal<AdminAdministrator[]>([]);
  readonly page = signal(1);
  readonly pageSize = signal(20);
  readonly totalItems = signal(0);
  readonly totalPages = signal(0);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly busyId = signal<string | null>(null);
  readonly feedback = signal<Feedback | null>(null);
  readonly status = signal<SectionStatus>('all');
  readonly search = signal('');
  readonly inviteEmail = signal('');
  readonly inviting = signal(false);
  readonly editingId = signal<string | null>(null);
  readonly editEmail = signal('');
  readonly confirming = signal<ConfirmAction | null>(null);
  readonly confirmReason = signal('');

  readonly hasPrevious = computed(() => this.page() > 1);
  readonly hasNext = computed(() => this.page() < this.totalPages());
  readonly statusOptions: ReadonlyArray<{ value: SectionStatus; label: string }> = [
    { value: 'all', label: 'Todos' },
    { value: 'active', label: 'Ativos' },
    { value: 'blocked', label: 'Bloqueados' },
    { value: 'invited', label: 'Convidados' },
  ];

  constructor() {
    this.searchChanges
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe((value) => {
        this.search.set(value);
        this.page.set(1);
        this.load();
      });
  }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.service
      .list({
        status: this.status(),
        search: this.search(),
        page: this.page(),
        pageSize: this.pageSize(),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          this.items.set(response.items);
          this.totalItems.set(response.totalItems);
          this.totalPages.set(response.totalPages);
          this.loading.set(false);
        },
        error: (error: unknown) => {
          this.error.set(
            extractApiErrorMessage(error, 'Não foi possível carregar os administradores.'),
          );
          this.loading.set(false);
        },
      });
  }

  onSearchInput(event: Event): void {
    const target = event.target as HTMLInputElement | null;
    this.searchChanges.next(target?.value ?? '');
  }

  onStatusChange(event: Event): void {
    const target = event.target as HTMLSelectElement | null;
    this.status.set((target?.value as SectionStatus) ?? 'all');
    this.page.set(1);
    this.load();
  }

  previousPage(): void {
    if (!this.hasPrevious()) return;
    this.page.update((page) => page - 1);
    this.load();
  }

  nextPage(): void {
    if (!this.hasNext()) return;
    this.page.update((page) => page + 1);
    this.load();
  }

  onInviteEmailInput(event: Event): void {
    const target = event.target as HTMLInputElement | null;
    this.inviteEmail.set(target?.value ?? '');
  }

  invite(): void {
    const email = this.inviteEmail().trim();
    if (!email) {
      this.feedback.set({ type: 'error', message: 'Informe o e-mail do novo administrador.' });
      return;
    }

    this.inviting.set(true);
    this.feedback.set(null);

    this.service
      .invite(email)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          this.inviting.set(false);
          this.inviteEmail.set('');
          this.feedback.set({
            type: 'success',
            message: `Convite enviado para ${response.email}. O link expira em 24 horas.`,
          });
          this.load();
        },
        error: (error: unknown) => {
          this.inviting.set(false);
          this.feedback.set({
            type: 'error',
            message: extractApiErrorMessage(error, 'Não foi possível convidar este e-mail.'),
          });
        },
      });
  }

  startEdit(item: AdminAdministrator): void {
    this.editingId.set(item.id);
    this.editEmail.set(item.email);
    this.feedback.set(null);
  }

  onEditEmailInput(event: Event): void {
    const target = event.target as HTMLInputElement | null;
    this.editEmail.set(target?.value ?? '');
  }

  cancelEdit(): void {
    this.editingId.set(null);
    this.editEmail.set('');
  }

  saveEdit(item: AdminAdministrator): void {
    const email = this.editEmail().trim();
    if (!email) {
      this.feedback.set({ type: 'error', message: 'Informe um e-mail válido.' });
      return;
    }

    if (email.toLowerCase() === item.email.toLowerCase()) {
      this.editingId.set(null);
      this.feedback.set({
        type: 'success',
        message: 'O e-mail informado já é o atual deste administrador.',
      });
      return;
    }

    this.runItemAction(item.id, this.service.updateEmail(item.id, email), (updated) => {
      this.editingId.set(null);

      if (updated.isCurrentUser) {
        // The server already revoked every session for this identity. Keeping the local token
        // would only produce 401s on the next request, so the actor returns to the login page.
        this.feedback.set({
          type: 'success',
          message: 'Seu e-mail foi atualizado. Entre novamente para continuar.',
        });
        this.auth.logout();
        void this.router.navigateByUrl('/painel/login');
        return false;
      }

      this.feedback.set({
        type: 'success',
        message: `E-mail de ${updated.email} atualizado. As sessões anteriores foram encerradas.`,
      });
      return true;
    });
  }

  resendInvitation(item: AdminAdministrator): void {
    this.runItemAction(item.id, this.service.resendInvitation(item.id), () => {
      this.feedback.set({
        type: 'success',
        message: `Convite reenviado para ${item.email}.`,
      });
    });
  }

  requestPasswordReset(item: AdminAdministrator): void {
    this.runItemAction(item.id, this.service.requestPasswordReset(item.id), () => {
      this.feedback.set({
        type: 'success',
        message: `Link de redefinição enviado para ${item.email} (válido por 60 minutos).`,
      });
    });
  }

  unblock(item: AdminAdministrator): void {
    this.runItemAction(item.id, this.service.unblock(item.id), (updated) => {
      this.replaceItem(updated);
      this.feedback.set({
        type: 'success',
        message: `${updated.email} foi desbloqueado. Ele precisa entrar novamente.`,
      });
    });
  }

  askConfirmation(item: AdminAdministrator, kind: ConfirmKind): void {
    this.confirming.set({ kind, item });
    this.confirmReason.set('');
    setTimeout(() => this.confirmCancel?.nativeElement.focus());
  }

  cancelConfirmation(): void {
    this.confirming.set(null);
    this.confirmReason.set('');
  }

  onConfirmReasonInput(event: Event): void {
    const target = event.target as HTMLInputElement | null;
    this.confirmReason.set(target?.value ?? '');
  }

  confirmPending(): void {
    const pending = this.confirming();
    if (!pending) return;

    const reason = this.confirmReason().trim() || undefined;
    const request: Observable<unknown> =
      pending.kind === 'block'
        ? this.service.block(pending.item.id, reason)
        : pending.kind === 'remove'
          ? this.service.remove(pending.item.id, reason)
          : this.service.cancelInvitation(pending.item.id);

    this.confirming.set(null);
    this.runItemAction<unknown>(pending.item.id, request, () => {
      this.feedback.set({
        type: 'success',
        message:
          pending.kind === 'block'
            ? `${pending.item.email} foi bloqueado. As sessões foram encerradas.`
            : pending.kind === 'remove'
              ? `${pending.item.email} foi removido da administração.`
              : `Convite de ${pending.item.email} cancelado.`,
      });
    });
  }

  canBlock(item: AdminAdministrator): boolean {
    return !item.isCurrentUser && !item.isLastActiveAdmin;
  }

  canRemove(item: AdminAdministrator): boolean {
    return !item.isCurrentUser && !item.isLastActiveAdmin;
  }

  blockTitle(item: AdminAdministrator): string {
    if (item.isCurrentUser) return 'Você não pode bloquear a própria conta.';
    if (item.isLastActiveAdmin) return 'A plataforma precisa de pelo menos um administrador ativo.';
    return 'Bloquear e encerrar as sessões deste administrador.';
  }

  removeTitle(item: AdminAdministrator): string {
    if (item.isCurrentUser) return 'Você não pode remover a própria conta.';
    if (item.isLastActiveAdmin) return 'A plataforma precisa de pelo menos um administrador ativo.';
    return 'Remover o acesso administrativo deste e-mail.';
  }

  statusLabel(status: AdminAccountStatus): string {
    switch (status) {
      case 'Active':
        return 'Ativo';
      case 'Blocked':
        return 'Bloqueado';
      default:
        return 'Convidado';
    }
  }

  invitationStateLabel(invitation: AdminInvitation): string {
    switch (invitation.state) {
      case 'Pending':
        return invitation.emailSentAtUtc ? 'Convite pendente' : 'Convite em envio';
      case 'Sending':
        return 'Convite em envio';
      case 'Expired':
        return 'Convite expirado';
      case 'Accepted':
        return 'Convite aceito';
      case 'Revoked':
        return 'Convite cancelado';
      default:
        return 'Convite substituído';
    }
  }

  invitationExpiry(invitation: AdminInvitation): string {
    const expires = new Date(invitation.expiresAtUtc).getTime();
    const remainingMinutes = Math.round((expires - Date.now()) / 60000);
    if (remainingMinutes <= 0) return 'expirado';
    if (remainingMinutes < 60) return `expira em ${remainingMinutes}min`;
    const hours = Math.round(remainingMinutes / 60);
    return `expira em ${hours}h`;
  }

  formatDate(value: string): string {
    if (!value || value.startsWith('1970-01-01')) return '—';
    return new Intl.DateTimeFormat('pt-BR', {
      dateStyle: 'short',
      timeStyle: 'short',
    }).format(new Date(value));
  }

  private runItemAction<T>(
    id: string,
    request: Observable<T>,
    onSuccess: (result: T) => boolean | void,
  ): void {
    this.busyId.set(id);
    this.feedback.set(null);

    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (result: T) => {
        this.busyId.set(null);
        if (onSuccess(result) !== false) {
          this.load();
        }
      },
      error: (error: unknown) => {
        this.busyId.set(null);
        this.feedback.set({
          type: 'error',
          message: extractApiErrorMessage(error, 'Não foi possível concluir a operação.'),
        });
      },
    });
  }

  private replaceItem(updated: AdminAdministrator): void {
    this.items.update((items) =>
      items.map((item) => (item.id === updated.id ? updated : item)),
    );
  }
}
