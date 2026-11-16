import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient, HttpBackend } from '@angular/common/http';
import { Observable, Subject, map, of, switchMap, tap } from 'rxjs';
import { ApiService } from './api.service';
import { environment } from '../../../environments/environment';
import { resolveTokenRole, TokenRole } from '../utils/jwt.helper';
import {
  RegisterCustomerRequest,
  RegisterResponse,
  LoginRequest,
  AuthTokenResponse,
  ConfirmEmailRequest,
  ConfirmEmailResponse,
  ConfirmRolePromotionRequest,
  ConfirmRolePromotionResponse,
  ResendConfirmationRequest,
  ResendConfirmationResponse,
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
  ChangePasswordRequest,
  AccountRole,
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
  /**
   * Identity the tab may act as. Resolved from the most privileged role in the token so an
   * administrative/seller credential is never mistaken for a customer session.
   */
  readonly identity = computed<TokenRole | null>(() => {
    const token = this.token();
    return token ? resolveTokenRole(token) : null;
  });
  readonly isCustomerSession = computed(() => this.identity() === 'Customer');
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

  resendConfirmation(req: ResendConfirmationRequest): Observable<ResendConfirmationResponse> {
    return this.api.post<ResendConfirmationResponse>('/api/auth/email/resend-confirmation', req);
  }

  /**
   * Confirms a pending second-role promotion. This endpoint is distinct from registration e-mail
   * confirmation and e-mail change; a promotion token is never accepted by those flows.
   */
  confirmRolePromotion(token: string): Observable<ConfirmRolePromotionResponse> {
    const request: ConfirmRolePromotionRequest = { token };
    return this.api.post<ConfirmRolePromotionResponse>('/api/auth/role-promotion/confirm', request);
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
  // the tab-scoped seller token so roles are never mixed within the same tab.
  private saveLocalToken(accessToken: string): void {
    localStorage.setItem(TOKEN_KEY, accessToken);
    sessionStorage.removeItem(SELLER_TOKEN_KEY);
    this.applyToken(accessToken);
  }

  private applyToken(accessToken: string): void {
    this.token.set(accessToken);
    // A seller/administrative credential must never keep a customer profile cached from a
    // previous session in the same tab: the storefront reads this signal to greet the customer.
    if (this.customerProfile() && resolveTokenRole(accessToken) !== 'Customer') {
      this.customerProfile.set(null);
    }
    this.tokenSubject.next(accessToken);
  }

  getToken(): string | null {
    return this.token();
  }

  isLoggedIn(): boolean {
    return this.isAuthenticated();
  }

  refreshToken(): Observable<AuthTokenResponse> {
    return this.requestRefreshToken().pipe(tap((res) => this.saveToken(res)));
  }

  /**
   * Restores a customer session only when the credential held by this tab belongs to a customer.
   *
   * An administrator or seller token is intentionally ignored: it is neither refreshed nor
   * exchanged for a `/api/customer/me` profile, so opening the public storefront with another
   * surface's session keeps the storefront anonymous (no 403, no toast, no redirect). The
   * administrative and seller tabs keep their own tokens untouched.
   */
  restoreCustomerSession(): Observable<CustomerProfileResponse | null> {
    if (this.token()) {
      return this.identity() === 'Customer' ? this.loadCustomerProfile() : of(null);
    }

    return this.refreshCustomerAccessToken().pipe(
      switchMap((restored) => (restored ? this.loadCustomerProfile() : of(null))),
    );
  }

  private loadCustomerProfile(): Observable<CustomerProfileResponse> {
    return this.api.get<CustomerProfileResponse>('/api/customer/me').pipe(
      tap((profile) => this.customerProfile.set(profile)),
    );
  }

  /**
   * Rotates the shared httpOnly refresh cookie and adopts the new access token only when the
   * cookie belongs to a customer. A seller/administrator cookie is rotated by the server but must
   * never be persisted by a public page, otherwise navigating the storefront would reactivate a
   * privileged session in this tab.
   */
  private refreshCustomerAccessToken(): Observable<boolean> {
    return this.requestRefreshToken().pipe(
      map((response) => {
        if (resolveTokenRole(response.accessToken) !== 'Customer') return false;

        this.saveToken(response);
        return true;
      }),
    );
  }

  private requestRefreshToken(): Observable<AuthTokenResponse> {
    return this.rawHttp.post<AuthTokenResponse>(
      `${this.baseUrl}/api/auth/refresh`,
      {},
      { withCredentials: true }
    );
  }

  updateCustomerProfile(request: UpdateCustomerProfileRequest): Observable<CustomerProfileResponse> {
    return this.api.put<CustomerProfileResponse>('/api/customer/me', request).pipe(
      tap((profile) => this.customerProfile.set(profile)),
    );
  }

  logout(): void {
    // Tab-scoped seller sessions own only their tab token; calling the
    // shared-cookie logout endpoint would revoke the refresh cookie used by
    // other tabs.
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

  validateResetToken(token: string, role?: AccountRole): Observable<ValidateResetTokenResponse> {
    const roleQuery = role ? `&role=${encodeURIComponent(role)}` : '';
    return this.api.get<ValidateResetTokenResponse>(
      `/api/auth/validate-reset-token?token=${encodeURIComponent(token)}${roleQuery}`,
    );
  }

  resetPassword(req: ResetPasswordRequest): Observable<ResetPasswordResponse> {
    return this.api.post<ResetPasswordResponse>('/api/auth/reset-password', req);
  }

  changePassword(req: ChangePasswordRequest): Observable<{ message: string }> {
    return this.api.post<{ message: string }>('/api/auth/change-password', req);
  }

  updateEmail(req: UpdateEmailRequest): Observable<{ message: string }> {
    return this.api.post<{ message: string }>('/api/auth/update-email', req);
  }
}
