import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { take } from 'rxjs';
import { OperationsService } from '../../core/services/operations.service';
import { formatSaoPauloDateTime } from '../../core/utils/sao-paulo-date.helper';
import {
  OperationsStatus,
  OutboxChannelName,
  OutboxChannelOperations,
  OutboxOperationsResponse,
  OutboxTotals,
  OutboxWorkerRole,
  OutboxWorkerSource,
} from '../../shared/models/operations.model';

const AUTO_REFRESH_INTERVAL_MS = 60_000;

@Component({
  selector: 'app-admin-operations-page',
  standalone: true,
  templateUrl: './admin-operations-page.component.html',
  styleUrl: './admin-operations-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminOperationsPageComponent implements OnInit {
  private readonly operations = inject(OperationsService);
  private readonly destroyRef = inject(DestroyRef);

  readonly data = signal<OutboxOperationsResponse | null>(null);
  readonly loading = signal(true);
  readonly refreshing = signal(false);
  readonly error = signal<string | null>(null);
  readonly permissionDenied = signal(false);
  readonly lastUpdatedAt = signal<Date | null>(null);
  readonly autoRefresh = signal(true);
  readonly refreshIntervalSeconds = AUTO_REFRESH_INTERVAL_MS / 1000;
  readonly channels = computed<OutboxChannelOperations[]>(() => {
    const snapshot = this.data();
    return snapshot ? [snapshot.business, snapshot.communications] : [];
  });

  private inFlight = false;
  private timer: ReturnType<typeof setInterval> | null = null;

  ngOnInit(): void {
    this.load();
    this.startAutoRefresh();
    this.destroyRef.onDestroy(() => this.stopAutoRefresh());
  }

  load(): void {
    if (this.inFlight) return;

    this.inFlight = true;
    this.error.set(null);
    this.permissionDenied.set(false);

    if (this.data()) {
      this.refreshing.set(true);
    } else {
      this.loading.set(true);
    }

    this.operations
      .getOutboxOperations()
      .pipe(take(1))
      .subscribe({
        next: (snapshot) => {
          this.data.set(snapshot);
          this.lastUpdatedAt.set(new Date());
          this.loading.set(false);
          this.refreshing.set(false);
          this.inFlight = false;
        },
        error: (error: unknown) => {
          this.loading.set(false);
          this.refreshing.set(false);
          this.inFlight = false;
          this.handleError(error);
        },
      });
  }

  toggleAutoRefresh(): void {
    const enabled = !this.autoRefresh();
    this.autoRefresh.set(enabled);

    if (enabled) {
      this.startAutoRefresh();
    } else {
      this.stopAutoRefresh();
    }
  }

  snapshotStatusLabel(status: OperationsStatus): string {
    switch (status) {
      case 'healthy':
        return 'Saudável';
      case 'degraded':
        return 'Degradado';
      case 'unhealthy':
        return 'Indisponível';
      case 'not_configured':
        return 'Não configurado';
      default:
        return 'Desconhecido';
    }
  }

  statusClass(status: OperationsStatus): string {
    switch (status) {
      case 'healthy':
        return 'is-healthy';
      case 'degraded':
        return 'is-degraded';
      case 'unhealthy':
        return 'is-unhealthy';
      case 'not_configured':
        return 'is-neutral';
      default:
        return 'is-unknown';
    }
  }

  channelLabel(channel: OutboxChannelName): string {
    return channel === 'Business' ? 'Negócio' : 'Comunicações';
  }

  workerRoleLabel(role: OutboxWorkerRole): string {
    switch (role) {
      case 'Business':
        return 'Negócio';
      case 'Communications':
        return 'Comunicações';
      case 'All':
        return 'Completo (negócio + comunicações)';
      default:
        return 'Web';
    }
  }

  workerSourceLabel(source: OutboxWorkerSource): string {
    return source === 'process' ? 'Processo local' : 'Hangfire';
  }

  dependencyLabel(name: string): string {
    switch (name) {
      case 'api':
        return 'API';
      case 'postgresql':
        return 'PostgreSQL';
      case 'redis':
        return 'Redis';
      default:
        return name;
    }
  }

  formatCount(value: number): string {
    return value.toLocaleString('pt-BR');
  }

  backlog(totals: OutboxTotals): number {
    return totals.pending + totals.processing;
  }

  formatAge(seconds: number | null): string {
    if (seconds === null) return 'Sem pendências';
    if (seconds < 60) return `${seconds} s`;

    const minutes = Math.floor(seconds / 60);
    if (minutes < 60) return `${minutes} min`;

    const hours = Math.floor(minutes / 60);
    if (hours < 24) {
      const remainingMinutes = minutes % 60;
      return remainingMinutes ? `${hours} h ${remainingMinutes} min` : `${hours} h`;
    }

    const days = Math.floor(hours / 24);
    const remainingHours = hours % 24;
    return remainingHours ? `${days} d ${remainingHours} h` : `${days} d`;
  }

  formatDateTime(value: string | null): string {
    return value ? formatSaoPauloDateTime(value) || '—' : '—';
  }

  formatUpdatedAt(): string {
    const updatedAt = this.lastUpdatedAt();
    return updatedAt ? formatSaoPauloDateTime(updatedAt) || '—' : '—';
  }

  formatDuration(durationMs: number | null): string {
    return durationMs === null ? '—' : `${durationMs} ms`;
  }

  private handleError(error: unknown): void {
    if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
      this.permissionDenied.set(true);
      this.data.set(null);
      this.error.set(null);
      this.lastUpdatedAt.set(null);
      this.autoRefresh.set(false);
      this.stopAutoRefresh();
      return;
    }

    // This is an admin observability surface: arbitrary server bodies can contain exception
    // details, SQL or infrastructure identifiers. Only authentication failures get tailored copy.
    this.error.set('Não foi possível carregar o painel de operações.');
  }

  private startAutoRefresh(): void {
    this.stopAutoRefresh();
    this.timer = setInterval(() => {
      if (this.autoRefresh()) this.load();
    }, AUTO_REFRESH_INTERVAL_MS);
  }

  private stopAutoRefresh(): void {
    if (this.timer !== null) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }
}
