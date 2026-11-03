import { Location } from '@angular/common';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router } from '@angular/router';
import { Observable, Subject, of, throwError } from 'rxjs';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { PaymentPageComponent } from './payment-page.component';
import { CartService } from '../../core/services/cart.service';
import { CheckoutService } from '../../core/services/checkout.service';
import { CustomerOrderTrackingService } from '../../core/services/customer-order-tracking.service';
import { OrderService } from '../../core/services/order.service';
import { AuthService } from '../../core/services/auth.service';
import { SignalRService } from '../../core/services/signalr.service';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';
import { AddressService } from '../../core/services/address.service';
import { StoreService } from '../../core/services/store.service';
import { ToastService } from '../../core/services/toast.service';

describe('PaymentPageComponent', () => {
  let checkoutMock: { [key: string]: any };
  let routerMock: { navigate: jest.Mock; url: string };
  let toastMock: { showSuccess: jest.Mock; showError: jest.Mock };

  beforeEach(async () => {
    localStorage.clear();
    sessionStorage.clear();

    checkoutMock = {
      fulfillmentType: signal(FulfillmentType.Delivery),
      customerAddressId: signal('addr1'),
      customerAddress: signal(null),
      lastOrderId: signal(null),
      lastOrderCode: signal(null),
      orderNotes: signal(''),
      preview: jest.fn().mockReturnValue(of({ deliveryFee: 0, freeShippingApplied: false, total: 20 })),
      confirm: jest.fn().mockReturnValue(of({ orderId: 'order1', code: '0001', total: 20 })),
    };
    routerMock = { navigate: jest.fn(), url: '/loja/checkout/pagamento' };
    toastMock = { showSuccess: jest.fn(), showError: jest.fn() };

    await TestBed.configureTestingModule({
      imports: [PaymentPageComponent],
      providers: [
        CartService,
        CustomerOrderTrackingService,
        { provide: CheckoutService, useValue: checkoutMock },
        { provide: AddressService, useValue: { list: jest.fn().mockReturnValue(of([{ id: 'addr1', city: 'Campos', cep: '28000-000', neighborhood: 'Jardim Aurora' }])) } },
        { provide: StoreService, useValue: { getStoreById: jest.fn().mockReturnValue(of({ address: { city: 'Campos', zipCode: '28010-000', neighborhood: 'Centro' } })) } },
        { provide: OrderService, useValue: { getOrder: jest.fn().mockReturnValue(of({ id: 'order1' })) } },
        { provide: AuthService, useValue: { token: signal('customer-token'), token$: of('customer-token'), getToken: () => 'customer-token' } },
        {
          provide: SignalRService,
          useValue: {
            startCustomerHub: jest.fn().mockResolvedValue(undefined),
            onCustomerEvent: jest.fn(),
            removeCustomerListener: jest.fn(),
            stopCustomerHub: jest.fn(),
            onCustomerStateChange: jest.fn(() => jest.fn()),
          },
        },
        { provide: ToastService, useValue: toastMock },
        { provide: Router, useValue: routerMock },
        { provide: Location, useValue: { back: jest.fn() } },
      ],
    }).compileComponents();

    const cart = TestBed.inject(CartService);
    cart.storeId.set('store1');
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);
  });

  it('renders only the four supported storefront methods as native radios', () => {
    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();

    const radios = fixture.debugElement.queryAll(By.css('input[type="radio"][name="payment-category"]'));

    expect(radios).toHaveLength(4);
    expect(radios.map(radio => radio.nativeElement.value)).toEqual([
      'pix-machine',
      'cash',
      'debit',
      'credit',
    ]);
    expect(fixture.debugElement.query(By.css('input[value="pix"]'))).toBeNull();
  });

  it('starts with no method selected so the choice is mandatory', () => {
    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.selected()).toBeNull();
    expect(fixture.componentInstance.canContinue()).toBe(false);
  });

  it('confirms Pix on the card machine without creating an online payment', () => {
    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.select('pix-machine');
    fixture.componentInstance.continue();

    expect(checkoutMock.confirm).toHaveBeenCalledWith(expect.objectContaining({
      paymentMethod: PaymentMethod.PixOnDelivery,
      storeId: 'store1',
      expectedTotal: 20,
      cashWithoutChange: undefined,
      cashChangeForAmount: undefined,
    }));
    expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja', 'pedido', 'order1']);
  });

  it('confirms debit and credit on the card machine', () => {
    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.select('debit');
    fixture.componentInstance.continue();
    expect(checkoutMock.confirm).toHaveBeenCalledWith(expect.objectContaining({
      paymentMethod: PaymentMethod.DebitOnDelivery,
    }));

    fixture.componentInstance.select('credit');
    fixture.componentInstance.continue();
    expect(checkoutMock.confirm).toHaveBeenCalledWith(expect.objectContaining({
      paymentMethod: PaymentMethod.CreditOnDelivery,
    }));
  });

  it('requires an explicit cash change choice', () => {
    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.select('cash');
    expect(fixture.componentInstance.canContinue()).toBe(false);

    fixture.componentInstance.setCashNeedsChange(false);
    expect(fixture.componentInstance.canContinue()).toBe(true);
    fixture.componentInstance.continue();

    expect(checkoutMock.confirm).toHaveBeenCalledWith(expect.objectContaining({
      paymentMethod: PaymentMethod.CashOnDelivery,
      cashWithoutChange: true,
      cashChangeForAmount: undefined,
    }));
  });

  it('rejects a change-for amount below the authoritative total', () => {
    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.select('cash');
    fixture.componentInstance.setCashNeedsChange(true);
    fixture.componentInstance.onChangeForInput('1900');

    expect(fixture.componentInstance.changeForValue()).toBe(19);
    expect(fixture.componentInstance.canContinue()).toBe(false);

    fixture.componentInstance.onChangeForInput('2500');
    expect(fixture.componentInstance.canContinue()).toBe(true);
    fixture.componentInstance.continue();

    expect(checkoutMock.confirm).toHaveBeenCalledWith(expect.objectContaining({
      paymentMethod: PaymentMethod.CashOnDelivery,
      cashWithoutChange: false,
      cashChangeForAmount: 25,
    }));
  });

  it('blocks checkout until the authoritative preview resolves', () => {
    const preview = new Subject<{ deliveryFee: number; freeShippingApplied: boolean; total: number }>();
    checkoutMock.preview.mockReturnValue(preview.asObservable());

    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();
    fixture.componentInstance.select('credit');
    fixture.detectChanges();

    expect(fixture.componentInstance.canContinue()).toBe(false);
    fixture.componentInstance.continue();
    expect(checkoutMock.confirm).not.toHaveBeenCalled();

    preview.next({ deliveryFee: 0, freeShippingApplied: false, total: 20 });
    preview.complete();
    fixture.detectChanges();

    expect(fixture.componentInstance.canContinue()).toBe(true);
  });

  it('blocks checkout when the preview fails and allows retrying it', () => {
    checkoutMock.preview.mockReturnValueOnce(throwError(() => ({ status: 500 })));

    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();
    fixture.componentInstance.select('credit');
    fixture.detectChanges();

    expect(fixture.componentInstance.previewError()).toBe(true);
    expect(fixture.componentInstance.canContinue()).toBe(false);
    fixture.componentInstance.continue();
    expect(checkoutMock.confirm).not.toHaveBeenCalled();

    checkoutMock.preview.mockReturnValue(of({ deliveryFee: 0, freeShippingApplied: false, total: 20 }));
    fixture.componentInstance.retryPreview();
    fixture.detectChanges();

    expect(fixture.componentInstance.previewError()).toBe(false);
    expect(fixture.componentInstance.canContinue()).toBe(true);
    expect(checkoutMock.preview).toHaveBeenCalledTimes(2);
  });

  it('uses the authoritative preview total for the cash change threshold', () => {
    checkoutMock.preview.mockReturnValue(of({ deliveryFee: 0, freeShippingApplied: false, total: 99 }));

    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();
    fixture.componentInstance.select('cash');
    fixture.componentInstance.setCashNeedsChange(true);
    fixture.componentInstance.onChangeForInput('5000');

    expect(fixture.componentInstance.total()).toBe(99);
    expect(fixture.componentInstance.canContinue()).toBe(false);

    fixture.componentInstance.onChangeForInput('9900');
    expect(fixture.componentInstance.canContinue()).toBe(true);
  });

  it('refreshes the preview and requires a new confirmation when the server total changed', () => {
    checkoutMock.confirm.mockReturnValueOnce(throwError(() => ({
      status: 409,
      error: {
        error: 'O total do pedido mudou. Confira o novo valor e confirme novamente.',
        total: 25,
        summary: { deliveryFee: 0, freeShippingApplied: false, total: 25 },
      },
    })));

    const cart = TestBed.inject(CartService);
    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();
    fixture.componentInstance.select('credit');
    fixture.componentInstance.continue();

    expect(routerMock.navigate).not.toHaveBeenCalled();
    expect(cart.items()).toHaveLength(1);
    expect(fixture.componentInstance.total()).toBe(25);
    expect(fixture.componentInstance.errorMessage()).toContain('mudou');

    checkoutMock.confirm.mockReturnValueOnce(of({ orderId: 'order1', code: '0001', total: 25 }));
    fixture.componentInstance.continue();

    expect(checkoutMock.confirm).toHaveBeenCalledTimes(2);
    expect(checkoutMock.confirm).toHaveBeenLastCalledWith(expect.objectContaining({
      expectedTotal: 25,
    }));
    expect(routerMock.navigate).toHaveBeenCalledTimes(1);
    expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja', 'pedido', 'order1']);
  });

  it('tracks the order before navigating', () => {
    const tracking = TestBed.inject(CustomerOrderTrackingService);
    const trackOrderSpy = jest.spyOn(tracking, 'trackOrder');

    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.select('credit');
    fixture.componentInstance.continue();

    expect(trackOrderSpy).toHaveBeenCalledWith('order1');
    expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja', 'pedido', 'order1']);
  });

  it('shows the server payment error instead of creating a different order', () => {
    checkoutMock.confirm.mockReturnValueOnce(
      throwError(() => ({ error: { error: 'O valor para troco deve ser maior ou igual ao total do pedido.' } })),
    );
    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.select('cash');
    fixture.componentInstance.setCashNeedsChange(false);
    fixture.componentInstance.continue();

    expect(fixture.componentInstance.errorMessage())
      .toContain('maior ou igual ao total');
    expect(routerMock.navigate).not.toHaveBeenCalled();
  });

  it('marks the details sheet as a modal dialog', () => {
    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.componentInstance.showDetailsModal.set(true);
    fixture.detectChanges();

    const modal = fixture.debugElement.query(By.css('.modal-sheet')).nativeElement as HTMLElement;

    expect(modal.getAttribute('role')).toBe('dialog');
    expect(modal.getAttribute('aria-modal')).toBe('true');
    expect(modal.getAttribute('aria-labelledby')).toBe('details-title');
  });

  it('shows the delivery coverage modal instead of a toast when checkout blocks the neighborhood', () => {
    checkoutMock.confirm.mockReturnValueOnce(throwError(() => ({ error: { error: 'Ainda nao entregamos no seu bairro.' } })));
    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.select('credit');
    fixture.componentInstance.continue();
    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('app-delivery-coverage-modal'))).not.toBeNull();
    expect(fixture.componentInstance.errorMessage()).toBeNull();
  });

  it('recognizes the flat HttpErrorResponse shape returned by the API', () => {
    checkoutMock.confirm.mockReturnValueOnce(throwError(() => ({ error: 'Ainda nao entregamos no seu bairro.' })));
    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.select('credit');
    fixture.componentInstance.continue();
    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('app-delivery-coverage-modal'))).not.toBeNull();
    expect(fixture.componentInstance.errorMessage()).toBeNull();
  });

  it('does not navigate after destroy when the order confirms', () => {
    const pending: ((order: any) => void)[] = [];
    checkoutMock.confirm.mockImplementation(
      () =>
        new Observable((subscriber) => {
          pending.push((order) => subscriber.next(order));
        }),
    );

    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();
    fixture.componentInstance.select('credit');
    fixture.componentInstance.continue();
    fixture.destroy();

    pending[0]({ orderId: 'order1', code: '0001' });

    expect(routerMock.navigate).not.toHaveBeenCalled();
  });

  it('cancels the checkout preview subscription on destroy', () => {
    let unsubscribed = false;
    checkoutMock.preview.mockReturnValue(
      new Observable(() => () => {
        unsubscribed = true;
      }),
    );

    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();
    fixture.destroy();

    expect(unsubscribed).toBe(true);
  });

  it('renders the Continuar bar as a primary in-flow bar inside the scroll content', () => {
    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();

    const bar = fixture.debugElement.query(By.css('app-sticky-action-bar'));
    expect(bar).not.toBeNull();

    const host = bar.nativeElement as HTMLElement;
    expect(host.classList.contains('sticky-action-primary')).toBe(true);
    expect(host.classList.contains('in-flow')).toBe(true);
    expect(host.classList.contains('in-flow-safe-area')).toBe(false);

    const content = fixture.debugElement.query(By.css('ion-content')).nativeElement as HTMLElement;
    const section = fixture.debugElement.query(By.css('.screen-padding')).nativeElement as HTMLElement;
    expect(content.contains(host)).toBe(true);
    expect(section.contains(host)).toBe(false);
    expect(section.compareDocumentPosition(host) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    const button = bar.query(By.css('button.sticky-action')).nativeElement as HTMLButtonElement;
    expect(button.textContent).toContain('Continuar');
    expect(button.disabled).toBe(true);
  });

  it('disables the bar while processing', () => {
    const fixture = TestBed.createComponent(PaymentPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.select('credit');
    fixture.componentInstance.processing.set(true);
    fixture.detectChanges();

    const bar = fixture.debugElement.query(By.css('app-sticky-action-bar'));
    const button = bar.query(By.css('button.sticky-action')).nativeElement as HTMLButtonElement;
    expect(button.disabled).toBe(true);
  });
});

