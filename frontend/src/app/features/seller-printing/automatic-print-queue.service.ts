import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { isWindowsPlatform } from '../../core/utils/platform.helper';
import { OrderService } from '../../core/services/order.service';
import { OrderDetails } from '../../shared/models/order.model';
import { OrderStatus } from '../../shared/enums/order-status.enum';
import { SellerPrintingService } from './seller-printing.service';
import {
  AutomaticPrintJob,
  OrderAcceptedForPrintingEvent,
  PendingPrintJob,
  PendingPrintJobsResponse,
  PrintingConfig,
  RecordPrintJobResultRequest,
} from './seller-printing.models';

/** Delays after the first failed attempt: 2s, 5s and 15s. */
export const AUTOMATIC_PRINT_RETRY_DELAYS_MS = [2_000, 5_000, 15_000] as const;

const COMPLETED_KEYS_STORAGE = 'urbeat:seller-printing:completed-operation-keys';
const MAX_COMPLETED_KEYS = 500;
const MAX_VISIBLE_JOBS = 20;
const PENDING_JOBS_PAGE_SIZE = 50;
const MAX_RECONCILE_PAGES = 100;

interface AcceptanceIdentity {
  acceptedAtUtc: string;
  sequence: number;
}

interface QueuedPrintOrder {
  orderId: string;
  event?: OrderAcceptedForPrintingEvent;
  details?: OrderDetails;
  acceptance?: AcceptanceIdentity;
}

@Injectable({ providedIn: 'root' })
export class AutomaticPrintQueueService {
  private readonly printing = inject(SellerPrintingService);
  private readonly orderService = inject(OrderService);
  private readonly api = inject(ApiService);

  private readonly jobsState = signal<AutomaticPrintJob[]>([]);
  private readonly seenOrderIds = new Set<string>();
  private configLoad?: Promise<void>;
  private configLoaded = false;

  readonly jobs = this.jobsState.asReadonly();
  readonly currentJob = computed(() =>
    this.jobsState().find((job) => job.status === 'queued' || job.status === 'printing' || job.status === 'retrying') ?? null,
  );
  readonly lastFailure = computed(() => {
    const jobs = this.jobsState();
    for (let i = jobs.length - 1; i >= 0; i -= 1) {
      if (jobs[i].status === 'failed') return jobs[i];
    }
    return null;
  });
  readonly lastPrinted = computed(() => {
    const jobs = this.jobsState();
    for (let i = jobs.length - 1; i >= 0; i -= 1) {
      if (jobs[i].status === 'printed') return jobs[i];
    }
    return null;
  });
  readonly lastOutcome = computed(() => {
    const jobs = this.jobsState();
    for (let i = jobs.length - 1; i >= 0; i -= 1) {
      if (jobs[i].status === 'printed' || jobs[i].status === 'failed') return jobs[i];
    }
    return null;
  });
  readonly statusTone = computed<'info' | 'success' | 'warning' | 'error' | null>(() => {
    const current = this.currentJob();
    if (current?.status === 'retrying') return 'warning';
    if (current) return 'info';

    const outcome = this.lastOutcome();
    if (outcome?.status === 'failed') return 'error';
    if (outcome?.status === 'printed') return 'success';
    return null;
  });
  readonly statusMessage = computed(() => {
    const current = this.currentJob();
    if (current?.status === 'retrying') {
      return `Reimprimindo pedido #${current.orderCode} (tentativa ${current.attempt} de ${current.maxAttempts})...`;
    }
    if (current) {
      return `Imprimindo pedido #${current.orderCode}...`;
    }

    const outcome = this.lastOutcome();
    if (outcome?.status === 'failed') {
      const agentHint = this.shouldHintAgentUnavailable(outcome)
        ? ' Agente local indisponível: verifique se o serviço está em execução.'
        : '';
      return `Falha ao imprimir o pedido #${outcome.orderCode}: ${outcome.message}${agentHint}`;
    }
    if (outcome?.status === 'printed') {
      return `Pedido #${outcome.orderCode} impresso automaticamente.`;
    }

    return null;
  });

