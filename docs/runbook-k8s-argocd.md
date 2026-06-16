# Runbook: Kubernetes + Argo CD on a local cluster (€0)

Run the whole tidewatch stack on a local **kind** cluster, deployed by **Argo CD**
via GitOps. No cloud, no cost. The same manifests move to AKS later by pointing
Argo CD at a different cluster — see ADR-003.

Stack in-cluster: `rabbitmq` → `ingestion` (HTTP API + consumer) ← `reading-source`,
`dashboard` (nginx SPA), `jaeger` (traces). An Ingress routes `/` to the
dashboard and `/api` to the ingestion API (same origin, no CORS).

## Prerequisites

- Docker
- [`kind`](https://kind.sigs.k8s.io/), `kubectl`
- This repo. For the **GitOps** path, the `deploy/k8s/` manifests must be pushed
  to `main` on GitHub — Argo CD syncs from the repo, not your working tree.

---

## 1. Create the cluster

```bash
kind create cluster --name tidewatch --config deploy/k8s/kind-cluster.yaml
```

## 2. Install the ingress controller (kind build)

```bash
kubectl apply -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/main/deploy/static/provider/kind/deploy.yaml
kubectl wait --namespace ingress-nginx \
  --for=condition=ready pod \
  --selector=app.kubernetes.io/component=controller \
  --timeout=180s
```

## 3. Build images and load them into kind

The manifests use local `:dev` images with `imagePullPolicy: IfNotPresent`, so
they must be present in the cluster (no registry — that keeps it free). **Load
them before syncing**, or pods fail with `ImagePullBackOff`.

```bash
docker build -f src/Tidewatch.Ingestion/Dockerfile -t tidewatch-ingestion:dev .
docker build -f src/Tidewatch.Source/Dockerfile  -t tidewatch-source:dev  .
docker build -f frontend/Dockerfile                  -t tidewatch-dashboard:dev frontend

kind load docker-image --name tidewatch \
  tidewatch-ingestion:dev tidewatch-source:dev tidewatch-dashboard:dev
```

> Re-loading after a rebuild: `kind load` again, then restart the workload —
> e.g. `kubectl rollout restart deployment/ingestion -n tidewatch`.

## 4a. Deploy with Argo CD (the GitOps path)

Manifests must be on `main` first (Argo CD reads the repo). Then:

```bash
# Install Argo CD
kubectl create namespace argocd
kubectl apply -n argocd -f https://raw.githubusercontent.com/argoproj/argo-cd/stable/manifests/install.yaml
kubectl rollout status deployment/argocd-server -n argocd --timeout=300s

# Register the app — Argo CD syncs deploy/k8s/base from the repo automatically
kubectl apply -f deploy/k8s/argocd/application.yaml

# Watch it converge
kubectl get application tidewatch -n argocd -w
```

Argo CD UI (optional):
```bash
kubectl port-forward -n argocd svc/argocd-server 8080:443
# user: admin, password:
kubectl -n argocd get secret argocd-initial-admin-secret -o jsonpath="{.data.password}" | base64 -d; echo
# open https://localhost:8080
```

## 4b. Deploy without Argo CD (quick local trial, no push needed)

To try the stack straight from your working tree, skip Argo CD and apply the
kustomization directly:

```bash
kubectl apply -k deploy/k8s/base
```

This is the same manifests, minus the GitOps layer — handy before pushing.

## 5. Watch it come up

```bash
kubectl get pods -n tidewatch -w
```

Expect `rabbitmq`, `jaeger`, `ingestion`, `reading-source`, `dashboard` all `Running`.
`ingestion` may restart a couple of times until `rabbitmq` is ready — that's the
consumer reconnecting, it settles.

## 6. Open it

- **Dashboard:** http://localhost/ — gauge cards updating every ~4 s.
- **API directly:** http://localhost/api/gauges
- **Jaeger UI** (no ingress route — port-forward):
  ```bash
  kubectl port-forward -n tidewatch svc/jaeger 16686:16686
  # open http://localhost:16686 → service tidewatch-ingestion
  ```

> **Dashboard "Traces ↗" deep-link (M8):** hidden by default because the bundled
> `config.json` leaves `jaegerBaseUrl` empty (Jaeger has no ingress route here).
> To enable it, expose Jaeger (port-forward as above or add an ingress route) and
> set `jaegerBaseUrl` to that reachable base URL in the dashboard's `config.json`.

> **Under-the-hood trace waterfall (M8, optional):** the "Under the hood" tab
> fetches traces from a same-origin `/jaeger-api` path. There is no such ingress
> route in this base, so the tab shows a graceful notice; add a route mapping
> `/jaeger-api` → the Jaeger query service (port 16686) to enable it.

## 7. Teardown

```bash
kind delete cluster --name tidewatch
```

Everything was in the cluster, so this removes it all. €0, nothing left running.

---

## Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| Pods `ImagePullBackOff` for `tidewatch-*` | Images not loaded into kind. Re-run step 3 `kind load`. |
| Argo CD app `OutOfSync`/can't fetch | Manifests not pushed to `main`, or wrong `repoURL` in `application.yaml`. |
| `http://localhost/` not reachable | ingress-nginx not ready (step 2), or kind created without `kind-cluster.yaml` port mappings. |
| `kind create` fails: `Bind for 0.0.0.0:443 failed: port is already allocated` | Host port 80/443 is taken by another container/service. Edit `kind-cluster.yaml` to map a free `hostPort` (e.g. `8443`/`8080`) and use that in the URLs. |
| `ingestion` CrashLoop | Check it can reach `rabbitmq`: `kubectl logs -n tidewatch deploy/ingestion`. RabbitMQ may still be starting. |
| Code change not reflected | Rebuild image → `kind load` → `kubectl rollout restart deployment/<name> -n tidewatch`. |
