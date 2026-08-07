export interface CepLookupResponse {
  cep: string;
  street: string;
  complement?: string;
  neighborhood: string;
  city: string;
  state: string;
}

export interface CustomerAddress {
  id: string;
  cep: string;
  street: string;
  number: string;
  neighborhood: string;
  city: string;
  state: string;
  complement?: string;
  reference?: string;
  isPrimary: boolean;
  latitude?: number | null;
  longitude?: number | null;
}

export interface UpsertCustomerAddress {
  cep: string;
  street: string;
  number: string;
  neighborhood: string;
  city: string;
  state: string;
  complement?: string;
  reference?: string;
  isPrimary?: boolean;
  latitude?: number | null;
  longitude?: number | null;
}
