# ADR-002: Dashboard state structure

**Date:** TBD  
**Status:** Proposed

---

## Context

The dashboard shows current levels, per-gauge alert status, and a short
recent-history trend. It is read-only and reflects state decided upstream.
The question is how to structure client state so the view stays simple and
the data flow stays predictable.

---

## Decision

_To be written._ Open questions: how readings arrive (REST polling vs. SSE),
where the trend window is held, and how much state belongs in the client at
all given that alert status is computed server-side.

---

## Consequences

### Benefits

_To be written._

### Trade-offs

_To be written._

### When to revisit

_To be written._
