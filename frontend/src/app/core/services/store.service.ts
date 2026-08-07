import { Injectable, inject } from '@angular/core';
import { StorePublishSummary } from '../../shared/models/store.model';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import {
  StorePublicDetails,
  Review,
  CreateStoreRequest,
  StoreResponse,
  UpdateStoreAddressRequest,
  StoreAddressResponse,
  UpdateDeliveryConfigRequest,
  UpsertStoreBusinessHoursRequest,
  StoreBusinessHoursResponse,
  CuisineTypeDto,
  DeliveryTimeOption,
  DeliveryNeighborhood,
  NeighborhoodSearchResult,
  NeighborhoodMapResponse,
  CityDto,
} from '../../shared/models/store.model';
import { Product, ProductCategory, StoreAdditional, StoreAdditionalGroup, StoreAdditionalRequest } from '../../shared/models/product.model';

@Injectable({ providedIn: 'root' })
export class StoreService {
  private readonly api = inject(ApiService);

  getStoreByPath(storePath: string): Observable<StorePublicDetails> {
    return this.api.get<StorePublicDetails>(`/api/public/stores/by-path/${storePath}`);
  }

  getStoreById(storeId: string): Observable<StorePublicDetails> {
    return this.api.get<StorePublicDetails>(`/api/public/stores/${storeId}`);
  }

  getReviews(storeId: string): Observable<Review[]> {
    return this.api.get<Review[]>(`/api/public/stores/${storeId}/reviews`);
  }

  getSellerReviews(): Observable<Review[]> {
    return this.api.get<Review[]>('/api/reviews/store');
  }

  getCuisineTypes(): Observable<CuisineTypeDto[]> {
    return this.api.get<CuisineTypeDto[]>('/api/stores/cuisine-types');
  }

  createCuisineType(name: string): Observable<CuisineTypeDto> {
    return this.api.post<CuisineTypeDto>('/api/stores/cuisine-types', { name });
  }

  getDeliveryTimeOptions(storeId: string): Observable<DeliveryTimeOption[]> {
    return this.api.get<DeliveryTimeOption[]>(`/api/stores/delivery-times?storeId=${storeId}`);
  }

  createDeliveryTime(storeId: string, minTimeMinutes: number, maxTimeMinutes: number): Observable<DeliveryTimeOption> {
    return this.api.post<DeliveryTimeOption>('/api/stores/delivery-times', { storeId, minTimeMinutes, maxTimeMinutes });
  }

  getDeliveryNeighborhoods(city: string): Observable<DeliveryNeighborhood[]> {
    return this.api.get<DeliveryNeighborhood[]>(`/api/stores/delivery-neighborhoods?city=${encodeURIComponent(city)}`);
  }

  getDeliveryNeighborhoodsByStore(storeId: string): Observable<DeliveryNeighborhood[]> {
    return this.api.get<DeliveryNeighborhood[]>(`/api/stores/delivery-neighborhoods-by-store?storeId=${encodeURIComponent(storeId)}`);
  }

  createDeliveryNeighborhood(neighborhood: string, city: string): Observable<DeliveryNeighborhood> {
    return this.api.post<DeliveryNeighborhood>('/api/stores/delivery-neighborhoods', { neighborhood, city });
  }

  searchNeighborhoods(cityId: string, storeId?: string, search?: string, activeOnly: boolean = true): Observable<NeighborhoodSearchResult[]> {
    let params = `cityId=${encodeURIComponent(cityId)}&activeOnly=${activeOnly}`;
    if (storeId) params += `&storeId=${encodeURIComponent(storeId)}`;
    if (search) params += `&search=${encodeURIComponent(search)}`;
    return this.api.get<NeighborhoodSearchResult[]>(`/api/neighborhoods/cities/${cityId}/search?${params}`);
  }

  getNeighborhoodsMap(cityId: string, storeId?: string): Observable<NeighborhoodMapResponse> {
    let params = '';
    if (storeId) params = `?storeId=${encodeURIComponent(storeId)}`;
    return this.api.get<NeighborhoodMapResponse>(`/api/neighborhoods/cities/${cityId}/map${params}`);
  }

  getCities(): Observable<CityDto[]> {
    return this.api.get<CityDto[]>('/api/neighborhoods/cities');
  }

  getMyStore(): Observable<StoreResponse> {
    return this.api.get<StoreResponse>('/api/stores/my-store');
  }

  createStore(req: CreateStoreRequest): Observable<StoreResponse> {
    return this.api.post<StoreResponse>('/api/stores', req);
  }

  updateStore(storeId: string, req: CreateStoreRequest): Observable<StoreResponse> {
    return this.api.put<StoreResponse>(`/api/stores/${storeId}`, req);
  }

  getStoreAddress(storeId: string): Observable<StoreAddressResponse> {
    return this.api.get<StoreAddressResponse>(`/api/stores/${storeId}/address`);
  }

