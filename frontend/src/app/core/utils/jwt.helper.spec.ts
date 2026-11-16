import { resolveTokenRole } from './jwt.helper';

const ROLE_CLAIM = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';

function encodeToken(payload: unknown): string {
  return `eyJhbGciOiJIUzI1NiJ9.${btoa(JSON.stringify(payload))}.signature`;
}

describe('resolveTokenRole', () => {
  it('resolves a customer token', () => {
    expect(resolveTokenRole(encodeToken({ role: 'Customer' }))).toBe('Customer');
  });

  it('resolves a seller token', () => {
    expect(resolveTokenRole(encodeToken({ role: 'Seller' }))).toBe('Seller');
  });

  it('resolves an admin token', () => {
    expect(resolveTokenRole(encodeToken({ role: 'Admin' }))).toBe('Admin');
  });

  it('reads the canonical Microsoft role claim', () => {
    expect(resolveTokenRole(encodeToken({ [ROLE_CLAIM]: 'Seller' }))).toBe('Seller');
  });

  it('gives the administrative role precedence over any other role in the token', () => {
    expect(resolveTokenRole(encodeToken({ role: ['Customer', 'Admin'] }))).toBe('Admin');
    expect(resolveTokenRole(encodeToken({ role: ['Seller', 'Admin'] }))).toBe('Admin');
  });

  it('gives the seller role precedence over customer in a multi-role token', () => {
    expect(resolveTokenRole(encodeToken({ role: ['Customer', 'Seller'] }))).toBe('Seller');
  });

  it('returns null for unknown roles, invalid tokens and empty values', () => {
    expect(resolveTokenRole(encodeToken({ role: 'Driver' }))).toBeNull();
    expect(resolveTokenRole('not-a-jwt')).toBeNull();
    expect(resolveTokenRole('')).toBeNull();
  });
});
