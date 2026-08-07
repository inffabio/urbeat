import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { Product, ProductCategory } from '../../shared/models/product.model';

@Injectable({ providedIn: 'root' })
export class CatalogService {
  private readonly api = inject(ApiService);

  getCategories(storeId: string): Observable<ProductCategory[]> {
    return this.api.get<ProductCategory[]>(`/api/public/stores/${storeId}/catalog/categories`);
  }

  getProducts(storeId: string): Observable<Product[]> {
    return this.api.get<Product[]>(`/api/public/stores/${storeId}/catalog/products`);
  }

  getFeaturedProducts(storeId: string): Observable<Product[]> {
    return this.api.get<Product[]>(`/api/public/stores/${storeId}/catalog/products/featured`);
  }
}