  upsertStoreAddress(storeId: string, req: UpdateStoreAddressRequest): Observable<StoreAddressResponse> {
    return this.api.put<StoreAddressResponse>(`/api/stores/${storeId}/address`, req);
  }

  updateDeliveryConfig(storeId: string, req: UpdateDeliveryConfigRequest): Observable<StoreResponse> {
    return this.api.patch<StoreResponse>(`/api/stores/${storeId}/delivery-config`, req);
  }

  getStoreBusinessHours(storeId: string): Observable<StoreBusinessHoursResponse> {
    return this.api.get<StoreBusinessHoursResponse>(`/api/stores/${storeId}/business-hours`);
  }

  upsertStoreBusinessHours(storeId: string, req: UpsertStoreBusinessHoursRequest): Observable<StoreBusinessHoursResponse> {
    return this.api.put<StoreBusinessHoursResponse>(`/api/stores/${storeId}/business-hours`, req);
  }

  getStorePublishSummary(storeId: string): Observable<StorePublishSummary> {
    return this.api.get<StorePublishSummary>(`/api/stores/${storeId}/publish/summary`);
  }

  publishStore(storeId: string): Observable<void> {
    return this.api.post<void>(`/api/stores/${storeId}/publish`, {});
  }

  uploadImage(file: File, type: string = 'store-media'): Observable<{ url: string }> {
    const formData = new FormData();
    formData.append('file', file);
    return this.api.post<{ url: string }>(`/api/stores/upload-image?type=${type}`, formData);
  }

  getStoreProducts(storeId: string): Observable<Product[]> {
    return this.api.get<Product[]>(`/api/stores/${storeId}/products`);
  }

  getStoreCategories(storeId: string): Observable<ProductCategory[]> {
    return this.api.get<ProductCategory[]>(`/api/stores/${storeId}/categories`);
  }

  createStoreCategory(storeId: string, body: { name: string; description?: string; displayOrder: number; isActive: boolean; isFeatured: boolean }): Observable<ProductCategory> {
    return this.api.post<ProductCategory>(`/api/stores/${storeId}/categories`, body);
  }

  updateStoreCategory(storeId: string, categoryId: string, body: { name: string; description?: string; displayOrder: number; isActive: boolean; isFeatured: boolean }): Observable<ProductCategory> {
    return this.api.put<ProductCategory>(`/api/stores/${storeId}/categories/${categoryId}`, body);
  }

  deleteStoreCategory(storeId: string, categoryId: string, reassignCategoryId?: string): Observable<void> {
    const query = reassignCategoryId ? `?reassignCategoryId=${encodeURIComponent(reassignCategoryId)}` : '';
    return this.api.delete<void>(`/api/stores/${storeId}/categories/${categoryId}${query}`);
  }

  reorderStoreCategories(storeId: string, items: { id: string; displayOrder: number }[]): Observable<void> {
    return this.api.put<void>(`/api/stores/${storeId}/categories/reorder`, items);
  }

  createProduct(storeId: string, body: any): Observable<Product> {
    return this.api.post<Product>(`/api/stores/${storeId}/products`, body);
  }

  updateProduct(storeId: string, productId: string, body: any): Observable<Product> {
    return this.api.put<Product>(`/api/stores/${storeId}/products/${productId}`, body);
  }

  deleteProduct(storeId: string, productId: string): Observable<void> {
    return this.api.delete<void>(`/api/stores/${storeId}/products/${productId}`);
  }

  getStoreAdditionals(storeId: string): Observable<StoreAdditional[]> {
    return this.api.get<StoreAdditional[]>(`/api/stores/${storeId}/additionals`);
  }

  getStoreAdditionalGroups(storeId: string): Observable<StoreAdditionalGroup[]> {
    return this.api.get<StoreAdditionalGroup[]>(`/api/stores/${storeId}/additionals/groups`);
  }

  createStoreAdditional(storeId: string, body: StoreAdditionalRequest): Observable<StoreAdditional> {
    return this.api.post<StoreAdditional>(`/api/stores/${storeId}/additionals`, body);
  }

  updateStoreAdditional(storeId: string, additionalId: string, body: StoreAdditionalRequest): Observable<StoreAdditional> {
    return this.api.put<StoreAdditional>(`/api/stores/${storeId}/additionals/${additionalId}`, body);
  }

  toggleStoreAdditional(storeId: string, additionalId: string, isActive: boolean): Observable<StoreAdditional> {
    return this.api.patch<StoreAdditional>(`/api/stores/${storeId}/additionals/${additionalId}/status`, { isActive });
  }

  deleteStoreAdditional(storeId: string, additionalId: string): Observable<void> {
    return this.api.delete<void>(`/api/stores/${storeId}/additionals/${additionalId}`);
  }
}