describe('PaymentPageComponent payment action layout', () => {
  it('declares the primary inline bar with the payment action and disabled rule', () => {
    const template = readFileSync(resolve(__dirname, 'payment-page.component.html'), 'utf8');

    expect(template).toMatch(
      /<app-sticky-action-bar[\s\S]*?placement="inline"[\s\S]*?appearance="primary"[\s\S]*?\[label\]="actionLabel\(\)"[\s\S]*?\[disabled\]="!canContinue\(\)"[\s\S]*?\(action\)="continue\(\)"/,
    );

    const barIndex = template.indexOf('<app-sticky-action-bar');
    expect(barIndex).toBeGreaterThan(template.indexOf('</section>'));
    expect(barIndex).toBeLessThan(template.indexOf('</ion-content>'));
  });

  it('bounds the route column and removes the legacy fixed-footer clearance', () => {
    const styles = readFileSync(resolve(__dirname, 'payment-page.component.scss'), 'utf8');

    expect(styles).toMatch(
      /:host\s*\{[\s\S]*display:\s*flex;[\s\S]*flex-direction:\s*column;[\s\S]*flex:\s*1 1 0;[\s\S]*min-height:\s*0;/,
    );
    expect(styles).toMatch(/\.payment-content\s*\{[\s\S]*min-height:\s*0;/);
    expect(styles).toMatch(/\.screen-padding\s*\{[\s\S]*padding:\s*0 18px 24px;/);
    expect(styles).not.toMatch(/--store-footer-clearance/);
    expect(styles).not.toMatch(/\.sticky-action-bar\s*\{/);
  });
});
