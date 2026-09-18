import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { signal } from '@angular/core';
import { of, Subject, throwError } from 'rxjs';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { CartPageComponent } from './cart-page.component';
import { CartService } from '../../core/services/cart.service';
import { CartCheckoutActionService } from '../../core/services/cart-checkout-action.service';
import { CheckoutService } from '../../core/services/checkout.service';
import { StoreService } from '../../core/services/store.service';
import { Router } from '@angular/router';
import { ActivatedRoute } from '@angular/router';
import { Location } from '@angular/common';
import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { AuthService } from '../../core/services/auth.service';

describe('CartPageComponent', () => {
  let cart: CartService;
  let storeServiceMock: { getStoreById: jest.Mock; getStoreByPath: jest.Mock };
  let checkoutServiceMock: { fulfillmentType: ReturnType<typeof signal<FulfillmentType>>; customerAddressId: ReturnType<typeof signal<string | null>>; preview: jest.Mock };
  let routerMock: { navigate: jest.Mock; url: string };
  let routeParentParamGetMock: jest.Mock;
  let authServiceMock: { customerProfile: jest.Mock; isLoggedIn: jest.Mock; restoreCustomerSession: jest.Mock };

  beforeEach(async () => {
    localStorage.clear();
    sessionStorage.clear();
    storeServiceMock = {
      getStoreById: jest.fn().mockReturnValue(of({ id: 's1', name: 'Loja', slug: 'loja', logoUrl: '', isOpenNow: true, supportsDelivery: true, supportsPickup: true })),
      getStoreByPath: jest.fn().mockReturnValue(of({ id: 's1', name: 'Loja', slug: 'loja', logoUrl: '' })),
    };
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

  it('should use registered add and remove icons for quantity controls', () => {
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('.qty-minus ion-icon')).componentInstance.name).toBe('remove');
    expect(fixture.debugElement.query(By.css('.qty-plus ion-icon')).componentInstance.name).toBe('add');
  });

  it('removes the clicked item from the cart page even when the item has no id', () => {
    cart.items.set([
      { productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 },
      { productId: 'p2', productName: 'Suco', quantity: 1, unitPrice: 5 },
    ]);

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.detectChanges();

    const removeButtons = fixture.debugElement.queryAll(By.css('.cart-remove'));
    expect(removeButtons.length).toBe(2);

    removeButtons[0].nativeElement.click();
    fixture.detectChanges();

    expect(cart.items().map((item) => item.productName)).toEqual(['Suco']);
  });

  it('publishes the checkout action visibility and disabled state for the shell', () => {
    const action = TestBed.inject(CartCheckoutActionService);
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.detectChanges();
    TestBed.flushEffects();

    expect(action.visible()).toBe(true);
    expect(action.disabled()).toBe(false);
  });

  it('publishes the registration detail while no customer profile is loaded', () => {
    const action = TestBed.inject(CartCheckoutActionService);
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.detectChanges();
    TestBed.flushEffects();

    expect(action.detail()).toBe('Registrar endereço e pagamento');
  });

  it('publishes the registration detail for an authenticated customer without a primary address', () => {
    authServiceMock.isLoggedIn.mockReturnValue(true);
    authServiceMock.customerProfile.mockReturnValue({ primaryAddressId: null });
    const action = TestBed.inject(CartCheckoutActionService);
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.detectChanges();
    TestBed.flushEffects();

    expect(action.detail()).toBe('Registrar endereço e pagamento');
  });

  it('publishes the payment detail once the authenticated customer profile is loaded', () => {
    authServiceMock.isLoggedIn.mockReturnValue(true);
    authServiceMock.customerProfile.mockReturnValue({ primaryAddressId: 'addr1' });
    const action = TestBed.inject(CartCheckoutActionService);
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.detectChanges();
    TestBed.flushEffects();

    expect(action.detail()).toBe('Pagamento');
  });

  it('marks the shell checkout action disabled while the cart is below the delivery minimum', () => {
    const action = TestBed.inject(CartCheckoutActionService);
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 5 }]);

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.detectChanges();
    TestBed.flushEffects();

    expect(action.visible()).toBe(true);
    expect(action.disabled()).toBe(true);
  });

  it('routes a continue request from the shell action into checkout navigation', () => {
    const action = TestBed.inject(CartCheckoutActionService);
    routerMock.url = '/carrinho';
    cart.setStore('s1', 'Loja', '');
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.componentInstance.ngOnInit();

    action.requestContinue();

    expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja', 'checkout', 'cadastro']);
  });

  it('resets the shared checkout action state when the cart page is destroyed', () => {
    const action = TestBed.inject(CartCheckoutActionService);
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.detectChanges();
    TestBed.flushEffects();
    expect(action.visible()).toBe(true);

    fixture.destroy();

    expect(action.visible()).toBe(false);
    expect(action.disabled()).toBe(false);
  });

  it('should keep the cart content out of fullscreen mode so the header is not clipped', () => {
    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.detectChanges();

    const content = fixture.debugElement.query(By.css('.cart-content')).nativeElement as HTMLElement;

    expect(content.hasAttribute('fullscreen')).toBe(false);
  });

  it('reserves bottom space in the cart scrollport for the fixed action bar', () => {
    const styles = readFileSync(resolve(__dirname, 'cart-page.component.scss'), 'utf8');

    expect(styles).toMatch(
      /\.cart-content\s*\{[\s\S]*--padding-bottom:\s*calc\(var\(--store-footer-clearance, calc\(64px \+ max\(8px, env\(safe-area-inset-bottom, 0px\)\)\)\) \+ 8px \+ 48px \+ 16px\)/,
    );
  });

  it('does not render the checkout action inside the routed cart page', () => {
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('app-sticky-action-bar'))).toBeNull();
  });

  it('uses the available viewport height without a fixed height that breaks scrolling', () => {
    const styles = readFileSync(resolve(__dirname, 'cart-page.component.scss'), 'utf8');
    const hostBlock = styles.match(/:host\s*\{([\s\S]*?)\n\s*\}/)?.[1] ?? '';
    const contentBlock = styles.match(/\.cart-content\s*\{([\s\S]*?)\n\s*\}/)?.[1] ?? '';

    expect(hostBlock).toContain('flex: 1 1 auto');
    expect(hostBlock).toContain('min-height: 0');
    expect(hostBlock).not.toMatch(/(?<!min-)height:\s*(100vh|100dvh|\d+px)/);
    expect(contentBlock).toContain('flex: 1 1 auto');
    expect(contentBlock).toContain('min-height: 0');
    expect(contentBlock).not.toMatch(/(?<!min-)height:\s*(100vh|100dvh|\d+px)/);
  });

  it('keeps the desktop cart max width at 620px', () => {
    const styles = readFileSync(resolve(__dirname, 'cart-page.component.scss'), 'utf8');

    expect(styles).toMatch(/@media\s*\(min-width:\s*900px\)\s*\{[\s\S]*max-width:\s*620px/);
  });

  it('should restore the store context from the route when persisted items have no store id', () => {
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);
    routeParentParamGetMock.mockReturnValue('loja');
    storeServiceMock.getStoreById.mockReturnValue(of({ id: 's1' } as any));
    storeServiceMock.getStoreByPath = jest.fn().mockReturnValue(of({ id: 's1', name: 'Loja', slug: 'loja', logoUrl: '' }));

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.componentInstance.ngOnInit();

    expect(storeServiceMock.getStoreByPath).toHaveBeenCalledWith('loja');
    expect(cart.storeId()).toBe('s1');
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

  it('should show the clear-cart confirmation copy and actions', () => {
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.componentInstance.showClearConfirm.set(true);
    fixture.detectChanges();

    const modalText = (fixture.debugElement.query(By.css('.modal')).nativeElement as HTMLElement).textContent ?? '';
    const buttonLabels = fixture.debugElement
      .queryAll(By.css('.modal-actions button'))
      .map((button) => (button.nativeElement.textContent as string).trim());

    expect(modalText).toContain('Deseja apagar todos os itens do carrinho?');
    expect(buttonLabels).toContain('Excluir');
    expect(buttonLabels).toContain('Cancelar');
  });

  it('gives the clear-cart confirmation dialog a focusable target and moves focus into it when opened', async () => {
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.detectChanges();

    const trigger = fixture.debugElement.query(By.css('[aria-label="Limpar carrinho"]')).nativeElement as HTMLButtonElement;
    trigger.click();
    fixture.detectChanges();
    await flushMicrotasks();

    const modal = fixture.debugElement.query(By.css('.modal')).nativeElement as HTMLElement;

    expect(modal.getAttribute('tabindex')).toBe('-1');
    expect(document.activeElement).toBe(modal);
  });

  it('makes the underlying cart content inert while the clear confirmation is open', () => {
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.detectChanges();

    const content = fixture.debugElement.query(By.css('ion-content')).nativeElement as HTMLElement;
    expect(content.hasAttribute('inert')).toBe(false);

    (fixture.debugElement.query(By.css('[aria-label="Limpar carrinho"]')).nativeElement as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(content.hasAttribute('inert')).toBe(true);

    const cancel = fixture.debugElement
      .queryAll(By.css('.modal-actions button'))
      .find((button) => (button.nativeElement.textContent as string).trim() === 'Cancelar')!.nativeElement as HTMLButtonElement;
    cancel.click();
    fixture.detectChanges();

    expect(content.hasAttribute('inert')).toBe(false);
  });

  it('cancels the clear confirmation on Escape without clearing the cart', () => {
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.componentInstance.showClearConfirm.set(true);
    fixture.detectChanges();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();

    expect(fixture.componentInstance.showClearConfirm()).toBe(false);
    expect(cart.items().length).toBe(1);
    expect(fixture.debugElement.query(By.css('.modal'))).toBeNull();
  });

  it('restores focus to the clear trigger when the confirmation is cancelled', async () => {
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.detectChanges();

    const trigger = fixture.debugElement.query(By.css('[aria-label="Limpar carrinho"]')).nativeElement as HTMLButtonElement;
    trigger.focus();
    trigger.click();
    fixture.detectChanges();
    await flushMicrotasks();

    const modal = fixture.debugElement.query(By.css('.modal')).nativeElement as HTMLElement;
    expect(modal.contains(document.activeElement)).toBe(true);

    const cancel = fixture.debugElement
      .queryAll(By.css('.modal-actions button'))
      .find((button) => (button.nativeElement.textContent as string).trim() === 'Cancelar')!.nativeElement as HTMLButtonElement;
    cancel.click();
    fixture.detectChanges();
    await flushMicrotasks();

    expect(document.activeElement).toBe(trigger);
  });

  it('restores focus to the clear trigger when Escape cancels the confirmation', async () => {
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.detectChanges();

    const trigger = fixture.debugElement.query(By.css('[aria-label="Limpar carrinho"]')).nativeElement as HTMLButtonElement;
    trigger.focus();
    trigger.click();
    fixture.detectChanges();
    await flushMicrotasks();

    const modal = fixture.debugElement.query(By.css('.modal')).nativeElement as HTMLElement;
    expect(modal.contains(document.activeElement)).toBe(true);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    await flushMicrotasks();

    expect(document.activeElement).toBe(trigger);
  });

  it('should keep the cart populated and not navigate when the clear confirmation is cancelled', () => {
    cart.setStore('s1', 'Loja', '');
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.componentInstance.showClearConfirm.set(true);
    fixture.detectChanges();

    const cancelButton = fixture.debugElement
      .queryAll(By.css('.modal-actions button'))
      .find((button) => (button.nativeElement.textContent as string).trim() === 'Cancelar');
    cancelButton!.nativeElement.click();
    fixture.detectChanges();

    expect(cart.items().length).toBe(1);
    expect(fixture.componentInstance.showClearConfirm()).toBe(false);
    expect(routerMock.navigate).not.toHaveBeenCalled();
  });

  it('should clear the cart and return to the store menu when the clear confirmation is accepted', () => {
    routeParentParamGetMock.mockReturnValue('loja');
    cart.setStore('s1', 'Loja', '');
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.componentInstance.showClearConfirm.set(true);
    fixture.detectChanges();

    const excluirButton = fixture.debugElement
      .queryAll(By.css('.modal-actions button'))
      .find((button) => (button.nativeElement.textContent as string).trim() === 'Excluir');
    excluirButton!.nativeElement.click();
    fixture.detectChanges();

    expect(cart.items()).toEqual([]);
    expect(routerMock.navigate).toHaveBeenCalledWith(['/', 'loja']);
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

  it('should keep an uncovered neighborhood silent in the cart for the payment modal', () => {
    cart.setStore('s1', 'Loja', '');
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);
    storeServiceMock.getStoreById.mockReturnValue(of({ id: 's1', isOpenNow: true, supportsDelivery: true, supportsPickup: true } as any));
    checkoutServiceMock.preview.mockReturnValue(throwError(() => ({
      status: 400,
      error: { error: 'Ainda nao entregamos no seu bairro.' },
    })));

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.checkoutPreviewError()).toBe(false);
    expect(fixture.debugElement.query(By.css('.error-banner'))).toBeNull();
    expect(fixture.componentInstance.deliveryFeePending()).toBe(true);
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

  it('should calculate delivery fee with the returning customer address before checkout', () => {
    authServiceMock.isLoggedIn.mockReturnValue(true);
    authServiceMock.customerProfile.mockReturnValue({ primaryAddressId: 'addr1' });
    cart.setStore('s1', 'Loja', '');
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);
    checkoutServiceMock.preview.mockReturnValue(of({ deliveryFee: 8, minimumOrderValue: 15, freeShippingApplied: false }));

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.detectChanges();

    expect(checkoutServiceMock.preview).toHaveBeenCalledWith(expect.objectContaining({ customerAddressId: 'addr1' }));
    expect(fixture.componentInstance.deliveryFee()).toBe(8);
    expect(fixture.componentInstance.deliveryFeePending()).toBe(false);
    fixture.detectChanges();

    const summaryText = (fixture.debugElement.query(By.css('.summary-card')).nativeElement.textContent as string)
      .replace(/\u00a0/g, ' ');
    expect(summaryText).toContain('R$ 8,00');
    expect(summaryText).toContain('R$ 28,00');
  });

  it('does not apply or reschedule a pending store request that resolves after destroy', () => {
    jest.useFakeTimers();
    jest.setSystemTime(new Date('2026-07-28T20:59:59.000Z'));
    const pending = new Subject<any>();
    cart.setStore('s1', 'Loja', '');
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);
    storeServiceMock.getStoreById.mockReturnValue(pending.asObservable());

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.componentInstance.ngOnInit();

    fixture.destroy();
    pending.next({ id: 's1', isOpenNow: true, nextStatusChangeAt: '2026-07-28T21:00:00.000Z' });
    jest.advanceTimersByTime(5000);

    expect(fixture.componentInstance.store()).toBeNull();
    expect(storeServiceMock.getStoreById).toHaveBeenCalledTimes(1);
    jest.useRealTimers();
  });

  it('ignores a pending route store lookup that resolves after destroy', () => {
    const pending = new Subject<any>();
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);
    routeParentParamGetMock.mockReturnValue('loja');
    storeServiceMock.getStoreByPath.mockReturnValue(pending.asObservable());

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.componentInstance.ngOnInit();

    fixture.destroy();
    pending.next({ id: 's1', name: 'Loja', logoUrl: '' });

    expect(cart.storeId()).toBeNull();
    expect(storeServiceMock.getStoreById).not.toHaveBeenCalled();
  });

  it('ignores a pending checkout preview that resolves after destroy', () => {
    const pending = new Subject<any>();
    cart.setStore('s1', 'Loja', '');
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);
    checkoutServiceMock.preview.mockReturnValue(pending.asObservable());

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.componentInstance.ngOnInit();

    fixture.destroy();
    pending.next({ deliveryFee: 9, minimumOrderValue: 30, freeShippingApplied: true });

    expect(fixture.componentInstance.deliveryFee()).toBe(0);
    expect(fixture.componentInstance.deliveryFeeKnown()).toBe(false);
    expect(fixture.componentInstance.minimumOrderValue()).toBe(15);
  });

  it('ignores a pending customer session restore that resolves after destroy', () => {
    const pending = new Subject<any>();
    cart.setStore('s1', 'Loja', '');
    cart.items.set([{ id: 'i1', productId: 'p1', productName: 'X-burguer', quantity: 1, unitPrice: 20 }]);
    authServiceMock.restoreCustomerSession.mockReturnValue(pending.asObservable());

    const fixture = TestBed.createComponent(CartPageComponent);
    fixture.componentInstance.ngOnInit();

    fixture.destroy();
    pending.next({ primaryAddressId: 'addr1' });

    expect(checkoutServiceMock.customerAddressId()).toBeNull();
    expect(checkoutServiceMock.preview).not.toHaveBeenCalled();
  });
});

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}
