import http from "k6/http";
import { check, sleep } from "k6";
import { Rate, Trend } from "k6/metrics";

const errorRate = new Rate("errors");
const detLatency = new Trend("deterministic_latency", true);

export const options = {
  scenarios: {
    pii_detection: {
      executor: "constant-vus",
      vus: 100,
      duration: "2m",
      exec: "piiDetection",
      tags: { scenario: "pii" },
    },
    schema_validation: {
      executor: "constant-vus",
      vus: 100,
      duration: "2m",
      exec: "schemaValidation",
      startTime: "0s",
      tags: { scenario: "schema" },
    },
    keyword_matching: {
      executor: "constant-vus",
      vus: 100,
      duration: "2m",
      exec: "keywordMatching",
      startTime: "0s",
      tags: { scenario: "keyword" },
    },
    spike_test: {
      executor: "ramping-vus",
      startVUs: 10,
      stages: [
        { duration: "30s", target: 100 },
        { duration: "30s", target: 1000 },  // 10x spike
        { duration: "30s", target: 1000 },
        { duration: "30s", target: 10 },
      ],
      exec: "mixedWorkload",
      startTime: "2m",
      tags: { scenario: "spike" },
    },
  },
  thresholds: {
    "deterministic_latency": ["p(50)<20", "p(95)<50", "p(99)<100"],
    "errors": ["rate<0.01"],
    "http_req_duration": ["p(95)<100"],
  },
};

const BASE_URL = __ENV.BASE_URL || "http://localhost:5000";

const PII_PAYLOADS = [
  "Contact john.doe@example.com for project details and call 555-123-4567",
  "Patient SSN: 123-45-6789, admitted for evaluation on 01/15/1990",
  "Transfer to IBAN DE89370400440532013000 for invoice payment",
  "Credit card 4532-1234-5678-9012 expires 12/25, CVV 123",
  "Send report to user@company.co.uk, CC: admin@internal.org",
  "Employee ID: E-12345, DOB: 03/22/1985, Department: Engineering",
  "The weather is nice today and I plan to go hiking in the mountains",
  "Quarterly report shows 15% growth in Q3 revenue metrics",
];

const INJECTION_PAYLOADS = [
  "'; DROP TABLE users; --",
  '<script>alert("xss")</script>',
  "; cat /etc/passwd",
  "1 UNION SELECT username, password FROM users",
  "Normal search query for product information",
];

const CREDENTIAL_PAYLOADS = [
  "password = SuperSecret123! in config.yml",
  "AKIAIOSFODNN7EXAMPLE is the AWS key",
  "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U",
  "No secrets here, just a regular message about the project",
];

function randomFrom(arr) {
  return arr[Math.floor(Math.random() * arr.length)];
}

export function piiDetection() {
  const payload = JSON.stringify({
    Content: randomFrom(PII_PAYLOADS),
    ResourceType: "text",
    Source: "user_input",
    Frameworks: ["GDPR", "HIPAA"],
  });

  const res = http.post(`${BASE_URL}/api/pipeline/detect`, payload, {
    headers: { "Content-Type": "application/json" },
  });

  detLatency.add(res.timings.duration);
  check(res, {
    "status 200": (r) => r.status === 200,
    "has decision": (r) => JSON.parse(r.body).Decision !== undefined,
    "under 50ms": (r) => r.timings.duration < 50,
  }) || errorRate.add(1);
}

export function schemaValidation() {
  const validJson = '{"name": "test", "value": 42, "nested": {"key": "val"}}';
  const invalidJson = '{"name": "test", "value": 42, "nested": {"key": "val"';

  const payload = JSON.stringify({
    Content: Math.random() > 0.5 ? validJson : invalidJson,
    ResourceType: "config",
    Source: "api_request",
    Frameworks: ["SOC2"],
  });

  const res = http.post(`${BASE_URL}/api/pipeline/detect`, payload, {
    headers: { "Content-Type": "application/json" },
  });

  detLatency.add(res.timings.duration);
  check(res, { "status 200": (r) => r.status === 200 }) || errorRate.add(1);
}

export function keywordMatching() {
  const contents = [
    "This document is classified and for internal only distribution",
    "The confidential report contains restricted information",
    "Public information about our company products and services",
    "Meeting notes from the weekly team standup",
  ];

  const payload = JSON.stringify({
    Content: randomFrom(contents),
    ResourceType: "text",
    Source: "user_input",
    Frameworks: ["SOC2"],
  });

  const res = http.post(`${BASE_URL}/api/pipeline/detect`, payload, {
    headers: { "Content-Type": "application/json" },
  });

  detLatency.add(res.timings.duration);
  check(res, { "status 200": (r) => r.status === 200 }) || errorRate.add(1);
}

export function mixedWorkload() {
  const isDeterministicOnly = Math.random() < 0.8; // 80% deterministic

  if (isDeterministicOnly) {
    const payloads = [...PII_PAYLOADS, ...INJECTION_PAYLOADS, ...CREDENTIAL_PAYLOADS];
    const payload = JSON.stringify({
      Content: randomFrom(payloads),
      ResourceType: "text",
      Source: "user_input",
      Frameworks: ["GDPR", "SOC2"],
    });

    const res = http.post(`${BASE_URL}/api/pipeline/detect`, payload, {
      headers: { "Content-Type": "application/json" },
    });

    detLatency.add(res.timings.duration);
    check(res, { "status 200": (r) => r.status === 200 }) || errorRate.add(1);
  } else {
    // 20% with LLM check (via workflow)
    const payload = JSON.stringify({
      content: randomFrom(PII_PAYLOADS),
      resource_type: "text",
      frameworks: ["GDPR"],
      source: "user_input",
    });

    const res = http.post(`${BASE_URL}/api/workflow/run`, payload, {
      headers: { "Content-Type": "application/json" },
      timeout: "10s",
    });

    check(res, { "status 200": (r) => r.status === 200 }) || errorRate.add(1);
  }

  sleep(0.1);
}
