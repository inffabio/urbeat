import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient, HttpBackend } from '@angular/common/http';
import { Observable, Subject, switchMap, tap } from 'rxjs';
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
  UpdateCustomerProfileRequest,
  SellerProfileResponse,
  UpdateSellerProfileRequest,
} from '../../shared/models/auth.model';

const TOKEN_KEY = 'urbeat_token';
const SELLER_TOKEN_KEY = 'urbeat_seller_token';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(ApiService);
  private readonly httpBackend = inject(HttpBackend);
  private readonly rawHttp = new HttpClient(this.httpBackend);
  private readonly baseUrl = environment.apiUrl;

  readonly token = signal<string | null>(
    sessionStorage.getItem(SELLER_TOKEN_KEY) ?? localStorage.getItem(TOKEN_KEY),
  );
  readonly customerProfile = signal<CustomerProfileResponse | null>(null);
  readonly isAuthenticated = computed(() => !!this.token());
  private readonly tokenSubject = new Subject<string | null>();
  readonly token$ = this.tokenSubject.asObservable();

  register(req: RegisterCustomerRequest): Observable<RegisterResponse> {
    return this.api.post<RegisterResponse>('/api/auth/register/customer', req);
  }

  registerSeller(req: RegisterCustomerRequest): Observable<RegisterResponse> {
    return this.api.post<RegisterResponse>('/api/auth/register/seller', req);
  }

  getSellerProfile(): Observable<SellerProfileResponse> {
    return this.api.get<SellerProfileResponse>('/api/seller/profile');
  }

  updateSellerProfile(request: UpdateSellerProfileRequest): Observable<SellerProfileResponse> {
    return this.api.put<SellerProfileResponse>('/api/seller/profile', request);
  }

  login(req: LoginRequest): Observable<AuthTokenResponse> {
    return this.api
      .post<AuthTokenResponse>('/api/auth/login/customer', req)
      .pipe(tap((res) => this.saveToken(res)));
  }

  loginSeller(req: LoginRequest): Observable<AuthTokenResponse> {
    return this.api
      .post<AuthTokenResponse>('/api/auth/login/seller', req)
      .pipe(tap((res) => this.saveSellerToken(res.accessToken)));
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
    this.saveLocalToken(res.accessToken);
  }

  // Seller sessions are scoped to the tab so two stores can be signed in on the
  // same machine without overwriting each other. The conflicting generic token
  // is removed so the tab cannot fall back to another role's identity.
  private saveSellerToken(accessToken: string): void {
    sessionStorage.setItem(SELLER_TOKEN_KEY, accessToken);
    localStorage.removeItem(TOKEN_KEY);
    this.applyToken(accessToken);
  }

  // Customer and admin keep the legacy shared localStorage behavior, clearing
  // any tab-scoped seller token so roles are never mixed within the same tab.
  private saveLocalToken(accessToken: string): void {
    localStorage.setItem(TOKEN_KEY, accessToken);
    sessionStorage.removeItem(SELLER_TOKEN_KEY);
    this.applyToken(accessToken);
  }

  private applyToken(accessToken: string): void {
    this.token.set(accessToken);
    this.tokenSubject.next(accessToken);
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

  updateCustomerProfile(request: UpdateCustomerProfileRequest): Observable<CustomerProfileResponse> {
    return this.api.put<CustomerProfileResponse>('/api/customer/me', request).pipe(
      tap((profile) => this.customerProfile.set(profile)),
    );
  }

  logout(): void {
    // A seller session owns only the tab token; calling the shared-cookie logout
    // endpoint would revoke the refresh cookie used by other store tabs.
    const sellerSession = sessionStorage.getItem(SELLER_TOKEN_KEY) !== null;

    this.token.set(null);
    this.customerProfile.set(null);
    this.tokenSubject.next(null);

    if (sellerSession) {
      sessionStorage.removeItem(SELLER_TOKEN_KEY);
      return;
    }

    localStorage.removeItem(TOKEN_KEY);
    sessionStorage.removeItem(SELLER_TOKEN_KEY);

    this.rawHttp
      .post(`${this.baseUrl}/api/auth/logout`, {}, { withCredentials: true })
      .subscribe({ error: () => undefined });
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
