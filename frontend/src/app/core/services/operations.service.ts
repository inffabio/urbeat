import { HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { SKIP_SERVER_ACCESS_OVERLAY } from '../interceptors/server-access.context';
import { OutboxOperationsResponse } from '../../shared/models/operations.model';

const OUTBOX_OPERATIONS_PATH = '/api/admin/operations/outbox';

export interface OutboxOperationsOptions {
  /** Automatic refresh: it must not flash the global server-access overlay. */
  background?: boolean;
}

@Injectable({ providedIn: 'root' })
export class OperationsService {
  private readonly api = inject(ApiService);

  getOutboxOperations(
    options?: OutboxOperationsOptions,
  ): Observable<OutboxOperationsResponse> {
    const context = options?.background
      ? new HttpContext().set(SKIP_SERVER_ACCESS_OVERLAY, true)
      : undefined;
    return this.api.get<OutboxOperationsResponse>(OUTBOX_OPERATIONS_PATH, undefined, context);
  }
}
