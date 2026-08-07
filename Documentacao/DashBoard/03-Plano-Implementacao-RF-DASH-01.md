# RF-DASH-01 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use test-driven-development before production code. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Criar a base operacional do painel do lojista em `/app/dashboard`, com shell protegido, redirecionamento do login vendedor, SignalR de novos pedidos e alerta sonoro configuravel.

**Architecture:** O frontend ganha um `SellerAppShellComponent` protegido por `authGuard`, uma pagina inicial `SellerDashboardPageComponent`, componentes compartilhados pequenos e uma facade central `SellerShellFacade` para loja, notificacoes e eventos de novos pedidos. O backend existente ja emite `ReceiveSellerNotification` via `/hubs/seller-notifications`; nesta entrega o frontend consome esse evento e recarrega dados reais, sem confiar no payload como fonte unica.

**Tech Stack:** Angular 20 standalone, Ionic 8 standalone, Angular Signals, RxJS, SignalR, Jest, .NET 9 APIs existentes.

## Global Constraints

- Rota base aprovada: `/app`.
- Primeira rota autenticada do lojista: `/app/dashboard`.
- Login real reaproveitado: `/login-vendedor` com `AuthService.loginSeller`.
- Dashboard generico para qualquer loja Urbeat; nao hardcodar Brasa Burguer, hamburgueria, imagens seed ou dados ficticios como conteudo real.
- Usar design system Urbeat: Inter, bordeaux/cream, superficies brancas, cantos arredondados, mobile-first e touch targets de 44px+.
- Backend continua dono das regras sensiveis; frontend apresenta dados e chama APIs.
- Evento real atual de novo pedido para vendedor: SignalR `ReceiveSellerNotification` com `NotificationType.NewOrder`.
- Som de pedido e preferencia local de UI; nao e regra de negocio.
- Rotas `/app/*` devem ser declaradas antes de `:storePath`.

---

## File Structure

### Criar

- `frontend/src/app/features/seller-shell/seller-app-shell.component.ts`: shell `/app` com `ion-split-pane`, `ion-menu`, listener SignalR e outlet.
- `frontend/src/app/features/seller-shell/seller-app-shell.component.html`: estrutura do shell, sidebar, topbar e outlet.
- `frontend/src/app/features/seller-shell/seller-app-shell.component.scss`: layout desktop/mobile no design system Urbeat.
- `frontend/src/app/features/seller-shell/seller-shell.facade.ts`: carrega loja/notificacoes, inicia hub, trata notificacao de novo pedido, expoe signals.
- `frontend/src/app/features/seller-shell/seller-shell.facade.spec.ts`: testes de fluxo de notificacao e som.
- `frontend/src/app/features/seller-shell/order-sound-alert.service.ts`: adapter de audio para novo pedido.
- `frontend/src/app/features/seller-shell/order-sound-alert.service.spec.ts`: testes de preferencia e falha de autoplay.
- `frontend/src/app/features/seller-dashboard/seller-dashboard-page.component.ts`: pagina inicial do painel.
- `frontend/src/app/features/seller-dashboard/seller-dashboard-page.component.html`: metricas basicas, sala aguardando pedidos e ultimos pedidos.
- `frontend/src/app/features/seller-dashboard/seller-dashboard-page.component.scss`: cards e estados responsivos.
- `frontend/src/app/features/seller-dashboard/seller-dashboard-page.component.spec.ts`: testes de render/estado de novo pedido.
- `frontend/src/app/shared/components/page-header/page-header.component.ts`: cabecalho compartilhado de paginas do painel.
- `frontend/src/app/shared/components/page-header/page-header.component.html`: titulo, descricao e acoes.
- `frontend/src/app/shared/components/page-header/page-header.component.scss`: estilo Urbeat.
- `frontend/src/app/shared/components/metric-card/metric-card.component.ts`: card de metrica compartilhado.
- `frontend/src/app/shared/components/metric-card/metric-card.component.html`: label, valor e apoio.
- `frontend/src/app/shared/components/metric-card/metric-card.component.scss`: estilo Urbeat.
- `frontend/src/app/core/services/seller-notification.service.ts`: wrapper HTTP para `/api/seller/notifications`.
- `frontend/src/app/shared/models/seller-notification.model.ts`: modelos de notificacao do vendedor.

### Modificar

- `frontend/src/app/app.routes.ts`: adicionar `/app` antes de `:storePath`, dashboard filho e redirects legados.
- `frontend/src/app/features/seller-login/seller-login-page.component.ts`: redirecionar login para `/app/dashboard`.
- `frontend/src/app/core/guards/auth.guard.ts`: redirecionar nao autenticado para `/login-vendedor`.
- `frontend/src/app/core/services/order.service.ts`: adicionar endpoints de vendedor usados pelo dashboard inicial.
- `frontend/src/app/core/services/signalr.service.ts`: adicionar `off` simetrico ou garantir listener removivel de vendedor.
- `frontend/src/app/core/icons.ts`: registrar icones usados pelo shell/dashboard, se necessario.

---

## Task 1: Rotas Protegidas e Redirect do Login

**Files:**
- Modify: `frontend/src/app/app.routes.ts`
- Modify: `frontend/src/app/features/seller-login/seller-login-page.component.ts`
- Modify: `frontend/src/app/core/guards/auth.guard.ts`
- Test: `frontend/src/app/features/seller-login/seller-login-page.component.spec.ts` ou criar se ainda nao existir

**Interfaces:**
- Consumes: `AuthService.isLoggedIn(): boolean`, `AuthService.loginSeller()`.
- Produces: rota `/app` protegida com filho `/app/dashboard`; login vendedor navega para `/app/dashboard`.

- [ ] **Step 1: Write failing login redirect test**

Add/adjust a test asserting seller login navigates to `/app/dashboard`:

