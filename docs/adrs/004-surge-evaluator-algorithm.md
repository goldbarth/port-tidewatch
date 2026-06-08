# ADR-004: Surge evaluator stage-determination algorithm

**Date:** 2026-06-08  
**Status:** Accepted

---

## Context

ADR-001 decided *that* the ingestion service evaluates readings against staged,
trend-aware thresholds, and *where* the threshold values live (configuration).
It did not decide *how* the evaluator turns a window of readings into a stage.

This ADR records that algorithm: the concrete rules `SurgeEvaluator` uses to map
a gauge's reading window plus its current stage to a stage name. The issue
("Surge evaluator: staged + trend-based stage determination", M2) asks for three
things — derive the stage from the window, account for a rising trend toward a
boundary, and avoid flapping on single outliers — without prescribing the
mechanism. The decisions below are the open ones that were resolved during the
focused implementation session.

Inputs and constraints carried in from earlier decisions:

- Stages come from `SurgeThresholdOptions`, validated at startup as strictly
  ascending by `MinMeters` with a `normal` stage at 0 (configured: `normal` 0 m,
  `warning` 4.50 m, `severe` 5.50 m NHN).
- The window is already trimmed to `TrendWindow` (30 min) by `GaugeStateHolder`.
- "Not a forecasting model — enough to avoid flapping on single outliers."

Each decision lists the options considered; the chosen option is marked
**(chosen)**. Rationale is recorded in the next section, per decision.

---

## Decisions

### D1 — Which level value determines the base stage

- **Median of the last N readings (chosen)** — robust against single spikes;
  doubles as outlier defence.
- Latest reading only — simplest, most reactive, but one outlier triggers a
  false stage change.
- Average over the window — smooth but laggy, and outliers still pull it.

### D2 — Sample count N for the median

- **Fixed N = 5 (chosen)** — predictable, independent of the simulator's send
  rate; a named constant.
- Whole window — no magic parameter, but N drifts with the send rate.
- N from options — configurable, but adds a config knob plus validation that is
  not needed yet.

### D3 — Anti-flapping when the level abates around a boundary

- **Margin hysteresis (chosen)** — escalate immediately; de-escalate only once
  the median clears the current stage's floor by a fixed margin. Deterministic,
  little state. Constrained by the invariant `HysteresisMargin >= TrendMargin`
  (see D6): a hysteresis band narrower than the trend band leaves a gap in which
  the level flaps between the pre-escalated and base stage, which would defeat
  the rule's purpose.
- None, rely on the median — less code, but residual jitter exactly at a
  boundary remains possible.
- Sustain counter — change stage only after M consecutive readings in the new
  stage; needs more per-gauge state, and the count is measured in readings, so
  the de-escalation delay drifts with the send rate.

### D4 — Evaluator signature (hysteresis needs the current stage)

- **(A) Add a `currentStage` parameter (chosen)** —
  `Evaluate(gaugeId, window, currentStage)`. Keeps the evaluator a pure function,
  testable without state; the consumer wires `GetAlertState` → `Evaluate` →
  `ApplyStageChange`.
- (B) Inject `GaugeStateHolder` and read the stage inside the evaluator — less
  consumer glue, but couples the evaluator to live state and is harder to test.

### D5 — How the rising trend is accounted for

- **Slope sign + proximity (chosen)** — if the level is rising (recent median >
  earlier median) and within `TrendMargin` below the next stage's floor,
  pre-escalate one stage. Simple, no forecast.
- Linear projection — fit a slope and project the boundary crossing; more code
  and edges toward forecasting, which the issue advises against.
- No trend — only the median base stage; less code, but the issue requires a
  trend.

### D6 — Where the tuning parameters live

