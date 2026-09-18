import { TestBed } from '@angular/core/testing';

import { CartCheckoutActionService } from './cart-checkout-action.service';

describe('CartCheckoutActionService', () => {
  let service: CartCheckoutActionService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(CartCheckoutActionService);
  });

  it('starts hidden and enabled with the checkout detail copy', () => {
    expect(service.visible()).toBe(false);
    expect(service.disabled()).toBe(false);
    expect(service.detail()).toBe('Escolha endereço e pagamento');
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
    expect(service.detail()).toBe('Escolha endereço e pagamento');
  });

  it('forwards continue requests to subscribers', () => {
    const next = jest.fn();
    const subscription = service.continueRequested.subscribe(next);

    service.requestContinue();

    expect(next).toHaveBeenCalledTimes(1);
    subscription.unsubscribe();
  });
});
