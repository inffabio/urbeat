import { Component, inject, signal, computed, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { IonContent, IonIcon } from '@ionic/angular/standalone';
import { AuthService } from '../../core/services/auth.service';
import { ToastService } from '../../core/services/toast.service';
import { AccountRole, ResendConfirmationResponse } from '../../shared/models/auth.model';

type PromotionState = 'idle' | 'confirming' | 'succeeded' | 'already' | 'error';

/** Visual resend cooldown used when the server response has no deadline (older API contract). */
const DEFAULT_RESEND_COOLDOWN_SECONDS = 120;

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
  readonly showChangeEmail = signal(false);
  readonly newEmail = signal('');
  readonly changingEmail = signal(false);

  // The cooldown is derived from a single deadline instead of decrementing a counter on every
  // tick. A restart only replaces the deadline, so duplicated intervals cannot drift the value.
  private readonly cooldownDeadlineMs = signal(0);
  private readonly clockMs = signal(Date.now());
  readonly secondsLeft = computed(() => {
    const remainingMs = this.cooldownDeadlineMs() - this.clockMs();
    return remainingMs > 0 ? Math.ceil(remainingMs / 1000) : 0;
  });
  readonly resendCooldownActive = computed(() => this.secondsLeft() > 0);

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

  private timerInterval: ReturnType<typeof setInterval> | null = null;
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
    // Opening the page is not an accepted resend request. No artificial cooldown is started and
    // nothing is persisted, so the first resend is available immediately after a reload or a
    // navigation; the server rate limiter remains the authority on repeated requests.
    this.resetResendCooldown();
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
    this.stopTimer();
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

  /** Clears any pending visual cooldown so the resend action is immediately available. */
  private resetResendCooldown(): void {
    this.stopTimer();
    this.clockMs.set(Date.now());
    this.cooldownDeadlineMs.set(0);
  }

  /**
   * Starts (or restarts) the visual resend cooldown. It is only called after the server accepted a
   * resend/e-mail-change request, never when the page merely opens. The deadline comes from the
   * server when available and is only a UX hint: an old cached contract falls back to 120 seconds,
   * and a deadline already in the past means the resend button is immediately available.
   */
  private startResendCooldown(deadlineMs?: number): void {
    this.stopTimer();

    const now = Date.now();
    const deadline = deadlineMs !== undefined && Number.isFinite(deadlineMs)
      ? Math.max(deadlineMs, now)
      : now + DEFAULT_RESEND_COOLDOWN_SECONDS * 1000;

    this.cooldownDeadlineMs.set(deadline);
    this.clockMs.set(now);

    if (this.secondsLeft() <= 0) {
      this.showChangeEmail.set(true);
      return;
    }

    this.showChangeEmail.set(false);
    this.timerInterval = setInterval(() => {
      this.clockMs.set(Date.now());
      if (this.secondsLeft() <= 0) {
        this.stopTimer();
        this.showChangeEmail.set(true);
      }
    }, 1000);
  }

  private stopTimer(): void {
    if (this.timerInterval === null) return;
    clearInterval(this.timerInterval);
    this.timerInterval = null;
  }

  private resolveResendDeadline(response?: ResendConfirmationResponse | null): number | undefined {
    const raw = response?.resendAvailableAtUtc;
    if (!raw) return undefined;

    const parsed = Date.parse(raw);
    return Number.isFinite(parsed) ? parsed : undefined;
  }

  formatTime(): string {
    const s = this.secondsLeft();
    const m = Math.floor(s / 60);
    const sec = s % 60;
    return `${m}:${sec.toString().padStart(2, '0')}`;
  }

  resend(): void {
    const e = this.email().trim();
    // The cooldown is enforced here as well as in the template `[disabled]`: an accepted resend
    // already consumed the server budget, and the guard must not depend on the button state.
    if (!e || this.resending() || this.resendCooldownActive()) return;

    this.resending.set(true);
    this.auth.resendConfirmation({ email: e }).subscribe({
      next: (res) => {
        this.resending.set(false);
        // The backend only acknowledges that the request was accepted/enqueued; delivery is
        // asynchronous and is intentionally not claimed.
        this.startResendCooldown(this.resolveResendDeadline(res));
        this.toast.showSuccess(
          'Solicitação de reenvio enviada. Se o e-mail estiver cadastrado, você receberá um novo link de confirmação.',
        );
      },
      error: (err) => {
        // A rejected request was never accepted, so the cooldown is neither started nor restarted
        // here: the button is released and the server decides when the next attempt is allowed.
        this.resending.set(false);
        if (err?.status === 429) {
          this.toast.showWarning(
            'Muitas solicitações de reenvio. Aguarde alguns minutos e tente novamente.',
          );
          return;
        }
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
        this.startResendCooldown();
        this.toast.showSuccess(
          'E-mail atualizado! Solicitação de novo link de confirmação enviada.',
        );
      },
      error: (err) => {
        this.changingEmail.set(false);
        this.toast.showError(err?.error?.message || err?.error?.detail || 'Erro ao atualizar e-mail.');
      },
    });
  }
}
