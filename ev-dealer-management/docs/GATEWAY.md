# API Gateway (APIGatewayService)

Ocelot-based gateway in front of the six microservices. Local dev URL:
`http://localhost:5036` (Vite's `VITE_API_BASE_URL` default). See also
[EVENTS.md](EVENTS.md) for the async side of the system.

## Routes — ocelot.json is the single source of truth

`Program.cs` loads `ocelot.json` from disk at startup (it used to build an
19-route in-memory copy while `ocelot.json` sat unused — a change to the file
had no effect). Edit routes in `ocelot.json`; the gateway picks them up on
restart.

| Upstream | Service (dev port) |
|---|---|
| `/api/auth/*`, `/api/users/*`, `/api/admin/users*` | UserService (7001) |
| `/api/vehicles/*`, `/api/dealers/*`, `/api/vehicletypes/*`, `/api/export/*`, `/images/*` | VehicleService (5068) |
| `/api/Orders/*`, `/api/Contracts/*`, `/api/Quotes/*`, `/api/Payments/*`, `/api/Promotions/*`, `/api/Deliveries/*`, `/api/Sales/*` | SalesService (5003) |
| `/api/customers/*`, `/api/TestDrives/*`, `/api/Complaints/*` (+ legacy alias `/api/CustomerService/Complaints/*`) | CustomerService (5039) |
| `/api/reports/*` | ReportingService (5208) |
| `/api/Notification/*` (alias `/api/notifications/*`), `/api/DeviceTokens/*` (Issue #33 registry) | NotificationService (5051) |
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

## Endpoint rewrites (docker-compose readiness)

Every downstream entry in `ocelot.json` uses `localhost` with the service's dev
port. To run the gateway in a compose network, configure `Gateway:Rewrites` —
a **list** of `{ "From", "To" }` pairs whose values are full `host:port`
authorities. The value carries the port too because containers do not listen
on the dev ports (Kestrel binds 80/8080 inside the container; the dev port is
only a host-side mapping):

```json
"Gateway": {
  "Rewrites": [
    { "From": "localhost:7001", "To": "userservice:80" },
    { "From": "localhost:5068", "To": "vehicleservice:8080" },
    { "From": "localhost:5003", "To": "salesservice:80" },
    { "From": "localhost:5039", "To": "customerservice:80" },
    { "From": "localhost:5208", "To": "reportingservice:80" },
    { "From": "localhost:5051", "To": "notificationservice:80" }
  ]
}
```

or env vars `Gateway__Rewrites__0__From=localhost:7001` +
`Gateway__Rewrites__0__To=userservice:80`. `Program.cs` applies the rewrites
to every `DownstreamHostAndPorts` entry in the loaded file and to the
gateway's own `/health` probe list.

Since Issue #78 a `To` may also be **scheme-qualified** —
`https://userservice.onrender.com` (bare host defaults to port 443; `http://`
defaults to 80; an explicit `:port` wins). The scheme then also replaces the
route's `DownstreamScheme`, which is how a Render deployment addresses
services by their public https URLs when private networking isn't available.
Unparseable values (paths, junk ports, empty host) are dropped with a startup
warning and leave the route untouched — the gateway keeps serving on the
un-rewritten address rather than crashing.

Two shape constraints, learned the hard way in review: a **host-only** rewrite
would collapse all six services onto one container name (every entry shares
the host `localhost` and differs only by port), hence full authorities; and
`:` is the .NET config path separator, so it **cannot appear in a key** —
hence a list of pairs instead of a map. The table above is now the final
docker-compose wiring (W5): `docker-compose.yml` sets exactly these six
`Gateway__Rewrites__N__*` env vars on the `apigateway` service, which
publishes **5036:80** so the frontend URL is unchanged. Because of that, do
**not** also run a gateway on the host while compose is up — it collides on
5036, and a host-run gateway would need `localhost:5223/5224/...` mappings
(the published host ports), not these container authorities.

## CORS

`AllowFrontend` policy permits the Vite dev origins
(`http://localhost:5173|5174|5175`) with credentials, applied **before**
`UseOcelot`. The gateway's own `UseHttpsRedirection` was removed: it is served
over plain http in dev/compose, and the redirect broke curl/container callers
that don't follow it.
