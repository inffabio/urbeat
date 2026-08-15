import { Component, inject, signal, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { IonContent, IonIcon } from '@ionic/angular/standalone';
import { AuthService } from '../../core/services/auth.service';
import { ToastService } from '../../core/services/toast.service';

@Component({
  selector: 'app-email-confirm-page',
  standalone: true,
  imports: [CommonModule, IonContent, IonIcon, RouterModule],
  templateUrl: './email-confirm-page.component.html',
  styleUrl: './email-confirm-page.component.scss',
  host: { '[class.urbeat-onboarding]': 'true' },
})
export class EmailConfirmPageComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);

  readonly loading = signal(true);
  readonly succeeded = signal(false);
  readonly alreadyConfirmed = signal(false);
  readonly waitingForEmail = signal(false);
  readonly error = signal('');
  private redirectTimer: ReturnType<typeof setTimeout> | null = null;

  ngOnInit(): void {
    const code = this.route.snapshot.paramMap.get('code');

    if (code) {
      this.auth.confirmEmailByCode(code).subscribe({
        next: this.handleSuccess.bind(this),
        error: this.handleError.bind(this)
      });
      return;
    }

    const userId = this.route.snapshot.queryParamMap.get('userId');
    const token = this.route.snapshot.queryParamMap.get('token');

    if (userId && token) {
      this.auth.confirmEmail({ userId, token }).subscribe({
        next: this.handleSuccess.bind(this),
        error: this.handleError.bind(this)
      });
      return;
    }

    // No code or token provided: user likely arrived here right after registration
    this.loading.set(false);
    this.waitingForEmail.set(true);
    this.toast.showInfo('Enviamos um link de confirmação para o seu e-mail. Por favor, verifique sua caixa de entrada e clique no link para continuar.');
  }

  private handleSuccess(res: any): void {
    this.loading.set(false);
    this.succeeded.set(res.succeeded && !res.alreadyConfirmed);
    this.alreadyConfirmed.set(res.alreadyConfirmed);
    if (res.succeeded) {
      this.redirectTimer = setTimeout(() => {
        this.router.navigate(['/login-vendedor'], { replaceUrl: true });
      }, 3000);
    }
  }

  private handleError(): void {
    this.loading.set(false);
    this.error.set('Não foi possível confirmar o e-mail. Tente novamente ou peça um novo link.');
    this.toast.showError('Não foi possível confirmar o e-mail. Tente novamente ou peça um novo link.');
  }

  ngOnDestroy(): void {
    if (this.redirectTimer) clearTimeout(this.redirectTimer);
  }
}
