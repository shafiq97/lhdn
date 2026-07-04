# MyInvois Gateway — Design Spec

**Date:** 2026-07-04
**Status:** Approved (Approach A — mock-first integration service)
**Author:** Shafiq (with Claude)

## Purpose

Portfolio-grade .NET 8 integration service for Malaysia's LHDN MyInvois e-Invoice system. Demonstrates production-quality API integration work (OAuth2, document mapping, resilience, idempotency) for Upwork/GitHub showcase, and serves as a working foundation for real client engagements once pointed at LHDN's preprod/production APIs.

**Success criteria:**
- `docker compose up` gives a fully working demo (API + mock LHDN + SQLite) with no external dependencies or credentials.
- Swapping to the real MyInvois sandbox requires only configuration changes (base URLs, client ID/secret), no code changes.
- README lets a non-technical client understand what it does; architecture diagram + CI badge + tests signal engineering quality.

## Constraints & Assumptions

- No LHDN sandbox credentials yet (registration requires Malaysian TIN, takes days). All development against a built-in mock that mimics the MyInvois API surface.
- Digital signature (document format v1.1) is **out of scope for v1** — requires an X.509 certificate issued to a registered taxpayer. Mock accepts unsigned documents (v1.0-style). Documented in README as roadmap.
- Windows dev machine; must build/run with .NET 8 SDK + Docker.

## Architecture

Single solution `MyInvoisGateway`, three projects:

```
MyInvoisGateway/
├── src/
│   ├── MyInvoisGateway.Api/       # the integration service (deliverable)
│   └── MockLhdn/                  # fake MyInvois API for local dev/demo
├── tests/
│   └── MyInvoisGateway.Tests/     # xUnit: unit + integration (WebApplicationFactory)
├── deploy/
│   ├── docker-compose.yml
│   └── k8s/                       # Deployment, Service, ConfigMap, Secret manifests
└── .github/workflows/ci.yml      # build + test on push/PR
```

### MyInvoisGateway.Api

ASP.NET Core 8, controller-based (matches enterprise convention clients expect).

**Public endpoints:**
| Method | Route | Behavior |
|---|---|---|
| POST | `/api/invoices` | Accept simplified invoice DTO → validate → map to UBL 2.1 JSON → SHA-256 hash + base64 encode → submit to MyInvois `documentsubmissions` → persist record → return local id + LHDN submission uid. Requires `Idempotency-Key` header; duplicate key returns original result (HTTP 200 with same body), never double-submits. |
| GET | `/api/invoices/{id}` | Local record + latest LHDN status. If status non-terminal and stale (> 30s), refresh from MyInvois `getSubmission` before returning. |
| POST | `/api/invoices/{id}/cancel` | Call MyInvois cancel within the 72-hour window; update local state. |
| GET | `/health` | Liveness/readiness (DB reachable, token acquirable). |

**Internal components:**
- `IMyInvoisClient` — typed client abstraction: `SubmitDocumentAsync`, `GetSubmissionAsync`, `CancelDocumentAsync`. Single implementation `MyInvoisHttpClient` using `HttpClientFactory`; base URL from config (mock vs real sandbox vs production).
- `TokenService` — OAuth2 client-credentials flow against `connect/token`; caches token in memory until 5 min before expiry; thread-safe single-flight refresh.
- `UblMapper` — maps `InvoiceRequest` DTO → UBL 2.1 invoice JSON (document type 01, v1.0). Pure static mapping, fully unit-tested against known-good sample payload.
- `Idempotency` — EF Core table keyed on idempotency key + request hash; conflicting reuse (same key, different body) returns HTTP 422.
- Resilience: Polly on the HTTP client — retry with exponential backoff + jitter on 5xx/timeout (3 attempts), no retry on 4xx; circuit breaker after 5 consecutive failures.
- Typed error model: LHDN validation errors surface as HTTP 422 with the LHDN error detail array; infrastructure failures as 502 with correlation id.

**Invoice state machine:** `Submitted → Valid | Invalid | Cancelled` (mirrors MyInvois lifecycle; `Valid` after LHDN validation passes, `Cancelled` only from `Valid` within 72h).

### MockLhdn

Minimal API project imitating the MyInvois surface the gateway uses:
- `POST /connect/token` — issues a fake JWT, honors client_id/secret from config, 3600s expiry.
- `POST /api/v1.0/documentsubmissions` — validates document structure (required UBL fields, hash matches body), returns submission uid; ~10% of submissions (deterministic by TIN suffix) return `Invalid` on later polls to exercise the failure path.
- `GET /api/v1.0/documentsubmissions/{uid}` — returns status; transitions `InProgress → Valid/Invalid` after configurable delay (default 5s) to exercise polling.
- `PUT /api/v1.0/documents/state/{uid}/state` — cancel; enforces 72h window rule (window shortened to 5 min in mock config for demoability).

### Storage

SQLite via EF Core (file db, zero setup, in-memory for tests). Migrations committed. Tables: `Invoices`, `IdempotencyRecords`. Rationale: demo portability beats engine realism; EF Core means swapping to SQL Server/Postgres is a provider change, stated in README.

### Observability & Ops

- Serilog JSON console logging with correlation id enrichment.
- OpenTelemetry traces (ASP.NET Core + HttpClient instrumentation), console exporter by default, OTLP endpoint via config.
- Dockerfiles for both services; `docker-compose.yml` runs api + mock together.
- K8s manifests in `deploy/k8s` (Deployment, Service, ConfigMap; Secret template for client credentials) — CKA showcase, referenced in README.
- GitHub Actions: restore, build, test on push/PR; badge in README.

## Error Handling Summary

| Failure | Behavior |
|---|---|
| LHDN 400/422 (validation) | Persist `Invalid` + error details; return 422 with LHDN error array |
| LHDN 401 (token expired mid-flight) | Single forced token refresh + one retry, then fail |
| LHDN 5xx / timeout | Polly retry ×3 w/ backoff; then 502 + correlation id; invoice stays `Submitted` for later poll |
| Duplicate `Idempotency-Key`, same body | Return original response (200) |
| Duplicate `Idempotency-Key`, different body | 422, no submission |
| Missing `Idempotency-Key` | 400 |

## Testing

- **Unit:** `UblMapper` (golden sample payload), `TokenService` (expiry/refresh/single-flight), idempotency logic, state machine transitions.
- **Integration:** WebApplicationFactory spinning Api with mock LHDN wired in-process; full happy path (submit → poll → valid), invalid-document path, cancel path, idempotent resubmit, LHDN-down path (mock killed → 502 + retained `Submitted` state).
- Target: every endpoint and every error-table row covered by at least one test.

## Out of Scope (v1) — README Roadmap

- Digital signature / document format v1.1 (needs taxpayer certificate)
- Consolidated invoices, credit/debit notes (document types 02/03/04)
- Real LHDN preprod integration (config swap once registered)
- UI dashboard
- Multi-tenant credentials

## Delivery

- Public GitHub repo `myinvois-gateway` under Shafiq97, MIT license.
- README: what/why, architecture diagram, quickstart (`docker compose up` + curl examples), config reference, roadmap, disclaimer (not affiliated with LHDN).
- Local path: `C:\Users\shafiq\source\repos\myinvois-gateway`.
