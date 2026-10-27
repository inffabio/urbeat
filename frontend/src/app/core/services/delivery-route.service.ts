import { HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import {
  SKIP_GLOBAL_ERROR_TOAST,
  SKIP_SERVER_ACCESS_OVERLAY,
} from '../interceptors/server-access.context';
import {
  CreateDeliveryRouteRequest,
  DeliveryRoute,
  DeliveryRouteOperationResult,
  PublicDeliveryRoute,
  PublicDeliveryRoutePinResult,
  ShareIndividualDeliveryResult,
  UpdateDeliveryRouteRequest,
} from '../../shared/models/delivery-route.model';

@Injectable({ providedIn: 'root' })
export class DeliveryRouteService {
  private readonly api = inject(ApiService);

  listStoreRoutes(): Observable<DeliveryRoute[]> {
    return this.api.get<DeliveryRoute[]>('/api/delivery-routes/store');
  }

  createRoute(request: CreateDeliveryRouteRequest): Observable<DeliveryRouteOperationResult> {
    return this.api.post<DeliveryRouteOperationResult>('/api/delivery-routes/store', request);
  }

  updateRoute(
    routeId: string,
    request: UpdateDeliveryRouteRequest,
  ): Observable<DeliveryRouteOperationResult> {
    return this.api.put<DeliveryRouteOperationResult>(
      `/api/delivery-routes/store/${routeId}`,
      request,
    );
  }

  shareRoute(routeId: string): Observable<DeliveryRouteOperationResult> {
    return this.api.post<DeliveryRouteOperationResult>(
      `/api/delivery-routes/store/${routeId}/share`,
      {},
    );
  }

  revokeRoute(routeId: string): Observable<DeliveryRouteOperationResult> {
    return this.api.post<DeliveryRouteOperationResult>(
      `/api/delivery-routes/store/${routeId}/revoke`,
      {},
    );
  }

  shareIndividualDelivery(orderId: string): Observable<ShareIndividualDeliveryResult> {
    return this.api.post<ShareIndividualDeliveryResult>(
      `/api/orders/store/${orderId}/delivery-link`,
      {},
    );
  }

  getPublicRoute(token: string): Observable<PublicDeliveryRoute> {
    return this.api.get<PublicDeliveryRoute>(
      `/api/public/delivery-routes/${encodeURIComponent(token)}`,
      undefined,
      this.publicContext(),
    );
  }

  getPublicIndividualDelivery(token: string): Observable<PublicDeliveryRoute> {
    return this.api.get<PublicDeliveryRoute>(
      `/api/public/deliveries/${encodeURIComponent(token)}`,
      undefined,
      this.publicContext(),
    );
  }

  confirmPublicDelivery(
    token: string,
    sequence: number,
    pin: string,
  ): Observable<PublicDeliveryRoutePinResult> {
    return this.api.post<PublicDeliveryRoutePinResult>(
      `/api/public/delivery-routes/${encodeURIComponent(token)}/stops/${sequence}/deliver`,
      { pin },
      this.publicContext(),
    );
  }

  confirmPublicIndividualDelivery(
    token: string,
    pin: string,
  ): Observable<PublicDeliveryRoutePinResult> {
    return this.api.post<PublicDeliveryRoutePinResult>(
      `/api/public/deliveries/${encodeURIComponent(token)}/deliver`,
      { pin },
      this.publicContext(),
    );
  }

  private publicContext(): HttpContext {
    return new HttpContext()
      .set(SKIP_GLOBAL_ERROR_TOAST, true)
      .set(SKIP_SERVER_ACCESS_OVERLAY, true);
  }
}
