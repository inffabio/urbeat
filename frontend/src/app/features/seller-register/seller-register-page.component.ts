import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { IonContent, IonIcon, IonSpinner } from '@ionic/angular/standalone';
import { AuthService } from '../../core/services/auth.service';
import { PlanOfferService } from '../../core/services/plan-offer.service';
import { ToastService } from '../../core/services/toast.service';
import { RegisterResponse, RegisterSellerRequest } from '../../shared/models/auth.model';
import {
  PublicPlanOfferItem,
  planPeriodOptions,
} from '../../shared/models/plan-offer.model';
import {
  SubscriptionBillingPeriod,
  queryToBillingPeriod,
} from '../../shared/models/subscription.model';

@Component({
  selector: 'app-seller-register-page',
  standalone: true,
  imports: [CommonModule, FormsModule, IonContent, IonIcon, IonSpinner],
  templateUrl: './seller-register-page.component.html',
  styleUrl: './seller-register-page.component.scss',
  host: { '[class.urbeat-onboarding]': 'true' },
})
export class SellerRegisterPageComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly planOfferService = inject(PlanOfferService);
  private readonly toastService = inject(ToastService);

  readonly fullName = signal('');
  readonly document = signal('');
  readonly whatsapp = signal('');
  readonly email = signal('');
  readonly password = signal('');
  readonly confirmPassword = signal('');

  readonly plans = signal<PublicPlanOfferItem[]>([]);
  readonly plansLoading = signal(true);
  readonly plansError = signal(false);
  readonly invalidSelection = signal(false);
  readonly selectedPlanId = signal<string | null>(null);
  readonly selectedBillingPeriod = signal<SubscriptionBillingPeriod | null>(null);

  readonly selectedPlan = computed<PublicPlanOfferItem | null>(() => {
    const planId = this.selectedPlanId();
    if (!planId) return null;
    return this.plans().find((plan) => plan.id === planId) ?? null;
  });

  /** Trial do plano recebido da landing; nunca é inferido do catálogo. */
  readonly trialDays = computed(() => this.selectedPlan()?.trialDays ?? 0);

  readonly planSummary = computed(() => {
    const plan = this.selectedPlan();
    const period = this.selectedBillingPeriod();
    if (!plan || !period) return null;
    const option = planPeriodOptions(plan).find((item) => item.period === period);
    if (!option) return null;
    return {
      name: plan.name,
      periodLabel: option.label,
      amountLabel: `${this.formatCurrency(option.amount)}${period === SubscriptionBillingPeriod.Annual ? '/ano' : '/mês'}`,
      trialLabel: plan.trialDays > 0
        ? `${plan.trialDays} ${plan.trialDays === 1 ? 'dia' : 'dias'} grátis`
        : 'Primeira cobrança imediata',
    };
  });

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

  ngOnInit(): void {
    this.loadPlans();
  }

  loadPlans(): void {
    this.plansLoading.set(true);
    this.plansError.set(false);
    this.planOfferService.getOffer().subscribe({
      next: (offer) => {
        const plans = (offer?.plans ?? []).filter(
          (plan) => plan.name?.trim() && (plan.monthlyEnabled || plan.annualEnabled),
        );
        this.plans.set(plans);
        this.plansLoading.set(false);
        this.applyQuerySelection();
      },
      error: () => {
        this.plans.set([]);
        this.plansLoading.set(false);
        this.plansError.set(true);
      },
    });
  }

  formatCurrency(value: number): string {
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value);
  }

  /**
   * A escolha do plano acontece somente na landing. O cadastro apenas resolve a
   * seleção recebida na URL contra o catálogo público; nunca escolhe por conta
   * própria, nem mesmo quando existe um único plano ativo. O backend continua
   * sendo a validação autoritativa.
   */
  private applyQuerySelection(): void {
    const query = this.route.snapshot?.queryParamMap;
    const planId = query?.get('planId')?.trim() || null;
    const rawPeriod = query?.get('billingPeriod')?.trim() || null;
    const period = queryToBillingPeriod(rawPeriod);

    this.selectedPlanId.set(null);
    this.selectedBillingPeriod.set(null);
    this.invalidSelection.set(false);

    if (!planId || !rawPeriod) {
      return;
    }

    const plan = this.plans().find((item) => item.id === planId);
    if (!plan || !period || !planPeriodOptions(plan).some((option) => option.period === period)) {
      this.invalidSelection.set(true);
      return;
    }

    this.selectedPlanId.set(plan.id);
    this.selectedBillingPeriod.set(period);
  }

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

    if (!this.selectedPlan() || !this.selectedBillingPeriod()) {
      errs['plan'] = this.invalidSelection()
        ? 'O plano ou a periodicidade escolhida não está mais disponível. Volte à página inicial e escolha outra opção.'
        : 'Escolha seu plano na página inicial e volte para concluir o cadastro.';
    }

    this.errors.set(errs);
    return Object.keys(errs).length === 0;
  }

  submit(): void {
    this.submitted.set(true);

    if (this.plansLoading() || this.plansError()) {
      this.toastService.showError('Não foi possível carregar os planos. Recarregue a página e tente novamente.');
      return;
    }

    if (!this.validate()) return;

    this.loading.set(true);

    // The current session is intentionally NOT cleared: when the signed-in customer adds a
    // seller access, the bearer token proves ownership of the existing account and the customer
    // password/session must survive the operation. No existing password is ever requested here.
    const req: RegisterSellerRequest = {
      fullName: this.fullName().trim(),
      email: this.email().trim(),
      password: this.password(),
      phoneNumber: this.whatsapp().replace(/\D/g, ''),
      document: this.document().replace(/\D/g, ''),
      planId: this.selectedPlanId()!,
      billingPeriod: this.selectedBillingPeriod()!,
    };

    this.auth.registerSeller(req).subscribe({
      next: (res) => {
        this.loading.set(false);
        if (res.rolePromotionPending) {
          // The e-mail already belongs to an urbeat account. Nothing was changed yet: the owner
          // must confirm the single-use link sent to that address.
          this.toastService.showSuccess(
            'Se este e-mail já pertence a uma conta urbeat, enviamos um link para confirmar o novo acesso de lojista. Sua senha atual não foi alterada.',
          );
          this.router.navigate(['/confirmacao-email'], {
            queryParams: { email: this.email().trim(), promotionPending: true, role: 'Seller' },
          });
        } else if (res.emailConfirmationPending) {
          this.persistEmailChangeChallenge(this.email().trim(), res.emailChangeChallenge);
          this.toastService.showSuccess(
            res.roleAddedToExistingAccount
              ? 'Acesso lojista criado! Sua senha de cliente continua a mesma. Enviamos um link de confirmação para o seu e-mail.'
              : 'Cadastro realizado! Enviamos um link de confirmação para o seu e-mail.',
          );
          this.router.navigate(['/confirmacao-email'], {
            queryParams: {
              email: this.email().trim(),
              ...(res.userId ? { userId: res.userId } : {}),
            },
          });
        } else {
          this.toastService.showSuccess(
            res.roleAddedToExistingAccount
              ? 'Acesso lojista criado! Sua senha de cliente continua a mesma.'
              : 'Conta de vendedor ativada! Faça login para continuar.',
          );
          this.router.navigate(['/login-vendedor']);
        }
      },
      error: (err) => {
        this.loading.set(false);
        if (err?.error?.documentAlreadyRegistered) {
          if (err.error.emailConfirmationPending) {
            this.toastService.showSuccess(
              'CPF já cadastrado. Um novo link de confirmação foi enviado para o seu e-mail.'
            );
            const userId = err?.error?.userId;
            this.router.navigate(['/confirmacao-email'], {
              queryParams: {
                email: this.email().trim(),
                ...(userId ? { userId } : {}),
              },
            });
          } else {
            this.toastService.showError('CPF já cadastrado.');
          }
        } else if (err?.status === 409) {
          // Generic on purpose: never reveal which role already exists for this e-mail and never
          // ask for an existing password, which is not part of this flow anymore.
          this.toastService.showError(
            'Não foi possível adicionar o acesso lojista com este e-mail.',
          );
        } else if (err?.error?.errors && Array.isArray(err.error.errors) && err.error.errors.length > 0) {
          this.toastService.showError(err.error.errors.join(' '));
        } else {
          this.toastService.showError('Não foi possível criar sua conta agora. Tente novamente.');
        }
      },
    });
  }

  private persistEmailChangeChallenge(email: string, challenge?: string): void {
    try {
      const key = `urbeat.emailChangeChallenge:${email.trim().toLowerCase()}`;
      if (challenge) {
        sessionStorage.setItem(key, challenge);
      } else {
        sessionStorage.removeItem(key);
      }
    } catch {
      // Storage may be unavailable (private mode / SSR); the change-email action will simply be
      // unavailable until the user re-registers.
    }
  }
}
