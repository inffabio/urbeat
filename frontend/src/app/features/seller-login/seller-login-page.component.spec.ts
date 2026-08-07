import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { of } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';
import { ToastService } from '../../core/services/toast.service';
import { SellerLoginPageComponent } from './seller-login-page.component';

describe('SellerLoginPageComponent', () => {
  let fixture: ComponentFixture<SellerLoginPageComponent>;
  let component: SellerLoginPageComponent;
  let authServiceMock: { loginSeller: jest.Mock; resendConfirmation: jest.Mock };
  let routerMock: { navigate: jest.Mock };

  beforeEach(async () => {
    authServiceMock = {
      loginSeller: jest.fn(),
      resendConfirmation: jest.fn(),
    };
    routerMock = { navigate: jest.fn() };

    await TestBed.configureTestingModule({
      imports: [SellerLoginPageComponent],
      providers: [
        { provide: AuthService, useValue: authServiceMock },
        { provide: Router, useValue: routerMock },
        { provide: ActivatedRoute, useValue: {} },
        { provide: ToastService, useValue: { showError: jest.fn() } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SellerLoginPageComponent);
    component = fixture.componentInstance;
  });

  it('should navigate to /app/dashboard after seller login succeeds', () => {
    authServiceMock.loginSeller.mockReturnValue(of({ accessToken: 'token', refreshToken: 'refresh' }));
    component.loginForm.setValue({ email: 'seller@urbeat.com.br', password: '12345678' });

    component.onSubmit();

    expect(routerMock.navigate).toHaveBeenCalledWith(['/app/dashboard']);
  });
});
