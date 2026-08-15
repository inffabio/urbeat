import { Component, inject, signal, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { IonContent, IonIcon } from '@ionic/angular/standalone';
import { AuthService } from '../../core/services/auth.service';
import { ToastService } from '../../core/services/toast.service';

@Component({
  selector: 'app-email-confirmation-page',
  standalone: true,
  imports: [CommonModule, FormsModule, IonContent, IonIcon, RouterModule],
  templateUrl: './email-confirmation-page.component.html',
  styleUrl: './email-confirmation-page.component.scss',
  host: { '[class.urbeat-onboarding]': 'true' },
})
export class EmailConfirmationPageComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);

  readonly email = signal('');
  readonly userId = signal('');
  readonly resending = signal(false);
  readonly secondsLeft = signal(120);
  readonly showChangeEmail = signal(false);
  readonly newEmail = signal('');
  readonly changingEmail = signal(false);

  private timerInterval: any;

  ngOnInit(): void {
    this.email.set(this.route.snapshot.queryParamMap.get('email') ?? '');
    this.userId.set(this.route.snapshot.queryParamMap.get('userId') ?? '');
    this.startTimer();
  }

  ngOnDestroy(): void {
    clearInterval(this.timerInterval);
  }

  private startTimer(): void {
    clearInterval(this.timerInterval);
    this.timerInterval = setInterval(() => {
      this.secondsLeft.update(v => {
        if (v <= 1) {
          clearInterval(this.timerInterval);
          this.showChangeEmail.set(true);
          return 0;
        }
        return v - 1;
      });
    }, 1000);
  }

  formatTime(): string {
    const s = this.secondsLeft();
    const m = Math.floor(s / 60);
    const sec = s % 60;
    return `${m}:${sec.toString().padStart(2, '0')}`;
  }

  resend(): void {
    const e = this.email();
    if (!e || this.resending()) return;
    this.resending.set(true);
    this.auth.resendConfirmation({ email: e }).subscribe({
      next: () => {
        this.resending.set(false);
        this.toast.showSuccess('E-mail reenviado! Verifique sua caixa de entrada.');
      },
      error: () => {
        this.resending.set(false);
        this.toast.showError('Erro ao reenviar. Tente novamente.');
      },
    });
  }

  changeEmail(): void {
    const newE = this.newEmail().trim();
    if (!newE || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(newE)) {
      this.toast.showWarning('Informe um e-mail válido.');
      return;
    }

    const uid = this.userId();
    const currentE = this.email();
    if (!uid || !currentE) return;

    this.changingEmail.set(true);
    this.auth.updateEmail({ userId: uid, currentEmail: currentE, newEmail: newE }).subscribe({
      next: () => {
        this.changingEmail.set(false);
        this.email.set(newE);
        this.newEmail.set('');
        this.showChangeEmail.set(false);
        this.secondsLeft.set(120);
        this.startTimer();
        this.toast.showSuccess('E-mail atualizado! Um novo link foi enviado.');
      },
      error: (err) => {
        this.changingEmail.set(false);
        this.toast.showError(err?.error?.message || err?.error?.detail || 'Erro ao atualizar e-mail.');
      },
    });
  }
}
