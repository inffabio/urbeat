import { Component, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { IonContent, IonIcon, IonSpinner } from '@ionic/angular/standalone';
import { AuthService } from '../../core/services/auth.service';
import { ToastService } from '../../core/services/toast.service';
import { RegisterCustomerRequest, RegisterResponse } from '../../shared/models/auth.model';

@Component({
  selector: 'app-seller-register-page',
  standalone: true,
  imports: [CommonModule, FormsModule, IonContent, IonIcon, IonSpinner],
  templateUrl: './seller-register-page.component.html',
  styleUrl: './seller-register-page.component.scss',
  host: { '[class.urbeat-onboarding]': 'true' },
})
export class SellerRegisterPageComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly toastService = inject(ToastService);

  readonly fullName = signal('');
  readonly document = signal('');
  readonly whatsapp = signal('');
  readonly email = signal('');
  readonly password = signal('');
  readonly confirmPassword = signal('');

  readonly showPassword = signal(false);
  readonly showConfirmPassword = signal(false);

  readonly passwordStrength = computed(() => {
    const p = this.password();
    return {
      length: p.length >= 8,
      upper: /[A-Z]/.test(p),
      lower: /[a-z]/.test(p),
      number: /[0-9]/.test(p),
      special: /[!@#$%^&*]/.test(p),
    };
  });

  readonly strengthScore = computed(() => {
    const s = this.passwordStrength();
    return [s.length, s.upper, s.lower, s.number, s.special].filter(Boolean).length;
  });

  readonly strengthLabel = computed(() => {
    const score = this.strengthScore();
    if (score <= 2) return 'Fraca';
    if (score <= 3) return 'Média';
    if (score <= 4) return 'Boa';
    return 'Forte';
  });

  readonly strengthColor = computed(() => {
    const score = this.strengthScore();
    if (score <= 2) return '#e53935';
    if (score <= 3) return 'var(--app-brand)';
    if (score <= 4) return '#f78963';
    return '#2e7d32';
  });
  readonly loading = signal(false);
  readonly submitted = signal(false);

  readonly errors = signal<Record<string, string>>({});
  readonly serverError = signal('');

  onWhatsappInput(value: string): void {
    const digits = value.replace(/\D/g, '').slice(0, 11);
    let formatted = digits;
    if (digits.length > 6) {
      formatted = `(${digits.slice(0, 2)}) ${digits.slice(2, 7)}-${digits.slice(7)}`;
    } else if (digits.length > 2) {
      formatted = `(${digits.slice(0, 2)}) ${digits.slice(2)}`;
    } else if (digits.length > 0) {
      formatted = `(${digits}`;
    }
    this.whatsapp.set(formatted);
    if (this.submitted()) this.clearError('whatsapp');
  }

  onFieldInput(field: string): void {
    if (this.submitted()) this.clearError(field);
  }

  onDocumentInput(value: string): void {
    let digits = value.replace(/\D/g, '').slice(0, 14);
    let formatted = digits;
    
    // Mask CPF: 000.000.000-00 or CNPJ: 00.000.000/0000-00
    if (digits.length <= 11) {
      // CPF Mask
      formatted = digits.replace(/(\d{3})(\d)/, '$1.$2')
                        .replace(/(\d{3})(\d)/, '$1.$2')
                        .replace(/(\d{3})(\d{1,2})$/, '$1-$2');
    } else {
      // CNPJ Mask
      formatted = digits.replace(/^(\d{2})(\d)/, '$1.$2')
                        .replace(/^(\d{2})\.(\d{3})(\d)/, '$1.$2.$3')
                        .replace(/\.(\d{3})(\d)/, '.$1/$2')
                        .replace(/(\d{4})(\d{1,2})$/, '$1-$2');
    }
    
    this.document.set(formatted);
    this.onFieldInput('document');
  }

  onDocumentBlur(): void {
    const docDigits = this.document().replace(/\D/g, '');
    if (docDigits.length === 11 || docDigits.length === 14) {
      if (!this.isValidCpfCnpj(docDigits)) {
        this.toastService.showWarning('CPF/CNPJ inválido!');
        this.setError('document', 'CPF/CNPJ inválido!');
      }
    }
  }

  private isValidCpfCnpj(digits: string): boolean {
    if (digits.length === 11) return this.isValidCpf(digits);
    if (digits.length === 14) return this.isValidCnpj(digits);
    return false;
  }

  private isValidCpf(cpf: string): boolean {
    if (/^(\d)\1{10}$/.test(cpf)) return false;
    let sum = 0;
    for (let i = 0; i < 9; i++) sum += parseInt(cpf[i]) * (10 - i);
    let d1 = (sum * 10) % 11;
    if (d1 === 10) d1 = 0;
    if (d1 !== parseInt(cpf[9])) return false;
    sum = 0;
    for (let i = 0; i < 10; i++) sum += parseInt(cpf[i]) * (11 - i);
    let d2 = (sum * 10) % 11;
    if (d2 === 10) d2 = 0;
    return d2 === parseInt(cpf[10]);
  }

  private isValidCnpj(cnpj: string): boolean {
    if (/^(\d)\1{13}$/.test(cnpj)) return false;
    const weights1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    const weights2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    let sum = 0;
    for (let i = 0; i < 12; i++) sum += parseInt(cnpj[i]) * weights1[i];
    let d1 = sum % 11 < 2 ? 0 : 11 - (sum % 11);
    if (d1 !== parseInt(cnpj[12])) return false;
    sum = 0;
    for (let i = 0; i < 13; i++) sum += parseInt(cnpj[i]) * weights2[i];
    let d2 = sum % 11 < 2 ? 0 : 11 - (sum % 11);
    return d2 === parseInt(cnpj[13]);
  }

  togglePassword(): void {
    this.showPassword.update((v) => !v);
  }

  toggleConfirmPassword(): void {
    this.showConfirmPassword.update((v) => !v);
  }

  private clearError(field: string): void {
    this.errors.update((e) => {
      const copy = { ...e };
      delete copy[field];
      return copy;
    });
  }

  private setError(field: string, msg: string): void {
    this.errors.update((e) => ({ ...e, [field]: msg }));
  }

  private validate(): boolean {
    const errs: Record<string, string> = {};

    if (this.fullName().trim().length < 3) {
      errs['fullName'] = 'Nome é obrigatório';
    }

    const docDigits = this.document().replace(/\D/g, '');
    if (docDigits.length > 0 && !this.isValidCpfCnpj(docDigits)) {
      errs['document'] = 'CPF/CNPJ inválido!';
    }

    const whatsappDigits = this.whatsapp().replace(/\D/g, '');
    if (whatsappDigits.length < 10) {
      errs['whatsapp'] = 'WhatsApp inválido';
    }

    const email = this.email().trim();
    if (!email || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
      errs['email'] = 'E-mail inválido';
    }

    const pwd = this.password();
    const s = this.passwordStrength();
    if (!s.length || !s.upper || !s.lower || !s.number || !s.special) {
      errs['password'] = 'A senha deve conter no mínimo 8 caracteres, incluindo letras maiúsculas, minúsculas, números e símbolos (!@#$%^&amp;*).';
    }

    if (this.confirmPassword() !== this.password()) {
      errs['confirmPassword'] = 'Senha não confere';
    }

    this.errors.set(errs);
    return Object.keys(errs).length === 0;
  }

  submit(): void {
    this.submitted.set(true);

    if (!this.validate()) return;

    this.loading.set(true);

    const req: RegisterCustomerRequest = {
      fullName: this.fullName().trim(),
      email: this.email().trim(),
      password: this.password(),
      phoneNumber: this.whatsapp().replace(/\D/g, ''),
    };
      
    // Patch para enviar document, tipagem no TS pode não ter document dependendo de model, forçamos com as
    (req as any).document = this.document().replace(/\D/g, '');

    this.auth.registerSeller(req).subscribe({
      next: (res) => {
        this.loading.set(false);
        if (res.emailConfirmationPending) {
          this.toastService.showSuccess('Cadastro realizado! Enviamos um link de confirmação para o seu e-mail.');
          this.router.navigate(['/confirmacao-email'], { queryParams: { email: this.email().trim(), userId: res.userId } });
        } else {
          this.toastService.showSuccess('Conta de vendedor ativada! Faça login para continuar.');
          this.router.navigate(['/login']);
        }
      },
      error: (err) => {
        this.loading.set(false);
        if (err?.error?.documentAlreadyRegistered) {
          if (err.error.emailConfirmationPending) {
            this.toastService.showSuccess(
              'CPF já cadastrado. Um novo link de confirmação foi enviado para o seu e-mail.'
            );
            this.router.navigate(['/confirmar-email']);
          } else {
            this.toastService.showError('CPF já cadastrado.');
          }
        } else if (err?.status === 409) {
          this.toastService.showError('Este e-mail já está em uso');
        } else if (err?.error?.errors && Array.isArray(err.error.errors) && err.error.errors.length > 0) {
          this.toastService.showError(err.error.errors.join(' '));
        } else {
          this.toastService.showError('Não foi possível criar sua conta agora. Tente novamente.');
        }
      },
    });
  }
}
