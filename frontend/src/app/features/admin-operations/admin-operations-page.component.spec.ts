import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { OperationsService } from '../../core/services/operations.service';
import { OutboxOperationsResponse } from '../../shared/models/operations.model';
import { AdminOperationsPageComponent } from './admin-operations-page.component';

const SNAPSHOT: OutboxOperationsResponse = {
  generatedAtUtc: '2026-11-09T12:00:00Z',
  status: 'degraded',
  backlogThreshold: 1000,
  totals: {
    pending: 3,
    processing: 1,
    processed: 10,
    failed: 2,
    oldestBacklogAtUtc: '2026-11-09T11:50:00Z',
    oldestBacklogAgeSeconds: 600,
  },
  business: {
    channel: 'Business',
    pending: 2,
    processing: 0,
    processed: 8,
    failed: 0,
    oldestBacklogAtUtc: '2026-11-09T11:50:00Z',
    oldestBacklogAgeSeconds: 600,
  },
  communications: {
    channel: 'Communications',
    pending: 1,
    processing: 1,
    processed: 2,
    failed: 2,
    oldestBacklogAtUtc: '2026-11-09T11:55:00Z',
    oldestBacklogAgeSeconds: 300,
  },
  messages: {
    pending: 2,
    processing: 0,
    processed: 8,
    failed: 1,
    oldestBacklogAtUtc: '2026-11-09T11:50:00Z',
    oldestBacklogAgeSeconds: 600,
  },
  workers: {
    available: true,
    status: 'degraded',
    message: 'Sem heartbeat recente para: Communications.',
    workers: [
      {
        role: 'Business',
        queues: ['business', 'default'],
        lastHeartbeatUtc: '2026-11-09T11:59:50Z',
        heartbeatAgeSeconds: 10,
        status: 'healthy',
        source: 'hangfire',
      },
    ],
  },
  dependencies: [
    { name: 'api', status: 'healthy', durationMs: null, message: null },
    { name: 'postgresql', status: 'healthy', durationMs: 12, message: null },
    { name: 'redis', status: 'unhealthy', durationMs: 2001, message: null },
  ],
};

describe('AdminOperationsPageComponent', () => {
  let fixture: ComponentFixture<AdminOperationsPageComponent>;
  let serviceMock: { getOutboxOperations: jest.Mock };

  beforeEach(async () => {
    jest.useFakeTimers();
    serviceMock = { getOutboxOperations: jest.fn(() => of(SNAPSHOT)) };

    await TestBed.configureTestingModule({
      imports: [AdminOperationsPageComponent],
      providers: [{ provide: OperationsService, useValue: serviceMock }],
    }).compileComponents();

    fixture = TestBed.createComponent(AdminOperationsPageComponent);
  });

  afterEach(() => {
    fixture.destroy();
    jest.useRealTimers();
  });

  it('shows a loading state before the first response', () => {
    serviceMock.getOutboxOperations.mockReturnValue(
      new Subject<OutboxOperationsResponse>(),
    );

    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Carregando operações');
    expect(serviceMock.getOutboxOperations).toHaveBeenCalledTimes(1);
  });

  it('renders counters, channels, workers and dependencies after success', () => {
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Operações');
    expect(text).toContain('Degradado');
    expect(text).toContain('Negócio');
    expect(text).toContain('Comunicações');
    expect(text).toContain('Hangfire');
    expect(text).toContain('Sem heartbeat recente para: Communications.');
    expect(text).toContain('PostgreSQL');
    expect(text).toContain('Redis');
    expect(text).toContain('Indisponível');
    expect(text).toContain('Taxas de processamento não são exibidas');
  });

  it('shows the empty state when no worker is visible', () => {
    serviceMock.getOutboxOperations.mockReturnValue(
      of({
        ...SNAPSHOT,
        workers: {
          available: true,
          status: 'unhealthy',
          message: 'Nenhum worker de outbox visível no Hangfire.',
          workers: [],
        },
      }),
    );

    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Nenhum worker visível');
  });

  it('shows the error state when the request fails', () => {
    serviceMock.getOutboxOperations.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 500,
            error: { detail: 'Falha ao consultar operações.' },
          }),
      ),
    );

    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Não foi possível carregar');
    expect(text).toContain('Não foi possível carregar o painel de operações.');
  });

  it('shows the permission state when the server rejects the admin role', () => {
    serviceMock.getOutboxOperations.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 403 })),
    );

    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Sem permissão');
    expect(text).not.toContain('Saudável');
  });

  it('refreshes manually without duplicating the initial request', () => {
    fixture.detectChanges();
    expect(serviceMock.getOutboxOperations).toHaveBeenCalledTimes(1);

    const refreshButton = fixture.nativeElement.querySelector(
      '.admin-action',
    ) as HTMLButtonElement;
    refreshButton.click();
    fixture.detectChanges();

    expect(serviceMock.getOutboxOperations).toHaveBeenCalledTimes(2);
  });
});
