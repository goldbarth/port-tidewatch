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
- **M6 – Demo & polish (v1.1)**: storyful data, dashboard polish, richer signals, alert events

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

---

## M6 – Demo & polish (v1.1)

Post-v1 work. v1 (M1–M5) is complete and presentable; these items make it
demonstrable and richer. Unlike the deliberately terse M1–M5 items, each has
explicit acceptance criteria.

### Storm-surge scenario in the simulator
**Labels:** simulator, demo
**Milestone:** M6
The simulator's pure random walk is clamped at 5.0 m, so `severe` (5.50 m) is
never reached and the most important alert stage cannot be shown; a recording of
random noise tells no story. Drive the simulator with a tide baseline plus a
scripted surge event so the alert cascade (normal → warning → severe → recede) is
demonstrable. Input only — no contract or architecture change.

**Acceptance criteria:**
- [x] At least one gauge crosses 4.50 m (warning) **and** 5.50 m (severe), then
      recedes to normal, within a few minutes.
- [x] At least one gauge stays `normal` throughout, for contrast.
- [x] The surge cycle is parameterisable (period / peak via const or env), with a
      tide baseline so motion looks plausible, not jagged noise.
- [x] The evaluator does not flap during the rise (single-outlier damping holds).
- [x] `Reading` contract and the publish path are unchanged; only the simulator
      changes.

### Dashboard visual polish
**Labels:** frontend, demo
**Milestone:** M6
Raise the dashboard from functional to presentable (use the `frontend-design`
skill). Add context and a system overview, refine the visuals.

**Acceptance criteria:**
- [x] Sparklines show the warning (4.50) and severe (5.50) reference lines.
- [x] A header summary shows the count of gauges per stage and the overall status
      / highest current level.
- [x] "seit HH:MM:SS" is replaced by relative time in the current stage
      (e.g. "warning for 3 min").
- [x] Refined layout, typography, and spacing; stage colour changes are animated.
- [x] A last-updated / connection indicator makes stale data visible.
- [x] Still read-only; same-origin relative `/api` behaviour (k8s/dev) preserved.

### Richer monitoring signals
**Labels:** ingestion, frontend, demo
**Milestone:** M6
More to watch per gauge than the level alone.

**Acceptance criteria:**
- [x] API DTO gains per-gauge rate-of-change (m/min over the window) and
      time-in-current-stage; optionally window min/max.
- [x] New fields are computed in the API mapper; the state holder stays raw (per ADR-002).
- [x] The dashboard surfaces them (e.g. a ▲/▼ trend arrow with the rate).
- [x] Unit/integration coverage for the computed fields.

### Demo assets
**Labels:** docs, demo
**Milestone:** M6
Capture the showcase so it survives beyond a live run.

**Acceptance criteria:**
- [ ] A short (≤ 90 s) screen recording of the surge scenario showing the stage
      cascade across gauges.
- [ ] README updated with a dashboard screenshot (and/or a clip/GIF).
- [ ] A brief "what you're seeing" caption tying it to the WADI threshold story.

### Alert-event publishing (deferred from v1.0)
**Labels:** ingestion, v1.1
**Milestone:** M6
The v1.1.0 step recorded in ADR-001: the single `ApplyStageChange` chokepoint
also publishes an alert event so additional consumers (notification, audit) can
subscribe.

**Acceptance criteria:**
- [x] On a genuine stage change, `ApplyStageChange` publishes an alert event
      (gauge, previous → new stage, level, timestamp) to a dedicated exchange.
- [x] Publishing is the only addition at that chokepoint; the state-update
      behaviour is unchanged, and no event is published when the stage holds.
- [x] The publish is traced consistently with the existing OpenTelemetry path.
- [x] Exchange/queue topology is declared in `RabbitMqTransport`; no threshold
      logic is added to the consumer.
- [x] Integration test: one stage transition yields exactly one alert event;
      a held stage yields none.
