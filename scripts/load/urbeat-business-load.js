// Deep, controlled k6 business-flow scenario for an explicitly authorized urbeat
// TEST environment.
//
// This file is intentionally fail-closed:
// - It refuses to start unless BUSINESS_TEST_AUTHORIZED=true and
//   ALLOW_SYNTHETIC_MUTATIONS=true are explicitly set by the operator.
// - It refuses production hostnames, including urbeat.com.br and its subdomains.
// - It never prints access tokens, refresh cookies, passwords, or pool contents.
// - It never calls online payment, webhook, e-mail resend, WhatsApp, or upload endpoints.
//
// Synthetic account strategy (no confirmation e-mail):
//   POST /api/checkout/customer-session creates a confirmed Customer identity and an
//   access token in one anonymous call. That Customer is then promoted to Seller with
//   POST /api/auth/register/seller using the customer access token as ownership
//   proof. Because the identity is already confirmed, the promotion does not enqueue
//   an e-mail confirmation job. The script asserts emailConfirmationPending === false
//   and fails closed if the backend ever changes that contract.
//
// Payment strategy:
//   Orders are confirmed with CashOnDelivery (pay at handover). No /api/payments call
//   is made, so no gateway, mock worker, or online-payment outbox path is exercised.
//
// Order side effects:
//   /api/checkout/confirm writes an order, order items, status history, audit rows,
//   durable notifications, and an OrderCreated outbox message. The outbox handler
//   pushes SignalR and logs print delivery as best-effort; it does not send WhatsApp.
//
// Cleanup:
//   There is no safe public API to delete stores or identities. The script never
//   deletes anything. Every synthetic name/e-mail/slug carries RUN_ID so an operator
//   can inventory and remove data manually. The PowerShell wrapper writes a read-only
//   cleanup plan; it never executes it.

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';

const LOAD_TAG = '[urbeat-business-load]';
const DEFAULT_BASE_URL = 'http://localhost:5000';
const DEFAULT_VUS = 5;
const MAX_VUS = 300;
const DEFAULT_DURATION = '30s';
const DEFAULT_RAMP_UP_DURATION = '30s';
const DEFAULT_SELLER_MAX_DURATION = '10m';
const DEFAULT_REQUEST_TIMEOUT = '15s';
const DEFAULT_SLEEP_SECONDS = 1;
const DEFAULT_EMAIL_DOMAIN = 'loadtest.invalid';
const DEFAULT_FULFILLMENT = 'pickup';

const PAYMENT_METHOD_CASH_ON_DELIVERY = 3;
const FULFILLMENT_TYPE_DELIVERY = 1;
const FULFILLMENT_TYPE_PICKUP = 2;
const ORDER_STATUS_RECEIVED = 3;
const ORDER_STATUS_PREPARING = 4;

// Custom metrics -----------------------------------------------------------------

const storesCreated = new Counter('urbeat_stores_created');
const productsCreated = new Counter('urbeat_products_created');
const storesPublished = new Counter('urbeat_stores_published');
const ordersCreated = new Counter('urbeat_orders_created');
const mockPayments = new Counter('urbeat_mock_payments');
const stepFailures = new Counter('urbeat_step_failures');
const blocked = new Counter('urbeat_blocked');
const stageDuration = new Trend('urbeat_stage_duration');
const onboardingSuccess = new Rate('urbeat_onboarding_success');
const orderSuccess = new Rate('urbeat_order_success');
const statusSmokeSuccess = new Rate('urbeat_status_smoke_success');

// Helpers ------------------------------------------------------------------------

function fail(message) {
  throw new Error(LOAD_TAG + ' ' + message);
}

function env(name) {
  const value = __ENV[name];
  if (value === undefined || value === null) return '';
  return String(value).trim();
}

function readPositiveInt(name, fallback, max) {
  const raw = env(name);
  if (raw === '') return fallback;
  const value = Number(raw);
  if (!Number.isInteger(value) || value < 1) {
    fail(name + ' must be a positive integer. Received: ' + raw);
  }
  if (max && value > max) {
    fail(name + ' must be <= ' + max + '. Received: ' + raw);
  }
  return value;
}

function readNonNegativeInt(name, fallback) {
  const raw = env(name);
  if (raw === '') return fallback;
  const value = Number(raw);
  if (!Number.isInteger(value) || value < 0) {
    fail(name + ' must be a non-negative integer. Received: ' + raw);
  }
  return value;
}

function readOptionalNonNegativeInt(name) {
  const raw = env(name);
  if (raw === '') return null;
  const value = Number(raw);
  if (!Number.isInteger(value) || value < 0) {
    fail(name + ' must be a non-negative integer. Received: ' + raw);
  }
  return value;
}

function readDuration(name, fallback) {
  const raw = env(name);
  if (raw === '') return fallback;
  if (!/^\d+(s|m|h)$/.test(raw)) {
    fail(name + ' must use the k6 duration format, e.g. 30s, 2m, or 1h. Received: ' + raw);
  }
  return raw;
}

function readBoolean(name, fallback) {
  const raw = env(name).toLowerCase();
  if (raw === '') return fallback;
  if (raw === 'true') return true;
  if (raw === 'false') return false;
  fail(name + ' must be true or false. Received: ' + raw);
}

function readChoice(name, allowed, fallback) {
  const raw = env(name).toLowerCase();
  if (raw === '') return fallback;
  if (allowed.indexOf(raw) < 0) {
    fail(name + ' must be one of: ' + allowed.join(', ') + '. Received: ' + raw);
  }
  return raw;
}

