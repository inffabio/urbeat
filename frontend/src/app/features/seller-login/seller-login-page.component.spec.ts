import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { of } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';
import { StoreService } from '../../core/services/store.service';
import { ToastService } from '../../core/services/toast.service';
import { SellerLoginPageComponent } from './seller-login-page.component';

describe('SellerLoginPageComponent', () => {
  let fixture: ComponentFixture<SellerLoginPageComponent>;
  let component: SellerLoginPageComponent;
  let authServiceMock: { loginSeller: jest.Mock; resendConfirmation: jest.Mock };
  let storeServiceMock: { getMyStore: jest.Mock; getStorePublishSummary: jest.Mock };
  let routerMock: { navigate: jest.Mock };

  beforeEach(async () => {
    authServiceMock = {
      loginSeller: jest.fn(),
      resendConfirmation: jest.fn(),
    };
    storeServiceMock = {
      getMyStore: jest.fn(),
      getStorePublishSummary: jest.fn(),
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

  it('should navigate to /app/dashboard when store exists and can publish', () => {
    authServiceMock.loginSeller.mockReturnValue(of({ accessToken: 'token', refreshToken: 'refresh' }));
    storeServiceMock.getMyStore.mockReturnValue(of({ id: 'store-1' }));
    storeServiceMock.getStorePublishSummary.mockReturnValue(of({ canPublish: true }));
    component.loginForm.setValue({ email: 'seller@urbeat.com.br', password: '12345678' });

    component.onSubmit();

    expect(routerMock.navigate).toHaveBeenCalledWith(['/app/dashboard']);
  });

  it('should navigate to /configurar-loja when store exists but wizard is incomplete', () => {
    authServiceMock.loginSeller.mockReturnValue(of({ accessToken: 'token', refreshToken: 'refresh' }));
    storeServiceMock.getMyStore.mockReturnValue(of({ id: 'store-1' }));
    storeServiceMock.getStorePublishSummary.mockReturnValue(of({ canPublish: false }));
    component.loginForm.setValue({ email: 'seller@urbeat.com.br', password: '12345678' });

    component.onSubmit();

    expect(routerMock.navigate).toHaveBeenCalledWith(['/configurar-loja']);
  });

  it('should navigate to /configurar-loja when seller has no store yet', () => {
    authServiceMock.loginSeller.mockReturnValue(of({ accessToken: 'token', refreshToken: 'refresh' }));
    storeServiceMock.getMyStore.mockReturnValue(of(null));
    component.loginForm.setValue({ email: 'seller@urbeat.com.br', password: '12345678' });

    component.onSubmit();

    expect(routerMock.navigate).toHaveBeenCalledWith(['/configurar-loja']);
  });
});
