import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface LandingPageContent {
  id: string;
  section: string;
  key: string;
  value: string;
  displayOrder: number;
  isActive: boolean;
  description?: string;
  createdAt: string;
  updatedAt: string;
}

export interface LandingPageContentRequest {
  section: string;
  key: string;
  value: string;
  displayOrder: number;
  isActive: boolean;
  description?: string;
}

@Injectable({
  providedIn: 'root',
})
export class LandingPageService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = environment.apiUrl;

  getAll(): Observable<LandingPageContent[]> {
    return this.http.get<LandingPageContent[]>(`${this.apiUrl}/api/landingpagecontent`);
  }

  getBySection(section: string): Observable<LandingPageContent[]> {
    return this.http.get<LandingPageContent[]>(`${this.apiUrl}/api/landingpagecontent/section/${section}`);
  }

  getById(id: string): Observable<LandingPageContent> {
    return this.http.get<LandingPageContent>(`${this.apiUrl}/api/landingpagecontent/${id}`);
  }

  create(request: LandingPageContentRequest): Observable<LandingPageContent> {
    return this.http.post<LandingPageContent>(`${this.apiUrl}/api/landingpagecontent`, request);
  }

  update(id: string, request: LandingPageContentRequest): Observable<LandingPageContent> {
    return this.http.put<LandingPageContent>(`${this.apiUrl}/api/landingpagecontent/${id}`, request);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/api/landingpagecontent/${id}`);
  }
}
