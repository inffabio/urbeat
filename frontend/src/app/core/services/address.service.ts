import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import {
  CepLookupResponse,
  CustomerAddress,
  UpsertCustomerAddress,
} from '../../shared/models/address.model';

@Injectable({ providedIn: 'root' })
export class AddressService {
  private readonly api = inject(ApiService);

  lookupCep(cep: string): Observable<CepLookupResponse> {
    const cleaned = cep.replace(/\D/g, '');
    return this.api.get<CepLookupResponse>(`/api/address-lookup/cep/${cleaned}`);
  }

  list(): Observable<CustomerAddress[]> {
    return this.api.get<CustomerAddress[]>('/api/customer/addresses');
  }

  create(payload: UpsertCustomerAddress): Observable<CustomerAddress> {
    return this.api.post<CustomerAddress>('/api/customer/addresses', payload);
  }
}
