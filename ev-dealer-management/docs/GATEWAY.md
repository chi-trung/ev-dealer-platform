# API Gateway (APIGatewayService)

Ocelot-based gateway in front of the six microservices. Local dev URL:
`http://localhost:5036` (Vite's `VITE_API_BASE_URL` default). See also
[EVENTS.md](EVENTS.md) for the async side of the system.

## Routes — ocelot.json is the single source of truth

`Program.cs` loads `ocelot.json` from disk at startup (it used to build an
19-route in-memory copy while `ocelot.json` sat unused — a change to the file
had no effect). Edit routes in `ocelot.json`.

| Upstream | Service (dev port) |
|---|---|
| `/api/auth/*`, `/api/users/*`, `/api/admin/users*` | UserService (7001) |
| `/api/vehicles/*`, `/api/dealers/*`, `/api/vehicletypes/*`, `/api/export/*`, `/images/*` | VehicleService (5068) |
| `/api/Orders/*`, `/api/Contracts/*`, `/api/Quotes/*`, `/api/Payments/*`, `/api/Promotions/*`, `/api/Deliveries/*`, `/api/ProcessedReservations/*`, `/api/Sales/*` | SalesService (5003) |
| `/api/customers/*`, `/api/TestDrives/*`, `/api/Complaints/*` (+ legacy alias `/api/CustomerService/Complaints/*`) | CustomerService (5039) |
| `/api/reports/*` | ReportingService (5208) |
| `/api/Notification/*` (alias `/api/notifications/*`) | NotificationService (5051) |
| `/api/health/{user,vehicle,sales,customer,reporting,notification}` | each service's `/health` |

## Health

- **Gateway:** `GET /health` — aggregates all six services by calling their
  `/health` in parallel (3s timeout). `200` when every service is healthy,
  otherwise `503` with a JSON body listing each service as
  `healthy | degraded | unreachable`. Implemented as a `Map()` branch
  **before** `UseOcelot` — Ocelot's responder 404s any path mapped after it.
- **Per service:** every service exposes `GET /health` via
  `AddHealthChecks()`/`MapHealthChecks` (NotificationService keeps its custom
  JSON endpoint; VehicleService already had it). Gateway routes listed above
  proxy them one level down as `/api/health/<name>`.

## Host rewrites (docker-compose readiness)

Every downstream entry in `ocelot.json` uses `localhost`. To run the gateway in
a compose network, set a rewrite map — `Program.cs` applies it to the loaded
routes (and the health probe list) at startup:

```json
"Gateway": { "Hosts": { "localhost": "userservice" } }
```

or env `Gateway__Hosts__localhost=userservice`. One map, applied to all
routes — per-service container names come later in W5 if services get distinct
DNS names (then use `Gateway__Hosts__user-service`-style keys instead; the
rewrite is per-host-value, so distinct hosts in the file rewrite
independently).

## CORS

`AllowFrontend` policy permits the Vite dev origins
(`http://localhost:5173|5174|5175`) with credentials, applied **before**
`UseOcelot`. The gateway's own `UseHttpsRedirection` was removed: it is served
over plain http in dev/compose, and the redirect broke curl/container callers
that don't follow it.
