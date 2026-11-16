import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';

/**
 * Circular store logo with an initial placeholder. A broken image is replaced by the same
 * placeholder, so the UI never renders a broken `img`. In lists the avatar is decorative and the
 * adjacent store name is the accessible text; standalone usage announces "Logo da {nome}".
 */
@Component({
  selector: 'app-store-logo-avatar',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span
      class="store-logo-avatar"
      [class.is-placeholder]="!logoUrl() || failed()"
      [style.width.px]="size()"
      [style.height.px]="size()"
      [attr.data-testid]="'store-logo-avatar'"
    >
      @if (logoUrl() && !failed()) {
        <img
          [src]="logoUrl()"
          [attr.alt]="alt()"
          loading="lazy"
          decoding="async"
          (error)="onError()"
        />
      } @else {
        <span class="store-logo-initial" aria-hidden="true">{{ initial() }}</span>
      }
    </span>
  `,
  styles: [`
    :host {
      display: inline-flex;
      flex: 0 0 auto;
    }

    .store-logo-avatar {
      display: inline-grid;
      place-items: center;
      overflow: hidden;
      border-radius: 50%;
      background: var(--app-brand-soft);
      color: var(--app-brand);
      border: 1px solid var(--app-line);
    }

    .store-logo-avatar img {
      width: 100%;
      height: 100%;
      object-fit: cover;
      display: block;
    }

    .store-logo-initial {
      font-size: 0.95rem;
      font-weight: 800;
      line-height: 1;
      text-transform: uppercase;
    }

    .is-placeholder .store-logo-initial {
      font-size: 1rem;
    }
  `],
})
export class StoreLogoAvatarComponent {
  readonly name = input.required<string>();
  readonly logoUrl = input<string | null>(null);
  /** True when the avatar is followed by the store name (list rows): alt must be empty. */
  readonly decorative = input(false);
  readonly size = input(44);

  readonly failed = signal(false);
  readonly initial = computed(() => {
    const name = this.name()?.trim() ?? '';
    return name.length > 0 ? name[0] : '?';
  });
  readonly alt = computed(() => (this.decorative() ? '' : `Logo da ${this.name()}`));

  onError(): void {
    this.failed.set(true);
  }
}
