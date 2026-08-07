import { Routes } from '@angular/router';
import { authChildGuard, authGuard } from './core/guards/auth.guard';
import { adminGuard } from './core/guards/admin.guard';
import { pendingChangesGuard } from './core/guards/pending-changes.guard';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./features/landing-page/landing-page.component').then(
        (m) => m.LandingPageComponent,
      ),
  },
  {
    path: 'landpage',
    loadComponent: () =>
      import('./features/landing-page/landing-page.component').then(
        (m) => m.LandingPageComponent,
      ),
  },
  {
    path: 'login-vendedor',
    loadComponent: () =>
      import('./features/seller-login/seller-login-page.component').then(
        (m) => m.SellerLoginPageComponent,
      ),
  },
  {
    path: 'recuperar-senha',
    loadComponent: () =>
      import('./features/forgot-password/forgot-password-page.component').then(
        (m) => m.ForgotPasswordPageComponent,
      ),
  },
  {
    path: 'recuperar-senha/email-enviado',
    loadComponent: () =>
      import('./features/forgot-password/email-sent-page.component').then(
        (m) => m.EmailSentPageComponent,
      ),
  },
  {
    path: 'redefinir-senha',
    loadComponent: () =>
      import('./features/forgot-password/reset-password-page.component').then(
        (m) => m.ResetPasswordPageComponent,
      ),
  },
  {
    path: 'cadastro',
    loadComponent: () =>
      import('./features/seller-register/seller-register-page.component').then(
        (m) => m.SellerRegisterPageComponent,
      ),
  },
  {
    path: 'painel/login',
    loadComponent: () =>
      import('./features/admin-login/admin-login-page.component').then(
        (m) => m.AdminLoginPageComponent,
      ),
  },
  {
    path: 'painel/landing-page',
    canActivate: [adminGuard],
    loadComponent: () =>
      import('./features/landing-page-admin/landing-page-admin.component').then(
        (m) => m.LandingPageAdminComponent,
      ),
  },
  {
    path: 'confirmacao-email',
    loadComponent: () =>
      import('./features/email-confirmation/email-confirmation-page.component').then(
        (m) => m.EmailConfirmationPageComponent,
      ),
  },
  {
    path: 'c/:code',
    loadComponent: () =>
      import('./features/email-confirm/email-confirm-page.component').then(
        (m) => m.EmailConfirmPageComponent,
      ),
  },
  {
    path: 'confirmar-email',
    loadComponent: () =>
      import('./features/email-confirm/email-confirm-page.component').then(
        (m) => m.EmailConfirmPageComponent,
      ),
  },
  {
    path: 'configurar-loja',
    children: [
      {
        path: '',
        loadComponent: () =>
          import('./features/store-config/store-config-page.component').then(
            (m) => m.StoreConfigPageComponent,
          ),
      },
      {
        path: 'horarios',
        loadComponent: () =>
          import('./features/store-config/hours/store-hours-page.component').then(
            (m) => m.StoreHoursPageComponent,
          ),
      },
      {
        path: 'entrega',
        loadComponent: () =>
          import('./features/store-config/delivery/store-delivery-page.component').then(
            (m) => m.StoreDeliveryPageComponent,
          ),
      },
      {
        path: 'produtos',
        loadComponent: () =>
          import('./features/store-config/products/store-products-page.component').then(
            (m) => m.StoreProductsPageComponent,
          ),
      },
      {
        path: 'publicar',
        loadComponent: () =>
          import('./features/store-config/publish/store-publish-page.component').then(
            (m) => m.StorePublishPageComponent,
          ),
      }
    ]
  },
  {
    path: 'produtos',
    loadComponent: () =>
      import('./features/products/products-page.component').then(
        (m) => m.ProductsPageComponent,
      ),
  },
  {
    path: 'app',
    canActivate: [authGuard],
    canActivateChild: [authChildGuard],
    loadComponent: () =>
      import('./features/seller-shell/seller-app-shell.component').then(
        (m) => m.SellerAppShellComponent,
      ),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        loadComponent: () =>
          import('./features/seller-dashboard/seller-dashboard-page.component').then(
            (m) => m.SellerDashboardPageComponent,
          ),
      },
      {
        path: 'pedidos',
        loadComponent: () =>
          import('./features/seller-orders/seller-orders-page.component').then(
            (m) => m.SellerOrdersPageComponent,
          ),
      },
      {
        path: 'clientes',
        loadComponent: () =>
          import('./features/seller-customers/seller-customers-page.component').then(
            (m) => m.SellerCustomersPageComponent,
          ),
      },
      {
        path: 'avaliacoes',
        loadComponent: () =>
          import('./features/seller-reviews/seller-reviews-page.component').then(
            (m) => m.SellerReviewsPageComponent,
          ),
      },
      {
        path: 'marketing',
        loadComponent: () =>
          import('./features/seller-marketing/seller-marketing-page.component').then(
            (m) => m.SellerMarketingPageComponent,
          ),
      },
      {
        path: 'cardapio',
        redirectTo: 'cardapio/produtos',
        pathMatch: 'full',
      },
      {
        path: 'cardapio/produtos',
        canDeactivate: [pendingChangesGuard],
        loadComponent: () =>
          import('./features/seller-products/seller-products-page.component').then(
            (m) => m.SellerProductsPageComponent,
          ),
      },
      {
        path: 'cardapio/categorias',
        loadComponent: () =>
          import('./features/seller-categories/seller-categories-page.component').then(
            (m) => m.SellerCategoriesPageComponent,
          ),
      },
      {
        path: 'cardapio/adicionais',
        loadComponent: () =>
          import('./features/seller-additionals/seller-additionals-page.component').then(
            (m) => m.SellerAdditionalsPageComponent,
          ),
      },
      {
        path: 'configuracoes/informacoes',
        loadComponent: () =>
          import('./features/seller-store-info/seller-store-info-page.component').then(
            (m) => m.SellerStoreInfoPageComponent,
          ),
      },
      {
        path: 'configuracoes/bio',
        canDeactivate: [pendingChangesGuard],
        loadComponent: () =>
          import('./features/seller-bio/seller-bio-page.component').then(
            (m) => m.SellerBioPageComponent,
          ),
      },
      {
        path: 'configuracoes/horarios',
        canDeactivate: [pendingChangesGuard],
        loadComponent: () =>
          import('./features/seller-hours/seller-hours-page.component').then(
            (m) => m.SellerHoursPageComponent,
          ),
      },
      {
        path: 'configuracoes/bairros',
        canDeactivate: [pendingChangesGuard],
        loadComponent: () =>
          import('./features/seller-neighborhoods/seller-neighborhoods-page.component').then(
            (m) => m.SellerNeighborhoodsPageComponent,
          ),
      },
      {
        path: 'configuracoes/impressao',
        loadComponent: () =>
          import('./features/seller-printing/seller-printing-page.component').then(
            (m) => m.SellerPrintingPageComponent,
          ),
      },
      {
        path: 'mensalidade',
        loadComponent: () =>
          import('./features/seller-subscription/seller-subscription-page.component').then(
            (m) => m.SellerSubscriptionPageComponent,
          ),
      },
      {
        path: 'instalar',
        loadComponent: () =>
          import('./features/seller-install/seller-install-page.component').then(
            (m) => m.SellerInstallPageComponent,
          ),
      },
    ],
  },
  {
    path: ':storePath',
    loadComponent: () =>
      import('./features/store/store-shell.component').then((m) => m.StoreShellComponent),
    children: [
      {
        path: '',
        loadComponent: () =>
          import('./features/store/store-page.component').then((m) => m.StorePageComponent),
      },
      {
        path: 'produto/:productId',
        loadComponent: () =>
          import('./features/product-detail/product-detail-page.component').then(
            (m) => m.ProductDetailPageComponent,
          ),
      },
      {
        path: 'carrinho',
        loadComponent: () =>
          import('./features/cart/cart-page.component').then((m) => m.CartPageComponent),
      },
      {
        path: 'checkout/cadastro',
        loadComponent: () =>
          import('./features/checkout/customer-page.component').then((m) => m.CustomerPageComponent),
      },
      {
        path: 'checkout/pagamento',
        loadComponent: () =>
          import('./features/checkout/payment-page.component').then((m) => m.PaymentPageComponent),
      },
      {
        path: 'checkout/confirmar-sms',
        loadComponent: () =>
          import('./features/checkout/sms-verification-page.component').then(
            (m) => m.SmsVerificationPageComponent,
          ),
      },
      {
        path: 'checkout/pagar',
        loadComponent: () =>
          import('./features/payment/online/online-payment-page.component').then(
            (m) => m.OnlinePaymentPageComponent,
          ),
      },
      {
        path: 'checkout/entrega',
        loadComponent: () =>
          import('./features/payment/delivery/delivery-payment-page.component').then(
            (m) => m.DeliveryPaymentPageComponent,
          ),
      },
      {
        path: 'pedido/:orderId',
        loadComponent: () =>
          import('./features/order-tracking/tracking-page.component').then(
            (m) => m.TrackingPageComponent,
          ),
      },
    ],
  },
  { path: '', redirectTo: '', pathMatch: 'full' },
  { path: '**', redirectTo: '' },
];
