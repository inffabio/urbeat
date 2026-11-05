import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { PublicPlanOffer } from '../../shared/models/plan-offer.model';

@Injectable({
  providedIn: 'root',
})
export class PlanOfferService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = environment.apiUrl;

  getOffer(): Observable<PublicPlanOffer> {
    return this.http.get<PublicPlanOffer>(`${this.apiUrl}/api/public/plans`);
  }
}
