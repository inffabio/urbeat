import { UpsertCustomerAddress } from '../models/address.model';

export type CustomerProfileField =
  | 'fullName'
  | 'phone'
  | 'email'
  | 'cep'
  | 'city'
  | 'state'
  | 'neighborhood'
  | 'street'
  | 'number';

export interface CustomerProfileFields {
  fullName: string;
  phone: string;
  email: string;
  cep: string;
  street: string;
  number: string;
  complement: string;
  neighborhood: string;
  city: string;
  state: string;
}

export interface CustomerProfileValue {
  profile: {
    fullName: string;
    email: string;
    phoneNumber: string;
  };
  address: UpsertCustomerAddress;
}

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export function onlyDigits(value: string): string {
  return value.replace(/\D/g, '');
}

export function isValidEmail(value: string): boolean {
  return EMAIL_PATTERN.test(value.trim());
}

export function isValidProfile(fields: Pick<CustomerProfileFields, 'fullName' | 'phone' | 'email'>): boolean {
  return (
    fields.fullName.trim().length >= 3 &&
    onlyDigits(fields.phone).length >= 10 &&
    isValidEmail(fields.email)
  );
}

export function isValidAddress(
  fields: Pick<CustomerProfileFields, 'cep' | 'street' | 'number' | 'city' | 'neighborhood' | 'state'>,
): boolean {
  return (
    onlyDigits(fields.cep).length === 8 &&
    fields.street.trim().length > 0 &&
    fields.number.trim().length > 0 &&
    fields.city.trim().length > 0 &&
    fields.neighborhood.trim().length > 0 &&
    fields.state.trim().length === 2
  );
}

export function customerProfileFieldError(
  field: CustomerProfileField,
  fields: CustomerProfileFields,
  options: { cepError?: boolean } = {},
): string {
  switch (field) {
    case 'fullName':
      return fields.fullName.trim().length >= 3 ? '' : 'Informe seu nome completo.';
    case 'phone':
      return onlyDigits(fields.phone).length >= 10 ? '' : 'Informe um telefone com DDD.';
    case 'email':
      return isValidEmail(fields.email) ? '' : 'Informe um e-mail válido.';
    case 'cep':
      if (options.cepError) return 'CEP não encontrado. Preencha o endereço manualmente.';
      return onlyDigits(fields.cep).length === 8 ? '' : 'Informe um CEP válido com 8 dígitos.';
    case 'city':
      return fields.city.trim().length > 0 ? '' : 'Informe a cidade.';
    case 'state':
      return fields.state.trim().length === 2 ? '' : 'Informe a UF com 2 letras.';
    case 'neighborhood':
      return fields.neighborhood.trim().length > 0 ? '' : 'Informe o bairro.';
    case 'street':
      return fields.street.trim().length > 0 ? '' : 'Informe a rua.';
    case 'number':
      return fields.number.trim().length > 0 ? '' : 'Informe o número.';
  }
}

export function formatPhoneInput(value: string): string {
  const digits = onlyDigits(value).slice(0, 11);
  let formatted = digits;
  if (digits.length > 2) formatted = `(${digits.slice(0, 2)}) ${digits.slice(2)}`;
  if (digits.length > 6) formatted = `(${digits.slice(0, 2)}) ${digits.slice(2, 7)}-${digits.slice(7)}`;
  return formatted;
}

export function formatCepInput(value: string): string {
  const digits = onlyDigits(value).slice(0, 8);
  return digits.length > 5 ? `${digits.slice(0, 5)}-${digits.slice(5)}` : digits;
}

export function profileSnapshot(fields: Pick<CustomerProfileFields, 'fullName' | 'email' | 'phone'>): string {
  return JSON.stringify({
    fullName: fields.fullName.trim(),
    email: fields.email.trim().toLowerCase(),
    phone: onlyDigits(fields.phone),
  });
}

export function addressSnapshot(
  fields: Pick<
    CustomerProfileFields,
    'cep' | 'street' | 'number' | 'complement' | 'neighborhood' | 'city' | 'state'
  >,
): string {
  return JSON.stringify({
    cep: onlyDigits(fields.cep),
    street: fields.street.trim(),
    number: fields.number.trim(),
    complement: fields.complement.trim(),
    neighborhood: fields.neighborhood.trim(),
    city: fields.city.trim(),
    state: fields.state.trim().toUpperCase(),
  });
}

export function buildCustomerProfileValue(fields: CustomerProfileFields): CustomerProfileValue {
  return {
    profile: {
      fullName: fields.fullName.trim(),
      email: fields.email.trim(),
      phoneNumber: onlyDigits(fields.phone),
    },
    address: {
      cep: onlyDigits(fields.cep),
      street: fields.street.trim(),
      number: fields.number.trim(),
      complement: fields.complement.trim() || undefined,
      neighborhood: fields.neighborhood.trim(),
      city: fields.city.trim(),
      state: fields.state.trim().toUpperCase(),
      isPrimary: true,
    },
  };
}