```ts
it('should navigate to /app/dashboard after seller login succeeds', () => {
  authServiceMock.loginSeller.mockReturnValue(of({ accessToken: 'token', refreshToken: 'refresh' }));
  component.loginForm.setValue({ email: 'seller@urbeat.com.br', password: '12345678' });

  component.onSubmit();

  expect(routerMock.navigate).toHaveBeenCalledWith(['/app/dashboard']);
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx jest --no-coverage src/app/features/seller-login/seller-login-page.component.spec.ts`

Expected: FAIL because current navigation points to `/configurar-loja` or spec file is missing. If spec is missing, create it with mocks for `AuthService`, `Router`, `ToastService` and the same assertion.

- [ ] **Step 3: Implement minimal login redirect**

Change only the success callback in `SellerLoginPageComponent`:

```ts
next: () => {
  this.loading.set(false);
  this.router.navigate(['/app/dashboard']);
},
```

- [ ] **Step 4: Write failing route/guard tests**

Add route-level expectations in an existing routes test or a focused test for route config:

```ts
it('declares /app before the public store slug route', () => {
  const appIndex = routes.findIndex((route) => route.path === 'app');
  const storeIndex = routes.findIndex((route) => route.path === ':storePath');

  expect(appIndex).toBeGreaterThanOrEqual(0);
  expect(storeIndex).toBeGreaterThanOrEqual(0);
  expect(appIndex).toBeLessThan(storeIndex);
});
```

Add guard expectation:

```ts
it('redirects unauthenticated sellers to /login-vendedor', () => {
  authServiceMock.isLoggedIn.mockReturnValue(false);

  const result = TestBed.runInInjectionContext(() => authGuard({} as any, {} as any));

  expect(result).toBe(false);
  expect(routerMock.navigateByUrl).toHaveBeenCalledWith('/login-vendedor');
});
```

- [ ] **Step 5: Run route/guard tests to verify failure**

Run focused Jest command for the route/guard specs.

Expected: FAIL because `/app` route does not exist and guard redirects to `/`.

- [ ] **Step 6: Implement routes and guard**

In `auth.guard.ts`:

```ts
if (auth.isLoggedIn()) return true;
router.navigateByUrl('/login-vendedor');
return false;
```

In `app.routes.ts`, add before `:storePath`:

```ts
{
  path: 'app',
  canActivate: [authGuard],
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
  ],
},
{
  path: 'configurar-loja',
  redirectTo: 'app/configuracoes/informacoes',
  pathMatch: 'full',
},
```

If old `/configurar-loja/*` children are still needed during transition, do not delete them in this task; add redirects only after replacement routes exist.

- [ ] **Step 7: Run tests to verify pass**

Run: focused Jest specs from this task.

Expected: PASS.

---

## Task 2: Sound Alert Service

**Files:**
- Create: `frontend/src/app/features/seller-shell/order-sound-alert.service.ts`
- Test: `frontend/src/app/features/seller-shell/order-sound-alert.service.spec.ts`

**Interfaces:**
- Produces: `OrderSoundAlertService.enabled`, `enable(): Promise<boolean>`, `disable(): void`, `playNewOrder(): Promise<boolean>`.
- Consumes later: `SellerShellFacade` calls `playNewOrder()` for new order events.

- [ ] **Step 1: Write failing tests**

```ts
it('should persist enabled preference when enabled', async () => {
  const service = TestBed.inject(OrderSoundAlertService);

  const enabled = await service.enable();

  expect(enabled).toBe(true);
  expect(service.enabled()).toBe(true);
  expect(localStorage.getItem('urbeat:seller-order-sound')).toBe('on');
});

it('should return false when audio playback is blocked', async () => {
  const service = TestBed.inject(OrderSoundAlertService);
  jest.spyOn(window.HTMLMediaElement.prototype, 'play').mockRejectedValueOnce(new DOMException('blocked'));

  await service.enable();
  const played = await service.playNewOrder();

  expect(played).toBe(false);
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx jest --no-coverage src/app/features/seller-shell/order-sound-alert.service.spec.ts`

Expected: FAIL because service does not exist.

- [ ] **Step 3: Implement service**

```ts
import { Injectable, signal } from '@angular/core';

const SOUND_KEY = 'urbeat:seller-order-sound';

@Injectable({ providedIn: 'root' })
export class OrderSoundAlertService {
  readonly enabled = signal(localStorage.getItem(SOUND_KEY) === 'on');
  readonly needsActivation = signal(false);

  private readonly audio = new Audio('/assets/sounds/new-order.mp3');

  async enable(): Promise<boolean> {
    this.enabled.set(true);
    localStorage.setItem(SOUND_KEY, 'on');
    const played = await this.playNewOrder({ activationTest: true });
    this.needsActivation.set(!played);
    return true;
  }

  disable(): void {
    this.enabled.set(false);
    this.needsActivation.set(false);
    localStorage.setItem(SOUND_KEY, 'off');
  }

  async playNewOrder(options?: { activationTest?: boolean }): Promise<boolean> {
    if (!this.enabled()) return false;

    try {
      this.audio.currentTime = 0;
      await this.audio.play();
      this.needsActivation.set(false);
      return true;
    } catch {
      this.needsActivation.set(true);
      return false;
    }
  }
}
```

If `/assets/sounds/new-order.mp3` does not exist, use a generated short silent-safe fallback later; for this task, the service path is stable and failure is non-blocking because playback errors are caught.

- [ ] **Step 4: Run tests to verify pass**

Run: `npx jest --no-coverage src/app/features/seller-shell/order-sound-alert.service.spec.ts`

Expected: PASS.

---

## Task 3: Seller Notification Models and API Service

**Files:**
- Create: `frontend/src/app/shared/models/seller-notification.model.ts`
- Create: `frontend/src/app/core/services/seller-notification.service.ts`
- Test: `frontend/src/app/core/services/seller-notification.service.spec.ts`

**Interfaces:**
- Produces: `SellerNotificationService.list(): Observable<SellerNotificationsResponse>` and `markAsRead(notificationId: string): Observable<void>`.
- Produces model `NotificationType.NewOrder = 1`.

- [ ] **Step 1: Write failing service test**

