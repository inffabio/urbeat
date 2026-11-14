export interface CepLookupResponse {
  cep: string;
  street: string;
  complement?: string;
  neighborhood: string;
  city: string;
  state: string;
  /** IBGE municipality code, when the answer comes from the local geographic base. */
  ibgeCode?: string | null;
  /** Aggregated logradouro coordinate (never an individual household point). */
  latitude?: number | null;
  longitude?: number | null;
  /** `local` for the CNEFE-derived base, `viacep` for the read-only fallback. */
  source?: 'local' | 'viacep';
  /** Active CNEFE dataset version; null when only ViaCEP answered. */
  datasetVersion?: string | null;
  /** True when the CEP covers more than one logradouro. */
  multipleMatches?: boolean;
  matches?: CepLookupMatch[];
}

export interface CepLookupMatch {
  street: string;
  neighborhood: string;
  city: string;
  state: string;
  ibgeCode?: string | null;
  latitude?: number | null;
  longitude?: number | null;
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
