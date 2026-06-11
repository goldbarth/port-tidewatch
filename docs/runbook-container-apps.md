# Runbook: Azure Container Apps deploy (azd) — the checkbox stack

The Container Apps deployment is a **checkbox/IaC artifact** (ADR-003): infrastructure
as Bicep + a CI/CD workflow, deployed on demand within the free grant and **torn down
afterwards** to stay at €0. The primary deployment is the local Kubernetes + Argo CD
stack (`runbook-k8s-argocd.md`).

Topology: `rabbitmq` (internal TCP) ← `ingestion` (external HTTP API + consumer) ←
`simulator`; the `dashboard` runs on Azure Static Web Apps (Free) and calls the
ingestion API cross-origin (CORS).

## Prerequisites

- [Azure Developer CLI (`azd`)](https://aka.ms/azd) and [`az`](https://aka.ms/azcli)
- An Azure subscription (pay-as-you-go); be ready to **delete it all afterwards**
- Docker (azd builds the images locally)

---

## 1. Deploy locally with azd

```bash
cd deploy/container-apps
azd auth login
azd up
```

`azd up` prompts for an environment name, a location, and the subscription, then:
provisions the Bicep infra → builds the ingestion/simulator images and pushes them to
ACR → builds the Angular app and deploys it to the Static Web App.

On success azd prints outputs, including:
- `INGESTION_FQDN` — the public ingestion host
- `DASHBOARD_URL` — the Static Web App URL

> **Known issue — the dashboard step fails under azd.** azd deploys the Static Web App
> with the SWA CLI using the environment name `default`, which the service rejects
> (`The environment name "default" is invalid`). The infra, ingestion, and simulator
> still provision/deploy fine; deploy the dashboard manually to the `production`
> environment instead (step 2b). If azd stopped before the container apps deployed, run
> `azd deploy ingestion` and `azd deploy simulator`.

## 2. Wire the dashboard to the API (cross-origin)

The SPA and the API are on different origins, so two one-time links are needed.

**a) Allow the dashboard origin on the API (CORS):**
```bash
az containerapp update \
  --name ingestion \
  --resource-group rg-<AZURE_ENV_NAME> \
  --set-env-vars Cors__AllowedOrigin=https://<DASHBOARD_URL>
```
(`Cors__AllowedOrigin` → the `Cors:AllowedOrigin` config the API reads.)

**b) Point the SPA at the API and deploy it manually.** Set the API base URL, rebuild,
and deploy to the `production` environment with the SWA CLI (works around the azd
`default`-env issue above). Run from the repo root; replace the host with your
`INGESTION_FQDN` and the SWA name with yours (`az staticwebapp list -g rg-<env>`):
```bash
printf '{\n  "apiBaseUrl": "https://<INGESTION_FQDN>"\n}\n' > frontend/public/config.json
( cd frontend && npm run build )
TOKEN=$(az staticwebapp secrets list -n <swa-name> -g rg-<AZURE_ENV_NAME> --query "properties.apiKey" -o tsv)
npx -y @azure/static-web-apps-cli deploy frontend/dist/tidewatch-dashboard/browser --deployment-token "$TOKEN" --env production
rm -f .env   # the SWA CLI writes the deployment token here — do not commit it
```
> Empty `apiBaseUrl` keeps the SPA on relative `/api` (the Kubernetes/dev behaviour) —
> only the cloud stack needs it set. Revert it before committing
> (`git checkout frontend/public/config.json`) so the repo default stays same-origin.

## 3. Verify

Open `DASHBOARD_URL` in a browser → gauge cards updating. Or hit the API directly:
```bash
curl https://<INGESTION_FQDN>/api/gauges
```

## 4. Tear down (back to €0)

```bash
azd down --purge --force
```
Removes the resource group and all resources. Nothing keeps billing.

---

## CI/CD: the GitHub deploy workflow (optional)

`.github/workflows/deploy-container-apps.yml` runs the same `azd up` on
**manual trigger** (`workflow_dispatch`) via federated OIDC (no stored secret).

Easiest setup — let azd wire the OIDC app + GitHub config for you:
```bash
cd deploy/container-apps
azd pipeline config --provider github
```

Or set these GitHub repository **Variables** manually (after creating an app
registration with a GitHub federated credential):
`AZURE_ENV_NAME`, `AZURE_LOCATION`, `AZURE_SUBSCRIPTION_ID`, `AZURE_CLIENT_ID`,
`AZURE_TENANT_ID`.

`ci.yml` (build + test on push/PR) needs no Azure access.

---

## Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| `AKSCapacityHeavyUsage` creating the Container Apps environment | Azure-side capacity in that region (CA environments run on AKS). Switch region: `azd down --purge --force`, `azd env set AZURE_LOCATION northeurope` (or `germanywestcentral`/`swedencentral`), `azd up`. The RG region is immutable, so tear down first. |
| Dashboard step fails: `The environment name "default" is invalid` | azd deploys the SWA with env `default`, which the service rejects. Deploy manually with `--env production` (step 2b). |
| `azd up` fails creating the Static Web App | SWA is region-limited. Set `staticWebAppLocation` (e.g. `westeurope`, `eastus2`) — it is a separate Bicep param from the main `location`. |
| ingestion revision can't pull from ACR | AcrPull role assignment may still be propagating on the first deploy. Re-run `azd deploy ingestion` after a minute. |
| ingestion can't reach the broker | rabbitmq uses internal TCP ingress on 5672; the API reaches it at `rabbitmq` (the `RabbitMq__HostName` env). Check the rabbitmq revision is running. |
| Dashboard loads but shows no data | CORS origin not set (step 2a) or `config.json` still empty/not redeployed (step 2b). Check the browser console for CORS errors. |
| OTLP export errors in ingestion logs | Expected — the Container Apps stack ships no tracing backend (ADR-003). Harmless; the API and consumer keep working. |