```ts
it('should list seller notifications', () => {
  service.list().subscribe((res) => {
    expect(res.unreadCount).toBe(1);
    expect(res.items[0].type).toBe(NotificationType.NewOrder);
  });

  const req = httpMock.expectOne('/api/seller/notifications');
  expect(req.request.method).toBe('GET');
  req.flush({ unreadCount: 1, items: [{ id: 'n1', orderId: 'o1', type: 1, title: 'Novo pedido recebido', message: 'Pedido #123', isRead: false, createdAtUtc: '2026-07-29T10:00:00Z' }] });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx jest --no-coverage src/app/core/services/seller-notification.service.spec.ts`

Expected: FAIL because service/model do not exist.

- [ ] **Step 3: Add models**

```ts
export enum NotificationType {
  NewOrder = 1,
  OrderReceived = 2,
  OrderPreparing = 3,
  OrderReady = 4,
  OrderOnDelivery = 5,
  OrderDelivered = 6,
  OrderCancelled = 7,
  SubscriptionDueSoon = 8,
  SubscriptionOverdue = 9,
  StoreBlockedBySubscription = 10,
}

export interface SellerNotification {
  id: string;
  orderId?: string | null;
  type: NotificationType;
  title: string;
  message: string;
  isRead: boolean;
  createdAtUtc: string;
}

export interface SellerNotificationsResponse {
  unreadCount: number;
  items: SellerNotification[];
}
```

- [ ] **Step 4: Add API service**

```ts
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { SellerNotificationsResponse } from '../../shared/models/seller-notification.model';

@Injectable({ providedIn: 'root' })
export class SellerNotificationService {
  private readonly api = inject(ApiService);

  list(): Observable<SellerNotificationsResponse> {
    return this.api.get<SellerNotificationsResponse>('/api/seller/notifications');
  }

  markAsRead(notificationId: string): Observable<void> {
    return this.api.patch<void>(`/api/seller/notifications/${notificationId}/read`, {});
  }
}
```

- [ ] **Step 5: Run tests to verify pass**

Run: `npx jest --no-coverage src/app/core/services/seller-notification.service.spec.ts`

Expected: PASS.

---

## Task 4: Expand OrderService for Seller Dashboard

**Files:**
- Modify: `frontend/src/app/core/services/order.service.ts`
- Modify: `frontend/src/app/shared/models/order.model.ts`
- Test: `frontend/src/app/core/services/order.service.spec.ts`

**Interfaces:**
- Produces: `getStoreReport(startDateUtc?: string, endDateUtc?: string)`, `getStoreOrders(query?: StoreOrdersQuery)`, `getStoreOrder(orderId: string)`, `updateStoreOrderStatus(orderId: string, newStatus: OrderStatus, notes?: string)`.
- Consumes existing backend endpoints.

- [ ] **Step 1: Write failing tests**

```ts
it('should request store report', () => {
  service.getStoreReport('2026-07-29T00:00:00Z', '2026-07-30T00:00:00Z').subscribe((report) => {
    expect(report.totalOrders).toBe(2);
  });

  const req = httpMock.expectOne('/api/orders/store/report?startDateUtc=2026-07-29T00%3A00%3A00Z&endDateUtc=2026-07-30T00%3A00%3A00Z');
  expect(req.request.method).toBe('GET');
  req.flush({ totalOrders: 2, totalRevenue: 100, startDateUtc: '2026-07-29T00:00:00Z', endDateUtc: '2026-07-30T00:00:00Z' });
});

it('should update seller order status', () => {
  service.updateStoreOrderStatus('order1', OrderStatus.Preparing, 'Aceito').subscribe();

  const req = httpMock.expectOne('/api/orders/order1/status');
  expect(req.request.method).toBe('PATCH');
  expect(req.request.body).toEqual({ newStatus: OrderStatus.Preparing, notes: 'Aceito' });
  req.flush({ id: 'order1', code: '123', storeId: 'store1', status: OrderStatus.Preparing, total: 20, createdAtUtc: '2026-07-29T10:00:00Z', items: [], history: [] });
});
```

- [ ] **Step 2: Run tests to verify failure**

Run: `npx jest --no-coverage src/app/core/services/order.service.spec.ts`

Expected: FAIL because methods/models do not exist.

- [ ] **Step 3: Add models**

Append to `order.model.ts`:

```ts
export interface StoreOrdersReport {
  totalOrders: number;
  totalRevenue: number;
  startDateUtc?: string | null;
  endDateUtc?: string | null;
}

export interface PagedOrderSummary {
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  items: OrderSummary[];
}

export interface StoreOrdersQuery {
  page?: number;
  pageSize?: number;
  status?: OrderStatus;
  startDateUtc?: string;
  endDateUtc?: string;
}
```

- [ ] **Step 4: Add methods**

```ts
getStoreReport(startDateUtc?: string, endDateUtc?: string): Observable<StoreOrdersReport> {
  const params = new URLSearchParams();
  if (startDateUtc) params.set('startDateUtc', startDateUtc);
  if (endDateUtc) params.set('endDateUtc', endDateUtc);
  const query = params.toString();
  return this.api.get<StoreOrdersReport>(`/api/orders/store/report${query ? `?${query}` : ''}`);
}

getStoreOrders(query: StoreOrdersQuery = {}): Observable<PagedOrderSummary> {
  const params = new URLSearchParams();
  if (query.page != null) params.set('page', String(query.page));
  if (query.pageSize != null) params.set('pageSize', String(query.pageSize));
  if (query.status != null) params.set('status', String(query.status));
  if (query.startDateUtc) params.set('startDateUtc', query.startDateUtc);
  if (query.endDateUtc) params.set('endDateUtc', query.endDateUtc);
  const qs = params.toString();
  return this.api.get<PagedOrderSummary>(`/api/orders/store${qs ? `?${qs}` : ''}`);
}

getStoreOrder(orderId: string): Observable<OrderDetails> {
  return this.api.get<OrderDetails>(`/api/orders/store/${orderId}`);
}

updateStoreOrderStatus(orderId: string, newStatus: OrderStatus, notes?: string): Observable<OrderDetails> {
  return this.api.patch<OrderDetails>(`/api/orders/${orderId}/status`, { newStatus, notes });
}
```