  /**
   * Handles a live/signal event. Safe to call multiple times for the same order: the acceptance
   * identity and the local/persisted completed keys make the flow idempotent.
   */
  async enqueueAcceptedOrder(event: OrderAcceptedForPrintingEvent): Promise<void> {
    if (!event?.orderId) return;

    await this.ensureConfigLoaded();
    if (!this.printing.config().autoPrint) return;
    if (this.seenOrderIds.has(event.orderId)) return;

    this.seenOrderIds.add(event.orderId);
    const job = this.createJob(event.orderId, event);
    this.addJob(job);
    void this.runJob(job, { orderId: event.orderId, event });
  }

  /**
   * Backend-authoritative reconciliation after load/reconnect. The server returns every Pending
   * acceptance for the seller's stores using its own monotonic cursor. The cursor is used only for
   * pagination inside this scan; it is never persisted as a local watermark, so a page reload and
   * a first run on a new device cannot discard legitimate acceptances.
   */
  async reconcilePendingJobs(): Promise<void> {
    await this.ensureConfigLoaded(true);

    if (!this.printing.config().autoPrint) {
      return;
    }

    let afterCursor = 0;
    for (let pageNumber = 0; pageNumber < MAX_RECONCILE_PAGES; pageNumber += 1) {
      let page: PendingPrintJobsResponse;
      try {
        page = await firstValueFrom(this.api.get<PendingPrintJobsResponse>(
          `/api/seller/printing/jobs?afterCursor=${afterCursor}&limit=${PENDING_JOBS_PAGE_SIZE}`,
        ));
      } catch {
        // The next load/reconnect retries the same server-authoritative scan.
        return;
      }

      for (const pending of page.items ?? []) {
        this.enqueuePendingJob(pending);
      }

      if (!page.hasMore) {
        return;
      }

      const nextCursor = Number(page.nextCursor);
      if (!Number.isFinite(nextCursor) || nextCursor <= afterCursor) {
        return;
      }
      afterCursor = nextCursor;
    }
  }

  private enqueuePendingJob(pending: PendingPrintJob): void {
    if (!pending?.orderId || this.seenOrderIds.has(pending.orderId)) return;

    this.seenOrderIds.add(pending.orderId);
    const event: OrderAcceptedForPrintingEvent = {
      orderId: pending.orderId,
      storeId: pending.storeId,
      orderCode: pending.orderCode,
      sequence: pending.acceptanceSequence,
      acceptedAtUtc: pending.acceptedAtUtc,
    };
    const job = this.createJob(pending.orderId, event);

    if (pending.status === 'ambiguous') {
      this.addJob({
        ...job,
        status: 'failed',
        message: pending.lastError
          ?? 'Resultado físico incerto: o agente caiu durante o envio. Faça o retry explícito no agente após conferir a impressora.',
      });
      return;
    }

    this.addJob(job);
    void this.runJob(job, { orderId: pending.orderId, event });
  }