- **Named constants in `SurgeEvaluator` (chosen)** — `SampleCount = 5`,
  `HysteresisMargin = 0.15 m`, `TrendMargin = 0.15 m`, subject to the invariant
  `HysteresisMargin >= TrendMargin` (guarded in the static constructor). Scope is
  deliberately narrow; they can move into `SurgeThresholdOptions` with validation
  if per-deployment tuning is ever needed — at which point the invariant becomes
  a validation rule.
- Options/appsettings now — more flexible, but unused configuration the project
  does not need yet.

---

## Rationale

_(Step 2 — to be worked through together.)_

### D1 — Median of the last N readings

The window can contain a single anomalous reading — a sensor glitch, a wave
slap, a transient spike — that does not reflect the gauge's actual level. The
base-stage rule must not turn one such value into a stage change, because a
stage change drives an alert. This is the reliability property that decides D1:
the same true water level must map to the same stage regardless of a lone
outlier in the window.

The median has that property structurally. A single spike lands at the end of
the sorted samples, not in the middle, so it cannot move the result; with a full
window of N = 5 it takes three same-direction outliers to shift the median — and
three same-direction readings are no longer an outlier but a genuine trend,
which is exactly what should move the stage (see D5). The latest-reading rule has
the opposite property: one outlier is the whole signal. The average sits in
between — it smooths nothing away, it only dilutes, so a large spike still pulls
the result across a boundary while also lagging real movement.

The median is the standard outlier defence in sensor processing for this reason,
and choosing it here means outlier resistance is a property of the base value
itself rather than a separate filter bolted on afterwards. (With a partial
window of an even number of samples the median averages the two middle values,
so a spike can move it slightly; this affects only the brief start-up transient
before the window fills, and remains more robust than the latest reading.)

### D2 — Fixed N = 5

With the median chosen in D1, "the last N readings" needs a concrete N. The
reliability property at stake here is different from D1: not outlier resistance,
but rate-independence — the same level history must produce the same stage no
matter how fast the simulator (or a real gauge) sends. A fixed N delivers that
directly: five readings are five readings, and the evaluator's behaviour does
not change when the send rate does.

Taking the median over the whole window breaks exactly this. The window is
trimmed to 30 minutes by wall-clock time, so the sample count inside it tracks
the send rate: a dense rate fills it with hundreds of readings and the median
turns sluggish, reacting late to a real surge; a sparse rate leaves only two or
three and the median is barely more robust than the latest reading. The
behaviour would then drift with a parameter the evaluator does not control — the
opposite of the reliability property D2 exists to secure.

Making N a configuration option was rejected for a separate reason: it adds a
knob and its validation (N >= 1, and odd to keep the median a real sample — see
D1) for flexibility no current consumer needs. A fixed named constant is the
smaller surface; promoting it to configuration later is a trivial refactor if a
real tuning need appears (D6).

### D3 — Margin hysteresis

A level hovering at a boundary is the case that produces false alerts: the
median sits right at the stage floor and crosses it back and forth on normal
measurement noise, so the stage — and the alert it drives — toggles on every
reading. D1's median smooths spikes but not this; a value genuinely sitting at
4.50 produces a median at 4.50. The reliability property D3 secures is stage
stability while the level lingers at a boundary, which the median alone cannot
give.

Hysteresis is deliberately asymmetric, and the asymmetry is the point rather
than a compromise: escalation is immediate, because a rising surge must never be
held back, while de-escalation requires the level to clear the stage floor by a
margin before the stage is lowered. Safety upward, quiet downward — the correct
direction for flood monitoring, where a late escalation is a missed warning but
a late de-escalation is merely a stage that lingers one reading longer.

The alternatives were weaker on the same property. Relying on the median alone
leaves the boundary jitter unaddressed. A sustain counter — hold a change until
M consecutive readings confirm it — works but measures its delay in readings, so
the hold time drifts with the send rate, reintroducing the rate-dependence D2
rejected; and it costs per-gauge state the value-based margin does not.