- [ ] **Step 5: Run tests to verify pass**

Run: `npx jest --no-coverage src/app/core/services/order.service.spec.ts`

Expected: PASS.

---

## Task 5: SellerShellFacade Handles Real-Time New Orders

**Files:**
- Create: `frontend/src/app/features/seller-shell/seller-shell.facade.ts`
- Test: `frontend/src/app/features/seller-shell/seller-shell.facade.spec.ts`

**Interfaces:**
- Consumes: `StoreService.getMyStore()`, `SellerNotificationService.list()`, `SignalRService.startSellerHub()`, `SignalRService.onSellerEvent()`, `OrderSoundAlertService.playNewOrder()`.
- Produces signals: `store`, `notifications`, `unreadCount`, `newOrderPulse`, `loading`, `error`, `soundEnabled`, `soundNeedsActivation`.

- [ ] **Step 1: Write failing facade test**

```ts
it('should start seller hub and play sound when a NewOrder notification arrives', async () => {
  let sellerCallback: ((notification: SellerNotification) => void) | undefined;
  signalRServiceMock.onSellerEvent.mockImplementation((eventName: string, cb: (notification: SellerNotification) => void) => {
    if (eventName === 'ReceiveSellerNotification') sellerCallback = cb;
  });
  storeServiceMock.getMyStore.mockReturnValue(of({ id: 'store1', name: 'Loja Teste', slug: 'loja-teste', isOpen: true, isSubscriptionBlocked: false }));
  notificationServiceMock.list.mockReturnValue(of({ unreadCount: 0, items: [] }));

  await facade.init();
  sellerCallback?.({ id: 'n1', orderId: 'o1', type: NotificationType.NewOrder, title: 'Novo pedido recebido', message: 'Pedido #123', isRead: false, createdAtUtc: '2026-07-29T10:00:00Z' });

  expect(signalRServiceMock.startSellerHub).toHaveBeenCalled();
  expect(soundServiceMock.playNewOrder).toHaveBeenCalled();
  expect(facade.unreadCount()).toBe(1);
  expect(facade.newOrderPulse()?.orderId).toBe('o1');
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx jest --no-coverage src/app/features/seller-shell/seller-shell.facade.spec.ts`

Expected: FAIL because facade does not exist.

- [ ] **Step 3: Implement facade**

```ts
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { StoreService } from '../../core/services/store.service';
import { SignalRService } from '../../core/services/signalr.service';
import { SellerNotificationService } from '../../core/services/seller-notification.service';
import { SellerNotification, NotificationType } from '../../shared/models/seller-notification.model';
import { StoreResponse } from '../../shared/models/store.model';
import { OrderSoundAlertService } from './order-sound-alert.service';

@Injectable({ providedIn: 'root' })
export class SellerShellFacade {
  private readonly storeService = inject(StoreService);
  private readonly signalR = inject(SignalRService);
  private readonly notificationsApi = inject(SellerNotificationService);
  private readonly sound = inject(OrderSoundAlertService);

  readonly store = signal<StoreResponse | null>(null);
  readonly notifications = signal<SellerNotification[]>([]);
  readonly unreadCount = signal(0);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly newOrderPulse = signal<SellerNotification | null>(null);
  readonly soundEnabled = this.sound.enabled;
  readonly soundNeedsActivation = this.sound.needsActivation;
  readonly storeName = computed(() => this.store()?.name ?? 'Minha loja');

  private initialized = false;

  async init(): Promise<void> {
    if (this.initialized) return;
    this.initialized = true;
    this.loading.set(true);
    this.error.set(null);

    try {
      const [store, notifications] = await Promise.all([
        firstValueFrom(this.storeService.getMyStore()),
        firstValueFrom(this.notificationsApi.list()),
      ]);
      this.store.set(store);
      this.notifications.set(notifications.items);
      this.unreadCount.set(notifications.unreadCount);
      await this.signalR.startSellerHub();
      this.signalR.onSellerEvent('ReceiveSellerNotification', (notification: SellerNotification) => {
        this.handleSellerNotification(notification);
      });
    } catch {
      this.error.set('Nao foi possivel carregar o painel do lojista.');
    } finally {
      this.loading.set(false);
    }
  }

  async enableSound(): Promise<void> {
    await this.sound.enable();
  }

  disableSound(): void {
    this.sound.disable();
  }

  private handleSellerNotification(notification: SellerNotification): void {
    this.notifications.update((items) => [notification, ...items.filter((item) => item.id !== notification.id)]);
    if (!notification.isRead) this.unreadCount.update((count) => count + 1);

    if (notification.type === NotificationType.NewOrder) {
      this.newOrderPulse.set(notification);
      void this.sound.playNewOrder();
    }
  }
}
```

- [ ] **Step 4: Run tests to verify pass**

Run: `npx jest --no-coverage src/app/features/seller-shell/seller-shell.facade.spec.ts`

Expected: PASS.

---

## Task 6: Seller Shell UI

**Files:**
- Create: `frontend/src/app/features/seller-shell/seller-app-shell.component.ts`
- Create: `frontend/src/app/features/seller-shell/seller-app-shell.component.html`
- Create: `frontend/src/app/features/seller-shell/seller-app-shell.component.scss`
- Test: `frontend/src/app/features/seller-shell/seller-app-shell.component.spec.ts`

**Interfaces:**
- Consumes: `SellerShellFacade.init()`, `storeName()`, `unreadCount()`, `soundEnabled()`, `soundNeedsActivation()`.
- Produces: shell containing `<ion-router-outlet id="seller-main-content">`.

- [ ] **Step 1: Write failing component tests**

