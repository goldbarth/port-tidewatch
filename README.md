<p align="center">
  <img src="docs/assets/port-tidewatch-logo.svg" alt="Tidewatch" width="96" height="96" />
</p>

<h1 align="center">Tidewatch</h1>
<p align="center">
  <strong>Water-Level Ingestion &amp; Storm-Surge Alerting</strong><br>
  <sub><code>port-tidewatch</code></sub>
</p>

<p align="center">
A small, focused ingestion service for port water-level telemetry, with
threshold-based storm-surge alerting and a read-only monitoring dashboard.
</p>

<p align="center">
<a href="https://github.com/goldbarth/port-tidewatch/releases"><img src="https://img.shields.io/github/v/release/goldbarth/port-tidewatch?logo=github&label=release" alt="Release"></a>
<a href="https://github.com/goldbarth/port-tidewatch/actions/workflows/ci.yml"><img src="https://github.com/goldbarth/port-tidewatch/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
<a href="https://github.com/goldbarth/port-tidewatch/actions/workflows/deploy-container-apps.yml"><img src="https://img.shields.io/github/actions/workflow/status/goldbarth/port-tidewatch/deploy-container-apps.yml?label=deploy&logo=github" alt="Deploy"></a>
</p>

<p align="center">
<img src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white" alt=".NET 10">
<img src="https://img.shields.io/badge/RabbitMQ-FF6600?logo=rabbitmq&logoColor=white" alt="RabbitMQ">
<img src="https://img.shields.io/badge/OpenTelemetry-000000?logo=opentelemetry&logoColor=white" alt="OpenTelemetry">
<img src="https://img.shields.io/badge/Angular-DD0031?logo=angular&logoColor=white" alt="Angular">
<img src="https://img.shields.io/badge/Docker-2496ED?logo=docker&logoColor=white" alt="Docker">
<img src="https://img.shields.io/badge/Kubernetes-326CE5?logo=kubernetes&logoColor=white" alt="Kubernetes">
<img src="https://img.shields.io/badge/Argo_CD-EF7B4D?logo=argo&logoColor=white" alt="Argo CD">

