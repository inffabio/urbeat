import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import {
  OrderDetails,
  OrderSummary,
  PagedOrderSummary,
  PagedSellerCustomerSummary,
  SellerCustomerSummary,
  SellerDeliverySummary,
  StoreCustomersQuery,
  StoreOrdersQuery,
  StoreOrdersReport,
} from '../../shared/models/order.model';

@Injectable({ providedIn: 'root' })
export class OrderService {
  private readonly api = inject(ApiService);

  getOrder(orderId: string): Observable<OrderDetails> {
    return this.api.get<OrderDetails>(`/api/orders/${orderId}`);
  }

  getMyOrders(): Observable<OrderSummary[]> {
    return this.api.get<OrderSummary[]>('/api/orders/my');
  }

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

  getStoreCustomers(query: StoreCustomersQuery = {}): Observable<PagedSellerCustomerSummary> {
    const params = new URLSearchParams();
    if (query.page != null) params.set('page', String(query.page));
    if (query.pageSize != null) params.set('pageSize', String(query.pageSize));
    if (query.search) params.set('search', query.search);
    if (query.status) params.set('status', query.status);
    if (query.sort) params.set('sort', query.sort);
    const qs = params.toString();

    return this.api.get<PagedSellerCustomerSummary>(`/api/orders/store/customers${qs ? `?${qs}` : ''}`);
  }

  updateStoreCustomer(customerUserId: string, body: {
    name: string;
    email: string;
    phone: string;
    cep?: string;
    street?: string;
    number?: string;
    complement?: string;
    neighborhood?: string;
    city?: string;
    state?: string;
  }): Observable<SellerCustomerSummary> {
    return this.api.put<SellerCustomerSummary>(`/api/orders/store/customers/${customerUserId}`, body);
  }

  toggleStoreCustomer(customerUserId: string, isActive: boolean): Observable<SellerCustomerSummary> {
    return this.api.patch<SellerCustomerSummary>(`/api/orders/store/customers/${customerUserId}/status`, { isActive });
  }

  getStoreDeliveries(): Observable<SellerDeliverySummary[]> {
    return this.api.get<SellerDeliverySummary[]>('/api/orders/store/deliveries');
  }

  getStoreOrder(orderId: string): Observable<OrderDetails> {
    return this.api.get<OrderDetails>(`/api/orders/store/${orderId}`);
  }

  updateStoreOrderStatus(
    orderId: string,
    newStatus: OrderStatus,
    notes?: string,
  ): Observable<OrderDetails> {
    return this.api.patch<OrderDetails>(`/api/orders/${orderId}/status`, { newStatus, notes });
  }
}