```ts
it('should initialize the seller shell facade', () => {
  const fixture = TestBed.createComponent(SellerAppShellComponent);
  fixture.detectChanges();

  expect(facadeMock.init).toHaveBeenCalled();
});

it('should show activate sound action when audio needs activation', () => {
  facadeMock.soundNeedsActivation.mockReturnValue(true);

  const fixture = TestBed.createComponent(SellerAppShellComponent);
  fixture.detectChanges();

  expect(fixture.nativeElement.textContent).toContain('Ativar som de pedidos');
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx jest --no-coverage src/app/features/seller-shell/seller-app-shell.component.spec.ts`

Expected: FAIL because component does not exist.

- [ ] **Step 3: Implement component TS**

```ts
import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { IonBadge, IonButton, IonButtons, IonContent, IonHeader, IonIcon, IonItem, IonLabel, IonList, IonMenu, IonMenuButton, IonRouterOutlet, IonSplitPane, IonTitle, IonToolbar } from '@ionic/angular/standalone';
import { AuthService } from '../../core/services/auth.service';
import { SellerShellFacade } from './seller-shell.facade';

@Component({
  selector: 'app-seller-app-shell',
  standalone: true,
  imports: [CommonModule, RouterModule, IonSplitPane, IonMenu, IonContent, IonList, IonItem, IonLabel, IonIcon, IonBadge, IonRouterOutlet, IonHeader, IonToolbar, IonButtons, IonMenuButton, IonTitle, IonButton],
  templateUrl: './seller-app-shell.component.html',
  styleUrl: './seller-app-shell.component.scss',
})
export class SellerAppShellComponent implements OnInit {
  readonly facade = inject(SellerShellFacade);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly menuItems = [
    { label: 'Dashboard', route: '/app/dashboard', icon: 'grid-outline' },
    { label: 'Pedidos', route: '/app/pedidos', icon: 'receipt-outline', badge: true },
    { label: 'Produtos', route: '/app/cardapio/produtos', icon: 'storefront-outline' },
    { label: 'Horarios', route: '/app/configuracoes/horarios', icon: 'time-outline' },
    { label: 'Bairros', route: '/app/configuracoes/bairros', icon: 'map-outline' },
  ];

  ngOnInit(): void {
    void this.facade.init();
  }

  async enableSound(): Promise<void> {
    await this.facade.enableSound();
  }

  toggleSound(): void {
    if (this.facade.soundEnabled()) this.facade.disableSound();
    else void this.facade.enableSound();
  }

  logout(): void {
    this.auth.logout();
    this.router.navigate(['/login-vendedor']);
  }
}
```

- [ ] **Step 4: Implement template**

```html
<ion-split-pane contentId="seller-main-content" when="(min-width: 1200px)" class="seller-shell">
  <ion-menu contentId="seller-main-content" type="overlay" class="seller-menu">
    <ion-content>
      <aside class="sidebar" aria-label="Menu do lojista">
        <div class="brand-block">
          <strong>{{ facade.storeName() }}</strong>
          <span>Painel da loja</span>
        </div>

        <button type="button" class="store-status" [class.open]="facade.store()?.isOpen" routerLink="/app/configuracoes/horarios">
          <span class="status-dot" aria-hidden="true"></span>
          <span>{{ facade.store()?.isOpen ? 'Loja aberta' : 'Loja fechada' }}</span>
        </button>

        <ion-list class="menu-list">
          @for (item of menuItems; track item.route) {
            <ion-item [routerLink]="item.route" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: item.route === '/app/dashboard' }" button lines="none">
              <ion-icon [name]="item.icon" aria-hidden="true"></ion-icon>
              <ion-label>{{ item.label }}</ion-label>
              @if (item.badge && facade.unreadCount() > 0) {
                <ion-badge>{{ facade.unreadCount() }}</ion-badge>
              }
            </ion-item>
          }
        </ion-list>

        <div class="sidebar-actions">
          <button type="button" class="sound-btn" (click)="toggleSound()">
            {{ facade.soundEnabled() ? 'Som ligado' : 'Som desligado' }}
          </button>
          <button type="button" class="logout-btn" (click)="logout()">Sair</button>
        </div>
      </aside>
    </ion-content>
  </ion-menu>

  <main id="seller-main-content" class="seller-main">
    <ion-header class="seller-topbar">
      <ion-toolbar>
        <ion-buttons slot="start"><ion-menu-button></ion-menu-button></ion-buttons>
        <ion-title>{{ facade.storeName() }}</ion-title>
        <ion-buttons slot="end">
          <ion-button type="button" (click)="toggleSound()">{{ facade.soundEnabled() ? 'Som' : 'Sem som' }}</ion-button>
        </ion-buttons>
      </ion-toolbar>
    </ion-header>

    @if (facade.soundNeedsActivation()) {
      <button type="button" class="activation-banner" (click)="enableSound()">Ativar som de pedidos</button>
    }

    <div class="new-order-live" aria-live="polite">
      @if (facade.newOrderPulse(); as orderSignal) {
        Novo pedido recebido: {{ orderSignal.message }}
      }
    </div>

    <ion-router-outlet id="seller-main-content"></ion-router-outlet>
  </main>
</ion-split-pane>
```

- [ ] **Step 5: Implement SCSS**

Use Urbeat tokens only:

