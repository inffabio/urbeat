// Non-destructive k6 smoke test for the urbeat API.
//
// Safety contract:
// - Only GET requests.
// - Defaults to the local API and a single virtual user.
// - No tokens, credentials, cookies, or private payloads.
// - Never call login, checkout, store creation, WhatsApp, or any mutating endpoint.
//
// Configuration (environment variables):
// - BASE_URL          API origin. Default: http://localhost:5000
// - PUBLIC_PATH       Optional safe public GET path, e.g. /api/public/plans
// - VUS               Virtual users (target for ramp). Default: 1
// - DURATION          Hold duration at the target VU count. Default: 30s
// - RAMP_UP_DURATION  Ramp-up stage duration when SCENARIO=ramp. Default: 30s
// - SCENARIO          smoke (constant VUs) or ramp (ramping VUs). Default: smoke
// - SLEEP_SECONDS     Pause between iterations. Default: 1

import http from 'k6/http';
import { check, sleep } from 'k6';

const DEFAULT_BASE_URL = 'http://localhost:5000';
const HEALTH_PATH = '/health';
const DEFAULT_VUS = 1;
const DEFAULT_DURATION = '30s';
const DEFAULT_RAMP_UP_DURATION = '30s';
const DEFAULT_SLEEP_SECONDS = 1;
const REQUEST_TIMEOUT = '10s';

function fail(message) {
  throw new Error('[urbeat-api-smoke] ' + message);
}

function readPositiveInt(name, fallback) {
  const raw = __ENV[name];
  if (raw === undefined || raw === null || String(raw).trim() === '') {
    return fallback;
  }

  const value = Number(String(raw).trim());
  if (!Number.isInteger(value) || value < 1) {
    fail(name + ' must be a positive integer. Received: ' + raw);
  }

  return value;
}

function readNonNegativeNumber(name, fallback) {
  const raw = __ENV[name];
  if (raw === undefined || raw === null || String(raw).trim() === '') {
    return fallback;
  }

  const value = Number(String(raw).trim());
  if (!Number.isFinite(value) || value < 0) {
    fail(name + ' must be a non-negative number. Received: ' + raw);
  }

  return value;
}

function readDuration(name, fallback) {
  const raw = __ENV[name];
  if (raw === undefined || raw === null || String(raw).trim() === '') {
    return fallback;
  }

  const value = String(raw).trim();
  if (!/^\d+(s|m|h)$/.test(value)) {
    fail(name + ' must use the k6 duration format, e.g. 30s, 2m, or 1h. Received: ' + raw);
  }

  return value;
}

function readChoice(name, allowed, fallback) {
  const raw = __ENV[name];
  if (raw === undefined || raw === null || String(raw).trim() === '') {
    return fallback;
  }

  const value = String(raw).trim().toLowerCase();
  if (allowed.indexOf(value) < 0) {
    fail(name + ' must be one of: ' + allowed.join(', ') + '. Received: ' + raw);
  }

  return value;
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

  if (/^https?:\/\/[^/]*@/i.test(value)) {
    fail('BASE_URL must not include credentials.');
  }

  return value;
}

function normalizePublicPath(raw) {
  if (raw === undefined || raw === null) {
    return '';
  }

  const value = String(raw).trim();
  if (value === '') {
    return '';
  }

  if (value.indexOf('/') !== 0) {
    fail('PUBLIC_PATH must start with "/". Received: ' + raw);
  }

  if (/^\/\//.test(value)) {
    fail('PUBLIC_PATH must be a path on BASE_URL, not a protocol-relative URL.');
  }

  if (/\s/.test(value)) {
    fail('PUBLIC_PATH must not contain whitespace.');
  }

  return value;
}

const BASE_URL = normalizeBaseUrl(__ENV.BASE_URL || DEFAULT_BASE_URL);
const PUBLIC_PATH = normalizePublicPath(__ENV.PUBLIC_PATH);
const VUS = readPositiveInt('VUS', DEFAULT_VUS);
const DURATION = readDuration('DURATION', DEFAULT_DURATION);
const RAMP_UP_DURATION = readDuration('RAMP_UP_DURATION', DEFAULT_RAMP_UP_DURATION);
const SCENARIO = readChoice('SCENARIO', ['smoke', 'ramp'], 'smoke');
const SLEEP_SECONDS = readNonNegativeNumber('SLEEP_SECONDS', DEFAULT_SLEEP_SECONDS);

const thresholds = {
  checks: ['rate>0.99'],
  http_req_failed: ['rate<0.01'],
  'http_req_duration{endpoint:health}': ['p(95)<500', 'p(99)<1000'],
};

if (PUBLIC_PATH !== '') {
  thresholds['http_req_duration{endpoint:public}'] = ['p(95)<800', 'p(99)<1500'];
}

// Both scenarios are read-only and share the same iteration body. 'ramp' starts at 0 VUs, reaches
// VUS over RAMP_UP_DURATION and then holds that load for DURATION, which isolates latency drift
// caused by connection/thread warm-up from steady-state behavior.
const scenarios = SCENARIO === 'ramp'
  ? {
      ramp: {
        executor: 'ramping-vus',
        startVUs: 0,
        stages: [
          { duration: RAMP_UP_DURATION, target: VUS },
          { duration: DURATION, target: VUS },
        ],
        gracefulRampDown: '5s',
      },
    }
  : {
      smoke: {
        executor: 'constant-vus',
        vus: VUS,
        duration: DURATION,
        gracefulStop: '5s',
      },
    };

export const options = {
  scenarios: scenarios,
  thresholds: thresholds,
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

console.log(
  '[urbeat-api-smoke] base=' + BASE_URL +
  ' publicPath=' + (PUBLIC_PATH === '' ? '(not configured)' : PUBLIC_PATH) +
  ' scenario=' + SCENARIO +
  (SCENARIO === 'ramp' ? ' rampUp=' + RAMP_UP_DURATION : '') +
  ' vus=' + VUS +
  ' duration=' + DURATION
);

export default function () {
  const healthResponse = http.get(BASE_URL + HEALTH_PATH, {
    tags: { endpoint: 'health' },
    timeout: REQUEST_TIMEOUT,
  });

  check(healthResponse, {
    'health responds with 2xx': function (response) {
      return response.status >= 200 && response.status < 300;
    },
  });

  if (PUBLIC_PATH !== '') {
    const publicResponse = http.get(BASE_URL + PUBLIC_PATH, {
      tags: { endpoint: 'public' },
      timeout: REQUEST_TIMEOUT,
    });

    check(publicResponse, {
      'public path responds with 2xx': function (response) {
        return response.status >= 200 && response.status < 300;
      },
    });
  }

  if (SLEEP_SECONDS > 0) {
    sleep(SLEEP_SECONDS);
  }
}
