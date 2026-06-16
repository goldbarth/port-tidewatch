# ADR-003: Azure Container Apps vs. Kubernetes + Argo CD

**Date:** 2026-06-11  
**Status:** Accepted  
**Amended:** 2026-06-16 — added observability-visualisation scope consequence (M8)

---

## Context

The project needs a deployment story. Two targets are in scope:

- **Azure Container Apps** — a managed, low-ceremony container runtime.
- **Kubernetes + Argo CD** — a self-managed cluster with GitOps continuous
  delivery.

Two constraints shape the choice:

- **No standing cloud cost.** The project runs on a pay-as-you-go subscription
  and must hold at €0. Anything that bills while idle is disqualified as a
  permanent deployment.
- **Demonstrability.** The deployment is part of what the project shows off; a
  reviewer should be able to reproduce it from the repository, not just take a
  screenshot on faith.

Azure Container Apps has a monthly free grant and scales to zero, so brief runs
are effectively free. Azure Kubernetes Service (AKS) has a free control plane
but **billable node VMs** — there is no €0 way to keep an AKS cluster running.

---

## Decision

### Kubernetes + Argo CD is the primary target, run on a local cluster

The Kubernetes + Argo CD GitOps deployment is the main deliverable. Because AKS
nodes are not free, the cluster target is a **local Kubernetes (kind)** rather
than AKS. The manifests and the Argo CD `Application` live in the repository
(`deploy/k8s/`), so the full GitOps flow is reproducible by anyone: create a
local cluster, install Argo CD, point it at the repo, watch it sync. This keeps
the cost at €0 permanently and makes the GitOps story readable and runnable
rather than hidden behind a cloud account.

Moving to AKS later is a matter of swapping the cluster and image source; the
manifests and Argo CD wiring are the portable part and do not change.

### Azure Container Apps is the secondary target, shipped as IaC + CI

Container Apps is delivered as **infrastructure-as-code (azd + Bicep) plus a
CI/CD workflow** in the repository. The artifact — the Bicep modules and the
pipeline — is the deliverable. A deployment can be stood up within the free
grant to verify it, then scaled to zero or torn down; it is not kept running.

### Shared containerization

Both targets consume the same container images (ingestion, simulator,
dashboard). The dashboard is additionally hosted on **Azure Static Web Apps
(Free tier)** for the Container Apps stack — a local cluster cannot be reached
by a cloud-hosted SPA, so the local stack runs the dashboard as an in-cluster
nginx container, and the Container Apps stack pairs with Static Web Apps.

---

## Consequences

### Benefits

- €0 standing cost: the permanent deployment is a local cluster; the cloud
  deployment is on-demand within free tiers and grants.
- The GitOps deployment is fully reproducible from the repo — the strongest form
  of "it works."
- One set of container images serves both targets; the Argo CD/Kustomize wiring
  is portable to AKS later without rework.

### Trade-offs

- A local cluster is not a live public URL; demonstrating it requires running it
  locally rather than handing out a link. Accepted — the reproducibility and €0
  cost outweigh a standing public endpoint.
- Two dashboard hosting paths (in-cluster nginx for Kubernetes, Static Web Apps
  for Container Apps) is slightly more surface than a single path. Accepted to
  keep both targets idiomatic.
- Observability *visualisation* (a dashboard view of the OpenTelemetry path) is
  out of scope on these targets. The instrumentation from M3 stays, but
  surfacing it visually presupposes a distributed deployment with real network
  hops, broker backpressure, and a reachable Jaeger — none of which the kind
  single-node or SWA-Free targets provide. The latency signal on a local
  cluster is a flat near-zero line; it measures an in-memory method call, not a
  pipeline under load, and Jaeger is not reachable from a cloud-hosted SPA. M8
  ("Observability sichtbar") is dropped on this basis — not deferred, since the
  deployment situation that would justify it does not change on its own. See the
  AKS revisit path below: a real cluster is the precondition that makes the
  signal worth showing.

### When to revisit

- If a budget appears, the Kubernetes target can move from kind to AKS by
  swapping the cluster and pointing Argo CD at it — the manifests are already
  the portable part. The same move is the precondition for revisiting M8: a
  distributed cluster is what gives the latency signal something to show.
- If the Container Apps stack ever needs to be permanently live, revisit the
  scale-to-zero/tear-down model against the free grant limits.