```scss
:host { display: block; min-height: 100vh; background: var(--app-bg, #ede9e3); }
.seller-shell { --side-width: 280px; min-height: 100vh; }
.seller-menu { --width: 280px; }
.sidebar { min-height: 100%; padding: 22px; background: var(--app-ink, #161616); color: var(--app-surface, #fff); display: flex; flex-direction: column; gap: 18px; }
.brand-block { display: grid; gap: 4px; }
.brand-block strong { font-size: 20px; font-weight: 800; }
.brand-block span { color: rgba(255,255,255,.68); font-size: 13px; }
.store-status, .sound-btn, .logout-btn, .activation-banner { min-height: 44px; border: 0; border-radius: 999px; font: inherit; cursor: pointer; }
.store-status { display: flex; align-items: center; gap: 10px; padding: 0 14px; background: rgba(255,255,255,.08); color: inherit; }
.status-dot { width: 10px; height: 10px; border-radius: 50%; background: var(--app-muted-warm, #b5a89e); }
.store-status.open .status-dot { background: var(--app-success-green, #119441); }
.menu-list { background: transparent; }
ion-item { --background: transparent; --color: rgba(255,255,255,.78); --border-radius: 16px; margin-bottom: 6px; }
ion-item.active { --background: var(--app-brand, #D54A51); --color: var(--app-surface, #fff); }
.sidebar-actions { margin-top: auto; display: grid; gap: 10px; }
.sound-btn { background: rgba(255,255,255,.1); color: #fff; }
.logout-btn { background: transparent; color: rgba(255,255,255,.76); }
.seller-main { min-height: 100vh; background: var(--app-bg, #ede9e3); }
.seller-topbar { box-shadow: none; }
.activation-banner { margin: 12px 18px 0; padding: 0 18px; background: var(--app-brand-soft, #FDECEE); color: var(--app-brand, #D54A51); font-weight: 800; }
.new-order-live { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); }
```

- [ ] **Step 6: Run component tests**

Run: `npx jest --no-coverage src/app/features/seller-shell/seller-app-shell.component.spec.ts`

Expected: PASS.

---

## Task 7: Shared Page Header and Metric Card

**Files:**
- Create: `frontend/src/app/shared/components/page-header/page-header.component.ts/html/scss`
- Create: `frontend/src/app/shared/components/metric-card/metric-card.component.ts/html/scss`
- Test: `frontend/src/app/shared/components/page-header/page-header.component.spec.ts`
- Test: `frontend/src/app/shared/components/metric-card/metric-card.component.spec.ts`

**Interfaces:**
- Produces: `<app-page-header [title] [description] />`.
- Produces: `<app-metric-card [label] [value] [supportingText] />`.

- [ ] **Step 1: Write failing tests**

```ts
it('renders page title and description', () => {
  const fixture = TestBed.createComponent(PageHeaderComponent);
  fixture.componentRef.setInput('title', 'Visao geral da loja hoje');
  fixture.componentRef.setInput('description', 'Acompanhe pedidos e faturamento em tempo real.');
  fixture.detectChanges();

  expect(fixture.nativeElement.textContent).toContain('Visao geral da loja hoje');
  expect(fixture.nativeElement.textContent).toContain('Acompanhe pedidos e faturamento em tempo real.');
});

it('renders metric label and value', () => {
  const fixture = TestBed.createComponent(MetricCardComponent);
  fixture.componentRef.setInput('label', 'Pedidos hoje');
  fixture.componentRef.setInput('value', '9');
  fixture.detectChanges();

  expect(fixture.nativeElement.textContent).toContain('Pedidos hoje');
  expect(fixture.nativeElement.textContent).toContain('9');
});
```

- [ ] **Step 2: Run tests to verify failure**

Run both new component specs.

Expected: FAIL because components do not exist.

- [ ] **Step 3: Implement PageHeaderComponent**

```ts
import { Component, input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({ selector: 'app-page-header', standalone: true, imports: [CommonModule], templateUrl: './page-header.component.html', styleUrl: './page-header.component.scss' })
export class PageHeaderComponent {
  readonly title = input.required<string>();
  readonly description = input<string>('');
}
```

```html
<header class="page-header">
  <h1>{{ title() }}</h1>
  @if (description()) { <p>{{ description() }}</p> }
</header>
```

- [ ] **Step 4: Implement MetricCardComponent**

```ts
import { Component, input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({ selector: 'app-metric-card', standalone: true, imports: [CommonModule], templateUrl: './metric-card.component.html', styleUrl: './metric-card.component.scss' })
export class MetricCardComponent {
  readonly label = input.required<string>();
  readonly value = input.required<string>();
  readonly supportingText = input<string>('');
}
```

```html
<article class="metric-card" tabindex="0">
  <span>{{ label() }}</span>
  <strong>{{ value() }}</strong>
  @if (supportingText()) { <small>{{ supportingText() }}</small> }
</article>
```

- [ ] **Step 5: Add SCSS for both components**

Page header:

```scss
.page-header { display: grid; gap: 6px; margin-bottom: 18px; }
h1 { margin: 0; color: var(--app-ink, #161616); font-size: clamp(24px, 4vw, 34px); line-height: 1.08; letter-spacing: -0.04em; font-weight: 800; }
p { margin: 0; color: var(--app-text-secondary, #6f6f76); font-size: 15px; }
```

Metric card:

```scss
.metric-card { min-height: 118px; padding: 18px; border-radius: var(--app-radius-lg, 18px); background: var(--app-surface, #fff); border: 1px solid var(--app-border-light, #eadfd6); display: grid; gap: 8px; }
span { color: var(--app-text-secondary, #6f6f76); font-size: 13px; font-weight: 700; }
strong { color: var(--app-ink, #161616); font-size: 30px; line-height: 1; font-weight: 800; letter-spacing: -0.04em; }
small { color: var(--app-slate-warm, #565049); font-size: 12px; }
.metric-card:focus-visible { outline: none; box-shadow: 0 0 0 3px var(--app-brand-shadow, rgba(213,74,81,.2)); }
```

- [ ] **Step 6: Run tests to verify pass**

Run component specs.

Expected: PASS.

---

## Task 8: Seller Dashboard Page

**Files:**
- Create: `frontend/src/app/features/seller-dashboard/seller-dashboard-page.component.ts`
- Create: `frontend/src/app/features/seller-dashboard/seller-dashboard-page.component.html`
- Create: `frontend/src/app/features/seller-dashboard/seller-dashboard-page.component.scss`
- Test: `frontend/src/app/features/seller-dashboard/seller-dashboard-page.component.spec.ts`

**Interfaces:**
- Consumes: `OrderService.getStoreReport()`, `OrderService.getStoreOrders({ pageSize: 5 })`, `SellerShellFacade.newOrderPulse()`.
- Produces: first dashboard with metrics and “aguardando pedidos” state.

