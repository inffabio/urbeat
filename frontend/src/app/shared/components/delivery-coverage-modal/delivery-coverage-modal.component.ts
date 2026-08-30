import { Component, EventEmitter, Input, Output } from '@angular/core';
import { IonIcon } from '@ionic/angular/standalone';

export interface DeliveryCoverageAddress {
  city?: string;
  state?: string;
  zipCode?: string;
  cep?: string;
  neighborhood?: string;
}

@Component({
  selector: 'app-delivery-coverage-modal',
  standalone: true,
  imports: [IonIcon],
  template: `
    <div class="delivery-coverage-backdrop" role="presentation" (click)="close.emit()">
      <section
        class="delivery-coverage-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="delivery-coverage-title"
        (click)="$event.stopPropagation()"
      >
        <button type="button" class="delivery-coverage-close" aria-label="Fechar aviso" (click)="close.emit()">
          <ion-icon name="close" aria-hidden="true"></ion-icon>
        </button>
        <div class="delivery-coverage-icon" aria-hidden="true">
          <ion-icon name="location-outline"></ion-icon>
        </div>
        <h2 id="delivery-coverage-title">Área de entrega</h2>
        <p class="delivery-coverage-intro">Compare os endereços antes de continuar:</p>

        <div class="delivery-coverage-grid">
          <div class="delivery-coverage-column">
            <span class="delivery-coverage-label">Endereço da loja</span>
            <strong>{{ formatZipCode(storeAddress?.zipCode) }} · {{ storeAddress?.state || 'UF não informada' }} · {{ storeAddress?.city || 'Não informado' }}</strong>
            <span>{{ storeAddress?.neighborhood || 'Bairro não informado' }}</span>
          </div>
          <div class="delivery-coverage-column delivery-coverage-column--customer">
            <span class="delivery-coverage-label">Seu endereço</span>
            <strong>{{ formatZipCode(customerAddress?.cep || customerAddress?.zipCode) }} · {{ customerAddress?.state || 'UF não informada' }} · {{ customerAddress?.city || 'Não informado' }}</strong>
            <span>{{ customerAddress?.neighborhood || 'Bairro não informado' }}</span>
          </div>
        </div>

        <p class="delivery-coverage-message">Esta loja não faz entrega neste bairro</p>
        <button type="button" class="delivery-coverage-menu-button" (click)="backToMenu.emit()">
          Voltar ao cardápio
        </button>
      </section>
    </div>
  `,
  styles: [`
    .delivery-coverage-backdrop { position: fixed; inset: 0; z-index: 100; display: grid; place-items: center; padding: 20px; background: rgba(22, 22, 22, .48); }
    .delivery-coverage-modal { position: relative; width: min(390px, 100%); padding: 22px 18px 18px; border: 1px solid var(--app-border-light); border-radius: 18px; background: var(--app-surface); box-shadow: var(--shadow-lg); text-align: center; }
    .delivery-coverage-close { position: absolute; top: 10px; right: 10px; width: 36px; height: 36px; border: 0; border-radius: 50%; background: var(--app-bg); color: var(--app-text-secondary); font-size: 18px; }
    .delivery-coverage-icon { display: grid; place-items: center; width: 38px; height: 38px; margin: 0 auto 8px; border-radius: 50%; background: var(--app-brand-soft); color: var(--app-brand); font-size: 21px; }
    .delivery-coverage-modal h2 { margin: 0; font-size: 17px; font-weight: 800; }
    .delivery-coverage-intro { margin: 5px 0 14px; color: var(--app-text-secondary); font-size: 11px; }
    .delivery-coverage-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 8px; text-align: left; }
    .delivery-coverage-column { display: flex; min-width: 0; flex-direction: column; gap: 4px; padding: 10px; border: 1px solid var(--app-border-light); border-radius: 10px; color: var(--app-text-secondary); font-size: 11px; line-height: 1.25; }
    .delivery-coverage-column--customer { border-color: var(--app-brand-soft); background: var(--app-brand-soft); }
    .delivery-coverage-column strong, .delivery-coverage-label { color: var(--app-ink); font-size: 10px; font-weight: 800; }
    .delivery-coverage-label { color: var(--app-brand); text-transform: uppercase; letter-spacing: .03em; }
    .delivery-coverage-message { margin: 14px 0; color: var(--app-error); font-size: 12px; font-weight: 800; }
    .delivery-coverage-menu-button { width: 100%; min-height: 44px; border: 0; border-radius: 10px; background: var(--app-brand); color: #fff; font-size: 13px; font-weight: 800; }
  `],
})
export class DeliveryCoverageModalComponent {
  @Input() storeAddress: DeliveryCoverageAddress | null = null;
  @Input() customerAddress: DeliveryCoverageAddress | null = null;
  @Output() readonly close = new EventEmitter<void>();
  @Output() readonly backToMenu = new EventEmitter<void>();

  formatZipCode(value?: string): string {
    const digits = value?.replace(/\D/g, '') ?? '';
    return digits.length === 8 ? `${digits.slice(0, 2)}.${digits.slice(2, 5)}-${digits.slice(5)}` : 'CEP não informado';
  }
}
