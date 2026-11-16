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

type ResetState = 'loading' | 'invalid' | 'form' | 'success';

/**
 * Public administrative password reset page. The opaque token is read from the URL fragment and
 * stripped with Location.replaceState before the validation request.
 */
@Component({
  selector: 'app-admin-password-reset-page',
  standalone: true,
  imports: [RouterModule, PasswordSetupFormComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './admin-password-reset-page.component.html',
  styleUrl: './admin-password-reset-page.component.scss',
})
export class AdminPasswordResetPageComponent {
  private readonly service = inject(AdminAccountService);
  private readonly location = inject(Location);
  private readonly destroyRef = inject(DestroyRef);

  readonly state = signal<ResetState>('loading');
  readonly emailMasked = signal<string | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly successMessage = signal('Senha atualizada. Entre com a nova senha.');

  private token = '';

  constructor() {
    const token = this.readAndStripToken();
    if (!token) {
      this.state.set('invalid');
      return;
    }

    this.token = token;
    this.service
      .validatePasswordResetToken(token)
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
      .confirmPasswordReset({
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
            this.describeError(error, 'Não foi possível redefinir a senha. Solicite outro link.'),
          );
        },
      });
  }

  private describeError(error: unknown, fallback: string): string {
    const code = (error as { error?: { code?: unknown } } | null)?.error?.code;
    if (code === 'TOKEN_ALREADY_USED') {
      return 'Este link já foi usado. Solicite um novo link de redefinição.';
    }
    if (code === 'INVALID_OR_EXPIRED_TOKEN') {
      return 'Este link é inválido ou expirou. Solicite um novo link de redefinição.';
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
