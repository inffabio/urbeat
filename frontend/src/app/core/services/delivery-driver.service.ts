import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import {
  AssignDeliveryDriverRequest,
  SellerDeliveryDriver,
  UpsertStoreDeliveryDriverRequest,
} from '../../shared/models/delivery-driver.model';
import { OrderDetails } from '../../shared/models/order.model';

/**
 * Seller-side operational motoboy management. The motoboy has no login, password or token:
 * delivery execution happens exclusively through the public bearer links handled by
 * `DeliveryRouteService`.
 */
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

  assignOrder(orderId: string, storeDriverId: string): Observable<OrderDetails> {
    const body: AssignDeliveryDriverRequest = { deliveryDriverId: storeDriverId };
    return this.api.put<OrderDetails>(`/api/orders/store/${orderId}/delivery-driver`, body);
  }

  removeAssignment(orderId: string): Observable<OrderDetails> {
    return this.api.delete<OrderDetails>(`/api/orders/store/${orderId}/delivery-driver`);
  }
}
