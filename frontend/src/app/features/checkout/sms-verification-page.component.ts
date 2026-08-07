import { CommonModule, Location } from '@angular/common';
import { Component, ElementRef, OnDestroy, OnInit, QueryList, ViewChildren, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { IonContent, IonIcon } from '@ionic/angular/standalone';

import { AuthService } from '../../core/services/auth.service';
import { CheckoutService } from '../../core/services/checkout.service';
import { getStorePathFromUrl } from '../../shared/utils/router.utils';

@Component({
  selector: 'app-sms-verification-page',
  standalone: true,
  imports: [CommonModule, FormsModule, IonContent, IonIcon],
  templateUrl: './sms-verification-page.component.html',
  styleUrl: './sms-verification-page.component.scss',
})
export class SmsVerificationPageComponent implements OnInit, OnDestroy {
  private readonly codeLength = 4;
  private readonly checkout = inject(CheckoutService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  private timerId: ReturnType<typeof setInterval> | null = null;

  @ViewChildren('digitInput') private readonly digitInputs?: QueryList<ElementRef<HTMLInputElement>>;

  readonly digits = signal<string[]>(Array.from({ length: this.codeLength }, () => ''));
  readonly error = signal('');
  readonly submitting = signal(false);
  readonly resending = signal(false);
  readonly secondsRemaining = signal(60);
  readonly maskedPhone = computed(() => this.checkout.verificationMaskedPhone() ?? 'seu celular');
  readonly code = computed(() => this.digits().join(''));
  readonly canResend = computed(() => this.secondsRemaining() <= 0 && !this.resending() && !this.submitting());
  readonly timerLabel = computed(() => {
    const seconds = this.secondsRemaining();
    const minutes = Math.floor(seconds / 60).toString().padStart(2, '0');
    const remainder = (seconds % 60).toString().padStart(2, '0');
    return `${minutes}:${remainder}`;
  });

  ngOnInit(): void {
    if (!this.checkout.verificationId()) {
      this.router.navigate(['/', getStorePathFromUrl(this.router), 'checkout', 'cadastro']);
      return;
    }

    this.updateCountdown();
    this.timerId = setInterval(() => this.updateCountdown(), 1000);
  }

  ngOnDestroy(): void {
    if (this.timerId) clearInterval(this.timerId);
  }

  onBack(): void {
    this.location.back();
  }

  setDigit(index: number, value: string): void {
    const digit = value.replace(/\D/g, '').slice(-1);
    this.error.set('');
    this.digits.update((current) => current.map((item, i) => (i === index ? digit : item)));

    if (digit && index < this.codeLength - 1) {
      this.focusInput(index + 1);
    }

    this.confirmWhenComplete();
  }

  onKeyDown(event: KeyboardEvent, index: number): void {
    if (event.key === 'Backspace' && !this.digits()[index] && index > 0) {
      this.focusInput(index - 1);
    }
  }

  onPaste(event: ClipboardEvent): void {
    event.preventDefault();
    const pasted = event.clipboardData?.getData('text').replace(/\D/g, '').slice(0, this.codeLength) ?? '';
    if (pasted.length === 0) return;

    this.error.set('');
    this.digits.set(Array.from({ length: this.codeLength }, (_, index) => pasted[index] ?? ''));
    this.focusInput(Math.min(pasted.length, this.codeLength) - 1);
    this.confirmWhenComplete();
  }

  resend(): void {
    const verificationId = this.checkout.verificationId();
    if (!verificationId || !this.canResend()) return;

    this.resending.set(true);
    this.error.set('');
    this.checkout.resendCustomerVerification({ verificationId }).subscribe({
      next: (response) => {
        this.checkout.verificationExpiresAtUtc.set(response.expiresAtUtc ?? null);
        this.checkout.verificationResendAvailableAtUtc.set(response.resendAvailableAtUtc ?? null);
        this.digits.set(Array.from({ length: this.codeLength }, () => ''));
        this.updateCountdown();
        this.resending.set(false);
        this.focusInput(0);
      },
      error: (err) => {
        this.error.set(err?.error?.error ?? 'Não foi possível reenviar o código. Tente novamente.');
        this.resending.set(false);
      },
    });
  }

  private confirmWhenComplete(): void {
    const verificationId = this.checkout.verificationId();
    const code = this.code();
    if (!verificationId || code.length !== this.codeLength || this.submitting()) return;

    this.submitting.set(true);
    this.checkout.confirmCustomerVerification({ verificationId, code }).subscribe({
      next: (response) => {
        if (!response.succeeded || !response.accessToken || !response.refreshToken) {
          this.error.set(response.error ?? 'Código inválido. Confira os números e tente novamente.');
          this.submitting.set(false);
          return;
        }

        this.auth.saveToken({
          accessToken: response.accessToken,
          refreshToken: response.refreshToken,
          expiresAtUtc: response.expiresAtUtc ?? '',
          refreshTokenExpiresAtUtc: response.refreshTokenExpiresAtUtc ?? '',
        });
        this.checkout.customerAddressId.set(response.customerAddressId ?? null);
        this.submitting.set(false);
        this.router.navigate(['/', getStorePathFromUrl(this.router), 'checkout', 'pagamento']);
      },
      error: (err) => {
        this.error.set(err?.error?.error ?? err?.error?.errorCode ?? 'Código inválido. Confira os números e tente novamente.');
        this.submitting.set(false);
      },
    });
  }

  private updateCountdown(): void {
    const resendAt = this.checkout.verificationResendAvailableAtUtc();
    if (!resendAt) {
      this.secondsRemaining.set(0);
      return;
    }

    const remainingMs = new Date(resendAt).getTime() - Date.now();
    this.secondsRemaining.set(Math.max(0, Math.ceil(remainingMs / 1000)));
  }

  private focusInput(index: number): void {
    queueMicrotask(() => this.digitInputs?.get(index)?.nativeElement.focus());
  }
}
