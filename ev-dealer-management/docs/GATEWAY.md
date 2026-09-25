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
| `/api/customers/*`, `/api/TestDrives/*`, `/api/CustomerService/Complaints/*` (rewrites to the controller's `/api/Complaints/*`) | CustomerService (5039) |
| `/api/reports/*` | ReportingService (5208) |
| `/api/notifications/*` (rewrites to the controller's `/api/Notification/*`), `/api/DeviceTokens/*` (Issue #33 registry) | NotificationService (5051) |
| `/api/health/{user,vehicle,sales,customer,reporting,notification}` | each service's `/health` |

## Health

The gateway exposes two health endpoints with different meanings — do not
substitute one for the other:

- **`GET /health/live` — liveness.** Answers `200` with
  `{"status":"healthy",...}` as soon as the process has built its pipeline and
  reached `app.Run()`. Depends on **nothing** upstream. This is what a deploy
  gate or crash-loop probe should poll: a routing gateway's own readiness says
  nothing about whether its upstreams are up.
- **`GET /health` — readiness.** Aggregates all six services by calling their
  `/health` in parallel (3s timeout). `200` when every service is healthy,
  otherwise `503` with a JSON body listing each service as
  `healthy | degraded | unreachable`. Implemented as a `Map()` branch
  **before** `UseOcelot` — Ocelot's responder 404s any path mapped after it.

Both are prefix branches under `app.Map`, and `branch.Run()` is terminal with
no fallthrough, so **the more specific path must register first**:
`/health/live` is registered before `/health`, or the `"/health"` prefix
swallows it (by `PathString.StartsWithSegments`, not exact equality) and the
liveness endpoint silently answers the aggregate's `503` — the failure mode
issue #133 was opened for. Add any further `/health/*` route at the top of
that block.

**Per service:** every service exposes `GET /health` via
`AddHealthChecks()`/`MapHealthChecks`, and since #135 that endpoint is a real
readiness probe, not the framework's unconditional 200. Two checks are
registered through `Common.Health`:

- **`rabbitmq`** — opens its OWN short-lived connection to the configured
  broker. It deliberately does *not* ask the registered publisher for its
  connection state: every broker client here is an `AddSingleton` that .NET
  constructs on first resolution, and nothing at startup resolves one, so the
  container legitimately returns null at boot even with a healthy broker —
  reporting that as Unhealthy would be a false negative that fails the first
  deploy gate. `AddHostedService<T>` consumers are not resolvable by their own
  type at all.
- **`database`** — `CanConnect` against the service's own DbContext.

Both are tagged `readiness`. The response is JSON naming the failing
dependency (`{"status":"Unhealthy","entries":{"rabbitmq":{"status":"Unhealthy",
"description":"RabbitMQ is unreachable at localhost:5672"}}}`) rather than the
bare `Unhealthy` string the framework writes by default — which is what makes
the gateway's 503 actionable. NotificationService's old hardcoded-200 `MapGet`
was replaced by the same `MapHealthChecks`; UserService and ReportingService,
which have no broker client, register the database check only. Gateway routes
listed above proxy them one level down as `/api/health/<name>`.

Note the asymmetry: every service runs `Migrate()`/`EnsureCreated()` before
`app.Run()`, and #90 made a bad connection string rethrow out of startup, so an
unreachable DB at boot kills the process and the `database` check's Unhealthy
arm is reached only when the DB drops after startup. A broker failure at boot
leaves the process alive and listening, which is exactly why `/health` must
not answer 200 for it.

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
gateway's own `/health` aggregate probe list (not `/health/live`, which
probes nothing upstream).

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
