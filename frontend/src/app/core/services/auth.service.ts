import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient, HttpBackend } from '@angular/common/http';
import { Observable, switchMap, tap } from 'rxjs';
import { ApiService } from './api.service';
import { environment } from '../../../environments/environment';
import {
  RegisterCustomerRequest,
  RegisterResponse,
  LoginRequest,
  AuthTokenResponse,
  ConfirmEmailRequest,
  ConfirmEmailResponse,
  ResendConfirmationRequest,
  ForgotPasswordRequest,
  ForgotPasswordResponse,
  ValidateResetTokenResponse,
  ResetPasswordRequest,
  ResetPasswordResponse,
  UpdateEmailRequest,
  CustomerProfileResponse,
} from '../../shared/models/auth.model';

const TOKEN_KEY = 'urbeat_token';
const REFRESH_KEY = 'urbeat_refresh';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(ApiService);
  private readonly httpBackend = inject(HttpBackend);
  private readonly rawHttp = new HttpClient(this.httpBackend);
  private readonly baseUrl = environment.apiUrl;

  readonly token = signal<string | null>(localStorage.getItem(TOKEN_KEY));
  readonly customerProfile = signal<CustomerProfileResponse | null>(null);
  readonly isAuthenticated = computed(() => !!this.token());

  register(req: RegisterCustomerRequest): Observable<RegisterResponse> {
    return this.api.post<RegisterResponse>('/api/auth/register/customer', req);
  }

  registerSeller(req: RegisterCustomerRequest): Observable<RegisterResponse> {
    return this.api.post<RegisterResponse>('/api/auth/register/seller', req);
  }

  login(req: LoginRequest): Observable<AuthTokenResponse> {
    return this.api
      .post<AuthTokenResponse>('/api/auth/login/customer', req)
      .pipe(tap((res) => this.saveToken(res)));
  }

  loginSeller(req: LoginRequest): Observable<AuthTokenResponse> {
    return this.api
      .post<AuthTokenResponse>('/api/auth/login/seller', req)
      .pipe(tap((res) => this.saveToken(res)));
  }

  loginAdmin(req: LoginRequest): Observable<AuthTokenResponse> {
    return this.api
      .post<AuthTokenResponse>('/api/auth/login/admin', req)
      .pipe(tap((res) => this.saveToken(res)));
  }

  confirmEmail(req: ConfirmEmailRequest): Observable<ConfirmEmailResponse> {
    return this.api.post<ConfirmEmailResponse>('/api/auth/email/confirm', req);
  }

  confirmEmailByCode(code: string): Observable<ConfirmEmailResponse> {
    return this.api.post<ConfirmEmailResponse>(`/api/auth/email/confirm/${code}`, {});
  }

  resendConfirmation(req: ResendConfirmationRequest): Observable<ConfirmEmailResponse> {
    return this.api.post<ConfirmEmailResponse>('/api/auth/email/resend-confirmation', req);
  }

  saveToken(res: AuthTokenResponse): void {
    localStorage.setItem(TOKEN_KEY, res.accessToken);
    localStorage.setItem(REFRESH_KEY, res.refreshToken);
    this.token.set(res.accessToken);
  }

  getToken(): string | null {
    return this.token();
  }

  isLoggedIn(): boolean {
    return this.isAuthenticated();
  }

  refreshToken(): Observable<AuthTokenResponse> {
    return this.rawHttp.post<AuthTokenResponse>(
      `${this.baseUrl}/api/auth/refresh`,
      {},
      { withCredentials: true }
    ).pipe(tap((res) => this.saveToken(res)));
  }

  restoreCustomerSession(): Observable<CustomerProfileResponse> {
    if (this.isLoggedIn()) {
      return this.api.get<CustomerProfileResponse>('/api/customer/me').pipe(
        tap((profile) => this.customerProfile.set(profile)),
      );
    }

    return this.refreshToken().pipe(
      switchMap(() => this.api.get<CustomerProfileResponse>('/api/customer/me')),
      tap((profile) => this.customerProfile.set(profile)),
    );
  }

  logout(): void {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(REFRESH_KEY);
    this.token.set(null);
    this.customerProfile.set(null);
  }

  forgotPassword(req: ForgotPasswordRequest): Observable<ForgotPasswordResponse> {
    return this.api.post<ForgotPasswordResponse>('/api/auth/forgot-password', req);
  }

  validateResetToken(token: string): Observable<ValidateResetTokenResponse> {
    return this.api.get<ValidateResetTokenResponse>(`/api/auth/validate-reset-token?token=${encodeURIComponent(token)}`);
  }

  resetPassword(req: ResetPasswordRequest): Observable<ResetPasswordResponse> {
    return this.api.post<ResetPasswordResponse>('/api/auth/reset-password', req);
  }

  updateEmail(req: UpdateEmailRequest): Observable<{ message: string }> {
    return this.api.post<{ message: string }>('/api/auth/update-email', req);
  }
}
