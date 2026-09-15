import {
  addressSnapshot,
  buildCustomerProfileValue,
  customerProfileFieldError,
  formatCepInput,
  formatPhoneInput,
  isValidAddress,
  isValidEmail,
  isValidProfile,
  profileSnapshot,
  type CustomerProfileFields,
} from './customer-profile.rules';

function fields(overrides: Partial<CustomerProfileFields> = {}): CustomerProfileFields {
  return {
    fullName: '',
    phone: '',
    email: '',
    cep: '',
    street: '',
    number: '',
    complement: '',
    neighborhood: '',
    city: '',
    state: '',
    ...overrides,
  };
}

describe('customer-profile.rules', () => {
  it('validates emails consistently for both flows', () => {
    expect(isValidEmail('cliente@urbeat.com')).toBe(true);
    expect(isValidEmail('  cliente@urbeat.com  ')).toBe(true);
    expect(isValidEmail('cliente@urbeat')).toBe(false);
    expect(isValidEmail('')).toBe(false);
  });

  it('validates the profile section', () => {
    expect(isValidProfile(fields({ fullName: 'Ana', phone: '(21) 99999-9999', email: 'ana@x.com' }))).toBe(true);
    expect(isValidProfile(fields({ fullName: 'An', phone: '(21) 99999-9999', email: 'ana@x.com' }))).toBe(false);
    expect(isValidProfile(fields({ fullName: 'Ana', phone: '123', email: 'ana@x.com' }))).toBe(false);
    expect(isValidProfile(fields({ fullName: 'Ana', phone: '(21) 99999-9999', email: 'bad' }))).toBe(false);
  });

  it('validates the address section', () => {
    const valid = fields({
      cep: '20010-000',
      street: 'Rua A',
      number: '10',
      neighborhood: 'Centro',
      city: 'Rio de Janeiro',
      state: 'RJ',
    });
    expect(isValidAddress(valid)).toBe(true);
    expect(isValidAddress({ ...valid, state: 'R' })).toBe(false);
    expect(isValidAddress({ ...valid, cep: '2001' })).toBe(false);
    expect(isValidAddress({ ...valid, number: '' })).toBe(false);
  });

  it('returns the same field messages used by both flows', () => {
    expect(customerProfileFieldError('fullName', fields())).toBe('Informe seu nome completo.');
    expect(customerProfileFieldError('phone', fields())).toBe('Informe um telefone com DDD.');
    expect(customerProfileFieldError('email', fields())).toBe('Informe um e-mail válido.');
    expect(customerProfileFieldError('cep', fields())).toBe('Informe um CEP válido com 8 dígitos.');
    expect(customerProfileFieldError('city', fields())).toBe('Informe a cidade.');
    expect(customerProfileFieldError('state', fields())).toBe('Informe a UF com 2 letras.');
    expect(customerProfileFieldError('neighborhood', fields())).toBe('Informe o bairro.');
    expect(customerProfileFieldError('street', fields())).toBe('Informe a rua.');
    expect(customerProfileFieldError('number', fields())).toBe('Informe o número.');
    expect(customerProfileFieldError('cep', fields(), { cepError: true })).toBe(
      'CEP não encontrado. Preencha o endereço manualmente.',
    );
    expect(customerProfileFieldError('city', fields({ city: 'Rio' }))).toBe('');
  });

  it('formats phone and CEP inputs the same way everywhere', () => {
    expect(formatPhoneInput('21999999999')).toBe('(21) 99999-9999');
    expect(formatPhoneInput('219999')).toBe('(21) 9999');
    expect(formatPhoneInput('219999999999')).toBe('(21) 99999-9999');
    expect(formatCepInput('20010000')).toBe('20010-000');
    expect(formatCepInput('20010')).toBe('20010');
  });

  it('builds normalized snapshots for dirty comparison', () => {
    const value = fields({
      fullName: '  Ana  ',
      phone: '(21) 99999-9999',
      email: '  ANA@X.COM ',
      cep: '20010-000',
      street: ' Rua A ',
      number: ' 10 ',
      neighborhood: ' Centro ',
      city: ' Rio de Janeiro ',
      state: 'rj',
    });

    expect(profileSnapshot(value)).toBe(
      JSON.stringify({ fullName: 'Ana', email: 'ana@x.com', phone: '21999999999' }),
    );
    expect(addressSnapshot(value)).toBe(
      JSON.stringify({
        cep: '20010000',
        street: 'Rua A',
        number: '10',
        complement: '',
        neighborhood: 'Centro',
        city: 'Rio de Janeiro',
        state: 'RJ',
      }),
    );
  });

  it('builds the submit value shared by the profile form and checkout', () => {
    const value = buildCustomerProfileValue(fields({
      fullName: ' Ana ',
      phone: '(21) 99999-9999',
      email: ' ana@x.com ',
      cep: '20010-000',
      street: ' Rua A ',
      number: ' 10 ',
      complement: ' ',
      neighborhood: ' Centro ',
      city: ' Rio ',
      state: 'rj',
    }));

    expect(value.profile).toEqual({ fullName: 'Ana', email: 'ana@x.com', phoneNumber: '21999999999' });
    expect(value.address).toEqual({
      cep: '20010000',
      street: 'Rua A',
      number: '10',
      complement: undefined,
      neighborhood: 'Centro',
      city: 'Rio',
      state: 'RJ',
      isPrimary: true,
    });
  });
});
