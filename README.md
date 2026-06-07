<div align="center">

# 🌊 port-tidewatch

**A small, focused ingestion service for port water-level telemetry, with
threshold-based storm-surge alerting and a read-only monitoring dashboard.**

![Status](https://img.shields.io/badge/status-work_in_progress-yellow)
![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![RabbitMQ](https://img.shields.io/badge/RabbitMQ-FF6600?logo=rabbitmq&logoColor=white)
![OpenTelemetry](https://img.shields.io/badge/OpenTelemetry-000000?logo=opentelemetry&logoColor=white)
![Angular](https://img.shields.io/badge/Angular-DD0031?logo=angular&logoColor=white)
![Docker](https://img.shields.io/badge/Docker-2496ED?logo=docker&logoColor=white)
![Kubernetes](https://img.shields.io/badge/Kubernetes-326CE5?logo=kubernetes&logoColor=white)
![Argo CD](https://img.shields.io/badge/Argo_CD-EF7B4D?logo=argo&logoColor=white)

</div>

* * *

The domain is modelled on the Hamburg storm-surge warning service (WADI):
a warning is raised when an expected surge peak can exceed **4.50 m above
sea level (NHN)** / 2.40 m above mean high water (MThw). tidewatch ingests
simulated water-level readings, evaluates them against that threshold, and
surfaces the result.

> **Scope is intentionally narrow:** one domain, one ingestion path, no write
> operations from the UI. The goal is a reliable, observable ingestion
> pipeline end to end.

* * *

## Why this project

The Hamburg port runs on reliable, observable, security-relevant
infrastructure. tidewatch works the ingestion-and-alerting pattern in a
domain I care about, and takes the next steps in my stack — Angular and
Kubernetes/GitOps — on real ground rather than in the abstract.

* * *

## Status

Work in progress. The repository is built so that every intermediate state
is coherent — see the roadmap below for what is done and what is planned.

## What it does

- A simulator emits water-level readings for a set of gauges.
- An ingestion service consumes readings via RabbitMQ (with a dead-letter
  path for poison messages), evaluates each reading against the WADI
  threshold, and emits an alert state.
- A read-only Angular dashboard shows current levels, per-gauge alert status
  (normal / warning), and a short recent-history trend.

* * *

## Architecture (planned)

```
┌─────────────┐    readings     ┌──────────────────┐  alerts / state    ┌─────────────┐
│  simulator  │ ──────────────▶ │ ingestion service│ ─────────────────▶ │  dashboard  │
│   (.NET)    │    RabbitMQ     │      (.NET)      │     REST / SSE     │  (Angular)  │
└─────────────┘                 └──────────────────┘                    └─────────────┘
                                          │
                                          │ poison messages
                                          ▼
                                ┌──────────────────┐
                                │ dead-letter queue│
                                └──────────────────┘
```

Architecture decisions are recorded as ADRs under `docs/adrs/`. Open questions
are tracked there too, so the reasoning is visible even where the
implementation is not finished.

| #   | Decision                                                                                          |
|-----|---------------------------------------------------------------------------------------------------|
| 001 | [Threshold evaluation lives in the ingestion service](docs/adrs/001-threshold-logic-in-service.md) |
| 002 | [Dashboard state structure](docs/adrs/002-angular-state-structure.md)                             |
| 003 | [Azure Container Apps vs. Kubernetes + Argo CD](docs/adrs/003-container-apps-vs-kubernetes.md)     |

* * *

## Tech Stack

| Concern           | Technology                                |
|-------------------|-------------------------------------------|
| Service & API     | .NET 10 / ASP.NET Core                    |
| Messaging         | RabbitMQ                                  |
| Observability     | OpenTelemetry                             |
| Testing           | Testcontainers                            |
| Dashboard         | Angular                                   |
| Containerisation  | Docker                                    |
| Baseline deploy   | Azure Container Apps                       |
| GitOps deploy     | Kubernetes + Argo CD (final phase)        |

* * *

## Roadmap

Built in milestones, each an intermediate state that stays coherent. The full
issue-by-issue breakdown lives in **[`docs/ISSUES.md`](docs/ISSUES.md)**.

| Milestone | Focus | Status |
|-----------|-------|--------|
| **M1 · Foundation** | Repo scaffold, contracts, threshold configuration | Done |
| **M2 · Ingestion** | RabbitMQ transport, consumer + dead-letter, per-gauge state, surge evaluator | In progress |
| **M3 · Observability & Tests** | OpenTelemetry tracing, Testcontainers integration tests | Planned |
| **M4 · Dashboard** | Angular read-only view — levels, status, trend | Planned |
| **M5 · Deploy** | Azure Container Apps baseline → Kubernetes + Argo CD (GitOps) | Planned |

* * *

## License

[MIT](LICENSE) © Felix Wahl