> 📖 **Written up on my site:** [the project](https://www.goldbarth.dev/projects/port-tidewatch),
> and — in German, leicht verdaulich — [the surge-evaluator decision (ADR-004)](https://www.goldbarth.dev/decisions/surge-evaluator-decisions).

---

The domain is modelled on the Hamburg storm-surge warning service (WADI):
a warning is raised when an expected surge peak can exceed **4.50 m above
sea level (NHN)** / 2.40 m above mean high water (MThw). tidewatch ingests
simulated water-level readings, evaluates them against that threshold, and
surfaces the result.

> **Scope is intentionally narrow:** one domain, one ingestion path, no write
> operations from the UI. The goal is a reliable, observable ingestion
> pipeline end to end.

---

## Why this project

The Hamburg port runs on reliable, observable, security-relevant
infrastructure. tidewatch works the ingestion-and-alerting pattern in a
domain I care about, and takes the next steps in my stack — Angular and
Kubernetes/GitOps — on real ground rather than in the abstract.

---

## What it does

- A simulator emits water-level readings for a set of gauges.
- An ingestion service consumes readings via RabbitMQ (with a dead-letter
  path for poison messages), evaluates each reading against the WADI
  threshold, and emits an alert state.
- A read-only Angular dashboard shows current levels, per-gauge alert status
  (normal / warning / severe), and a short recent-history trend.

---

## Demo

<table>
<tr>
<td width="33%"><img src="docs/assets/demo/tidewatch-dashboard-screenshot-1.png" alt="Tidewatch dashboard with every gauge in the normal stage"></td>
<td width="33%"><img src="docs/assets/demo/tidewatch-dashboard-screenshot-2.png" alt="Tidewatch dashboard during a storm surge, the CUX gauge in the warning stage"></td>
<td width="33%"><img src="docs/assets/demo/tidewatch-dashboard-screenshot-3.png" alt="Tidewatch dashboard at the surge peak, the CUX gauge in the severe stage"></td>
</tr>
<tr>
<td align="center"><sub>Calm — every gauge <code>normal</code>, overall status normal.</sub></td>
<td align="center"><sub>Surge rising — <code>CUX</code> in <code>warning</code> (5.37 m), overall status warning.</sub></td>
<td align="center"><sub>Surge peak — <code>CUX</code> crosses <code>severe</code> (5.83 m), overall status severe.</sub></td>
</tr>
</table>

<p align="center"><sub>▶ <a href="docs/release-notes/v1.1.0.md#demo">Watch the ~60-second surge demo</a> — the full <code>normal → warning → severe → recede</code> cascade.</sub></p>

**What you're seeing.** Each card is a gauge, plotted on a fixed **0–6 m NHN**
scale. The shaded bands *are* the WADI thresholds: **warning at 4.50 m** and
**severe at 5.50 m** (2.40 m over mean high water). One gauge — `CUX` — runs a
scripted storm surge and cascades `normal → warning → severe → recede`; the
others hold `normal` for contrast. The header summarises gauges per stage, the
highest current level, and overall status, while the live / last-updated
indicator makes stale data obvious — turning the threshold story into a single
glance.

---

## Architecture

```
┌─────────────┐    readings     ┌──────────────────┐  alerts / state    ┌─────────────┐
│  simulator  │ ──────────────▶ │ ingestion service│ ─────────────────▶ │  dashboard  │
│   (.NET)    │    RabbitMQ     │      (.NET)      │   REST (polling)   │  (Angular)  │
└─────────────┘                 └──────────────────┘                    └─────────────┘
                                          │
                                          │ poison messages
                                          ▼
                                ┌──────────────────┐
                                │ dead-letter queue│
                                └──────────────────┘
```

### Alert-state lifecycle

A gauge moves between stages as its evaluated level crosses the configured
thresholds (m above NHN). Stages are strictly ordered; the evaluator derives
them from the reading window, not from a single spike.

```
  normal ──(≥ 4.50 m)──▶ warning ──(≥ 5.50 m)──▶ severe
    ▲                       │                       │
    └───────────────────────┴───────────────────────┘
                  (level falls back below stage)
```

Architecture decisions are recorded as ADRs under `docs/adrs/`. Open questions
are tracked there too, so the reasoning is visible even where the
implementation is not finished.

| #   | Concern                       | Decision                                                                                          |
|-----|-------------------------------|---------------------------------------------------------------------------------------------------|
| 001 | Where threshold logic lives   | [Threshold evaluation lives in the ingestion service](docs/adrs/001-threshold-logic-in-service.md) |
| 002 | Dashboard client state        | [Angular state structure](docs/adrs/002-angular-state-structure.md)                               |
| 003 | Deploy target                 | [Azure Container Apps vs. Kubernetes + Argo CD](docs/adrs/003-container-apps-vs-kubernetes.md)     |
| 004 | Stage-determination algorithm | [Surge evaluator algorithm](docs/adrs/004-surge-evaluator-algorithm.md) · [Blog (DE)](https://www.goldbarth.dev/decisions/surge-evaluator-decisions) |

---

## API

The ingestion service exposes a deliberately thin, read-only HTTP surface for
the dashboard. The reading consumer runs as a hosted service alongside it; all
state is live and in-memory.

| Method | Path          | Description                                                                 |
|--------|---------------|-----------------------------------------------------------------------------|
| `GET`  | `/healthz`    | Liveness probe — returns `ok`.                                              |
| `GET`  | `/api/gauges` | Snapshot of every gauge: current level, alert stage, and downsampled trend. |

`/api/gauges` returns one object per gauge:

```jsonc
{
  "gaugeId": "st-pauli",
  "level": 4.62,              // metres above NHN, latest reading (null if none yet)
  "stage": "warning",         // normal | warning | severe
  "changedAt": "2026-06-12T08:41:00Z",
  "trend": [                  // recent window, downsampled to ≤ 24 points
    { "t": "2026-06-12T08:30:00Z", "v": 4.41 },
    { "t": "2026-06-12T08:35:00Z", "v": 4.58 }
  ],
  "rateMetersPerMin": 0.04,   // least-squares rate-of-change over the window (null if < 2 points)
  "timeInStageSeconds": 180,  // how long the gauge has held its current stage (null if none yet)
  "windowMin": 4.38,          // window extent — lowest reading (null if empty)
  "windowMax": 4.71           // window extent — highest reading (null if empty)
}
```

> The derived signals (`rateMetersPerMin`, `timeInStageSeconds`, `windowMin` /
> `windowMax`) are computed in the API mapper, not held in state — the state
> holder stays raw (ADR-002).

---

## Tech Stack

| Concern           | Technology                                |
|-------------------|-------------------------------------------|
| Service & API     | .NET 10 / ASP.NET Core                    |
| Messaging         | RabbitMQ                                  |
| Observability     | OpenTelemetry                             |
| Testing           | xUnit · Testcontainers                    |
| Dashboard         | Angular                                   |
| Containerisation  | Docker                                    |
| Baseline deploy   | Azure Container Apps                       |
| GitOps deploy     | Kubernetes + Argo CD (final phase)        |

---

## Out of scope (deliberately)

The narrow scope is a design choice, not a backlog. Kept out so the pipeline
stays the thing that gets done well:

- **No writes from the UI** — the dashboard is read-only by design (ADR-002).
- **No persistence layer** — gauge state is live, in-memory only; there is no
  historical store. The point is the ingestion path, not a time-series database.
- **No auth on the API** — single-tenant showcase; the surface is two read-only
  endpoints.
- **One domain, one ingestion path** — no multi-network federation, no
  multi-tenancy. A real public gauge feed (PEGELONLINE) is planned alongside the
  simulator in v1.2 (M7); for the current cut the simulator is the sole source.
- **Notification delivery** — `ApplyStageChange` now publishes alert events at
  its single chokepoint (v1.1 / M6), but *acting* on them (email / push, a
  notification consumer) stays out of scope; the showcase ends at the event.

---

## Status

Milestones M1–M6 are complete — **v1.0 is presentable end to end and v1.1 (demo
& polish) has landed.** M7–M8 (v1.2–v1.3) are planned post-v1.1 work. Every
intermediate state is built to stay coherent — see the roadmap.

## Roadmap

Built in milestones, each an intermediate state that stays coherent. The full
issue-by-issue breakdown lives in **[`docs/ISSUES.md`](docs/ISSUES.md)**.

| Milestone                                  | Focus | Status      |
|--------------------------------------------|-------|-------------|
| **M1 · Foundation**                        | Repo scaffold, contracts, threshold configuration | Done        |
| **M2 · Ingestion**                         | RabbitMQ transport, consumer + dead-letter, per-gauge state, surge evaluator | Done        |
| **M3 · Observability & Tests**             | OpenTelemetry tracing, Testcontainers integration tests | Done        |
| **M4 · Dashboard**                         | Angular read-only view — levels, status, trend | Done |
| **M5 · Deploy**                            | Kubernetes + Argo CD (GitOps, primary) and Azure Container Apps (IaC + CI) | Done |
| **M6 · Demo & polish (v1.1)**              | Storm-surge scenario, dashboard polish, richer signals, demo assets, alert events | Done |
| **M7 · Real data (v1.2)**                  | Real PEGELONLINE Elbe feed alongside the simulator, source selection, threshold what-if panel | Planned |
| **M8 · Observability made visible (v1.3)** | Surface the OpenTelemetry path — latency pulse, Jaeger deep-link, optional trace waterfall | Planned |

> **M5 ordering:** Kubernetes + Argo CD is the primary deployment, run on a local
> cluster to stay at €0; Azure Container Apps ships as IaC + CI, deployed on demand.
> The reasoning is in [ADR-003](docs/adrs/003-container-apps-vs-kubernetes.md).

---

## Running it

| Stack | Runbook |
|-------|---------|
| Local dev (broker + ingestion + simulator + `ng serve`) | [runbook-local-dashboard.md](docs/runbook-local-dashboard.md) |
| Kubernetes + Argo CD (local kind, GitOps) | [runbook-k8s-argocd.md](docs/runbook-k8s-argocd.md) |
| Azure Container Apps (azd) | [runbook-container-apps.md](docs/runbook-container-apps.md) |

### Local surfaces

Once the local stack is up, these are the addresses you'll use:

| Surface | URL | Notes |
|---------|-----|-------|
| Dashboard (`ng serve`) | <http://localhost:4200> | Proxies `/api` to the service. |
| Ingestion API | <http://localhost:5080/api/gauges> | Read-only; also `/healthz`. |
| RabbitMQ management UI | <http://localhost:15672> | **Dev-only** — default `guest` / `guest`. |

### .NET solution commands

The solution is a `.slnx` — needs the **.NET 10 SDK**.

```bash
# Build / restore
dotnet build port-tidewatch.slnx
dotnet restore port-tidewatch.slnx

# Run the ingestion service (needs a reachable RabbitMQ — see appsettings RabbitMq section)
dotnet run --project src/Tidewatch.Ingestion

# Run the reading-source host (publishes Reading messages; RABBITMQ_HOST env var overrides host).
# Defaults to the scripted simulator; switch to the live Elbe feed with ReadingSource=Pegelonline.
dotnet run --project src/Tidewatch.Source
ReadingSource=Pegelonline dotnet run --project src/Tidewatch.Source
```

### Reading source: simulator vs. live feed

`Tidewatch.Source` runs exactly one reading source, chosen at startup by the
`ReadingSource` config switch (appsettings key or env var) — the same build serves
the scripted demo or the real feed without recompiling:

| `ReadingSource` | Source | Notes |
|-----------------|--------|-------|
| `Simulator` (default) | Scripted surge | One gauge runs warning → severe → recede; the rest stay normal. |
| `Pegelonline` | Live WSV/PEGELONLINE Elbe feed | Polls the configured Hamburg Elbe gauges; cm → m and PNP → NHN applied in an explicit mapping layer. |

A missing or unrecognised `ReadingSource` fails at startup (same fail-fast posture
as the threshold config). Both sources emit the identical `Reading` shape, so the
ingestion path cannot tell them apart.

The live feed covers four Hamburg Elbe gauges, configured by UUID under the
`Pegelonline` section: **St. Pauli**, **Bunthaus**, **Over**, **Zollenspieker**.
PEGELONLINE data is Datenlizenz Deutschland Zero 2.0 (free, no auth). HPA tidal
gauges expose no `gaugeZero`, so their PNP is set explicitly (Hamburg PNP =
NHN −5.00 m).

---

## Testing

Two .NET test projects plus the Angular suite. Integration tests stand up a
real broker via Testcontainers, so the message path is exercised end to end —
no mocked transport.

| Layer | Project / command | Scope |
|-------|-------------------|-------|
| Unit | `Tidewatch.Ingestion.UnitTests` | Surge-evaluator stage logic — thresholds, trend, outlier handling. |
| Integration | `Tidewatch.Ingestion.IntegrationTests` | Full consume path against a Testcontainers RabbitMQ, incl. the dead-letter route for poison messages. |
| Frontend | `npm test` (in `frontend/`) | Angular component / state tests. |

```bash
# All .NET tests (Docker daemon must be running — Testcontainers starts a RabbitMQ container)
dotnet test

# A single test or class
dotnet test --filter "FullyQualifiedName~SurgeEvaluatorTests"
```

---

## Effort accounting

This is an AI-assisted build, and I'd rather be transparent about it than coy.
v1.0 — the full pipeline (RabbitMQ transport with a dead-letter path, per-gauge
state, the surge evaluator, OpenTelemetry tracing, Testcontainers tests, the
Angular dashboard, and *both* a Kubernetes/Argo CD and an Azure Container Apps
deploy) — came together over roughly **five focused days**.

What got compressed was keystrokes: boilerplate, DTOs, wiring, test scaffolds.
What did **not** get compressed was the thinking. The architectural calls — where
threshold logic lives, the queue topology, the evaluator algorithm, the deploy
ordering — are mine, made deliberately and written down as ADRs before the code
followed. The AI is a fast pair of hands; the design ownership stayed with me.
That's the honest accounting, and it's why the ADRs exist.

---

## License

[MIT](LICENSE) © Felix Wahl