One constraint binds D3 to the trend rule: the hysteresis band must be at least
as wide as the trend band (`HysteresisMargin >= TrendMargin`). Below that, a
level wobbling in the gap between the two bands flaps between the pre-escalated
and base stage on each reading — the very failure hysteresis exists to prevent.
The invariant and its reasoning are recorded in D5, where the trend side of the
coupling lives; here it is enough to note that D3 is only reliable in
conjunction with it.

### D4 — Add a `currentStage` parameter

The hysteresis in D3 needs to know the stage the gauge is currently in — "hold
the current stage" has no meaning without it. That input has to reach the
evaluator somehow, and the choice is where the dependency sits. The reliability
property here is not runtime behaviour but verifiability: the evaluator holds the
entire stage-determination logic, so it is the one place whose correctness must
be provable, and that is easiest when it is a pure function of its inputs.

Passing the current stage as a parameter keeps it pure. `Evaluate(gaugeId,
window, currentStage)` is fully determined by its arguments, so every case — the
boundary examples, the jitter case, the de-escalation threshold — is exercised
by calling it with values and asserting the result, no state to stand up. The
consumer does the wiring: read the stage from `GetAlertState`, call `Evaluate`,
apply the result with `ApplyStageChange`.

Injecting `GaugeStateHolder` and reading the stage inside the evaluator was the
alternative. It saves a little consumer glue but couples the core logic to live
mutable state: every test then has to construct a holder and populate it to
exercise a branch that a single argument would have set directly, and the
evaluator's output stops being a function of visible inputs. The small glue
saving is not worth trading away the property that makes the logic checkable.

### D5 — Slope sign + proximity

The issue is explicit: "not a forecasting model — enough to avoid flapping."
That is a direct instruction toward the simplest mechanism that gives early
warning, and it decides D5 by ruling its main rival out by description. The
reliability property here is minimal assumptions: every parameter a trend rule
introduces is something that can be wrong, so the rule should introduce as few as
possible.

Slope sign plus proximity introduces none beyond the one `TrendMargin`. Two
boolean conditions, both from data already in the window: the level is rising,
and it is within `TrendMargin` below the next floor. If both hold, pre-escalate
one stage. No fit, no horizon, no projected crossing — nothing to tune and
nothing to be wrong about. Linear projection is the opposite: fitting a slope and
projecting a boundary crossing is forecasting in all but name, against the
issue's explicit steer, and each added assumption (fit window, horizon length) is
another way to mislead. Dropping the trend entirely was not an option — the issue
requires it.

"Rising" is defined on the same robust basis as the stage, deliberately:
`median(last N) > median(first N)`, not the last raw reading against the first.
Defining the trend on single readings would hang the rising signal on exactly the
outliers D1 filters out of the stage — a spike at either end would fake a trend.
Measuring both ends with the median keeps the trend consistent with the base
stage and immune to the same noise. (When the window is short the two medians
overlap; this only weakens the trend signal toward "not rising," which is the
safe direction — it under-escalates rather than firing falsely.)

The trend rule is coupled to the hysteresis by an invariant:
`HysteresisMargin >= TrendMargin`. The two margins define two bands below a floor
— the trend pre-escalates from `nextFloor - TrendMargin` upward, and the
hysteresis holds a stage down to `floor - HysteresisMargin`. If the hysteresis
band is narrower than the trend band, a gap opens between them: a level wobbling
in that gap is pre-escalated by the trend on a rising reading, then dropped back
by the hysteresis on the next, flapping every reading — the exact failure D3
exists to prevent, reappearing through the trend. Setting the two margins equal
closes the gap exactly: the trend stops pre-escalating and the hysteresis stops
holding at the same level (with the configured stages, 4.35), so there is no band
in which the stage can oscillate. A wider hysteresis margin would also close the
gap but would overhang into the base stage and delay legitimate de-escalation —
confirmed by the worked example at 4.35, which a wider margin would wrongly hold
at `warning`. Equality is therefore the tightest safe setting, not a tuned
preference: the smallest hysteresis that removes the flap without delaying a real
de-escalation.

