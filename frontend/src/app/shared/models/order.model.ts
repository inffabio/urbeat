import { OrderStatus } from '../enums/order-status.enum';
import { FulfillmentType } from '../enums/fulfillment-type.enum';
import { PaymentMethod } from '../enums/payment-method.enum';

export interface OrderItemOptionPrice {
  name: string;
  price: number;
}

export interface OrderItem {
  productName: string;
  quantity: number;
  unitPrice: number;
  totalPrice: number;
  notes?: string;
  variationName?: string;
  weightGrams?: number;
  choiceOptionName?: string;
  additionalNames?: string;
  optionPrices?: OrderItemOptionPrice[];
}

export interface OrderHistoryEntry {
  createdAtUtc: string;
  previousStatus: OrderStatus | null;
  newStatus: OrderStatus;
  changedByUserId?: string;
  notes?: string;
}

export interface OrderDetails {
  id: string;
  code: string;
  customerUserId?: string;
  customerName?: string;
  customerPhoneNumber?: string;
  storeId: string;
  storeName?: string;
  fulfillmentType: FulfillmentType;
  status: OrderStatus;
  paymentMethod: PaymentMethod;
  subtotal: number;
  deliveryFee: number;
  total: number;
  createdAtUtc: string;
  deliveryConfirmedAtUtc?: string;
  sellerCompletedAtUtc?: string;
  deliveryDriverId?: string;
  deliveryDriverName?: string;
  deliveryStartedAtUtc?: string;
  deliveryPinConfirmedAtUtc?: string;
  deliveryRouteId?: string | null;
  deliveryRouteStatus?: number | null;
  deliveryPin?: string;
  addressCep?: string;
  addressStreet?: string;
  addressNumber?: string;
  addressNeighborhood?: string;
  addressCity?: string;
  addressState?: string;
  addressComplement?: string;
  addressReference?: string;
  notes?: string;
  items: OrderItem[];
  history: OrderHistoryEntry[];
}

export interface OrderSummary {
  id: string;
  code: string;
  storeId: string;
  customerName?: string;
  customerPhoneNumber?: string;
  fulfillmentType?: FulfillmentType;
  paymentMethod?: PaymentMethod;
  addressSummary?: string;
  itemsSummary?: string;
  status: OrderStatus;
  subtotal?: number;
  deliveryFee?: number;
  total: number;
  createdAtUtc: string;
  deliveryConfirmedAtUtc?: string;
  sellerCompletedAtUtc?: string;
  deliveryDriverId?: string;
  deliveryDriverName?: string;
  deliveryStartedAtUtc?: string;
  deliveryPinConfirmedAtUtc?: string;
  deliveryRouteId?: string | null;
  deliveryRouteStatus?: number | null;
}

export interface StoreOrdersReport {
  totalOrders: number;
  totalRevenue: number;
  inProgressOrders: number;
  startDateUtc?: string | null;
  endDateUtc?: string | null;
}

export interface SellerCustomerSummary {
  id: string;
  name: string;
  email: string;
  phone: string;
  cep: string;
  street: string;
  number: string;
  complement: string;
  neighborhood: string;
  city: string;
  state: string;
  totalOrders: number;
  totalSpent: number;
  lastOrderAtUtc: string | null;
  isActive: boolean;
}

export interface SellerCustomerMetrics {
  totalCustomers: number;
  activeCustomers: number;
  recurringCustomers: number;
  newCustomersThisMonth: number;
  averageTicket: number;
}

export interface PagedSellerCustomerSummary {
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  metrics: SellerCustomerMetrics;
  items: SellerCustomerSummary[];
}

export interface SellerDeliverySummary {
  id: string;
  code: string;
  customerName?: string;
  customerPhoneNumber?: string;
  addressSummary?: string;
  status: OrderStatus;
  total: number;
  createdAtUtc: string;
  deliveryDriverId?: string;
  deliveryDriverName?: string;
  deliveryStartedAtUtc?: string;
  deliveryPinConfirmedAtUtc?: string;
  deliveryRouteId?: string | null;
  deliveryRouteStatus?: number | null;
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
  /** Additive exclusive upper bound; mutually exclusive with endDateUtc. */
  endExclusiveUtc?: string;
}

export type StoreAnalyticsPeriod = 'day' | 'week' | 'month' | 'year';

export interface StoreAnalyticsQuery {
  period: StoreAnalyticsPeriod;
  /** Local civil date in yyyy-MM-dd. */
  anchor: string;
  weekBlock?: number;
}

export interface StoreAnalyticsKpis {
  deliveredOrders: number;
  revenue: number;
  deliveryFees: number;
  averageTicket: number;
}

export interface StoreAnalyticsProduct {
  productName: string;
  quantity: number;
  revenue: number;
}

export interface StoreAnalyticsPayment {
  paymentMethod: PaymentMethod;
  deliveredOrders: number;
  revenue: number;
}

export interface StoreAnalyticsMonth {
  year: number;
  month: number;
  deliveredOrders: number;
  revenue: number;
}

export interface StoreAnalytics {
  period: StoreAnalyticsPeriod;
  anchorDate: string;
  weekBlock: number | null;
  rangeStartUtc: string;
  rangeEndUtcExclusive: string;
  timeZone: string;
  chartYear: number;
  kpis: StoreAnalyticsKpis;
  products: StoreAnalyticsProduct[];
  payments: StoreAnalyticsPayment[];
  months: StoreAnalyticsMonth[];
  availableYears: number[];
}

export interface StoreSalesQuery extends StoreAnalyticsQuery {
  page?: number;
  pageSize?: number;
}

export interface StoreSalesTotals {
  /** Sum of Order.Total; already includes delivery fees. */
  salesTotal: number;
  /** Sum of Order.DeliveryFee; already contained in salesTotal. */
  deliveryFees: number;
}

export interface StoreSaleItem {
  productName: string;
  variationName?: string | null;
  quantity: number;
  unitPrice: number;
  totalPrice: number;
}

export interface StoreSaleRow {
  orderId: string;
  code: string;
  customerName?: string | null;
  createdAtUtc: string;
  subtotal: number;
  deliveryFee: number;
  /** Order total, already including deliveryFee. */
  total: number;
  items: StoreSaleItem[];
}

export interface PagedStoreSales {
  period: StoreAnalyticsPeriod;
  anchorDate: string;
  weekBlock: number | null;
  rangeStartUtc: string;
  rangeEndUtcExclusive: string;
  timeZone: string;
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  totals: StoreSalesTotals;
  sales: StoreSaleRow[];
}

export interface StoreCustomersQuery {
  page?: number;
  pageSize?: number;
  search?: string;
  status?: 'all' | 'active' | 'inactive';
  sort?: 'lastOrderDesc' | 'nameAsc' | 'totalOrdersAsc' | 'totalOrdersDesc' | 'totalSpentAsc' | 'totalSpentDesc';
}
