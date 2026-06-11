# ADR-002: Dashboard state structure

**Date:** 2026-06-11  
**Status:** Accepted

---

## Context

The dashboard shows current levels, per-gauge alert status, and a short
recent-history trend. It is read-only and reflects state decided upstream
(ADR-001: the ingestion service owns the alert state). The question is how to
structure the data flow and client state so the view stays simple and
predictable.

Three sub-questions sat open:

- where the read API lives,
- how readings reach the client (REST polling vs. server push),
- how much state belongs in the client, given that alert status is already
  computed server-side.

---

## Decision

### The read API lives on the ingestion host

The ingestion service exposes a small read-only HTTP surface
(`GET /api/gauges`) alongside the reading consumer. The live state already
lives in-process in the ingestion service's state holder; serving it from the
same host keeps a single source of truth and avoids a second component plus a
shared store (Redis/DB) that the dashboard would otherwise need to read
cross-process. The state holder gains only a read accessor; no logic moves.

### REST polling, not server push

The client polls `GET /api/gauges` on a fixed interval (a few seconds). The
simulator emits readings every ~2 s, so a 3–5 s poll is fresh enough, and
polling keeps the client trivial and the data flow predictable. Server push
(SSE/WebSocket) is deliberately not used now: it would pull the alert-event
publishing chokepoint forward, and that point is reserved for v1.1.0 (ADR-001).
When push is wanted, the single `ApplyStageChange` transition point is where it
attaches.

### Stage and trend are shaped server-side

The alert stage is computed server-side and sent as a plain string; the client
renders it and derives nothing. The trend window is held server-side (the state
holder already trims it) and downsampled to a fixed number of points in the API
mapper before it leaves the service — a view concern kept out of the state
holder. The client receives a small, render-ready payload regardless of how
large the raw window is.

### Minimal client state

The client holds no derived or long-lived state: a single polled snapshot of
the gauges, exposed as one signal, is the whole model. No client-side store
(e.g. NgRx), no client-held history beyond what the last response carries.
Because alert status and trend are decided upstream, the client is a pure
projection of the latest server response.

### Angular: standalone + signals

The dashboard is an Angular standalone application using signals for state and
change detection (the current Angular default). In development the dev server
proxies `/api` to the ingestion host, so no CORS configuration is needed; CORS
is revisited only if the dashboard is later served from a different origin
(deploy phase).

---

## Consequences

### Benefits

- One source of truth for gauge state; the dashboard cannot disagree with the
  service (consistent with ADR-001).
- The client stays trivial — a polled signal and a projection — with no store
  framework and no client-side evaluation to keep in sync.
- Payload size is bounded by the server downsample, independent of window
  length, so the trend stays cheap to send and render.

### Trade-offs

- Polling means the view lags reality by up to one interval and issues requests
  even when nothing changed. Acceptable at this cadence and gauge count; a push
  model would remove both at the cost of the deferred alert-event work.
- Hosting the API on the ingestion host couples the read surface to the
  ingestion lifecycle. Acceptable while there is a single service; a separate
  read API would require a shared state store.

### When to revisit

- When alert-event publishing lands (v1.1.0), reconsider replacing or
  augmenting polling with server push off the `ApplyStageChange` chokepoint.
- When the dashboard is served from a different origin than the API (deploy
  phase), add the CORS policy then rather than now.