### D6 — Named constants in `SurgeEvaluator`

`SampleCount`, `HysteresisMargin`, and `TrendMargin` are the three knobs the
algorithm could expose. The reliability property D6 turns on is surface area:
every value promoted to configuration becomes a path that can be set wrong, a
validation rule to write, and a default to maintain — cost that only pays off
when something actually needs to vary per deployment, and nothing does yet. There
is one consumer and one set of stages.

Keeping the three as named constants makes them inspectable and changeable in one
place without any of that surface. The invariant from D5 sharpens the point
rather than weakening it: the two margins are not independent knobs but a coupled
pair, so exposing them would mean validating `HysteresisMargin >= TrendMargin` at
startup, not just bounding each on its own. That is real complexity to take on
speculatively, for flexibility no one has asked for.

The path forward is deliberately left open. If a real per-deployment tuning need
appears, the constants move into `SurgeThresholdOptions` and the invariant moves
with them as a validation rule — a small, mechanical refactor. Choosing constants
now does not foreclose configuration later; it declines to pay for it before it
is needed.

---

## Resulting algorithm

```
Evaluate(gaugeId, window, currentStage):
  N = 5, HysteresisMargin = 0.15 m, TrendMargin = 0.15 m   # invariant: Hyst >= Trend
  if window empty                      -> "normal"
  level   = median(last N readings)
  earlier = median(first N readings)
  rising  = level > earlier
  base    = highest stage with MinMeters <= level
  # trend pre-escalation
  if base is not the top stage and rising and
     level >= next(base).MinMeters - TrendMargin:
        candidate = next(base)
  else candidate = base
  # de-escalation hysteresis (current treated as "normal" if null/unknown)
  if candidate is below current and
     level > current.MinMeters - HysteresisMargin:
        return current        # hold within the hysteresis band
  return candidate
```

Worked examples with the configured stages (`normal` 0, `warning` 4.50,
`severe` 5.50):

| Median | Trend   | Current   | Result    | Why                                     |
|--------|---------|-----------|-----------|-----------------------------------------|
| 4.55   | rising  | normal    | `warning` | over the boundary, escalate immediately |
| 4.45   | rising  | normal    | `warning` | trend: 4.45 ≥ 4.50 − 0.15, pre-escalated |
| 4.45   | falling | warning   | `warning` | hysteresis: 4.45 > 4.50 − 0.15, held    |
| 4.40   | falling | warning   | `warning` | still in band: 4.40 > 4.35, held — no flap |
| 4.35   | falling | warning   | `normal`  | reaches 4.35: trend trigger and hysteresis floor coincide, de-escalation confirmed |

The fourth row is the case the `HysteresisMargin >= TrendMargin` invariant
protects: a level wobbling around 4.40 sits inside the trend band but is held at
`warning` rather than flapping back to `normal` each reading. Because the two
margins are equal, the trend stops pre-escalating and hysteresis stops holding at
the same level (4.35), so there is no gap between them in which the stage could
oscillate.

---

## Consequences

### Benefits

- Outlier defence, trend pre-escalation, and flap resistance are each a small,
  named rule; behaviour is inspectable and unit-testable as a pure function.

### Trade-offs

- The tuning parameters (D2, D6) are constants, so changing them is a recompile
  until a tuning need justifies moving them to configuration.
- `HysteresisMargin` and `TrendMargin` are coupled by the invariant
  `HysteresisMargin >= TrendMargin`; they cannot be tuned fully independently.
  If they move to configuration, the invariant must move with them as a
  validation rule.
- Trend uses a sign-and-proximity nudge, not a projection; it will not warn far
  ahead of a fast-rising surge — by design.

### When to revisit

- If the simulator/real send rate makes a fixed N = 5 too short or too long a
  horizon (D2).
- If operations need to tune margins per deployment without a recompile (D6).
- If flapping is observed in practice despite the median + hysteresis (D3).
