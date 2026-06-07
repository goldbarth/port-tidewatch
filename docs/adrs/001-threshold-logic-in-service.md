# ADR-001: Threshold evaluation lives in the ingestion service

**Date:** 2026-06-07  
**Status:** Accepted

---

## Context

Water-level readings must be evaluated against storm-surge thresholds modelled
on the Hamburg warning service (WADI). The first warning stage is at 4.50 m
above sea level (NHN); higher stages exist for severe surge. The evaluation
could live in the ingestion service, in the dashboard, or in a separate
component.

Two further questions sit alongside the placement question:

- whether to evaluate on a single instantaneous reading or on a short trend
- where the threshold values themselves are defined

---

## Decision

### Placement

The ingestion service evaluates readings and owns the alert state. The alert
state is computed once, close to the data, as readings are consumed. The
dashboard is a read-only view of an already-decided state and performs no
evaluation of its own. This keeps a single source of truth for the alert state
and avoids duplicating threshold logic across components.

### Staged thresholds

Evaluation is multi-stage (e.g. normal / warning / severe), not a single
boolean over one limit. A staged model matches how the real warning service is
structured and gives the dashboard a meaningful status gradient rather than a
binary flag.

### Trend, not just instantaneous value

A warning reflects an expected surge peak, not a single momentary reading.
Evaluation therefore considers a short window of recent readings (is the level
rising toward a stage boundary?) rather than only the latest value. The window
is deliberately small; this is not a forecasting model, only enough to avoid
flapping on single outliers.

### Thresholds are configuration, not code

Stage names and boundaries live in configuration (appsettings), so a stage can
be added or a boundary adjusted without a recompile. For a warning system,
threshold values are operational data, not application logic.

### Alert events are deferred (v1.1.0)

When the alert stage changes, that transition is handled at a single, isolated
point in the service. For now that point only updates the state. Publishing an
alert event to a dedicated exchange — so additional consumers (notification,
audit) can subscribe — is a planned next step, not part of the initial cut. The
transition point is isolated now so the extension is an addition later, not a
rewrite.

---

## Reference data

All levels are metres above **NHN** (Normalhöhennull), the German national height
datum. For tidal context, the Hamburg warning service also quotes **MThw** (mittleres
Tidehochwasser, mean tidal high water); at St. Pauli 4.50 m NHN ≈ 2.40 m over MThw.

### Warning trigger (WADI / Hamburg Port Authority)

The Hamburger Sturmflutwarndienst (WADI) issues a forecast when an expected surge
peak may exceed:

| Trigger              | Level (NHN) | Level (over MThw) |
|----------------------|-------------|-------------------|
| WADI forecast issued | 4.50 m      | 2.40 m            |

This 4.50 m NHN value is the known, authoritative first warning threshold and anchors
the `warning` stage in configuration.

### Disaster-protection stages (Behörde für Inneres und Sport, Hamburg)

Operational escalation stages keyed to expected peak level:

| Stage | Level (NHN) | Meaning / action                                                |
|-------|-------------|-----------------------------------------------------------------|
| 0     | 3.65–5.00 m | Isolated flooding of harbour streets                            |
| 1     | 5.00–5.50 m | Harbour sections closed; police redirect traffic                |
| 2     | 5.50–6.50 m | Full harbour evacuation; 300+ dike-defence personnel            |
| 3     | 6.50–7.30 m | Harbour sealed; 500+ personnel; Elbtunnel closes at 6.80 m      |
| 4     | ≥ 7.30 m    | Sirens; mass evacuation of low-lying districts; ~1000 personnel |

Context: stage 4 has never been reached; the highest recorded surge was 6.45 m NHN on
1976-01-03.

### BSH surge classification

The Bundesamt für Seeschifffahrt und Hydrographie (BSH) classifies surge severity
relative to MThw for the German North Sea coast (incl. Hamburg). With MThw ≈ 2.10 m
NHN at St. Pauli (derived from 4.50 m NHN = 2.40 m over MThw):

| BSH class              | over MThw | ≈ NHN  |
|------------------------|-----------|--------|
| Sturmflut              | 1.5 m     | 3.60 m |
| schwere Sturmflut      | 2.5 m     | 4.60 m |
| sehr schwere Sturmflut | 3.5 m     | 5.60 m |

### Service-stage mapping

Stage names and boundaries in `SurgeThresholds` are derived from these values but are
configuration, not code — tunable without a recompile:

| Service stage | Boundary (NHN) | Basis                                                                                                                                                          |
|---------------|----------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `normal`      | 0 m            | baseline below any warning                                                                                                                                     |
| `warning`     | 4.50 m         | WADI forecast trigger (= 2.40 m over MThw), the official first warning threshold                                                                               |
| `severe`      | 5.50 m         | converges on two references: BSH *sehr schwere Sturmflut* (3.5 m over MThw ≈ 5.60 m NHN) and disaster-protection Stage 2 (5.50 m NHN, full harbour evacuation) |

The `severe` boundary is set to **5.50 m NHN**: the round disaster-protection Stage 2
value (full harbour evacuation), which also coincides (within 0.10 m) with the BSH
*sehr schwere Sturmflut* line. The two independent classifications agreeing here makes
5.50 m a defensible operational boundary rather than an arbitrary pick.

### Sources

- [Hamburg Port Authority — Hochwasserschutz / WADI](https://www.hamburg-port-authority.de/de/)
- [Behörde für Inneres und Sport — Sturmflut-Maßnahmen](https://www.hamburg.de/politik-und-verwaltung/behoerden/behoerde-fuer-inneres-und-sport/themen/katastrophenschutz/sturmflut-massnahmen-93280)
- [BSH — Sturmfluten (Klassifikation)](https://www.bsh.de/DE/THEMEN/Wasserstand_und_Gezeiten/Sturmfluten/sturmfluten_node.html)

---

## Consequences

### Benefits

- One source of truth for alert state; the dashboard cannot disagree with the
  service.
- Adding or tuning a stage is a configuration change, testable without code
  changes.

### Trade-offs

- The staged + trend logic is slightly more code than a single-value check,
  and needs tests for the boundary and rising-trend cases.

### When to revisit

- The deferred alert-event exchange is committed to as direction (v1.1.0) and
  recorded on the roadmap, so its absence now is a scope decision rather than
  an omission.
