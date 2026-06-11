# Runbook: run the dashboard locally in a browser

End-to-end local setup to see the Angular dashboard showing live gauge data:
**RabbitMQ → Simulator → Ingestion (HTTP API) → Angular dashboard**.

You need four things running: a broker, the ingestion service, the simulator
(traffic), and the Angular dev server.

## Prerequisites

- .NET 10 SDK
- Docker daemon running
- Node 22.12+ (`node -v`)
- Frontend deps installed once: `cd frontend && npm install`

## Ports

| Service             | Port  | Note                                  |
|---------------------|-------|---------------------------------------|
| RabbitMQ (AMQP)     | 5672  | broker                                |
| Ingestion HTTP API  | 5080  | **must** be 5080 — the dev proxy points here |
| Angular dev server  | 4200  | open this in the browser              |

---

## 1. Start the broker

> Use the `rabbitmq:3.11` image. In this environment `rabbitmq:3-management`
> (3.13.x) fails to boot with `.erlang.cookie: eacces`. `3.11` is what the
> integration tests use and it starts cleanly.

```bash
docker run -d --rm --name tidewatch-rmq -p 5672:5672 rabbitmq:3.11
# wait until ready:
until docker exec tidewatch-rmq rabbitmq-diagnostics -q ping; do sleep 1; done
```

Want the management UI? Use `rabbitmq:3.11-management` and add `-p 15672:15672`
(UI at http://localhost:15672, guest/guest).

## 2. Start the ingestion service (API + consumer) on port 5080

```bash
ASPNETCORE_URLS=http://localhost:5080 dotnet run --project src/Tidewatch.Ingestion
```

Wait for `Now listening on: http://localhost:5080`. Leave it running.

> The service exports OTLP traces to `localhost:4317`. If no collector/Jaeger is
> running you'll see periodic export-failure log lines — harmless, the API and
> consumer keep working. To see traces, run a Jaeger all-in-one exposing 4317.

## 3. Start the simulator (generates readings)

In a second terminal:

```bash
dotnet run --project src/Tidewatch.Simulator
```

It publishes readings for four gauges (CUX, HEL, STP, BHV) every ~2 s and prints
each one. Leave it running.

## 4. Start the Angular dev server

In a third terminal:

```bash
cd frontend
npx ng serve
```

Wait for `Local: http://localhost:4200/`. The dev server proxies `/api` to the
ingestion service on 5080 (see `frontend/proxy.conf.json`), so no CORS setup is
needed.

## 5. Open the dashboard

Open **http://localhost:4200** in the browser. You should see:

- one card per gauge, updating every ~4 s (the poll interval),
- the current level in metres, a stage badge, and a small trend sparkline,
- the border/badge colour reflecting the stage: green `normal`, amber
  `warning`, red `severe`.

> **Reaching a warning stage:** the simulator does a slightly upward random walk
> clamped to a max of 5.0 m, so levels drift up over time and a gauge will reach
> `warning` (4.50 m) after a while. `severe` (5.50 m) will **not** trigger with
> the default simulator because of the 5.0 m clamp — that's expected. To force a
> stage quickly, raise a gauge's level by editing the simulator, or publish a
> high reading manually.

---

## Quick API checks (without the browser)

```bash
curl -s http://localhost:5080/healthz                 # -> "ok"
curl -s http://localhost:5080/api/gauges | jq         # gauge snapshots + trend
# through the dev proxy (proves the proxy forwards):
curl -s http://localhost:4200/api/gauges | jq
```

## Teardown

- `Ctrl+C` in the simulator, ingestion, and `ng serve` terminals.
- Stop the broker: `docker rm -f tidewatch-rmq` (started with `--rm`, so it is
  also removed on stop).

---

## Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| Broker container exits with `.erlang.cookie: eacces` | Don't use `rabbitmq:3-management` here — use `rabbitmq:3.11`. |
| Dashboard shows "Warte auf Daten…" forever | Ingestion not on 5080, or simulator not running, or broker down. Check `curl http://localhost:5080/api/gauges`. |
| `/api/gauges` empty `[]` | No readings consumed yet — is the simulator running and the broker up? |
| Proxy 504 / ECONNREFUSED in `ng serve` logs | Ingestion isn't listening on 5080. Confirm `ASPNETCORE_URLS=http://localhost:5080`. |
| Ingestion stops right after start | No reachable broker — start the broker (step 1) before ingestion. |
| Port already in use | Something else holds 5672/5080/4200. Stop it or change the port (and update `proxy.conf.json` if you move 5080). |
