import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { AdminStoreSessionService } from '../../../core/services/admin-store-session.service';

/**
 * Permanent indicator that the dashboard is operating on a delegated store. It is UX only: the
 * server denies every request without a valid context. The CTA always clears the tab-scoped session
 * and returns to the administrative store list.
 */
@Component({
  selector: 'app-admin-store-banner',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (sessionService.session(); as current) {
      <div
        class="admin-store-banner"
        [class.is-expired]="sessionService.isExpired()"
        [attr.role]="sessionService.isExpired() ? 'status' : null"
        [attr.aria-live]="sessionService.isExpired() ? 'polite' : null"
      >
        <span class="admin-store-banner-text" [title]="fullText(current.storeName)">
          {{ sessionService.isExpired() ? 'Sessão administrativa expirada' : fullText(current.storeName) }}
        </span>
        <button type="button" class="admin-store-banner-action" (click)="returnToAdmin()">
          Voltar ao painel administrativo
        </button>
      </div>
    }
  `,
  styles: [`
    :host {
      display: block;
    }

    .admin-store-banner {
      position: sticky;
      top: 0;
      z-index: 5;
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
      padding: calc(8px + env(safe-area-inset-top, 0px)) 16px 8px;
      background: var(--app-brand, #7b1e3a);
      color: #fff;
      border-bottom: 1px solid color-mix(in srgb, #fff 18%, transparent);
    }

    .admin-store-banner.is-expired {
      background: var(--app-error);
    }

    .admin-store-banner-text {
      min-width: 0;
      overflow: hidden;
      white-space: nowrap;
      text-overflow: ellipsis;
      font-size: 13px;
      font-weight: 700;
    }

    .admin-store-banner-action {
      min-height: 44px;
      flex: 0 0 auto;
      padding: 0 14px;
      border: 1px solid rgba(255, 255, 255, 0.55);
      border-radius: 10px;
      background: rgba(255, 255, 255, 0.12);
      color: #fff;
      font: inherit;
      font-size: 13px;
      font-weight: 700;
      cursor: pointer;
    }

    .admin-store-banner-action:hover {
      background: rgba(255, 255, 255, 0.22);
    }

    .admin-store-banner-action:focus-visible {
      outline: none;
      box-shadow: 0 0 0 3px var(--app-brand-shadow, rgba(255, 255, 255, 0.45));
    }

    @media (max-width: 600px) {
      .admin-store-banner {
        gap: 8px;
        padding-inline: 12px;
      }

      .admin-store-banner-action {
        padding-inline: 10px;
      }
    }
  `],
})
export class AdminStoreBannerComponent {
  readonly sessionService = inject(AdminStoreSessionService);
  private readonly router = inject(Router);

  fullText(storeName: string): string {
    return `modo administrador — loja ${storeName}`;
  }

  returnToAdmin(): void {
    const returnUrl = this.sessionService.returnUrl();
    this.sessionService.end();
    void this.router.navigateByUrl(returnUrl);
  }
}
