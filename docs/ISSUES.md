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
- **M7 – Echte Daten (v1.2)**: real PEGELONLINE Elbe feed alongside the simulator, source selection, threshold what-if panel
- **M8 – Observability sichtbar (v1.3)**: surface the OpenTelemetry path in the dashboard — latency pulse, Jaeger deep-link, optional trace waterfall

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
- [x] A short (≤ 90 s) screen recording of the surge scenario showing the stage
      cascade across gauges.
- [x] README updated with a dashboard screenshot (and/or a clip/GIF).
- [x] A brief "what you're seeing" caption tying it to the WADI threshold story.

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

---

## M7 – Real data (v1.2)

Post-v1.1 work. Replace the simulator as the sole source with a real public
gauge feed (PEGELONLINE / WSV), without touching the `Reading` contract or the
consumer path. The simulator stays available for the scripted surge demo; the
real feed runs alongside it. Like the M6 items, each has explicit acceptance
criteria.

> **Domain note:** PEGELONLINE publishes `W` values in centimetres relative to
> the gauge zero (Pegelnullpunkt, PNP), not in metres NHN. The adapter must
> convert cm → m and add the per-station PNP offset (`gaugeZero.value`, m above
> NHN) to land on true NHN metres. License is Datenlizenz Deutschland Zero 2.0
> (free, attribution-free); no auth required.

### PEGELONLINE source adapter
**Labels:** ingestion, integration
**Milestone:** M7
A source adapter that polls the PEGELONLINE REST-API
(`/stations/{uuid}/W/currentmeasurement.json` for the latest value,
`/stations/{uuid}/W/measurements.json?start=...` to backfill the window) and
emits the same `Reading` records the simulator does. Station UUIDs are
configuration. Honour the API's `ETag` / `Cache-Control` for conditional polling.

**Acceptance criteria:**
- [x] Configured Hamburg Elbe gauges (e.g. St. Pauli, Bunthaus, Over,
  Zollenspieker) by UUID in appsettings, not hard-coded.
- [x] cm → m conversion and PNP → NHN offset applied in an explicit mapping
  layer; a unit test pins a known value end to end.
- [x] Conditional GET via `If-None-Match`; a `304` does not emit a duplicate
  reading.
- [x] Transient API failure (timeout, 5xx) is logged and retried; the path does
  not crash the ingestion service.
- [x] `Reading` contract, evaluator, and consumer path are unchanged; only a new
  source feeds them.

### Source selection: simulator vs. live feed
**Labels:** ingestion, config
**Milestone:** M7
Make the active reading source selectable so the same build can run the scripted
surge (demo) or the real Elbe feed (production-near), without recompiling.

**Acceptance criteria:**
- [x] A single config switch (`ReadingSource: Simulator | Pegelonline`) selects
  the active source at startup.
- [x] A bad/empty source configuration fails at startup, consistent with the
  threshold-options validation pattern.
- [x] Both sources produce identical `Reading` shapes; downstream cannot tell
  them apart.
- [x] README documents how to switch and which gauges the live feed covers.

### Threshold "what-if" panel
**Labels:** frontend, demo
**Milestone:** M7
A read-only dashboard panel that lets the viewer drag the warning/severe
thresholds and see the current windows re-classified live, illustrating the
"thresholds are configuration, not code" decision against real data.

**Acceptance criteria:**
- [x] Dragging a threshold re-derives the displayed stage per gauge client-side;
  no API write, no server state change.
- [x] A "reset to configured thresholds" control restores the appsettings
  values.
- [x] The panel is clearly marked as a local exploration, not a system setting.
- [x] Same-origin relative `/api` behaviour and read-only posture preserved.

---

## M8 – Observability made visible (v1.3) – Reverted

Post-v1.2 work. Surface the OpenTelemetry path that already runs (M3) in the
dashboard, so the pipeline's health is visible, not just wired. Best built after
M7 so the latency signal reflects real ingest. Each item has explicit acceptance
criteria.

