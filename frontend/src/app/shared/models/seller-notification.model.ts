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
