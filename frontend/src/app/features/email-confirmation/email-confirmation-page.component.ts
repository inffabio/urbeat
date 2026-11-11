import { Component, inject, signal, computed, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { IonContent, IonIcon } from '@ionic/angular/standalone';
import { AuthService } from '../../core/services/auth.service';
import { ToastService } from '../../core/services/toast.service';
import { AccountRole } from '../../shared/models/auth.model';

type PromotionState = 'idle' | 'confirming' | 'succeeded' | 'already' | 'error';

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
  readonly emailChangeChallenge = signal('');
  readonly resending = signal(false);
  readonly secondsLeft = signal(120);
  readonly showChangeEmail = signal(false);
  readonly newEmail = signal('');
  readonly changingEmail = signal(false);

  // Role promotion has its own explicit state. A promotion token is never posted to the e-mail
  // confirmation or e-mail change endpoints.
  readonly promotionMode = signal(false);
  readonly promotionPending = signal(false);
  readonly promotionState = signal<PromotionState>('idle');
  readonly promotionRole = signal<AccountRole | ''>('');
  readonly promotionRoleLabel = computed(() =>
    this.promotionRole() === 'Customer' ? 'cliente' : 'lojista',
  );
  // Only the seller flow has a dedicated login route. Customer sessions are restored in the store
  // where the person orders (there is no customer login page), so the page gives explicit guidance
  // instead of linking to the landing page and pretending it is a login screen.
  readonly promotionLoginRoute = computed<string | null>(() =>
    this.promotionRole() === 'Customer' ? null : '/login-vendedor',
  );
  readonly promotionRetryRoute = computed<string | null>(() =>
    this.promotionRole() === 'Customer' ? null : '/cadastro',
  );

  private timerInterval: any;
  private promotionReferrerMeta: HTMLMetaElement | null = null;

  ngOnInit(): void {
    // The promotion token arrives in the URL fragment (never the query string), so it is not sent
    // to the server, a proxy access log or a Referer header. index.html scrubs it before Angular
    // boots and leaves it only in memory; the direct fragment read is the fallback for a page that
    // was opened without that bootstrap script. The query string is intentionally NOT accepted:
    // keeping the old `?promotionToken=` surface would defeat the fix.
    const promotionFragment = this.readPromotionFragment();
    const promotionRole = promotionFragment.role ?? this.route.snapshot.queryParamMap.get('role');
    const promotionPending = this.route.snapshot.queryParamMap.get('promotionPending') === 'true';

    if (promotionRole === 'Customer' || promotionRole === 'Seller') {
      this.promotionRole.set(promotionRole);
    }

    if (promotionFragment.token) {
      this.promotionMode.set(true);
      this.promotionState.set('confirming');
      // The token is now held only in memory. Remove the fragment before anything else so it never
      // reaches the browser history, a copied link or a third-party page, then POST.
      this.applyNoReferrerPolicy();
      this.stripPromotionFragmentFromUrl();
      this.confirmPromotion(promotionFragment.token);
      return;
    }

    if (promotionPending) {
      this.promotionMode.set(true);
      this.promotionPending.set(true);
      this.email.set(this.route.snapshot.queryParamMap.get('email') ?? '');
      return;
    }

    const email = this.route.snapshot.queryParamMap.get('email') ?? '';
    this.email.set(email);
    this.userId.set(this.route.snapshot.queryParamMap.get('userId') ?? '');
    this.emailChangeChallenge.set(this.readEmailChangeChallenge(email));
    this.startTimer();
  }

  /**
   * Reads the promotion payload once, from the in-memory copy captured by index.html or, as a
   * fallback, from the URL fragment exposed by the router/window. Query parameters are ignored.
   */
  private readPromotionFragment(): { token: string; role: string | null } {
    const captured = this.readCapturedPromotionFragment();
    if (captured.token) {
      // The bootstrap script already scrubbed the URL; delete the in-memory copy after use.
      this.clearCapturedPromotionFragment();
      return captured;
    }

    const routeFragment = this.route.snapshot.fragment;
    const fragment = routeFragment
      ? routeFragment.replace(/^#/, '')
      : typeof window !== 'undefined' && window.location.hash
        ? window.location.hash.replace(/^#/, '')
        : '';
    if (!fragment) {
      return { token: '', role: null };
    }

    const params = new URLSearchParams(fragment);
    return { token: params.get('promotionToken') ?? '', role: params.get('role') };
  }

  private readCapturedPromotionFragment(): { token: string; role: string | null } {
    if (typeof window === 'undefined') return { token: '', role: null };
    const payload = (window as unknown as {
      __urbeatPromotionFragment?: { token?: string; role?: string | null };
    }).__urbeatPromotionFragment;
    return { token: payload?.token ?? '', role: payload?.role ?? null };
  }

  private clearCapturedPromotionFragment(): void {
    if (typeof window === 'undefined') return;
    delete (window as unknown as { __urbeatPromotionFragment?: unknown }).__urbeatPromotionFragment;
  }

  private confirmPromotion(token: string): void {
    this.auth.confirmRolePromotion(token).subscribe({
      next: (res) => {
        this.promotionRole.set(res.role ?? this.promotionRole());
        this.promotionState.set(res.alreadyApplied ? 'already' : 'succeeded');
      },
      error: () => {
        this.promotionState.set('error');
      },
    });
  }

  private readEmailChangeChallenge(email: string): string {
    if (!email) return '';
    try {
      return sessionStorage.getItem(`urbeat.emailChangeChallenge:${email.trim().toLowerCase()}`) ?? '';
    } catch {
      return '';
    }
  }

  ngOnDestroy(): void {
    clearInterval(this.timerInterval);
    this.promotionReferrerMeta?.remove();
    this.promotionReferrerMeta = null;
  }

  /**
   * Removes the one-time promotion fragment from the visible URL while keeping the token in memory
   * for the in-flight confirmation. The token is never written to localStorage/sessionStorage,
   * logged or re-added to the URL.
   */
  private stripPromotionFragmentFromUrl(): void {
    if (typeof window === 'undefined' || !window.history?.replaceState) return;

    const params = new URLSearchParams(window.location.hash.replace(/^#/, ''));
    if (!params.has('promotionToken')) return;

    params.delete('promotionToken');
    params.delete('role');
    const remaining = params.toString();
    window.history.replaceState(
      window.history.state,
      '',
      `${window.location.pathname}${window.location.search}${remaining ? `#${remaining}` : ''}`,
    );
  }

  /**
   * Scoped Referrer-Policy while the confirmation page is mounted. The token is stripped from the
   * URL first, but this also prevents the Referer from carrying the promotion page to third parties
   * loaded from it. Only added when the document has no global policy already.
   */
  private applyNoReferrerPolicy(): void {
    if (typeof document === 'undefined') return;
    if (document.querySelector('meta[name="referrer"]')) return;

    const meta = document.createElement('meta');
    meta.name = 'referrer';
    meta.content = 'no-referrer';
    document.head.appendChild(meta);
    this.promotionReferrerMeta = meta;
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
    const challenge = this.emailChangeChallenge();
    if (!uid || !currentE) return;
    if (!challenge) {
      this.toast.showWarning('Não foi possível alterar o e-mail. Refaça o cadastro para receber um novo link.');
      return;
    }

    this.changingEmail.set(true);
    this.auth.updateEmail({ userId: uid, currentEmail: currentE, newEmail: newE, emailChangeChallenge: challenge }).subscribe({
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
