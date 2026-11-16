import { Location } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { RouterModule } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AdminAccountService } from '../../core/services/admin-account.service';
import { extractApiErrorMessage } from '../../core/utils/api-error.helper';
import {
  PasswordSetupFormComponent,
  PasswordSetupSubmission,
} from '../../shared/components/password-setup-form/password-setup-form.component';

type InviteState = 'loading' | 'invalid' | 'form' | 'success';

/**
 * Public first-access page for an invited administrator. The opaque token is read from the URL
 * fragment and removed with Location.replaceState before any request, so it never reaches the
 * server through a path, query string, Referer header or proxy log.
 */
@Component({
  selector: 'app-admin-invite-page',
  standalone: true,
  imports: [RouterModule, PasswordSetupFormComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './admin-invite-page.component.html',
  styleUrl: './admin-invite-page.component.scss',
})
export class AdminInvitePageComponent {
  private readonly service = inject(AdminAccountService);
  private readonly location = inject(Location);
  private readonly destroyRef = inject(DestroyRef);

  readonly state = signal<InviteState>('loading');
  readonly emailMasked = signal<string | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly successMessage = signal(
    'Conta ativada. Entre com sua nova senha.',
  );

  private token = '';

  constructor() {
    const token = this.readAndStripToken();
    if (!token) {
      this.state.set('invalid');
      return;
    }

    this.token = token;
    this.service
      .validateInvitationToken(token)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (validation) => {
          if (!validation.valid) {
            this.state.set('invalid');
            return;
          }

          this.emailMasked.set(validation.emailMasked ?? null);
          this.state.set('form');
        },
        error: () => this.state.set('invalid'),
      });
  }

  onSubmitted(submission: PasswordSetupSubmission): void {
    this.busy.set(true);
    this.error.set(null);

    this.service
      .acceptInvitation({
        token: this.token,
        newPassword: submission.newPassword,
        confirmPassword: submission.confirmPassword,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.busy.set(false);
          this.successMessage.set(result.message);
          this.token = '';
          this.state.set('success');
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.error.set(
            this.describeError(error, 'Não foi possível ativar a conta. Peça um novo convite.'),
          );
        },
      });
  }

  private describeError(error: unknown, fallback: string): string {
    const code = (error as { error?: { code?: unknown } } | null)?.error?.code;
    if (code === 'TOKEN_ALREADY_USED') {
      return 'Este link já foi usado. Peça um novo convite a um administrador.';
    }
    if (code === 'INVALID_OR_EXPIRED_TOKEN') {
      return 'Este link é inválido ou expirou. Peça um novo convite a um administrador.';
    }
    return extractApiErrorMessage(error, fallback);
  }

  private readAndStripToken(): string | null {
    const loc = globalThis.location;
    if (!loc) return null;

    const hash = loc.hash.startsWith('#') ? loc.hash.slice(1) : loc.hash;
    const params = new URLSearchParams(hash);
    const token = params.get('token')?.trim() ?? '';
    if (!token) return null;

    this.location.replaceState(loc.pathname, loc.search.replace(/^\?/, ''));
    return token;
  }
}
