import { Injectable, signal } from '@angular/core';
import { Subject } from 'rxjs';

export const CART_CHECKOUT_DETAIL_ADDRESS = 'Registrar endereço e pagamento';
export const CART_CHECKOUT_DETAIL_PAYMENT = 'Pagamento';

export interface CartCheckoutActionState {
  visible: boolean;
  disabled: boolean;
  detail?: string;
}

@Injectable({ providedIn: 'root' })
export class CartCheckoutActionService {
  readonly visible = signal(false);
  readonly disabled = signal(false);
  readonly detail = signal(CART_CHECKOUT_DETAIL_ADDRESS);

  private readonly continueRequest$ = new Subject<void>();
  readonly continueRequested = this.continueRequest$.asObservable();

  setState(state: CartCheckoutActionState): void {
    this.visible.set(state.visible);
    this.disabled.set(state.disabled);
    if (state.detail !== undefined) {
      this.detail.set(state.detail);
    }
  }

  reset(): void {
    this.visible.set(false);
    this.disabled.set(false);
    this.detail.set(CART_CHECKOUT_DETAIL_ADDRESS);
  }

  requestContinue(): void {
    this.continueRequest$.next();
  }
}
