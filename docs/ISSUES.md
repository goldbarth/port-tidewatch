# Issues

Planned work items for the initial cut. Grouped by milestone. The
`create-issues.sh` script in this directory creates these as GitHub issues via
the `gh` CLI.

Milestones:
- **M1 – Foundation**: repo scaffold, contracts, configuration
- **M2 – Ingestion**: transport, consumer, state, evaluator
- **M3 – Observability & Tests**: OpenTelemetry, integration tests
- **M4 – Dashboard**: Angular read-only view
- **M5 – Deploy**: Container Apps baseline, then Kubernetes + Argo CD

---

## M1 – Foundation

### Contracts: Reading and AlertState records
**Labels:** foundation
**Milestone:** M1
Define shared records in `Tidewatch.Contracts`: `Reading` (GaugeId, Value as
decimal in metres, Timestamp as DateTimeOffset) and `AlertState` (GaugeId,
current stage, timestamp of last stage change). No logic, no dependencies on
other projects.

### Threshold configuration with startup validation
**Labels:** foundation, config
**Milestone:** M1
Add `SurgeThresholdOptions` bound to the `SurgeThresholds` appsettings section:
`Reference`, `TrendWindow` (TimeSpan), ordered stages (Name, MinMeters).
Validate once at startup — stages sorted by MinMeters, no gaps, a "normal"
stage at 0. A bad configuration must fail at startup, not at first surge.

---

## M2 – Ingestion

### RabbitMQ transport infrastructure
**Labels:** ingestion, infrastructure
**Milestone:** M2
Separate infrastructure class owning the RabbitMQ connection, channel, and the
exchange / queue / dead-letter declarations. Keep transport apart from
processing.

### Reading consumer with dead-letter handling
**Labels:** ingestion
**Milestone:** M2
Hosted/background service on the ingestion queue. Receive, deserialise to
`Reading`, nack to dead-letter on deserialisation or basic-validity failure,
store valid readings in the state holder. The evaluator is intentionally not
called from this path yet (see CLAUDE.md / Evaluation). No threshold logic here.

### Per-gauge state holder with isolated stage-change point
**Labels:** ingestion
**Milestone:** M2
Per gauge: reading window (within `TrendWindow`, older discarded) and current
stage. Methods to add a reading, query the window, and `ApplyStageChange` — the
single isolated point called on a stage change. For now updates state only; in
v1.1.0 the same point also publishes the alert event. Singleton; mind thread
safety.

### Surge evaluator: staged + trend-based stage determination
**Labels:** ingestion, core
**Milestone:** M2
`ISurgeEvaluator` taking GaugeId and the current reading window, returning the
stage. Derive the stage from the window, account for rising trend toward a
boundary, check against configured thresholds. Not a forecasting model — enough
to avoid flapping on single outliers. (Focused session — design first.)

---

## M3 – Observability & Tests

### OpenTelemetry tracing across the ingestion path
**Labels:** observability
**Milestone:** M3
Trace the path from message receipt through evaluation to state change. Wire
only at this stage, not during scaffolding.

### Integration tests with Testcontainers
**Labels:** tests
**Milestone:** M3
Cover the boundary case and the rising-trend case end to end against a real
broker via Testcontainers.

---

## M4 – Dashboard

### Angular read-only dashboard
**Labels:** frontend
**Milestone:** M4
Current levels, per-gauge alert status (normal / warning / severe), short
recent-history trend. Read-only. State structure to be recorded in ADR 0002.

---

## M5 – Deploy

> **Ordering note (ADR-003):** Kubernetes + Argo CD is the primary deployment,
> run on a local kind cluster to stay at €0; Azure Container Apps ships as IaC +
> CI, deployed on demand and torn down. This inverts the original
> baseline-first framing below. Both are delivered — see the runbooks:
> [k8s + Argo CD](runbook-k8s-argocd.md), [Container Apps](runbook-container-apps.md).

### Kubernetes manifests + Argo CD GitOps sync
**Labels:** deploy
**Milestone:** M5
Kustomize manifests (`deploy/k8s/base`) for rabbitmq, ingestion, simulator,
dashboard, and jaeger, with an Ingress for same-origin `/api` routing. An Argo CD
`Application` syncs them from the repo; the full flow runs on a local kind cluster.

### End-to-end deploy on Azure Container Apps with CI/CD
**Labels:** deploy
**Milestone:** M5
azd + Bicep infrastructure (`deploy/container-apps`) and GitHub Actions (CI on
push/PR, manual azd deploy via OIDC). The dashboard runs on Azure Static Web Apps
(Free) and reaches the ingestion API cross-origin via CORS. Deployed on demand
within the free grant; torn down to hold €0.
