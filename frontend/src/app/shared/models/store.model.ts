export interface StoreAddress {
  storeId?: string;
  street: string;
  number: string;
  complement?: string;
  neighborhood: string;
  city: string;
  state: string;
  zipCode: string;
  reference?: string;
  latitude?: number;
  longitude?: number;
}

export interface BusinessHourShift {
  id?: string;
  startTime: string;
  endTime: string;
}

export interface BusinessHour {
  dayOfWeek: number; // 0=Sunday
  isOpen: boolean;
  shifts: BusinessHourShift[];
}

export interface UpsertStoreBusinessHoursRequest {
  items: BusinessHour[];
}

export interface StoreBusinessHoursResponse {
  storeId?: string;
  items: BusinessHour[];
}

export interface StorePublicDetails {
  id: string;
  name: string;
  slug: string;
  phoneNumber: string;
  description: string;
  cuisineType: string;
  bannerUrl: string;
  logoUrl: string;
  isOpen: boolean;
  isOpenNow: boolean;
  nextOpeningAt?: string;
  nextStatusChangeAt?: string;
  closedMessage?: string;
  supportsDelivery: boolean;
  supportsPickup: boolean;
  deliveryFee: number;
  minimumOrderValue: number;
  freeShippingThreshold?: number;
  freeShippingToday?: boolean;
  initialMinute?: number;
  finalMinute?: number;
  address?: StoreAddress;
  businessHours?: BusinessHour[];
  averageRating: number;
  totalReviews: number;
}

export interface Review {
  id: string;
  orderId?: string;
  customerName: string;
  rating: number;
  comment: string;
  createdAtUtc: string;
}

export interface CreateStoreRequest {
  name: string;
  slug: string;
  phoneNumber: string;
  document?: string;
  pixKey?: string;
  instagramUrl?: string;
  facebookUrl?: string;
  tikTokUrl?: string;
  websiteUrl?: string;
  description?: string;
  cuisineType: string;
  bannerUrl?: string | null;
  logoUrl?: string | null;
  supportsDelivery: boolean;
  supportsPickup: boolean;
  initialMinute?: number;
  finalMinute?: number;
  maxDeliveryRadiusKm: number;
}

export interface UpdateStoreRequest extends CreateStoreRequest {}

export interface StoreResponse {
  id: string;
  ownerUserId: string;
  name: string;
  slug: string; // Canonical identifier for routing (kebab-case)
  phoneNumber: string;
  document?: string;
  pixKey?: string;
  instagramUrl?: string;
  facebookUrl?: string;
  tikTokUrl?: string;
  websiteUrl?: string;
  description: string;
  cuisineType: string;
  bannerUrl?: string;
  logoUrl?: string;
  isOpen: boolean;
  isSubscriptionBlocked: boolean;
  supportsDelivery: boolean;
  supportsPickup: boolean;
  initialMinute?: number;
  finalMinute?: number;
  maxDeliveryRadiusKm?: number;
  lastImportedRadiusKm?: number;
  minimumOrderValue: number;
  freeShippingThreshold?: number;
  freeShippingToday?: boolean;
  deliveryAreas: StoreDeliveryArea[];
  averageRating: number;
  totalReviews: number;
}

export interface UpdateStoreAddressRequest {
  street: string;
  number: string;
  complement?: string;
  neighborhood: string;
  city: string;
  state: string;
  zipCode: string;
  reference?: string;
}

export interface StoreAddressResponse extends UpdateStoreAddressRequest {
  storeId: string;
  latitude?: number;
  longitude?: number;
}

export interface StoreDeliveryArea {
  id?: string;
  neighborhood: string;
  deliveryFee: number;
  minimumOrderValue?: number;
  freeShippingThreshold?: number | null;
  isActive?: boolean;
  notes?: string;
}

export interface UpdateDeliveryConfigRequest {
  deliveryFee: number;
  minimumOrderValue: number;
  freeShippingThreshold?: number;
  freeShippingToday?: boolean;
  deliveryAreas: StoreDeliveryArea[];
}

export interface CuisineTypeDto {
  id: string;
  name: string;
}

export interface DeliveryTimeOption {
  id: string;
  minTimeMinutes: number;
  maxTimeMinutes: number;
  formattedTime: string;
}

export interface DeliveryNeighborhood {
  id: string;
  neighborhood: string;
  city: string;
  latitude?: number;
  longitude?: number;
}

export interface NeighborhoodSearchResult {
  id: string;
  name: string;
  latitude?: number;
  longitude?: number;
  isActive: boolean;
  freightRate?: {
    id: string;
    rate: number;
  };
}

export interface NeighborhoodMapResponse {
  city: {
    id: string;
    name: string;
    uf: string;
  };
  items: NeighborhoodMapItem[];
  withoutCoordinates: { neighborhoodId: string; name: string }[];
}

export interface NeighborhoodMapItem {
  neighborhoodId: string;
  name: string;
  latitude?: number;
  longitude?: number;
  rate: number;
  active: boolean;
}

export interface CityDto {
  id: string;
  name: string;
  uf: string;
}



export interface StorePublishSummary {
  storeDetails: {
    name: string;
    cuisineType: string;
    phoneNumber: string;
    description: string;
    address: string;
    city: string;
    logoUrl: string | null;
    bannerUrl: string | null;
  };
  businessHours: { dayOfWeek: number; opensAt: string; closesAt: string }[];
  deliveryFees: { baseFee: number; minimumOrderValue: number; estimatedTimeMin: string; freeShippingThreshold?: number | null };
  deliveryAreas: { name: string; deliveryFee: number }[];
  rules: {
    detailsOk: boolean;
    hoursOk: boolean;
    deliveryOk: boolean;
    productsOk: boolean;
  };
  productsStats?: {
    total: number;
    byCategory: { name: string; count: number }[];
  };
  productsPreview?: {
    id: string;
    name: string;
    price: number;
    imageUrl: string | null;
    categoryName: string;
    isActive: boolean;
  }[];
  completionPercentage: number;
  canPublish: boolean;
}
