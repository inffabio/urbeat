import { OrderStatus } from '../enums/order-status.enum';
import { FulfillmentType } from '../enums/fulfillment-type.enum';
import { PaymentMethod } from '../enums/payment-method.enum';

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
  total: number;
  createdAtUtc: string;
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

export interface StoreCustomersQuery {
  page?: number;
  pageSize?: number;
  search?: string;
  status?: 'all' | 'active' | 'inactive';
  sort?: 'lastOrderDesc' | 'nameAsc' | 'totalOrdersAsc' | 'totalOrdersDesc' | 'totalSpentAsc' | 'totalSpentDesc';
}