- [ ] **Step 1: Write failing tests**

```ts
it('loads dashboard metrics from seller order report', () => {
  orderServiceMock.getStoreReport.mockReturnValue(of({ totalOrders: 2, totalRevenue: 100 }));
  orderServiceMock.getStoreOrders.mockReturnValue(of({ page: 1, pageSize: 5, totalItems: 0, totalPages: 0, items: [] }));

  const fixture = TestBed.createComponent(SellerDashboardPageComponent);
  fixture.detectChanges();

  expect(fixture.nativeElement.textContent).toContain('2');
  expect(fixture.nativeElement.textContent).toContain('R$');
});

it('shows the operational waiting message', () => {
  orderServiceMock.getStoreReport.mockReturnValue(of({ totalOrders: 0, totalRevenue: 0 }));
  orderServiceMock.getStoreOrders.mockReturnValue(of({ page: 1, pageSize: 5, totalItems: 0, totalPages: 0, items: [] }));

  const fixture = TestBed.createComponent(SellerDashboardPageComponent);
  fixture.detectChanges();

  expect(fixture.nativeElement.textContent).toContain('Aguardando novos pedidos');
});
```

- [ ] **Step 2: Run tests to verify failure**

Run: `npx jest --no-coverage src/app/features/seller-dashboard/seller-dashboard-page.component.spec.ts`

Expected: FAIL because page does not exist.

- [ ] **Step 3: Implement component TS**

```ts
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { OrderService } from '../../core/services/order.service';
import { OrderSummary, StoreOrdersReport } from '../../shared/models/order.model';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { MetricCardComponent } from '../../shared/components/metric-card/metric-card.component';
import { SellerShellFacade } from '../seller-shell/seller-shell.facade';

@Component({
  selector: 'app-seller-dashboard-page',
  standalone: true,
  imports: [CommonModule, RouterModule, PageHeaderComponent, MetricCardComponent],
  templateUrl: './seller-dashboard-page.component.html',
  styleUrl: './seller-dashboard-page.component.scss',
})
export class SellerDashboardPageComponent implements OnInit {
  private readonly orders = inject(OrderService);
  readonly shell = inject(SellerShellFacade);

  readonly loading = signal(true);
  readonly error = signal(false);
  readonly report = signal<StoreOrdersReport | null>(null);
  readonly recentOrders = signal<OrderSummary[]>([]);

  readonly averageTicket = computed(() => {
    const report = this.report();
    if (!report || report.totalOrders <= 0) return 0;
    return report.totalRevenue / report.totalOrders;
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.orders.getStoreReport().subscribe({
      next: (report) => {
        this.report.set(report);
        this.orders.getStoreOrders({ pageSize: 5 }).subscribe({
          next: (orders) => {
            this.recentOrders.set(orders.items);
            this.loading.set(false);
          },
          error: () => {
            this.error.set(true);
            this.loading.set(false);
          },
        });
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }

  formatCurrency(value: number): string {
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value);
  }
}
```

- [ ] **Step 4: Implement template**

```html
<section class="dashboard-page">
  <app-page-header title="Visao geral da loja hoje" description="Acompanhe pedidos, faturamento e alertas em tempo real." />

  @if (loading()) {
    <div class="skeleton-grid" role="status" aria-label="Carregando dashboard">
      <div></div><div></div><div></div><div></div>
    </div>
  } @else if (error()) {
    <div class="error-card" role="alert">
      <strong>Nao foi possivel carregar o dashboard</strong>
      <button type="button" (click)="load()">Tentar novamente</button>
    </div>
  } @else {
    <div class="metrics-grid">
      <app-metric-card label="Pedidos hoje" [value]="String(report()?.totalOrders ?? 0)" supportingText="Atualizado em tempo real" />
      <app-metric-card label="Faturamento" [value]="formatCurrency(report()?.totalRevenue ?? 0)" supportingText="Pedidos confirmados" />
      <app-metric-card label="Ticket medio" [value]="formatCurrency(averageTicket())" supportingText="Receita / pedidos" />
      <app-metric-card label="Pendentes" [value]="String(shell.unreadCount())" supportingText="Novos avisos" />
    </div>

    <article class="ops-card" [class.has-new]="shell.newOrderPulse()">
      <span class="ops-kicker">Sala operacional</span>
      <h2>{{ shell.newOrderPulse() ? 'Novo pedido recebido' : 'Aguardando novos pedidos' }}</h2>
      <p>{{ shell.newOrderPulse()?.message || 'Deixe esta tela aberta para receber sinal visual e sonoro quando uma venda chegar.' }}</p>
      <a routerLink="/app/pedidos">Ver pedidos</a>
    </article>

    <section class="recent-orders">
      <header><h2>Ultimos pedidos</h2><a routerLink="/app/pedidos">Ver todos</a></header>
      @for (order of recentOrders(); track order.id) {
        <article class="recent-order">
          <strong>#{{ order.code }}</strong>
          <span>{{ formatCurrency(order.total) }}</span>
        </article>
      } @empty {
        <p class="empty">Nenhum pedido encontrado para esta loja.</p>
      }
    </section>
  }
</section>
```

- [ ] **Step 5: Implement SCSS**

