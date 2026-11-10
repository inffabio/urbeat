export type OperationsStatus =
  | 'healthy'
  | 'degraded'
  | 'unhealthy'
  | 'unknown'
  | 'not_configured';

export type OutboxChannelName = 'Business' | 'Communications';

export type OutboxWorkerRole = 'Business' | 'Communications' | 'All' | 'Web';

export type OutboxWorkerSource = 'hangfire' | 'process';

export interface OutboxTotals {
  pending: number;
  processing: number;
  processed: number;
  failed: number;
  oldestBacklogAtUtc: string | null;
  oldestBacklogAgeSeconds: number | null;
}

export interface OutboxChannelOperations extends OutboxTotals {
  channel: OutboxChannelName;
}

export type OutboxMessageTotals = OutboxTotals;

export interface OutboxWorkerStatus {
  role: OutboxWorkerRole;
  queues: string[];
  lastHeartbeatUtc: string | null;
  heartbeatAgeSeconds: number | null;
  status: OperationsStatus;
  source: OutboxWorkerSource;
}

export interface OutboxWorkerMonitoring {
  available: boolean;
  status: OperationsStatus;
  message: string | null;
  workers: OutboxWorkerStatus[];
}

export interface OperationsDependencyStatus {
  name: string;
  status: OperationsStatus;
  durationMs: number | null;
  message: string | null;
}

export interface OutboxOperationsResponse {
  generatedAtUtc: string;
  status: OperationsStatus;
  backlogThreshold: number;
  totals: OutboxTotals;
  business: OutboxChannelOperations;
  communications: OutboxChannelOperations;
  messages: OutboxMessageTotals;
  workers: OutboxWorkerMonitoring;
  dependencies: OperationsDependencyStatus[];
}
