import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { signal } from '@angular/core';
import { of, throwError } from 'rxjs';

import { CartPageComponent } from './cart-page.component';
import { CartService } from '../../core/services/cart.service';
import { CheckoutService } from '../../core/services/checkout.service';
import { StoreService } from '../../core/services/store.service';
import { Router } from '@angular/router';
import { ActivatedRoute } from '@angular/router';
import { Location } from '@angular/common';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { AuthService } from '../../core/services/auth.service';

describe('CartPageComponent', () => {
  let cart: CartService;
  let storeServiceMock: { getStoreById: jest.Mock };
  let checkoutServiceMock: { fulfillmentType: ReturnType<typeof signal<FulfillmentType>>; customerAddressId: ReturnType<typeof signal<string | null>>; preview: jest.Mock };
  let routerMock: { navigate: jest.Mock; url: string };
  let routeParentParamGetMock: jest.Mock;
  let authServiceMock: { customerProfile: jest.Mock; isLoggedIn: jest.Mock; restoreCustomerSession: jest.Mock };

  beforeEach(async () => {
    localStorage.clear();
    storeServiceMock = { getStoreById: jest.fn().mockReturnValue(of(null)) };
    checkoutServiceMock = {
      fulfillmentType: signal(FulfillmentType.Delivery),
      customerAddressId: signal(null),
      preview: jest.fn().mockReturnValue(of({ deliveryFee: 0, minimumOrderValue: 15, freeShippingApplied: false })),
    };
    routerMock = { navigate: jest.fn(), url: '/loja/carrinho' };
    routeParentParamGetMock = jest.fn().mockReturnValue('loja');
    authServiceMock = {
      customerProfile: jest.fn().mockReturnValue(null),
      isLoggedIn: jest.fn().mockReturnValue(false),
      restoreCustomerSession: jest.fn().mockReturnValue(of(null)),
    };

    await TestBed.configureTestingModule({
      imports: [CartPageComponent],
      providers: [
        CartService,
        { provide: CheckoutService, useValue: checkoutServiceMock },
        { provide: StoreService, useValue: storeServiceMock },
        { provide: AuthService, useValue: authServiceMock },
        { provide: Router, useValue: routerMock },
        { provide: ActivatedRoute, useValue: { parent: { snapshot: { paramMap: { get: routeParentParamGetMock } } } } },
        { provide: Location, useValue: { back: jest.fn() } },
      ],
    }).compileComponents();

    cart = TestBed.inject(CartService);
  });

  it('should render a cart item placeholder instead of a broken image when product image is missing', () => {
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('.cart-item-placeholder'))).not.toBeNull();
    expect(fixture.debugElement.query(By.css('.cart-product-card img'))).toBeNull();
  });

  it('should mark the clear-cart confirmation as a modal dialog', () => {
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', productImage: 'x.jpg', quantity: 1, unitPrice: 20 }]);

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.componentInstance.showClearConfirm.set(true);
    fixture.detectChanges();

    const modal = fixture.debugElement.query(By.css('.modal')).nativeElement as HTMLElement;

    expect(modal.getAttribute('role')).toBe('dialog');
    expect(modal.getAttribute('aria-modal')).toBe('true');
  });

  it('should use backend computed open status for cart availability and estimates', () => {
    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.componentInstance.store.set({ isOpen: true, isOpenNow: false, initialMinute: 30, finalMinute: 60 } as any);

    expect(fixture.componentInstance.storeOpen()).toBe(false);
    expect(fixture.componentInstance.etaDelivery()).toBe('');
    expect(fixture.componentInstance.etaPickup()).toBe('');
  });

  it('should refresh store status at backend-provided transition time', () => {
    jest.useFakeTimers();
    jest.setSystemTime(new Date('2026-07-28T20:59:59.000Z'));
    cart.setStore('s1', 'Loja', '');
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);
    storeServiceMock.getStoreById
      .mockReturnValueOnce(of({ id: 's1', isOpenNow: true, nextStatusChangeAt: '2026-07-28T21:00:00.000Z' } as any))
      .mockReturnValueOnce(of({ id: 's1', isOpenNow: false, closedMessage: 'A loja só estará aberta Quarta às 18:00.' } as any));

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.componentInstance.ngOnInit();
    jest.advanceTimersByTime(2000);

    expect(storeServiceMock.getStoreById).toHaveBeenCalledTimes(2);
    expect(fixture.componentInstance.storeOpen()).toBe(false);
    expect(fixture.componentInstance.store()?.closedMessage).toBe('A loja só estará aberta Quarta às 18:00.');
    jest.useRealTimers();
  });

  it('should use preview summary from below-minimum response without showing calculation error', () => {
    cart.setStore('s1', 'Loja', '');
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 10 }]);
    storeServiceMock.getStoreById.mockReturnValue(of({ id: 's1', isOpenNow: true, supportsDelivery: true, supportsPickup: true } as any));
    checkoutServiceMock.preview.mockReturnValue(throwError(() => ({
      status: 400,
      error: {
        error: 'Order is below minimum value.',
        summary: {
          deliveryFee: 0,
          minimumOrderValue: 20,
          freeShippingApplied: false,
        },
      },
    })));

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.componentInstance.ngOnInit();

    expect(fixture.componentInstance.checkoutPreviewError()).toBe(false);
    expect(fixture.componentInstance.minimumOrderValue()).toBe(20);
    expect(fixture.componentInstance.belowMinimum()).toBe(true);
  });

  it('should navigate to checkout cadastro using the parent store route', () => {
    routerMock.url = '/carrinho';
    cart.setStore('s1', 'Loja', '');
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);
    storeServiceMock.getStoreById.mockReturnValue(of({ id: 's1', isOpenNow: true, supportsDelivery: true, supportsPickup: true } as any));

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.componentInstance.ngOnInit();
    fixture.componentInstance.continueCheckout();

    expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja', 'checkout', 'cadastro']);
  });

  it('should skip cadastro and navigate to payment when the returning customer has a saved address', () => {
    authServiceMock.isLoggedIn.mockReturnValue(true);
    checkoutServiceMock.customerAddressId.set('addr1');
    cart.setStore('s1', 'Loja', '');
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);
    storeServiceMock.getStoreById.mockReturnValue(of({ id: 's1', isOpenNow: true, supportsDelivery: true, supportsPickup: true } as any));

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.componentInstance.ngOnInit();
    fixture.componentInstance.continueCheckout();

    expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja', 'checkout', 'pagamento']);
  });
});