### Pipeline latency pulse
**Labels:** observability, frontend
**Milestone:** M8
Expose per-gauge processing latency (receipt → evaluation → state change) from
the existing spans and show it in the dashboard as a small latency sparkline
plus a healthy / degraded indicator. Cheap, domain-honest: a stalled surge
pipeline is a real risk.

**Acceptance criteria:**
- [ ] API exposes a processing-latency figure (last value or p50/p95 over a
  short window) derived from the existing trace path, not a new measurement
  source.
- [ ] The dashboard shows the latency trend and a healthy / degraded state with
  a clear threshold.
- [ ] No change to the evaluator or `Reading` contract; the figure is computed
  from telemetry already collected.
- [ ] Stale telemetry surfaces as "degraded / no recent data", consistent with
  the connection indicator from #27.

### Jaeger deep-link from the dashboard
**Labels:** observability, frontend
**Milestone:** M8
From a gauge (or the latency pulse), link to the corresponding trace view in
Jaeger so a viewer can drill from "this looks slow" to the actual spans.

**Acceptance criteria:**
- [ ] A per-gauge (or per-event) link opens the relevant Jaeger trace/search.
- [ ] The Jaeger base URL is configuration (differs k8s vs. local), not
  hard-coded.
- [ ] The link degrades gracefully when Jaeger is not reachable (e.g. Container
  Apps deploy without it).

### Self-rendered trace waterfall (optional)
**Labels:** observability, frontend, stretch
**Milestone:** M8
A stretch item that renders a single reading's spans as an in-dashboard
waterfall (Gantt-style bars with offsets) from the Jaeger query API, to make the
OpenTelemetry instrumentation legible without leaving the app. Clearly an
"under the hood" tab, kept distinct from the domain view.

**Acceptance criteria:**
- [ ] One trace is fetched via the Jaeger query API and its spans drawn as
  time-offset bars on the existing SVG approach.
- [ ] The view is a clearly separated "under the hood" tab, not mixed into the
  gauge dashboard.
- [ ] Explicitly optional: the milestone is complete without it; it ships only
  if time allows.

## M9 – Readability (v1.3.0)

Post-v1.2 work. Make the displayed values legible: the dashboard should make
clear what the numbers mean and how current they are. Replaces the dropped M8
(see ADR-003 amendment). Like the M6/M7 items, each has explicit acceptance
criteria.

### Measurement age instead of poll age in the freshness indicator
**Labels:** bug, frontend, ingestion
**Milestone:** M9
**Introduced by:** M7 (PEGELONLINE source adapter)
The "live · vor x s" indicator measures the last successful API poll, not the
age of the measurement. With PEGELONLINE (source cadence ~60 s) this is
misleading — a fresh poll against a 50 s old station reading reads as "live".
Switch to age relative to `Reading.Timestamp`, per tile.

**Acceptance criteria:**
- [x] Per tile, age is computed from `Reading.Timestamp` against current time,
  not from the poll timestamp.
- [x] With the Simulator source, age stays ~0 s; "live" remains correct.
- [x] The stale threshold is source-dependent (Simulator: a few seconds;
  PEGELONLINE: > 2× source cadence, e.g. > 120 s), derived from the active
  `ReadingSource`, not hard-coded.
- [x] A normal 60 s PEGELONLINE interval does not trigger a stale state; only
  absence beyond the expected cadence does.
- [x] The existing stale/degraded state is preserved; only the threshold and
  underlying time basis change.
- [x] Read-only and same-origin `/api` unchanged.

### Current wall-clock time in the dashboard header
**Labels:** frontend
**Milestone:** M9
A warning system lacks a time anchor: you can see a measurement's age but not
the current time. A running local clock next to the freshness indicator closes
that.

**Acceptance criteria:**
- [ ] Running clock (HH:MM:SS, Europe/Berlin) in the header, next to the live
  indicator.
- [ ] Updates client-side every second; no API call, no server state.
- [ ] Reads together with the per-tile age as a coherent anchor: current time →
  measurement X s old → status.
