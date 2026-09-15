import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';
import { StoreService } from '../../core/services/store.service';
import { ToastService } from '../../core/services/toast.service';
import { SellerLoginPageComponent } from './seller-login-page.component';

describe('SellerLoginPageComponent', () => {
  let fixture: ComponentFixture<SellerLoginPageComponent>;
  let component: SellerLoginPageComponent;
  let authServiceMock: { loginSeller: jest.Mock; resendConfirmation: jest.Mock };
  let storeServiceMock: { getMyStore: jest.Mock };
  let routerMock: { navigate: jest.Mock };

  beforeEach(async () => {
    authServiceMock = {
      loginSeller: jest.fn(),
      resendConfirmation: jest.fn(),
    };
    storeServiceMock = {
      getMyStore: jest.fn(),
    };
    routerMock = { navigate: jest.fn() };

    await TestBed.configureTestingModule({
      imports: [SellerLoginPageComponent],
      providers: [
        { provide: AuthService, useValue: authServiceMock },
        { provide: StoreService, useValue: storeServiceMock },
        { provide: Router, useValue: routerMock },
        { provide: ActivatedRoute, useValue: {} },
        { provide: ToastService, useValue: { showError: jest.fn() } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SellerLoginPageComponent);
    component = fixture.componentInstance;
  });

  function login(email = 'seller@urbeat.com.br', password = '12345678'): void {
    authServiceMock.loginSeller.mockReturnValue(of({ accessToken: 'token', expiresAtUtc: '' }));
    component.loginForm.setValue({ email, password });
    component.onSubmit();
  }

  it('should navigate to /app/dashboard when the store is actually published', () => {
    storeServiceMock.getMyStore.mockReturnValue(of({ id: 'store-1', isPublished: true }));

    login();

    expect(routerMock.navigate).toHaveBeenCalledWith(['/app/dashboard']);
  });

  it('should navigate to /configurar-loja when the store is not published even if canPublish is true', () => {
    storeServiceMock.getMyStore.mockReturnValue(of({ id: 'store-1', isPublished: false, canPublish: true }));

    login();

    expect(routerMock.navigate).toHaveBeenCalledWith(['/configurar-loja']);
    expect(routerMock.navigate).not.toHaveBeenCalledWith(['/app/dashboard']);
  });

  it('should navigate to /configurar-loja when the store exists but is not published', () => {
    storeServiceMock.getMyStore.mockReturnValue(of({ id: 'store-1', isPublished: false }));

    login();

    expect(routerMock.navigate).toHaveBeenCalledWith(['/configurar-loja']);
  });

  it('should navigate to /configurar-loja when the seller has no store yet', () => {
    storeServiceMock.getMyStore.mockReturnValue(of(null));

    login();

    expect(routerMock.navigate).toHaveBeenCalledWith(['/configurar-loja']);
  });

  it('should navigate to /configurar-loja when loading the store fails', () => {
    storeServiceMock.getMyStore.mockReturnValue(throwError(() => new Error('Not found')));

    login();

    expect(routerMock.navigate).toHaveBeenCalledWith(['/configurar-loja']);
  });
});
