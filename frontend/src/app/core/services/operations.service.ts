import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { OutboxOperationsResponse } from '../../shared/models/operations.model';

const OUTBOX_OPERATIONS_PATH = '/api/admin/operations/outbox';

@Injectable({ providedIn: 'root' })
export class OperationsService {
  private readonly api = inject(ApiService);

  getOutboxOperations(): Observable<OutboxOperationsResponse> {
    return this.api.get<OutboxOperationsResponse>(OUTBOX_OPERATIONS_PATH);
  }
}
