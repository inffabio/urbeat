import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { NotificationType } from '../../shared/models/seller-notification.model';
import { ApiService } from './api.service';
import { SellerNotificationService } from './seller-notification.service';

describe('SellerNotificationService', () => {
  let service: SellerNotificationService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [SellerNotificationService, ApiService, provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(SellerNotificationService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should list seller notifications', () => {
    service.list().subscribe((res) => {
      expect(res.unreadCount).toBe(1);
      expect(res.items[0].type).toBe(NotificationType.NewOrder);
    });

    const req = httpMock.expectOne('/api/seller/notifications');
    expect(req.request.method).toBe('GET');
    req.flush({
      unreadCount: 1,
      items: [
        {
          id: 'n1',
          orderId: 'o1',
          type: 1,
          title: 'Novo pedido recebido',
          message: 'Pedido #123',
          isRead: false,
          createdAtUtc: '2026-07-29T10:00:00Z',
        },
      ],
    });
  });
});
