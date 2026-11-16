/**
 * Decodifica um token JWT de forma segura (sem validar assinatura, apenas lendo o payload).
 * Retorna o payload ou null se o token for inválido.
 */
export function decodeJwt(token: string): any | null {
  try {
    const base64Url = token.split('.')[1];
    const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
    const jsonPayload = decodeURIComponent(
      atob(base64)
        .split('')
        .map((c) => '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2))
        .join('')
    );
    return JSON.parse(jsonPayload);
  } catch (error) {
    console.error('Erro ao decodificar JWT:', error);
    return null;
  }
}

/**
 * Verifica se o token JWT contém a role "Admin".
 */
export function isAdmin(token: string): boolean {
  const payload = decodeJwt(token);
  if (!payload) return false;
  
  // O ASP.NET Core Identity geralmente coloca roles em "role" (string) ou "http://schemas.microsoft.com/ws/2008/06/identity/claims/role" (array)
  const role = payload.role || payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'];
  
  if (Array.isArray(role)) {
    return role.includes('Admin');
  }
  
  return role === 'Admin';
}

export function hasRole(token: string, expectedRole: string): boolean {
  const payload = decodeJwt(token);
  if (!payload) return false;

  const role = payload.role || payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'];

  if (Array.isArray(role)) {
    return role.includes(expectedRole);
  }

  return role === expectedRole;
}

export function isSeller(token: string): boolean {
  return hasRole(token, 'Seller');
}

export function isCustomer(token: string): boolean {
  return hasRole(token, 'Customer');
}

/** Identity carried by a token, already resolved to the most privileged role. */
export type TokenRole = 'Admin' | 'Seller' | 'Customer';

const ROLE_CLAIM = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';

/**
 * Resolves the identity a token may act as, using the most privileged role it carries.
 *
 * Admin takes precedence over Seller and Seller over Customer: an administrative or seller
 * credential must never be mistaken for a customer session by a public storefront surface, even
 * when the account also owns the Customer role. The backend is still authoritative for every
 * authorization decision; this helper only decides which credential the client may use.
 */
export function resolveTokenRole(token: string): TokenRole | null {
  const payload = decodeJwt(token);
  if (!payload) return null;

  const claim = payload.role ?? payload[ROLE_CLAIM];
  const roles: unknown[] = Array.isArray(claim) ? claim : [claim];

  if (roles.includes('Admin')) return 'Admin';
  if (roles.includes('Seller')) return 'Seller';
  if (roles.includes('Customer')) return 'Customer';
  return null;
}