  private async runJob(job: AutomaticPrintJob, queued: QueuedPrintOrder): Promise<void> {
    let details = queued.details;
    let acceptance = queued.acceptance;
    let lastMessage = '';

    for (let attempt = 1; attempt <= job.maxAttempts; attempt += 1) {
      if (attempt > 1) {
        const delay = AUTOMATIC_PRINT_RETRY_DELAYS_MS[Math.min(attempt - 2, AUTOMATIC_PRINT_RETRY_DELAYS_MS.length - 1)];
        this.patchJob(job.orderId, {
          status: 'retrying',
          attempt,
          message: lastMessage,
          nextRetryAtUtc: new Date(Date.now() + delay).toISOString(),
        });
        await this.delay(delay);
      }

      const config = this.printing.config();
      if (!config.autoPrint) {
        this.patchJob(job.orderId, {
          status: 'failed',
          attempt,
          message: 'Impressão automática desativada durante o processamento.',
        });
        return;
      }

      const configError = this.validateAutomaticConfig(config);
      if (configError) {
        const ackNote = await this.reportResult(job.orderId, '', false, configError);
        this.patchJob(job.orderId, {
          status: 'failed',
          attempt,
          message: ackNote ? `${configError} ${ackNote}` : configError,
        });
        return;
      }

      this.patchJob(job.orderId, {
        status: 'printing',
        attempt,
        printerName: config.printerName,
        profileId: this.printing.printerProfile(config),
        nextRetryAtUtc: undefined,
      });

      if (!details) {
        try {
          details = await firstValueFrom(this.orderService.getStoreOrder(queued.orderId));
          acceptance = this.findAcceptance(details, queued.event);
        } catch {
          lastMessage = 'Não foi possível carregar os detalhes do pedido.';
          if (attempt >= job.maxAttempts) {
            const ackNote = await this.reportResult(job.orderId, '', false, lastMessage);
            this.patchJob(job.orderId, {
              status: 'failed',
              attempt,
              message: ackNote ? `${lastMessage} ${ackNote}` : lastMessage,
            });
          }
          continue;
        }
      }

      acceptance ??= this.findAcceptance(details, queued.event);
      const operationKey = this.buildOperationKey(
        queued.orderId,
        acceptance,
        config,
        this.printing.printerProfile(config),
      );

      if (this.isOperationCompleted(operationKey)) {
        this.patchJob(job.orderId, {
          status: 'printed',
          attempt,
          key: operationKey,
          message: 'Pedido já impresso anteriormente.',
        });
        return;
      }

      const result = await this.printing.printOrderFromDetails(details, operationKey);

      if (result.uncertain) {
        const message = result.message
          || 'Resultado físico incerto: o agente caiu durante o envio. Faça o retry explícito no agente após conferir a impressora.';
        const ackNote = await this.reportResult(job.orderId, operationKey, false, message, true);
        this.patchJob(job.orderId, {
          status: 'failed',
          attempt,
          key: operationKey,
          message: ackNote ? `${message} ${ackNote}` : message,
          nextRetryAtUtc: undefined,
        });
        return;
      }

      if (result.ok) {
        this.rememberCompleted(operationKey);
        const ackNote = await this.reportResult(job.orderId, operationKey, true, result.message);
        this.patchJob(job.orderId, {
          status: 'printed',
          attempt,
          key: operationKey,
          message: ackNote ? `${result.message} ${ackNote}` : result.message,
          nextRetryAtUtc: undefined,
        });
        return;
      }

      lastMessage = result.message || 'A impressora não confirmou a impressão.';
      if (attempt >= job.maxAttempts) {
        await this.refreshAgentHealthIfNeeded(config);
        const ackNote = await this.reportResult(job.orderId, operationKey, false, lastMessage);
        this.patchJob(job.orderId, {
          status: 'failed',
          attempt,
          message: ackNote ? `${lastMessage} ${ackNote}` : lastMessage,
        });
      }
    }
  }

  private validateAutomaticConfig(config: PrintingConfig): string | null {
    if (!config.printerName.trim()) {
      return 'Nenhuma impressora configurada para a loja.';
    }

    if (config.connectionType === 'browser-print') {
      return 'A impressão automática não usa o diálogo do navegador. Selecione o agente local.';
    }

    if (config.connectionType === 'mock') {
      return 'O adaptador de teste não imprime pedidos reais. Selecione a impressora da loja.';
    }

    if (isWindowsPlatform() && config.connectionType !== 'local-agent') {
      return 'No Windows, a impressão automática exige o agente local instalado.';
    }

    // A stale health probe is only a hint: the print call remains the authoritative check and its
    // error feeds the retry/failure state.
    return null;
  }

  private findAcceptance(details: OrderDetails, event?: OrderAcceptedForPrintingEvent): AcceptanceIdentity | undefined {
    const history = details.history ?? [];
    for (let index = history.length - 1; index >= 0; index -= 1) {
      const entry = history[index];
      if (entry.previousStatus === OrderStatus.Received && entry.newStatus === OrderStatus.Preparing) {
        return {
          acceptedAtUtc: entry.createdAtUtc,
          // History contains the creation entry (sequence 1), so a transition's index maps to its
          // StatusVersion. The live event sequence is preferred when available and consistent.
          sequence: event?.sequence && event.sequence > 0 ? event.sequence : index + 2,
        };
      }
    }

    if (event?.acceptedAtUtc) {
      return {
        acceptedAtUtc: event.acceptedAtUtc,
        sequence: event.sequence && event.sequence > 0 ? event.sequence : 0,
      };
    }

    if (details.status === OrderStatus.Preparing) {
      return { acceptedAtUtc: details.createdAtUtc, sequence: 0 };
    }

    return undefined;
  }

  private buildOperationKey(
    orderId: string,
    acceptance: AcceptanceIdentity | undefined,
    config: PrintingConfig,
    profileId: string,
  ): string {
    const acceptedAt = acceptance?.acceptedAtUtc ?? 'unknown';
    const sequence = acceptance?.sequence ?? 0;
    return [
      'order',
      orderId,
      'accept',
      acceptedAt,
      'seq',
      String(sequence),
      'printer',
      this.sanitizeKeyPart(config.printerName),
      'profile',
      this.sanitizeKeyPart(profileId),
    ].join(':');
  }