function normalizeBaseUrl(raw) {
  const value = String(raw === undefined || raw === null ? '' : raw).trim().replace(/\/+$/, '');
  const match = /^(https?):\/\/([^/\s?#]+)(\/.*)?$/i.exec(value);
  if (!match) {
    fail('BASE_URL must be an absolute http(s) origin. Received: ' + raw);
  }
  if (match[3] !== undefined && match[3] !== '') {
    fail('BASE_URL must be an origin without a path. Received: ' + raw);
  }
  if (value.indexOf('?') >= 0 || value.indexOf('#') >= 0) {
    fail('BASE_URL must not include a query string or fragment. Received: ' + raw);
  }
  if (/^https?:\/\/[^/]*@/i.test(value)) {
    fail('BASE_URL must not include credentials.');
  }

  let hostPort = match[2].toLowerCase();
  if (hostPort.charAt(0) !== '[') {
    const colon = hostPort.indexOf(':');
    if (colon >= 0) hostPort = hostPort.substring(0, colon);
  }

  if (hostPort === 'urbeat.com.br'
    || hostPort === 'api.urbeat.com.br'
    || hostPort === 'www.urbeat.com.br'
    || (hostPort.length > 14 && hostPort.substring(hostPort.length - 14) === '.urbeat.com.br')) {
    fail('Refusing a production urbeat hostname: ' + hostPort);
  }

  return value;
}

function isGuid(value) {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
}

function pad8(value) {
  const text = String(value);
  return ('00000000' + text).substring(('00000000' + text).length - 8);
}

function truncate(text, limit) {
  if (!text) return '';
  const singleLine = String(text).replace(/\s+/g, ' ').trim();
  return singleLine.length > limit ? singleLine.substring(0, limit) + '...' : singleLine;
}

function safeJson(response) {
  try {
    return response.json();
  } catch (error) {
    return null;
  }
}

function authHeaders(token) {
  const headers = { 'Content-Type': 'application/json' };
  if (token) headers.Authorization = 'Bearer ' + token;
  return headers;
}

function jsonRequest(method, path, payload, token, stage, expectedStatuses) {
  const params = {
    headers: authHeaders(token),
    tags: { stage: stage },
    timeout: REQUEST_TIMEOUT,
    redirects: 0,
  };

  let response;
  if (method === 'GET') {
    response = http.get(BASE_URL + path, params);
  } else {
    const body = payload === null || payload === undefined ? null : JSON.stringify(payload);
    response = http.request(method, BASE_URL + path, body, params);
  }

  stageDuration.add(response.timings.duration, { stage: stage });

  const checkName = stage + ' expected status';
  const checkSet = {};
  checkSet[checkName] = function (r) {
    return expectedStatuses.indexOf(r.status) >= 0;
  };

  const ok = check(response, checkSet, { stage: stage });
  if (!ok) {
    stepFailures.add(1, { stage: stage });
    if (response.status === 429) {
      blocked.add(1, { reason: 'rate_limited', stage: stage });
    }
    console.warn(
      LOAD_TAG + ' ' + stage + ' failed status=' + response.status +
      ' body=' + truncate(response.body, 180)
    );
    return null;
  }

  return response;
}

// Configuration ------------------------------------------------------------------

const BASE_URL = normalizeBaseUrl(__ENV.BASE_URL || DEFAULT_BASE_URL);
const RUN_ID = env('RUN_ID');
const TOTAL_VUS = readPositiveInt('VUS', DEFAULT_VUS, MAX_VUS);
const DURATION = readDuration('DURATION', DEFAULT_DURATION);
const RAMP_UP_DURATION = readDuration('RAMP_UP_DURATION', DEFAULT_RAMP_UP_DURATION);
const SELLER_MAX_DURATION = readDuration('SELLER_MAX_DURATION', DEFAULT_SELLER_MAX_DURATION);
const REQUEST_TIMEOUT = readDuration('REQUEST_TIMEOUT', DEFAULT_REQUEST_TIMEOUT);
const SLEEP_SECONDS = readNonNegativeInt('SLEEP_SECONDS', DEFAULT_SLEEP_SECONDS);
const EMAIL_DOMAIN = (env('EMAIL_DOMAIN') || DEFAULT_EMAIL_DOMAIN).toLowerCase();
const FULFILLMENT = readChoice('FULFILLMENT_TYPE', ['pickup', 'delivery'], DEFAULT_FULFILLMENT);
const SELLER_STATUS_SMOKE = readBoolean('SELLER_STATUS_SMOKE', false);
const CUSTOMER_READBACK = readBoolean('CUSTOMER_READBACK', true);
const CUSTOMER_SESSION_PER_ITERATION = readBoolean('CUSTOMER_SESSION_PER_ITERATION', false);
const ALLOW_ACCOUNT_CREATION = env('ALLOW_SYNTHETIC_ACCOUNT_CREATION').toLowerCase() === 'true';
const ALLOW_NEIGHBORHOOD_IMPORT = env('ALLOW_NEIGHBORHOOD_IMPORT').toLowerCase() === 'true';
const OUTBOX_ADMIN_TOKEN = env('OUTBOX_ADMIN_TOKEN');

if (env('BUSINESS_TEST_AUTHORIZED').toLowerCase() !== 'true') {
  fail('Refusing to run: set BUSINESS_TEST_AUTHORIZED=true only for an authorized non-production target.');
}
if (env('ALLOW_SYNTHETIC_MUTATIONS').toLowerCase() !== 'true') {
  fail('Refusing to run: set ALLOW_SYNTHETIC_MUTATIONS=true only after explicit authorization.');
}
if (!/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(RUN_ID) || RUN_ID.length < 3 || RUN_ID.length > 32) {
  fail('RUN_ID is required and must be 3-32 chars of lowercase letters, digits, and single hyphens. Received: ' + RUN_ID);
}
if (!/^[a-z0-9.-]+$/.test(EMAIL_DOMAIN) || EMAIL_DOMAIN.indexOf('@') >= 0) {
  fail('EMAIL_DOMAIN must be a bare domain. Received: ' + EMAIL_DOMAIN);
}
if (EMAIL_DOMAIN === 'urbeat.com.br'
  || (EMAIL_DOMAIN.length > 14 && EMAIL_DOMAIN.substring(EMAIL_DOMAIN.length - 14) === '.urbeat.com.br')) {
  fail('EMAIL_DOMAIN must not be a production urbeat domain. Received: ' + EMAIL_DOMAIN);
}
if (!(EMAIL_DOMAIN.length > 8 && EMAIL_DOMAIN.substring(EMAIL_DOMAIN.length - 8) === '.invalid')
  && !(EMAIL_DOMAIN.length > 5 && EMAIL_DOMAIN.substring(EMAIL_DOMAIN.length - 5) === '.test')) {
  fail('EMAIL_DOMAIN must end with .invalid or .test so synthetic accounts can never receive real mail. Received: ' + EMAIL_DOMAIN);
}

let SELLER_VUS = readOptionalNonNegativeInt('SELLER_VUS');
let CUSTOMER_VUS = readOptionalNonNegativeInt('CUSTOMER_VUS');

if (SELLER_VUS === null && CUSTOMER_VUS === null) {
  // Keep the default gentle and deterministic: only runs with at least 5 VUs
  // reserve a small onboarding slice.
  SELLER_VUS = TOTAL_VUS >= 5 ? Math.max(1, Math.floor(TOTAL_VUS * 0.1)) : 0;
  CUSTOMER_VUS = TOTAL_VUS - SELLER_VUS;
} else if (SELLER_VUS === null) {
  SELLER_VUS = TOTAL_VUS - CUSTOMER_VUS;
} else if (CUSTOMER_VUS === null) {
  CUSTOMER_VUS = TOTAL_VUS - SELLER_VUS;
}

if (SELLER_VUS < 0 || CUSTOMER_VUS < 0 || SELLER_VUS + CUSTOMER_VUS !== TOTAL_VUS) {
  fail('SELLER_VUS + CUSTOMER_VUS must equal VUS (' + TOTAL_VUS + ').');
}
if (SELLER_VUS + CUSTOMER_VUS === 0) {
  fail('At least one scenario must be enabled.');
}

const TARGET_STORE_ID = env('TARGET_STORE_ID');
if (TARGET_STORE_ID !== '' && !isGuid(TARGET_STORE_ID)) {
  fail('TARGET_STORE_ID must be a GUID.');
}
const BOOTSTRAP_STORE_ID = env('BOOTSTRAP_STORE_ID');
if (BOOTSTRAP_STORE_ID !== '' && !isGuid(BOOTSTRAP_STORE_ID)) {
  fail('BOOTSTRAP_STORE_ID must be a GUID.');
}
const TARGET_DELIVERY_NEIGHBORHOOD = env('TARGET_DELIVERY_NEIGHBORHOOD');
const TARGET_DELIVERY_CITY = env('TARGET_DELIVERY_CITY') || 'São Paulo';
if (FULFILLMENT === 'delivery' && TARGET_STORE_ID !== '' && TARGET_DELIVERY_NEIGHBORHOOD === '') {
  fail('FULFILLMENT_TYPE=delivery with TARGET_STORE_ID requires TARGET_DELIVERY_NEIGHBORHOOD.');
}

// Creating a store address calls PUT /api/stores/{id}/address, which enqueues
// ImportStoreNeighborhoodsJob (OpenStreetMap importer) in the backend. Anchor and
// seller store creation are refused unless the operator attests that the test
// environment disables or mocks that business queue job.
const CREATES_STORE_ADDRESSES = SELLER_VUS > 0 || TARGET_STORE_ID === '';
if (CREATES_STORE_ADDRESSES && !ALLOW_NEIGHBORHOOD_IMPORT) {
  fail('Store address creation enqueues ImportStoreNeighborhoodsJob (OSM). Set ALLOW_NEIGHBORHOOD_IMPORT=true only after confirming the test environment disables or mocks that job.');
}

function loadPool(name) {
  const raw = env(name);
  if (raw === '') return null;

  let parsed;
  try {
    parsed = JSON.parse(raw);
  } catch (error) {
    fail(name + ' must be a valid JSON array of { email, password } objects.');
  }

  if (!Array.isArray(parsed) || parsed.length === 0) {
    fail(name + ' must be a non-empty JSON array.');
  }

  for (let i = 0; i < parsed.length; i++) {
    const item = parsed[i];
    if (!item || typeof item.email !== 'string' || typeof item.password !== 'string'
      || item.email.trim() === '' || item.password === '') {
      fail(name + ' entry ' + (i + 1) + ' must contain non-empty email and password.');
    }
    const lowerEmail = item.email.trim().toLowerCase();
    const at = lowerEmail.lastIndexOf('@');
    const domain = at >= 0 ? lowerEmail.substring(at + 1) : '';
    if (domain === 'urbeat.com.br'
      || (domain.length > 14 && domain.substring(domain.length - 14) === '.urbeat.com.br')) {
      fail(name + ' must not contain production urbeat e-mail accounts.');
    }
  }

  return parsed;
}

const SELLER_POOL = loadPool('SELLER_POOL_JSON');
const CUSTOMER_POOL = loadPool('CUSTOMER_POOL_JSON');
const ANCHOR_SELLER_EMAIL = env('ANCHOR_SELLER_EMAIL');
const ANCHOR_SELLER_PASSWORD = env('ANCHOR_SELLER_PASSWORD');

if ((ANCHOR_SELLER_EMAIL === '') !== (ANCHOR_SELLER_PASSWORD === '')) {
  fail('ANCHOR_SELLER_EMAIL and ANCHOR_SELLER_PASSWORD must be provided together.');
}
if (ANCHOR_SELLER_EMAIL !== '') {
  const anchorAt = ANCHOR_SELLER_EMAIL.toLowerCase().lastIndexOf('@');
  const anchorDomain = anchorAt >= 0 ? ANCHOR_SELLER_EMAIL.toLowerCase().substring(anchorAt + 1) : '';
  if (anchorDomain === 'urbeat.com.br'
    || (anchorDomain.length > 14 && anchorDomain.substring(anchorDomain.length - 14) === '.urbeat.com.br')) {
    fail('ANCHOR_SELLER_EMAIL must not be a production urbeat account.');
  }
}

if (SELLER_VUS > 0 && SELLER_POOL === null && !ALLOW_ACCOUNT_CREATION) {
  fail('Seller scenario needs SELLER_POOL_JSON or ALLOW_SYNTHETIC_ACCOUNT_CREATION=true.');
}
if (CUSTOMER_VUS > 0 && CUSTOMER_POOL === null && !ALLOW_ACCOUNT_CREATION) {
  fail('Customer scenario needs CUSTOMER_POOL_JSON or ALLOW_SYNTHETIC_ACCOUNT_CREATION=true.');
}

const NEEDS_ANCHOR_ACCOUNT = TARGET_STORE_ID === '' && ANCHOR_SELLER_EMAIL === '' && SELLER_POOL === null;
if (NEEDS_ANCHOR_ACCOUNT && !ALLOW_ACCOUNT_CREATION) {
  fail('Anchor store setup needs ANCHOR_SELLER_EMAIL, SELLER_POOL_JSON, or ALLOW_SYNTHETIC_ACCOUNT_CREATION=true.');
}

const NEEDS_ACCOUNT_CREATION =
  (SELLER_VUS > 0 && SELLER_POOL === null) ||
  (CUSTOMER_VUS > 0 && CUSTOMER_POOL === null) ||
  NEEDS_ANCHOR_ACCOUNT;

const SYNTHETIC_PASSWORD = env('SYNTHETIC_PASSWORD');
if (NEEDS_ACCOUNT_CREATION && SYNTHETIC_PASSWORD.length < 12) {
  fail('SYNTHETIC_PASSWORD must be at least 12 characters when synthetic account creation is enabled.');
}

const ANCHOR_USES_SELLER_POOL = TARGET_STORE_ID === '' && ANCHOR_SELLER_EMAIL === '' && SELLER_POOL !== null;
if (ANCHOR_USES_SELLER_POOL && SELLER_POOL.length < TOTAL_VUS + 1) {
  fail('SELLER_POOL_JSON must contain at least VUS + 1 entries when its first account is reserved for the anchor store.');
}
if (SELLER_POOL !== null && (SELLER_VUS > 0 || ANCHOR_USES_SELLER_POOL) && SELLER_POOL.length < TOTAL_VUS) {
  fail('SELLER_POOL_JSON must contain at least VUS entries.');
}
if (CUSTOMER_POOL !== null && CUSTOMER_VUS > 0 && CUSTOMER_POOL.length < TOTAL_VUS) {
  fail('CUSTOMER_POOL_JSON must contain at least VUS entries.');
}

function syntheticEmail(prefix, vu, iteration) {
  return 'load+' + RUN_ID + '+' + prefix + vu + (iteration > 0 ? '-' + iteration : '') + '@' + EMAIL_DOMAIN;
}

function syntheticPhone(vu, iteration) {
  const numeric = (vu * 100000 + iteration * 100 + 7) % 100000000;
  return '119' + pad8(numeric);
}

function syntheticName(prefix, vu) {
  return 'Load Test ' + RUN_ID + ' ' + prefix + vu;
}

function areaFor(label) {
  return 'Load Zone ' + RUN_ID + ' ' + label;
}

// Scenario options ---------------------------------------------------------------

const scenarios = {};

if (SELLER_VUS > 0) {
  scenarios.sellers = {
    executor: 'per-vu-iterations',
    vus: SELLER_VUS,
    iterations: 1,
    maxDuration: SELLER_MAX_DURATION,
    exec: 'sellerOnboarding',
  };
}

if (CUSTOMER_VUS > 0) {
  scenarios.customers = {
    executor: 'ramping-vus',
    startVUs: 0,
    stages: [
      { duration: RAMP_UP_DURATION, target: CUSTOMER_VUS },
      { duration: DURATION, target: CUSTOMER_VUS },
    ],
    gracefulRampDown: '10s',
    exec: 'customerOrders',
  };
}

const thresholds = {
  'http_req_duration{stage:catalog}': ['p(95)<1000', 'p(99)<2000'],
  'http_req_duration{stage:checkout_preview}': ['p(95)<1500', 'p(99)<3000'],
  'http_req_duration{stage:checkout_confirm}': ['p(95)<2000', 'p(99)<4000'],
  'http_req_duration{stage:customer_session}': ['p(95)<1500', 'p(99)<3000'],
};

if (SELLER_VUS > 0) {
  thresholds['http_req_failed{scenario:sellers}'] = ['rate<0.05'];
  thresholds['checks{stage:seller_store}'] = ['rate>0.95'];
  thresholds['urbeat_onboarding_success'] = ['rate>0.90'];
  thresholds['http_req_duration{stage:seller_publish}'] = ['p(95)<2500', 'p(99)<5000'];
}
if (CUSTOMER_VUS > 0) {
  thresholds['http_req_failed{scenario:customers}'] = ['rate<0.02'];
  thresholds['checks{stage:checkout_confirm}'] = ['rate>0.95'];
  thresholds['urbeat_order_success'] = ['rate>0.90'];
}
if (SELLER_STATUS_SMOKE && SELLER_VUS > 0) {
  thresholds['urbeat_status_smoke_success'] = ['rate>0.90'];
}

export const options = {
  scenarios: scenarios,
  thresholds: thresholds,
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
  userAgent: 'urbeat-business-load/1.0',
};

// Setup / teardown ---------------------------------------------------------------

function readOutboxSummary() {
  if (OUTBOX_ADMIN_TOKEN === '') return null;

  const response = http.get(BASE_URL + '/api/outbox/summary', {
    headers: authHeaders(OUTBOX_ADMIN_TOKEN),
    tags: { stage: 'outbox' },
    timeout: REQUEST_TIMEOUT,
    redirects: 0,
  });

  if (response.status !== 200) {
    console.warn(LOAD_TAG + ' outbox summary unavailable status=' + response.status + ' (read-only optional check)');
    return null;
  }

  return safeJson(response);
}

function getCuisineName() {
  const response = jsonRequest('GET', '/api/stores/cuisine-types', null, null, 'setup_cuisine', [200]);
  if (response === null) fail('Could not load cuisine types.');
  const types = safeJson(response);
  if (!Array.isArray(types) || types.length === 0 || !types[0].name) {
    fail('No active cuisine type is available for synthetic stores.');
  }
  return types[0].name;
}

function discoverPublicStoreId() {
  const response = jsonRequest('GET', '/api/public/stores', null, null, 'setup_public_stores', [200]);
  if (response === null) return '';
  const stores = safeJson(response);
  if (!Array.isArray(stores) || stores.length === 0) return '';
  return stores[0].id || '';
}

function getPublicStore(storeId) {
  const response = jsonRequest('GET', '/api/public/stores/' + storeId, null, null, 'setup_target_store', [200]);
  if (response === null) fail('Target store ' + storeId + ' is not publicly readable.');
  return safeJson(response);
}

function getCatalogProducts(storeId, stage) {
  const response = jsonRequest(
    'GET',
    '/api/public/stores/' + storeId + '/catalog/products',
    null,
    null,
    stage,
    [200]
  );
  if (response === null) return null;
  const products = safeJson(response);
  if (!Array.isArray(products)) return null;
  return products.filter(function (product) {
    return product.id && product.isAvailable !== false;
  });
}

function createCustomerSession(storeId, email, phone, fullName, neighborhood, city, stage) {
  const payload = {
    storeId: storeId,
    customer: {
      fullName: fullName,
      email: email,
      phoneNumber: phone,
    },
    address: {
      cep: '01001000',
      street: 'Rua de Teste',
      number: '100',
      complement: '',
      neighborhood: neighborhood,
      city: city,
      state: 'SP',
    },
  };

  const response = jsonRequest('POST', '/api/checkout/customer-session', payload, null, stage, [200]);
  if (response === null) return null;

  const body = safeJson(response);
  if (!body || body.succeeded !== true || !body.accessToken) {
    stepFailures.add(1, { stage: stage });
    console.warn(LOAD_TAG + ' customer-session did not return a confirmed token.');
    return null;
  }

  return {
    token: body.accessToken,
    addressId: body.customerAddressId || null,
  };
}

function promoteToSeller(customerToken, email, password, fullName, phone, currentPassword) {
  const payload = {
    fullName: fullName,
    email: email,
    password: password,
    phoneNumber: phone,
  };
  if (currentPassword) {
    payload.currentPassword = currentPassword;
  }

  const response = jsonRequest('POST', '/api/auth/register/seller', payload, customerToken, 'seller_register', [201]);
  if (response === null) return false;

  const body = safeJson(response);
  if (!body) {
    stepFailures.add(1, { stage: 'seller_register' });
    return false;
  }

  // Fail closed if the promotion unexpectedly requires e-mail confirmation. The
  // backend may already have queued the message, but the run must stop here and
  // report the contract change instead of continuing to create more accounts.
  if (body.emailConfirmationPending === true) {
    blocked.add(1, { reason: 'email_confirmation_required' });
    fail('Seller promotion unexpectedly requires e-mail confirmation; aborting before further registrations.');
  }

  return true;
}

function login(role, email, password, stage) {
  const payload = { email: email, password: password };
  const response = jsonRequest('POST', '/api/auth/login/' + role, payload, null, stage, [200]);
  if (response === null) return null;

  const body = safeJson(response);
  if (!body || !body.accessToken) {
    stepFailures.add(1, { stage: stage });
    console.warn(LOAD_TAG + ' ' + stage + ' did not return an access token.');
    return null;
  }

  return body.accessToken;
}

function sellerPoolIndex(vu) {
  const offset = ANCHOR_USES_SELLER_POOL ? 1 : 0;
  return (vu - 1 + offset) % SELLER_POOL.length;
}

function obtainSellerToken(data, prefix, vu, iteration) {
  if (SELLER_POOL !== null) {
    const account = SELLER_POOL[sellerPoolIndex(vu)];
    return login('seller', account.email, account.password, 'seller_login');
  }

  if (data.bootstrapStoreId === '') {
    fail('Synthetic seller creation needs a bootstrap store. Set BOOTSTRAP_STORE_ID or ensure a public store exists.');
  }

  const email = syntheticEmail(prefix, vu, iteration);
  const phone = syntheticPhone(vu, iteration);
  const fullName = syntheticName(prefix, vu);

  const session = createCustomerSession(
    data.bootstrapStoreId,
    email,
    phone,
    fullName,
    'Centro',
    'São Paulo',
    'seller_bootstrap_customer'
  );
  if (session === null) return null;

  if (!promoteToSeller(session.token, email, SYNTHETIC_PASSWORD, fullName, phone, 'Urbeat@' + phone)) {
    return null;
  }

  return login('seller', email, SYNTHETIC_PASSWORD, 'seller_login');
}

function createStore(token, runId, label, cuisineName) {
  const payload = {
    name: '[' + runId + '] Store ' + label,
    slug: 'load-' + runId + '-' + label,
    phoneNumber: '11999999999',
    cuisineType: cuisineName,
    supportsDelivery: true,
    supportsPickup: true,
    initialMinute: 20,
    finalMinute: 40,
    maxDeliveryRadiusKm: 50,
  };

  const response = jsonRequest('POST', '/api/stores', payload, token, 'seller_store', [201]);
  if (response === null) return null;

  const body = safeJson(response);
  if (!body || !body.id) {
    stepFailures.add(1, { stage: 'seller_store' });
    return null;
  }

  storesCreated.add(1);
  return body.id;
}

function configureStore(token, storeId, runId, area, label) {
  const addressResponse = jsonRequest(
    'PUT',
    '/api/stores/' + storeId + '/address',
    {
      street: 'Rua de Teste',
      number: '100',
      neighborhood: area,
      city: 'São Paulo',
      state: 'SP',
      zipCode: '01001000',
      complement: '',
      reference: '',
      latitude: -23.5505,
      longitude: -46.6333,
    },
    token,
    'seller_address',
    [200]
  );
  if (addressResponse === null) return null;

  const items = [];
  for (let day = 0; day < 7; day++) {
    items.push({
      dayOfWeek: day,
      isOpen: true,
      shifts: [{ startTime: '00:00:00', endTime: '23:59:59' }],
    });
  }

  const hoursResponse = jsonRequest(
    'PUT',
    '/api/stores/' + storeId + '/business-hours',
    { items: items },
    token,
    'seller_hours',
    [200]
  );
  if (hoursResponse === null) return null;

  const deliveryResponse = jsonRequest(
    'PATCH',
    '/api/stores/' + storeId + '/delivery-config',
    {
      deliveryFee: 5.0,
      minimumOrderValue: 0,
      freeShippingToday: false,
      maxDeliveryRadiusKm: 50,
      deliveryAreas: [
        { neighborhood: area, deliveryFee: 5.0, isActive: true, notes: '' },
      ],
    },
    token,
    'seller_delivery',
    [200]
  );
  if (deliveryResponse === null) return null;

  const categoryResponse = jsonRequest(
    'POST',
    '/api/stores/' + storeId + '/categories',
    {
      name: '[' + runId + '] Menu ' + label,
      description: 'Synthetic load test category',
      displayOrder: 1,
      isActive: true,
      isFeatured: false,
    },
    token,
    'seller_category',
    [201]
  );
  if (categoryResponse === null) return null;

  const category = safeJson(categoryResponse);
  if (!category || !category.id) {
    stepFailures.add(1, { stage: 'seller_category' });
    return null;
  }

  const productResponse = jsonRequest(
    'POST',
    '/api/stores/' + storeId + '/products',
    {
      categoryId: category.id,
      name: '[' + runId + '] Item ' + label,
      description: 'Synthetic load test product for ' + runId,
      price: 25.5,
      imageUrl: 'https://example.invalid/urbeat-load-test.png',
      isAvailable: true,
      isFeatured: false,
      displayOrder: 1,
      stockEnabled: false,
      stockQuantity: 0,
      saleMode: 'single',
      additionals: [],
      choiceOptions: [],
      variations: [],
      optionGroups: [],
    },
    token,
    'seller_product',
    [201]
  );
  if (productResponse === null) return null;

  const product = safeJson(productResponse);
  if (!product || !product.id) {
    stepFailures.add(1, { stage: 'seller_product' });
    return null;
  }

  productsCreated.add(1);
  return { categoryId: category.id, productId: product.id };
}

function publishStore(token, storeId) {
  const summaryResponse = jsonRequest(
    'GET',
    '/api/stores/' + storeId + '/publish/summary',
    null,
    token,
    'seller_publish_summary',
    [200]
  );
  if (summaryResponse === null) return false;

  const summary = safeJson(summaryResponse);
  if (!summary || summary.canPublish !== true) {
    blocked.add(1, { reason: 'publish_requirements_not_met' });
    console.warn(LOAD_TAG + ' publish summary is not ready for store ' + storeId + '.');
    return false;
  }

  const publishResponse = jsonRequest(
    'POST',
    '/api/stores/' + storeId + '/publish',
    null,
    token,
    'seller_publish',
    [200]
  );
  if (publishResponse === null) return false;

  const myStoreResponse = jsonRequest(
    'GET',
    '/api/stores/my-store',
    null,
    token,
    'seller_my_store',
    [200]
  );
  if (myStoreResponse === null) return false;

  const store = safeJson(myStoreResponse);
  if (!store || store.isOpen !== true || store.isPublished !== true) {
    stepFailures.add(1, { stage: 'seller_my_store' });
    console.warn(LOAD_TAG + ' store did not become open/published after publish.');
    return false;
  }

  storesPublished.add(1);
  return true;
}

function getAnchorSellerToken(data) {
  if (ANCHOR_SELLER_EMAIL !== '') {
    return login('seller', ANCHOR_SELLER_EMAIL, ANCHOR_SELLER_PASSWORD, 'anchor_login');
  }
  if (SELLER_POOL !== null) {
    const account = SELLER_POOL[0];
    return login('seller', account.email, account.password, 'anchor_login');
  }
  if (!ALLOW_ACCOUNT_CREATION) {
    fail('No anchor seller account is available. Set ANCHOR_SELLER_EMAIL/PASSWORD, provide SELLER_POOL_JSON, or enable synthetic account creation.');
  }
  if (data.bootstrapStoreId === '') {
    fail('Anchor seller creation needs a bootstrap store. Set BOOTSTRAP_STORE_ID or ensure a public store exists.');
  }

  const email = syntheticEmail('anchor', 0, 0);
  const phone = syntheticPhone(0, 0);
  const fullName = syntheticName('anchor', 0);

  const session = createCustomerSession(
    data.bootstrapStoreId,
    email,
    phone,
    fullName,
    'Centro',
    'São Paulo',
    'anchor_bootstrap_customer'
  );
  if (session === null) return null;

  if (!promoteToSeller(session.token, email, SYNTHETIC_PASSWORD, fullName, phone, 'Urbeat@' + phone)) {
    return null;
  }

  return login('seller', email, SYNTHETIC_PASSWORD, 'anchor_login');
}

function createAnchorStore(data, cuisineName) {
  const token = getAnchorSellerToken(data);
  if (token === null) fail('Could not authenticate the anchor seller.');

  const storeId = createStore(token, RUN_ID, 'anchor', cuisineName);
  if (storeId === null) fail('Could not create the anchor store.');

  const area = areaFor('anchor');
  const ids = configureStore(token, storeId, RUN_ID, area, 'anchor');
  if (ids === null) fail('Could not configure the anchor store.');

  if (!publishStore(token, storeId)) fail('Could not publish the anchor store.');

  return { storeId: storeId, productId: ids.productId, deliveryNeighborhood: area };
}

export function setup() {
  const outboxBefore = readOutboxSummary();
  const bootstrapStoreId = BOOTSTRAP_STORE_ID !== '' ? BOOTSTRAP_STORE_ID : discoverPublicStoreId();
  const targetStoreId = TARGET_STORE_ID;

  const data = {
    runId: RUN_ID,
    bootstrapStoreId: targetStoreId !== '' && bootstrapStoreId === '' ? targetStoreId : bootstrapStoreId,
    storeId: targetStoreId,
    productId: '',
    deliveryNeighborhood: TARGET_DELIVERY_NEIGHBORHOOD,
    cuisineName: '',
    anchorCreated: false,
    outboxBefore: outboxBefore,
  };

  if (SELLER_VUS > 0) {
    data.cuisineName = getCuisineName();
  }

  if (targetStoreId !== '') {
    const store = getPublicStore(targetStoreId);
    if (!store || store.isOpen !== true) {
      fail('TARGET_STORE_ID must reference an open store; checkout rejects closed stores.');
    }

    const products = getCatalogProducts(targetStoreId, 'setup_target_catalog');
    if (products === null || products.length === 0) {
      fail('TARGET_STORE_ID must have at least one available catalog product.');
    }

    data.productId = products[0].id;
    if (FULFILLMENT === 'delivery' && data.deliveryNeighborhood === '') {
      fail('FULFILLMENT_TYPE=delivery with TARGET_STORE_ID requires TARGET_DELIVERY_NEIGHBORHOOD.');
    }

    console.log(
      LOAD_TAG + ' setup target store=' + targetStoreId +
      ' product=' + data.productId +
      ' fulfillment=' + FULFILLMENT
    );
    return data;
  }

  if (data.cuisineName === '') {
    data.cuisineName = getCuisineName();
  }

  const anchor = createAnchorStore(data, data.cuisineName);
  data.storeId = anchor.storeId;
  data.productId = anchor.productId;
  data.deliveryNeighborhood = anchor.deliveryNeighborhood;
  data.anchorCreated = true;

  console.log(
    LOAD_TAG + ' setup anchor store=' + data.storeId +
    ' product=' + data.productId +
    ' fulfillment=' + FULFILLMENT
  );
  return data;
}

// Seller scenario ---------------------------------------------------------------

function placeStatusSmokeOrder(sellerToken, storeId, productId, area) {
  const vu = __VU;
  const email = syntheticEmail('status', vu, 0);
  const phone = syntheticPhone(vu, 1);
  const fullName = syntheticName('status', vu);

  const session = createCustomerSession(storeId, email, phone, fullName, area, 'São Paulo', 'status_customer_session');
  if (session === null) return false;

  const preview = previewOrder(session.token, storeId, productId, null, 'status_preview', 'pickup');
  if (preview === null || preview.storeIsOpen !== true) return false;

  const confirmation = confirmOrder(
    session.token,
    storeId,
    productId,
    preview.total,
    null,
    'status_confirm',
    'pickup'
  );
  if (confirmation === null) return false;

  ordersCreated.add(1);
  mockPayments.add(1);

  const statusResponse = jsonRequest(
    'PATCH',
    '/api/orders/' + confirmation.orderId + '/status',
    { newStatus: ORDER_STATUS_PREPARING, notes: 'Synthetic load test status transition' },
    sellerToken,
    'seller_status_transition',
    [200]
  );
  if (statusResponse === null) return false;

  const order = safeJson(statusResponse);
  if (!order || order.status !== ORDER_STATUS_PREPARING) {
    stepFailures.add(1, { stage: 'seller_status_transition' });
    return false;
  }

  const listResponse = jsonRequest(
    'GET',
    '/api/orders/store?page=1&pageSize=10',
    null,
    sellerToken,
    'seller_order_list',
    [200]
  );
  if (listResponse === null) return false;

  const list = safeJson(listResponse);
  if (!list || !Array.isArray(list.items)) {
    stepFailures.add(1, { stage: 'seller_order_list' });
    return false;
  }

  const found = list.items.some(function (item) {
    return item.id === confirmation.orderId;
  });
  if (!found) {
    stepFailures.add(1, { stage: 'seller_order_list' });
    console.warn(LOAD_TAG + ' seller order list did not contain the smoke order.');
    return false;
  }

  return true;
}

export function sellerOnboarding(data) {
  const vu = __VU;
  const label = 's' + vu;
  const area = areaFor(label);

  const token = obtainSellerToken(data, 's', vu, 0);
  if (token === null) {
    onboardingSuccess.add(false);
    return;
  }

  const storeId = createStore(token, data.runId, label, data.cuisineName);
  if (storeId === null) {
    onboardingSuccess.add(false);
    return;
  }

  const ids = configureStore(token, storeId, data.runId, area, label);
  if (ids === null) {
    onboardingSuccess.add(false);
    return;
  }

  if (!publishStore(token, storeId)) {
    onboardingSuccess.add(false);
    return;
  }

  onboardingSuccess.add(true);
  console.log(LOAD_TAG + ' seller onboarding complete store=' + storeId + ' label=' + label);

  if (SELLER_STATUS_SMOKE) {
    const ok = placeStatusSmokeOrder(token, storeId, ids.productId, area);
    statusSmokeSuccess.add(ok);
  }
}

// Customer scenario -------------------------------------------------------------

function getCustomerAddressId(token, neighborhood) {
  const listResponse = jsonRequest('GET', '/api/customer/addresses', null, token, 'customer_addresses', [200]);
  if (listResponse !== null) {
    const addresses = safeJson(listResponse);
    if (Array.isArray(addresses)) {
      const wanted = String(neighborhood || '').trim().toLowerCase();
      for (let i = 0; i < addresses.length; i++) {
        const address = addresses[i];
        if (address && address.id && String(address.neighborhood || '').trim().toLowerCase() === wanted) {
          return address.id;
        }
      }
    }
  }

  const createResponse = jsonRequest(
    'POST',
    '/api/customer/addresses',
    {
      cep: '01001000',
      number: '100',
      street: 'Rua de Teste',
      neighborhood: neighborhood,
      city: TARGET_DELIVERY_CITY,
      state: 'SP',
      isPrimary: true,
    },
    token,
    'customer_address_create',
    [201]
  );
  if (createResponse === null) return null;

  const address = safeJson(createResponse);
  return address && address.id ? address.id : null;
}

function obtainCustomerSession(data, vu, iteration) {
  if (CUSTOMER_POOL !== null) {
    const account = CUSTOMER_POOL[(vu - 1) % CUSTOMER_POOL.length];
    const token = login('customer', account.email, account.password, 'customer_login');
    if (token === null) return null;
    const area = data.deliveryNeighborhood || TARGET_DELIVERY_NEIGHBORHOOD || 'Centro';
    const addressId = FULFILLMENT === 'delivery' ? getCustomerAddressId(token, area) : null;
    if (FULFILLMENT === 'delivery' && addressId === null) return null;
    return { token: token, addressId: addressId };
  }

  const email = syntheticEmail('c', vu, iteration);
  const phone = syntheticPhone(vu, iteration + 10);
  const fullName = syntheticName('c', vu);
  const area = data.deliveryNeighborhood || TARGET_DELIVERY_NEIGHBORHOOD || 'Centro';
  const session = createCustomerSession(
    data.storeId,
    email,
    phone,
    fullName,
    area,
    TARGET_DELIVERY_CITY,
    'customer_session'
  );
  if (session === null) return null;

  if (FULFILLMENT === 'delivery' && !session.addressId) {
    blocked.add(1, { reason: 'customer_address_missing' });
    return null;
  }

  return session;
}

function checkoutItems(productId) {
  return [{ productId: productId, quantity: 1 }];
}

function checkoutBasePayload(storeId, productId, addressId, fulfillment) {
  const effectiveFulfillment = fulfillment || FULFILLMENT;
  const payload = {
    storeId: storeId,
    fulfillmentType: effectiveFulfillment === 'delivery' ? FULFILLMENT_TYPE_DELIVERY : FULFILLMENT_TYPE_PICKUP,
    paymentMethod: PAYMENT_METHOD_CASH_ON_DELIVERY,
    cashWithoutChange: true,
    items: checkoutItems(productId),
  };

  if (effectiveFulfillment === 'delivery') {
    payload.customerAddressId = addressId;
  }

  return payload;
}

function previewOrder(token, storeId, productId, addressId, stage, fulfillment) {
  const response = jsonRequest(
    'POST',
    '/api/checkout/preview',
    checkoutBasePayload(storeId, productId, addressId, fulfillment),
    token,
    stage || 'checkout_preview',
    [200]
  );
  if (response === null) return null;

  const summary = safeJson(response);
  if (!summary || typeof summary.total !== 'number') {
    stepFailures.add(1, { stage: stage || 'checkout_preview' });
    return null;
  }

  return summary;
}

function confirmOrder(token, storeId, productId, expectedTotal, addressId, stage, fulfillment) {
  const payload = checkoutBasePayload(storeId, productId, addressId, fulfillment);
  payload.expectedTotal = expectedTotal;
  payload.notes = 'Synthetic load test order ' + RUN_ID;

  const response = jsonRequest(
    'POST',
    '/api/checkout/confirm',
    payload,
    token,
    stage || 'checkout_confirm',
    [201]
  );
  if (response === null) return null;

  const confirmation = safeJson(response);
  if (!confirmation || !confirmation.orderId || !confirmation.code) {
    stepFailures.add(1, { stage: stage || 'checkout_confirm' });
    return null;
  }

  if (confirmation.status !== ORDER_STATUS_RECEIVED) {
    stepFailures.add(1, { stage: stage || 'checkout_confirm' });
    console.warn(LOAD_TAG + ' unexpected order status after confirm: ' + confirmation.status);
    return null;
  }

  return confirmation;
}

function readBackOrder(token, orderId) {
  const detailResponse = jsonRequest('GET', '/api/orders/' + orderId, null, token, 'order_read', [200]);
  if (detailResponse === null) return false;

  const detail = safeJson(detailResponse);
  if (!detail || detail.status !== ORDER_STATUS_RECEIVED) {
    stepFailures.add(1, { stage: 'order_read' });
    return false;
  }

  const listResponse = jsonRequest('GET', '/api/orders/my', null, token, 'order_list', [200]);
  if (listResponse === null) return false;

  const list = safeJson(listResponse);
  if (!Array.isArray(list)) {
    stepFailures.add(1, { stage: 'order_list' });
    return false;
  }

  const found = list.some(function (item) {
    return item.id === orderId;
  });
  if (!found) {
    stepFailures.add(1, { stage: 'order_list' });
    return false;
  }

  return true;
}

let vuSession = null;

export function customerOrders(data) {
  const vu = __VU;
  const iteration = __ITER;

  if (vuSession === null || CUSTOMER_SESSION_PER_ITERATION) {
    vuSession = obtainCustomerSession(data, vu, iteration);
  }

  if (vuSession === null) {
    orderSuccess.add(false);
    if (SLEEP_SECONDS > 0) sleep(SLEEP_SECONDS);
    return;
  }

  const products = getCatalogProducts(data.storeId, 'catalog');
  if (products === null || products.length === 0) {
    blocked.add(1, { reason: 'catalog_empty' });
    orderSuccess.add(false);
    if (SLEEP_SECONDS > 0) sleep(SLEEP_SECONDS);
    return;
  }

  const product = products[(vu + iteration) % products.length];
  const preview = previewOrder(
    vuSession.token,
    data.storeId,
    product.id,
    vuSession.addressId,
    'checkout_preview'
  );

  if (preview === null) {
    orderSuccess.add(false);
    if (SLEEP_SECONDS > 0) sleep(SLEEP_SECONDS);
    return;
  }

  if (preview.storeIsOpen !== true) {
    blocked.add(1, { reason: 'store_closed' });
    orderSuccess.add(false);
    if (SLEEP_SECONDS > 0) sleep(SLEEP_SECONDS);
    return;
  }

  const confirmation = confirmOrder(
    vuSession.token,
    data.storeId,
    product.id,
    preview.total,
    vuSession.addressId,
    'checkout_confirm'
  );

  if (confirmation === null) {
    orderSuccess.add(false);
    if (SLEEP_SECONDS > 0) sleep(SLEEP_SECONDS);
    return;
  }

  ordersCreated.add(1);
  // Pay-on-delivery confirmation is the only "payment" exercised here. No gateway
  // call is made; the counter exists to make that business outcome visible.
  mockPayments.add(1);
  orderSuccess.add(true);

  if (CUSTOMER_READBACK) {
    readBackOrder(vuSession.token, confirmation.orderId);
  }

  if (SLEEP_SECONDS > 0) sleep(SLEEP_SECONDS);
}

// Teardown -----------------------------------------------------------------------

export function teardown(data) {
  const outboxAfter = readOutboxSummary();

  if (data && data.outboxBefore && outboxAfter) {
    console.log(
      LOAD_TAG + ' outbox before=' + JSON.stringify(data.outboxBefore) +
      ' after=' + JSON.stringify(outboxAfter)
    );
  }

  console.log(
    LOAD_TAG + ' run complete | RUN_ID=' + RUN_ID +
    ' | synthetic e-mails use *@' + EMAIL_DOMAIN +
    ' | stores use slug load-' + RUN_ID + '-*' +
    ' | cleanup is MANUAL and must be scoped to RUN_ID'
  );
  console.log(
    LOAD_TAG + ' no store/user deletion API exists; use the wrapper cleanup plan and review it with the environment owner.'
  );
}
