import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import {
  SystemParameter,
  UpdateSystemParameterRequest,
} from '../../shared/models/system-parameter.model';

/** Business-only admin routes; technical parameters are rejected by the server. */
const BUSINESS_PARAMETERS_PATH = '/api/admin/system-parameters/business';

@Injectable({ providedIn: 'root' })
export class SystemParameterService {
  private readonly api = inject(ApiService);

  list(): Observable<SystemParameter[]> {
    return this.api.get<SystemParameter[]>(BUSINESS_PARAMETERS_PATH);
  }

  update(
    key: string,
    value: string,
    confirmRestrictedChange = false,
  ): Observable<SystemParameter> {
    const body: UpdateSystemParameterRequest = { value, confirmRestrictedChange };
    return this.api.put<SystemParameter>(
      `${BUSINESS_PARAMETERS_PATH}/${encodeURIComponent(key)}`,
      body,
    );
  }
}
