import { TestBed } from '@angular/core/testing';

import {
  CART_CHECKOUT_DETAIL_ADDRESS,
  CART_CHECKOUT_DETAIL_PAYMENT,
  CartCheckoutActionService,
} from './cart-checkout-action.service';

describe('CartCheckoutActionService', () => {
  let service: CartCheckoutActionService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(CartCheckoutActionService);
  });

  it('exposes the two conditional checkout detail copies', () => {
    expect(CART_CHECKOUT_DETAIL_ADDRESS).toBe('Registrar endereço e pagamento');
    expect(CART_CHECKOUT_DETAIL_PAYMENT).toBe('Pagamento');
  });

  it('starts hidden and enabled with the registration detail copy', () => {
    expect(service.visible()).toBe(false);
    expect(service.disabled()).toBe(false);
    expect(service.detail()).toBe(CART_CHECKOUT_DETAIL_ADDRESS);
  });

  it('publishes visibility and disabled state', () => {
    service.setState({ visible: true, disabled: true });

    expect(service.visible()).toBe(true);
    expect(service.disabled()).toBe(true);
  });

  it('keeps the previous detail when setState omits it', () => {
    service.setState({ visible: true, disabled: false, detail: 'Detalhe customizado' });
    service.setState({ visible: true, disabled: false });

    expect(service.detail()).toBe('Detalhe customizado');
  });

  it('resets to hidden and enabled with the default detail', () => {
    service.setState({ visible: true, disabled: true, detail: 'Detalhe customizado' });

    service.reset();

    expect(service.visible()).toBe(false);
    expect(service.disabled()).toBe(false);
    expect(service.detail()).toBe(CART_CHECKOUT_DETAIL_ADDRESS);
  });

  it('accepts the payment detail copy for the authenticated checkout', () => {
    service.setState({ visible: true, disabled: false, detail: CART_CHECKOUT_DETAIL_PAYMENT });

    expect(service.detail()).toBe('Pagamento');
  });

  it('forwards continue requests to subscribers', () => {
    const next = jest.fn();
    const subscription = service.continueRequested.subscribe(next);

    service.requestContinue();

    expect(next).toHaveBeenCalledTimes(1);
    subscription.unsubscribe();
  });
});
