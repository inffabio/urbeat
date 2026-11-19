import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
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
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, Subject, debounceTime, distinctUntilChanged } from 'rxjs';
import { AdminAccountService } from '../../core/services/admin-account.service';
import { AuthService } from '../../core/services/auth.service';
import { extractApiErrorMessage } from '../../core/utils/api-error.helper';
import {
  AdminAccountStatus,
  AdminAdministrator,
  AdminInvitation,
  PagedAdminAdministrators,
} from '../../shared/models/admin-account.model';

type SectionStatus = 'all' | 'active' | 'blocked' | 'invited';
type ConfirmKind = 'block' | 'remove' | 'cancel-invitation';

const STATUS_VALUES: readonly SectionStatus[] = ['all', 'active', 'blocked', 'invited'];

interface ConfirmAction {
  kind: ConfirmKind;
  item: AdminAdministrator;
}

interface Feedback {
  type: 'success' | 'error';
  message: string;
}

/**
 * Dedicated administrator directory at /admin/administradores. The list is paginated and its
 * page/filter state lives in the query string, so a link is shareable and survives reloads. The
 * server is authoritative for authorization, the last-active-admin invariant and token validity;
 * the component only reflects the hints it receives.
 */
@Component({
  selector: 'app-admin-administrators-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './admin-administrators-page.component.html',
  styleUrl: './admin-administrators-page.component.scss',
})
export class AdminAdministratorsPageComponent implements OnInit {
  private readonly service = inject(AdminAccountService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
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
  readonly hasFilters = computed(
    () => this.status() !== 'all' || this.search().trim().length > 0,
  );
  readonly pages = computed(() => {
    const total = this.totalPages();
    if (total <= 0) return [];
    if (total <= 7) return Array.from({ length: total }, (_, index) => index + 1);

    const start = Math.max(1, Math.min(this.page() - 3, total - 6));
    return Array.from({ length: 7 }, (_, index) => start + index);
  });
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
        this.syncUrl();
        this.load();
      });
  }

  ngOnInit(): void {
    this.readUrlState();
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
          const lastValidPage = Math.max(response.totalPages, 1);

          // Removing, blocking or cancelling the last row of a page can leave the requested page
          // out of range. Move to the last valid page instead of showing a false empty state.
          if (this.page() > lastValidPage) {
            this.page.set(lastValidPage);
            this.syncUrl();
            this.load();
            return;
          }

          this.applyResponse(response);
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
    this.searchChanges.next((target?.value ?? '').slice(0, 120));
  }

  onStatusChange(event: Event): void {
    const target = event.target as HTMLSelectElement | null;
    const value = target?.value;
    this.status.set(isStatus(value) ? value : 'all');
    this.page.set(1);
    this.syncUrl();
    this.load();
  }

  clearFilters(): void {
    this.search.set('');
    this.status.set('all');
    this.page.set(1);
    this.syncUrl();
    this.load();
  }

  goToPage(page: number): void {
    if (page < 1 || page > Math.max(this.totalPages(), 1) || page === this.page()) return;

    this.page.set(page);
    this.syncUrl();
    this.load();
  }

  previousPage(): void {
    if (this.hasPrevious()) this.goToPage(this.page() - 1);
  }

  nextPage(): void {
    if (this.hasNext()) this.goToPage(this.page() + 1);
  }

  onInviteEmailInput(event: Event): void {
    const target = event.target as HTMLInputElement | null;
    this.inviteEmail.set(target?.value ?? '');
  }

  invite(): void {
    // Re-entrancy guard: the disabled binding only lands on the next change detection, and the
    // app runs with eventCoalescing, so a repeated activation (double click, Enter key repeat or a
    // lagging frame) can reach this handler twice. One submission must never create two requests.
    if (this.inviting()) return;

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
          this.feedback.set(
            response.emailQueued === false
              ? {
                  type: 'error',
                  message:
                    `Convite criado para ${response.email}, mas o e-mail não foi enfileirado. ` +
                    'Use "Reenviar convite" para tentar novamente.',
                }
              : {
                  type: 'success',
                  message:
                    `Convite criado para ${response.email}. O link expira em 24 horas e o e-mail ` +
                    'será enviado em instantes.',
                },
          );
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
    this.runItemAction(item.id, this.service.resendInvitation(item.id), (result) => {
      this.feedback.set(
        result.emailQueued === false
          ? {
              type: 'error',
              message:
                `O reenvio do convite para ${item.email} não foi enfileirado. ` +
                'Tente novamente em instantes.',
            }
          : {
              type: 'success',
              message: `Reenvio do convite para ${item.email} solicitado. O e-mail será enviado em instantes.`,
            },
      );
    });
  }

  requestPasswordReset(item: AdminAdministrator): void {
    this.runItemAction(item.id, this.service.requestPasswordReset(item.id), (result) => {
      if (result.emailQueued === false && result.emailDeliveryPending) {
        this.feedback.set({
          type: 'error',
          message:
            `O link de redefinição de ${item.email} não foi enfileirado. ` +
            'Tente novamente em instantes.',
        });
        return;
      }

      this.feedback.set({
        type: 'success',
        message: result.emailDeliveryPending
          ? `Link de redefinição para ${item.email} solicitado (válido por 60 minutos).`
          : `O link de redefinição de ${item.email} já havia sido enviado e continua válido.`,
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

  cancellationTitle(item: AdminAdministrator): string {
    return item.isCurrentUser
      ? 'Você não pode cancelar o próprio convite.'
      : 'Cancelar o convite pendente deste e-mail.';
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

  private applyResponse(response: PagedAdminAdministrators): void {
    this.items.set(response.items);
    this.totalItems.set(response.totalItems);
    this.totalPages.set(response.totalPages);
    this.loading.set(false);
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

  private syncUrl(): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      replaceUrl: true,
      queryParams: {
        page: this.page() > 1 ? this.page() : null,
        status: this.status() !== 'all' ? this.status() : null,
        search: this.search().trim() || null,
      },
    });
  }

  private readUrlState(): void {
    const params = this.route.snapshot.queryParamMap;
    const page = Number(params.get('page'));

    this.page.set(Number.isFinite(page) && page >= 1 ? Math.floor(page) : 1);
    this.status.set(readStatus(params.get('status')));
    this.search.set((params.get('search') ?? '').slice(0, 120));
  }
}

function readStatus(value: string | null | undefined): SectionStatus {
  return isStatus(value) ? value : 'all';
}

function isStatus(value: string | null | undefined): value is SectionStatus {
  return !!value && (STATUS_VALUES as readonly string[]).includes(value);
}