```scss
.dashboard-page { padding: 22px; max-width: 1180px; margin: 0 auto; }
.metrics-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 14px; }
.ops-card, .recent-orders, .error-card { margin-top: 18px; padding: 18px; border-radius: var(--app-radius-lg, 18px); background: var(--app-surface, #fff); border: 1px solid var(--app-border-light, #eadfd6); }
.ops-card.has-new { background: var(--app-brand-soft, #FDECEE); border-color: var(--app-brand, #D54A51); }
.ops-kicker { color: var(--app-brand, #D54A51); font-size: 12px; font-weight: 800; }
h2 { margin: 6px 0; color: var(--app-ink, #161616); font-size: 22px; font-weight: 800; letter-spacing: -0.035em; }
p { color: var(--app-text-secondary, #6f6f76); }
a, button { min-height: 44px; display: inline-flex; align-items: center; border-radius: 999px; color: var(--app-brand, #D54A51); font-weight: 800; }
.recent-orders header { display: flex; justify-content: space-between; align-items: center; gap: 12px; }
.recent-order { display: flex; justify-content: space-between; padding: 12px 0; border-top: 1px solid var(--app-border-light, #eadfd6); }
.skeleton-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 14px; }
.skeleton-grid div { height: 118px; border-radius: 18px; background: var(--app-hairline-warm, #f3efe9); }
@media (max-width: 900px) { .metrics-grid, .skeleton-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
@media (max-width: 520px) { .dashboard-page { padding: 16px; } .metrics-grid, .skeleton-grid { grid-template-columns: 1fr; } }
```

- [ ] **Step 6: Run tests to verify pass**

Run: `npx jest --no-coverage src/app/features/seller-dashboard/seller-dashboard-page.component.spec.ts`

Expected: PASS.

---

## Task 9: Reuse Existing Store Config Routes Under /app

**Files:**
- Modify: `frontend/src/app/app.routes.ts`
- Test: route config spec from Task 1

**Interfaces:**
- Produces route aliases for products, hours, neighborhoods.

- [ ] **Step 1: Write failing route tests**

```ts
it('declares initial seller dashboard child routes for reused store config screens', () => {
  const appRoute = routes.find((route) => route.path === 'app');
  const childPaths = appRoute?.children?.map((child) => child.path) ?? [];

  expect(childPaths).toContain('cardapio/produtos');
  expect(childPaths).toContain('configuracoes/horarios');
  expect(childPaths).toContain('configuracoes/bairros');
});
```

- [ ] **Step 2: Run test to verify failure**

Expected: FAIL if child routes are not present.

- [ ] **Step 3: Add child routes**

Inside `/app` children:

```ts
{
  path: 'cardapio/produtos',
  loadComponent: () =>
    import('./features/store-config/products/store-products-page.component').then(
      (m) => m.StoreProductsPageComponent,
    ),
},
{
  path: 'configuracoes/horarios',
  loadComponent: () =>
    import('./features/store-config/hours/store-hours-page.component').then(
      (m) => m.StoreHoursPageComponent,
    ),
},
{
  path: 'configuracoes/bairros',
  loadComponent: () =>
    import('./features/store-config/delivery/store-delivery-page.component').then(
      (m) => m.StoreDeliveryPageComponent,
    ),
},
{
  path: 'pedidos',
  loadComponent: () =>
    import('./features/seller-dashboard/seller-dashboard-page.component').then(
      (m) => m.SellerDashboardPageComponent,
    ),
},
```

For `/app/pedidos`, this temporary route can point to dashboard until RF-DASH-03 creates the real page. Add a visible note in the dashboard link if needed.

- [ ] **Step 4: Run route tests to verify pass**

Expected: PASS.

---

## Task 10: Verification and Documentation Update

**Files:**
- Modify: `Documentacao/DashBoard/02-Backlog-Dashboard-Lojista-Urbeat.md` only if implementation discoveries change RF-DASH-01.

**Interfaces:**
- Produces: validated RF-DASH-01 base.

- [ ] **Step 1: Run focused frontend tests**

Run:

```powershell
npx jest --no-coverage src/app/features/seller-shell src/app/features/seller-dashboard src/app/core/services/order.service.spec.ts src/app/core/services/seller-notification.service.spec.ts src/app/features/seller-login/seller-login-page.component.spec.ts
```

Expected: PASS.

- [ ] **Step 2: Run Angular build**

Run from `frontend/`:

```powershell
npx ng build --configuration production
```

Expected: PASS.

- [ ] **Step 3: Run Impeccable detector on changed UI**

Run from repo root:

```powershell
node ".opencode/skills/impeccable/scripts/detect.mjs" --json "frontend/src/app/features/seller-shell/seller-app-shell.component.html" "frontend/src/app/features/seller-shell/seller-app-shell.component.scss" "frontend/src/app/features/seller-dashboard/seller-dashboard-page.component.html" "frontend/src/app/features/seller-dashboard/seller-dashboard-page.component.scss"
```

Expected: no blocking findings. Fix real findings; document intentional false positives.

- [ ] **Step 4: Manual smoke path**

Run `npx ng serve` if needed, then verify:

1. `/login-vendedor` logs in and redirects to `/app/dashboard`.
2. `/app/dashboard` shows shell and dashboard.
3. Sound toggle changes label between `Som ligado` and `Som desligado`.
4. `Ativar som de pedidos` appears if browser blocks audio.
5. Direct `/app/dashboard` refresh does not route to public store page.

Expected: all pass.

---

## Self-Review

Spec coverage:

- `/app/dashboard`: covered by Tasks 1, 6, 8.
- Login redirect: covered by Task 1.
- Seller shell: covered by Task 6.
- SignalR new order event: covered by Task 5.
- Sound alert: covered by Task 2 and integrated in Tasks 5/6/8.
- Generic any-store requirement: covered in global constraints and shell uses `getMyStore()`.
- Existing screens reuse: covered by Task 9.

Known intentional exclusions from RF-DASH-01:

- Full `/app/pedidos` kanban is RF-DASH-03.
- Rich backend dashboard aggregate is RF-DASH-02 follow-up if simple report is insufficient.
- Clientes, entregas, avaliacoes, mensalidade and PWA install are later RFs.

Placeholder scan:

- No implementation step may leave unspecified behavior. If a file path differs due to existing specs, keep the same assertions and place them in the existing nearest spec.

Type consistency:

- `NotificationType.NewOrder = 1` matches backend `NotificationType.NewOrder`.
- SignalR event name is `ReceiveSellerNotification` as used by `NotificationService.SignalR.cs`.
- Seller hub path is `/hubs/seller-notifications` via existing `SignalRService`.
