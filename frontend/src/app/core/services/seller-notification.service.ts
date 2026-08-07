import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { SellerNotificationsResponse } from '../../shared/models/seller-notification.model';
import { ApiService } from './api.service';

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
