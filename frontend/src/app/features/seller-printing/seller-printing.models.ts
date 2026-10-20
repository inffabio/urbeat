import { FulfillmentType } from '../../shared/enums/fulfillment-type.enum';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import { PaymentMethod } from '../../shared/enums/payment-method.enum';

export type PrinterConnectionType = 'android-bluetooth' | 'browser-print' | 'mock' | 'wifi' | 'local-agent';
export type PrinterPaperWidth = '58mm' | '80mm';

export interface LocalAgentStatus {
  available: boolean;
  mode: 'local-agent';
  printers: string[];
  message: string;
}

export interface PrinterPresetResponse {
  id: string;
  name: string;
  manufacturer: string;
  connectionType: string;
  paperWidth: string;
  commandSet: string;
  adapterId: string;
  description: string;
  isActive: boolean;
}

export interface PrinterPreset {
  id: string;
  name: string;
  manufacturer: string;
  connectionType: PrinterConnectionType;
  paperWidth: PrinterPaperWidth;
  commandSet: 'esc-pos' | 'browser';
  description: string;
}

export interface PrintingConfig {
  presetId: string;
  printerName: string;
  connectionType: PrinterConnectionType;
  paperWidth: PrinterPaperWidth;
  copies: number;
  autoCut: boolean;
  autoPrint: boolean;
  printKitchenCopy: boolean;
  printCounterCopy: boolean;
  printCustomerReceipt: boolean;
  printLogo: boolean;
  highlightOrderNumber: boolean;
  footerText: string;
  savedMacAddress: string;
  adapterId: string;
  logoUrl: string;
}

export interface PrintableOrderItem {
  name: string;
  quantity: number;
  unitPrice: number;
}

export interface PrintableOrder {
  orderId?: string;
  /** Stable idempotency key shared by dashboard and local print agent. */
  operationKey?: string;
  code: string;
  customerName?: string;
  customerPhoneNumber?: string;
  fulfillmentType?: FulfillmentType;
  paymentMethod?: PaymentMethod;
  status?: OrderStatus;
  itemsSummary?: string;
  items?: PrintableOrderItem[];
  total: number;
  createdAtUtc: string;
  addressSummary?: string;
  notes?: string;
}

export interface PrintResult {
  ok: boolean;
  mode: PrinterConnectionType;
  message: string;
  printedAtUtc: string;
  /** False when retrying could duplicate physical output (agent ambiguous state). */
  retryable?: boolean;
  /** True when the agent crashed around the spooler call and physical output is unknown. */
  uncertain?: boolean;
}

/** Payload pushed by the backend only for the Received -> Preparing acceptance transition. */
export interface OrderAcceptedForPrintingEvent {
  orderId: string;
  storeId?: string;
  orderCode?: string;
  sequence?: number;
  acceptedAtUtc?: string;
}

export interface PendingPrintJob {
  cursor: number;
  orderId: string;
  storeId: string;
  orderCode: string;
  acceptanceSequence: number;
  acceptedAtUtc: string;
  status: 'pending' | 'ambiguous' | 'completed';
  attemptCount: number;
  lastError?: string | null;
}

export interface PendingPrintJobsResponse {
  items: PendingPrintJob[];
  nextCursor: number;
  hasMore: boolean;
}

export interface RecordPrintJobResultRequest {
  success: boolean;
  ambiguous?: boolean;
  operationKey: string;
  message?: string | null;
}

export type AutomaticPrintStatus = 'queued' | 'printing' | 'retrying' | 'printed' | 'failed';

export interface AutomaticPrintJob {
  key: string;
  orderId: string;
  orderCode: string;
  acceptanceSequence: number;
  acceptedAtUtc: string;
  printerName: string;
  profileId: string;
  attempt: number;
  maxAttempts: number;
  status: AutomaticPrintStatus;
  message: string;
  nextRetryAtUtc?: string;
  updatedAtUtc: string;
}