  private sanitizeKeyPart(value: string): string {
    return (value || 'default').trim().replace(/[^a-zA-Z0-9_.-]+/g, '-').slice(0, 80);
  }

  private createJob(orderId: string, event?: OrderAcceptedForPrintingEvent): AutomaticPrintJob {
    const config = this.printing.config();
    const now = new Date().toISOString();
    return {
      key: `order:${orderId}`,
      orderId,
      orderCode: event?.orderCode ?? orderId,
      acceptanceSequence: event?.sequence ?? 0,
      acceptedAtUtc: event?.acceptedAtUtc ?? now,
      printerName: config.printerName,
      profileId: this.printing.printerProfile(config),
      attempt: 1,
      maxAttempts: AUTOMATIC_PRINT_RETRY_DELAYS_MS.length + 1,
      status: 'queued',
      message: 'Aguardando impressão.',
      updatedAtUtc: now,
    };
  }

  private addJob(job: AutomaticPrintJob): void {
    this.jobsState.update((jobs) => [...jobs, job].slice(-MAX_VISIBLE_JOBS));
  }

  private patchJob(orderId: string, patch: Partial<AutomaticPrintJob>): void {
    this.jobsState.update((jobs) => jobs.map((job) => (
      job.orderId === orderId
        ? { ...job, ...patch, updatedAtUtc: new Date().toISOString() }
        : job
    )));
  }

  private async ensureConfigLoaded(force = false): Promise<void> {
    if (force) {
      this.configLoaded = false;
      this.configLoad = undefined;
    }
    if (this.configLoaded) return;
    if (!this.configLoad) {
      this.configLoad = this.printing.loadStoreConfig()
        .catch(() => undefined)
        .then(() => {
          this.configLoaded = true;
        });
    }
    await this.configLoad;
  }

  private async reportResult(
    orderId: string,
    operationKey: string,
    success: boolean,
    message: string,
    ambiguous = false,
  ): Promise<string | null> {
    const request: RecordPrintJobResultRequest = {
      success,
      ambiguous,
      operationKey,
      message,
    };

    try {
      await firstValueFrom(this.api.post(
        `/api/seller/printing/jobs/${encodeURIComponent(orderId)}/result`,
        request,
      ));
      return null;
    } catch {
      // The local result is still shown; the server keeps the job Pending and the next
      // reconciliation retries it. The print agent operation key remains the duplicate guard.
      return 'Não foi possível confirmar o resultado ao servidor; o job será reconciliado.';
    }
  }

  private async refreshAgentHealthIfNeeded(config: PrintingConfig): Promise<void> {
    if (config.connectionType !== 'local-agent') return;
    try {
      await this.printing.refreshLocalAgent();
    } catch {
      // Health is advisory only.
    }
  }

  private shouldHintAgentUnavailable(job: AutomaticPrintJob): boolean {
    return this.printing.config().connectionType === 'local-agent'
      && this.printing.agentHealth().available === false;
  }

  private isOperationCompleted(operationKey: string): boolean {
    return this.readCompletedKeys().has(operationKey);
  }

  private rememberCompleted(operationKey: string): void {
    const keys = this.readCompletedKeys();
    keys.add(operationKey);
    const values = Array.from(keys).slice(-MAX_COMPLETED_KEYS);
    this.writeStorage(COMPLETED_KEYS_STORAGE, JSON.stringify(values));
  }

  private readCompletedKeys(): Set<string> {
    try {
      const raw = localStorage.getItem(COMPLETED_KEYS_STORAGE);
      if (!raw) return new Set();
      const parsed = JSON.parse(raw);
      return new Set(Array.isArray(parsed) ? parsed.filter((value): value is string => typeof value === 'string') : []);
    } catch {
      return new Set();
    }
  }

  private readStorage(key: string): string | null {
    try {
      return localStorage.getItem(key);
    } catch {
      return null;
    }
  }

  private writeStorage(key: string, value: string): void {
    try {
      localStorage.setItem(key, value);
    } catch {
      // Private mode/storage full: idempotency still survives on the print agent.
    }
  }

  private delay(ms: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }
}
