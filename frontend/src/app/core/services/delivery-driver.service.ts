import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import {
  AcceptDeliveryDriverInviteRequest,
  AcceptDeliveryDriverInviteResponse,
  AssignDeliveryDriverRequest,
  DeliveryDriverActionResult,
  DeliveryDriverInvitePreview,
  DeliveryDriverStore,
  DriverOrder,
  DriverOrdersResponse,
  InviteDeliveryDriverRequest,
  SellerDeliveryDriver,
  UpsertStoreDeliveryDriverRequest,
} from '../../shared/models/delivery-driver.model';
import { OrderDetails } from '../../shared/models/order.model';

@Injectable({ providedIn: 'root' })
export class DeliveryDriverService {
  private readonly api = inject(ApiService);

  listStoreDrivers(): Observable<SellerDeliveryDriver[]> {
    return this.api.get<SellerDeliveryDriver[]>('/api/delivery-drivers/store');
  }

  createDriver(request: UpsertStoreDeliveryDriverRequest): Observable<SellerDeliveryDriver> {
    return this.api.post<SellerDeliveryDriver>('/api/delivery-drivers', request);
  }

  updateDriver(
    storeDriverId: string,
    request: UpsertStoreDeliveryDriverRequest,
  ): Observable<SellerDeliveryDriver> {
    return this.api.put<SellerDeliveryDriver>(`/api/delivery-drivers/${storeDriverId}`, request);
  }

  setDriverActive(storeDriverId: string, isActive: boolean): Observable<SellerDeliveryDriver> {
    return this.api.patch<SellerDeliveryDriver>(
      `/api/delivery-drivers/${storeDriverId}/status`,
      { isActive },
    );
  }

  deleteDriver(storeDriverId: string): Observable<void> {
    return this.api.delete<void>(`/api/delivery-drivers/${storeDriverId}`);
  }

  /** Legacy invite flow, kept for compatibility with links already sent. */
  invite(request: InviteDeliveryDriverRequest): Observable<SellerDeliveryDriver> {
    return this.api.post<SellerDeliveryDriver>('/api/delivery-drivers/store/invite', request);
  }

  resendInvite(storeDriverId: string): Observable<SellerDeliveryDriver> {
    return this.api.post<SellerDeliveryDriver>(
      `/api/delivery-drivers/store/${storeDriverId}/resend-invite`,
      {},
    );
  }

  revoke(storeDriverId: string): Observable<SellerDeliveryDriver> {
    return this.api.post<SellerDeliveryDriver>(
      `/api/delivery-drivers/store/${storeDriverId}/revoke`,
      {},
    );
  }

  previewInvite(token: string): Observable<DeliveryDriverInvitePreview> {
    return this.api.get<DeliveryDriverInvitePreview>(
      `/api/delivery-drivers/invites/${encodeURIComponent(token)}`,
    );
  }

  acceptInvite(
    token: string,
    request: AcceptDeliveryDriverInviteRequest,
  ): Observable<AcceptDeliveryDriverInviteResponse> {
    return this.api.post<AcceptDeliveryDriverInviteResponse>(
      `/api/delivery-drivers/invites/${encodeURIComponent(token)}/accept`,
      request,
    );
  }

  assignOrder(orderId: string, storeDriverId: string): Observable<OrderDetails> {
    const body: AssignDeliveryDriverRequest = { deliveryDriverId: storeDriverId };
    return this.api.put<OrderDetails>(`/api/orders/store/${orderId}/delivery-driver`, body);
  }

  removeAssignment(orderId: string): Observable<OrderDetails> {
    return this.api.delete<OrderDetails>(`/api/orders/store/${orderId}/delivery-driver`);
  }

  getDriverStores(): Observable<DeliveryDriverStore[]> {
    return this.api.get<DeliveryDriverStore[]>('/api/driver/stores');
  }

  getDriverOrders(storeId: string): Observable<DriverOrdersResponse> {
    return this.api.get<DriverOrdersResponse>(`/api/driver/orders?storeId=${encodeURIComponent(storeId)}`);
  }

  getDriverOrder(orderId: string): Observable<DriverOrder> {
    return this.api.get<DriverOrder>(`/api/driver/orders/${orderId}`);
  }

  startDelivery(orderId: string): Observable<DeliveryDriverActionResult> {
    return this.api.post<DeliveryDriverActionResult>(`/api/driver/orders/${orderId}/start`, {});
  }

  confirmPin(orderId: string, pin: string): Observable<DeliveryDriverActionResult> {
    return this.api.post<DeliveryDriverActionResult>(`/api/driver/orders/${orderId}/confirm-pin`, { pin });
  }
}
