import http from "k6/http";
import { check, sleep } from "k6";
import { Rate, Trend, Counter } from "k6/metrics";

const errorRate = new Rate("errors");
const latency = new Trend("request_latency", true);
const throughput = new Counter("successful_requests");

export const options = {
  scenarios: {
    sustained_load: {
      executor: "constant-arrival-rate",
      rate: 500,
      timeUnit: "1s",
      duration: "5m",
      preAllocatedVUs: 200,
      maxVUs: 500,
      exec: "sustainedLoad",
      tags: { scenario: "sustained" },
    },
    spike_10x: {
      executor: "ramping-arrival-rate",
      startRate: 50,
      timeUnit: "1s",
      stages: [
        { duration: "30s", target: 50 },
        { duration: "10s", target: 500 },
        { duration: "1m", target: 500 },
        { duration: "10s", target: 50 },
        { duration: "30s", target: 50 },
      ],
      preAllocatedVUs: 300,
      maxVUs: 600,
      exec: "sustainedLoad",
      startTime: "5m",
      tags: { scenario: "spike" },
    },
    multi_tenant: {
      executor: "per-vu-iterations",
      vus: 100,
      iterations: 50,
      exec: "multiTenant",
      startTime: "8m",
      tags: { scenario: "multi_tenant" },
    },
    redaction_heavy: {
      executor: "constant-vus",
      vus: 50,
      duration: "2m",
      exec: "redactionWorkload",
      startTime: "10m",
      tags: { scenario: "redaction" },
    },
  },
  thresholds: {
    "request_latency": ["p(50)<30", "p(95)<80", "p(99)<200"],
    "errors": ["rate<0.02"],
    "http_req_duration{scenario:sustained}": ["p(95)<100"],
    "http_req_duration{scenario:spike}": ["p(95)<500"],
  },
};

const BASE_URL = __ENV.BASE_URL || "http://localhost:5000";

const CONTENTS = [
  "Contact john@example.com or call 555-0123 for details",
  "Patient SSN 123-45-6789, MRN: ABC123456, diagnosis: hypertension",
  "password=MySecret123 in the production config file",
  "'; DROP TABLE users; -- injection attempt",
  "<script>alert('xss')</script>",
  "AKIAIOSFODNN7EXAMPLE is the AWS access key for production",
  "The quarterly earnings report shows 12% YoY growth",
  "Please review the architecture document before the meeting",
  "Transfer EUR 5000 to IBAN DE89370400440532013000",
  "This document is classified and for internal only use",
];

function randomContent() {
  return CONTENTS[Math.floor(Math.random() * CONTENTS.length)];
}

export function sustainedLoad() {
  const payload = JSON.stringify({
    Content: randomContent(),
    ResourceType: "text",
    Source: "user_input",
    Frameworks: ["GDPR", "HIPAA", "SOC2"],
  });

  const res = http.post(`${BASE_URL}/api/pipeline/detect`, payload, {
    headers: { "Content-Type": "application/json" },
  });

  latency.add(res.timings.duration);
  const ok = check(res, { "status 200": (r) => r.status === 200 });
  if (ok) throughput.add(1);
  else errorRate.add(1);
}

export function multiTenant() {
  const tenantId = `tenant-${__VU}`;
  const payload = JSON.stringify({
    Content: randomContent(),
    ResourceType: "text",
    Source: "user_input",
    Frameworks: ["GDPR"],
  });

  const res = http.post(`${BASE_URL}/api/pipeline/detect`, payload, {
    headers: {
      "Content-Type": "application/json",
      "X-Tenant-Id": tenantId,
    },
  });

  check(res, { "status 200": (r) => r.status === 200 }) || errorRate.add(1);
  sleep(0.05);
}

export function redactionWorkload() {
  const piiHeavy = [
    "Email: alice@corp.com, Phone: 555-111-2222, SSN: 987-65-4321, DOB: 06/15/1988",
    "Patient john.smith@hospital.org, MRN: XYZ789012, blood type O+, allergy to penicillin",
    "Credit card 4111-1111-1111-1111 exp 12/26, billing: 123 Main St, IP: 192.168.1.100",
  ];

  const payload = JSON.stringify({
    Content: piiHeavy[Math.floor(Math.random() * piiHeavy.length)],
    Mode: "Standard",
  });

  const res = http.post(`${BASE_URL}/api/remediation/redact`, payload, {
    headers: { "Content-Type": "application/json" },
  });

  latency.add(res.timings.duration);
  check(res, {
    "status 200": (r) => r.status === 200,
    "has redactions": (r) => JSON.parse(r.body).Stats.TotalRedactions > 0,
    "under 20ms": (r) => r.timings.duration < 20,
  }) || errorRate.add(1);

  sleep(0.1);
}
