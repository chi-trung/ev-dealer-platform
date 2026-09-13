# Event Topology (RabbitMQ)

Canonical event bus for the EV Dealer microservices. Every publish is **persistent**
(`Persistent = true`) and every consumer **acks manually** (`autoAck: false`,
`BasicQos` prefetch 1 so one delivery is in flight per channel — this also keeps
same-email check-then-inserts from racing the unique index).

**Failure policy (W4, `Events/EventRetryPolicy.cs` in NotificationService and
CustomerService — identical copies, services share no assembly):**

- **Malformed payload** (empty/garbage body that can never parse): parked in
  `<queue>.dlq` — previously ack-and-discarded, which lost it invisibly.
- **Handler threw**: re-published to `<queue>.retry` (a queue with
  `x-message-ttl` = `RabbitMQ:RetryTtlMilliseconds`, default 5000ms) and the
  original delivery is acked; TTL expiry dead-letters it back to the main queue
  via the default exchange. The attempt count rides the broker-maintained
  `x-death` header; past `RabbitMQ:MaxDeliveryAttempts` (default 3) the message
  goes to `<queue>.dlq` instead of retrying again.
- The main queues are declared **without** x-dead-letter arguments (RabbitMQ
  refuses to redeclare an existing queue differently — live brokers already
  have them); the retry hop is explicit re-publish + ack. NotificationService
  handlers now `throw` after logging so this policy sees processing errors
  (including failed FCM sends) instead of swallowing them and acking.
- Changing `RetryTtlMilliseconds` on a broker that already has a `*.retry`
  queue makes the boot-time re-declare fail with a channel-level
  PRECONDITION_FAILED. `DeclareRetryTopology` runs that declare on a throwaway
  channel and logs the remedy instead of letting it kill the consumer: the old
  TTL stays in effect until an operator deletes the retry queue
  (`docker exec evm_rabbitmq rabbitmqctl delete_queue <queue>.retry` — drops
  messages pending retry) and restarts the service.

Broker connection settings live under the `RabbitMQ` config section of each service:
`HostName` / `Port` / `UserName` / `Password` (env override: `RabbitMQ__HostName` etc.).
`HostName` is the single key name across all services — in docker-compose it is
`rabbitmq`, for local runs `localhost`.

## Exchanges

| Exchange | Type | Publishers | Purpose |
|---|---|---|---|
| `vehicle_events` | topic | VehicleService, CustomerService | Vehicle domain events (`vehicle.*`) and test-drive scheduling (`testdrive.*`) |
| `customer_events` | topic | CustomerService | Customer lifecycle events (`customer.*`) |
| default (`""`) | direct-by-queue | SalesService | Sales events routed straight to a queue named by routing key |

## Event catalog

### `vehicle_events` (topic)

| Routing key | Producer (payload) | Consumer queues |
|---|---|---|
| `vehicle.created` | VehicleService — `VehicleCreatedEvent` (VehicleId, Model, Type, Price, DealerId, CreatedAt) | `vehicle.created` (NotificationService → log-only; payload carries no DeviceToken) |
| `vehicle.updated` | VehicleService — `VehicleUpdatedEvent` (VehicleId, Model, Type, Price, DealerId, UpdatedAt) | `vehicle.updated` (NotificationService → log-only; same) |
| `vehicle.deleted` | VehicleService — `VehicleDeletedEvent` (VehicleId, DeletedAt) | `vehicle.deleted` (NotificationService → log-only; same) |
| `vehicle.reserved` | VehicleService — `VehicleReservedEvent` (VehicleId, VehicleName, VehiclePrice, DealerId, CustomerName/Email/Phone, ColorVariantId/Name, Quantity, Notes, ReservedAt, DeviceToken) | `vehicle.reserved` (NotificationService → push), `customer_vehicle_reserved` (CustomerService → create/update customer + purchase) |
| `testdrive.scheduled` | CustomerService — `TestDriveScheduledEvent` (TestDriveId, CustomerId, VehicleId, DealerId, CustomerEmail, CustomerName, VehicleModel*, ScheduledDate, DeviceToken*) | `testdrive.scheduled` (NotificationService → push; token via registry when payload carries none) |

\* `VehicleModel` is currently empty (CustomerService has no local vehicle catalog) and
`DeviceToken` is null (the booking API doesn't collect one yet) — the consumer falls
back to `#<VehicleId>` in the notification and resolves the token from the
device-token registry (`customer:<CustomerId>`, Issue #33) before skipping push.

### `customer_events` (topic)

| Routing key | Producer (payload) | Consumer queues |
|---|---|---|
| `customer.created` | CustomerService — `CustomerCreatedEvent` (CustomerId: int, Name, Email, Timestamp) | `customer.created` (NotificationService → push via registry, `customer:<CustomerId>`; payload carries no DeviceToken — Issue #35) |
| `customer.updated` | CustomerService — `CustomerUpdatedEvent` (CustomerId, Name, Email, Phone, Address, Status, Timestamp) | `customer.updated` (NotificationService → push via registry, same resolution) |
| `customer.deleted` | CustomerService — `CustomerDeletedEvent` (CustomerId, Timestamp) | `customer.deleted` (NotificationService → push via registry, same resolution) |

Each queue binds `customer_events` by its EVENT routing key (never the
queue name — renames via `RabbitMQ:Queues:*` must not unbind), declares its
own `.retry`/`.dlq` triplet via the shared `EventRetryPolicy`, and dispatches
to `Consumers/Customer{Created,Updated,Deleted}Consumer.cs` — push-capable via
the device-token registry — multicast to all tokens for
`customer:<CustomerId>`, throw on `success == false`, log-only + logged
subject when nothing is registered (registry-only: unlike `order.created`
these payloads carry no `DeviceToken`, so there is no in-band-wins branch).

### Default exchange (SalesService)

SalesService publishes with `basicProperties.Persistent = true` to the default
exchange; the routing key **is** the queue name (from `RabbitMQ:Queues:*` config):

| Routing key = queue | Producer (payload) | Consumer |
|---|---|---|
| `sales.completed` | SalesService OrdersController — `SaleCompletedEvent` (OrderId, CustomerEmail, CustomerName, VehicleModel, TotalPrice, CompletedAt, DeviceToken?) | NotificationService `sales.completed` → push (skipped without DeviceToken; no CustomerId to look a registry key by) |
| `order.created` | SalesService OrdersController — `OrderCreatedEvent` (no DeviceToken field) | NotificationService `order.created` → push via registry (`customer:<CustomerId>`, Issue #33); log-only when the customer has no registered device |
| `quote.created` | SalesService QuotesController — `QuoteCreatedEvent` (no DeviceToken field) | NotificationService `quote.created` → same registry resolution as `order.created` |
| `contract.created` | SalesService ContractsController — `ContractCreatedEvent` (ContractId, ContractNumber, OrderId, CustomerId, DealerId, SalespersonId, TotalAmount, PaymentStatus, Status, CreatedAt; DeviceToken always null — the API collects none) | NotificationService `contract.created` → same registry resolution as `order.created` |
| `payment.received` | SalesService PaymentsController — `PaymentReceivedEvent` (PaymentId, OrderId, Amount, PaymentMethod, Status, PaidDate, CreatedAt) | `payment.received` (NotificationService → log-only; payload carries no CustomerEmail/DeviceToken) |
| `order.status.changed` | SalesService OrdersController — `OrderStatusChangedEvent` (OrderId, OrderNumber, OldStatus, NewStatus, ChangedAt) | `order.status.changed` (NotificationService → log-only; same) |

## Queue inventory

| Queue | Durable | Bound to | Consumed by |
|---|---|---|---|
| `vehicle.reserved` | yes | `vehicle_events` / `vehicle.reserved` | NotificationService |
| `vehicle.created` / `vehicle.updated` / `vehicle.deleted` | yes | `vehicle_events` / own routing key | NotificationService (log-only) |
| `testdrive.scheduled` | yes | `vehicle_events` / `testdrive.scheduled` | NotificationService |
| `customer_vehicle_reserved` | yes | `vehicle_events` / `vehicle.reserved` | CustomerService |
| `sales.completed` | yes | default exchange | NotificationService |
| `order.created` | yes | default exchange | NotificationService |
| `quote.created` | yes | default exchange | NotificationService |
| `contract.created` | yes | default exchange | NotificationService |
| `customer.created` / `customer.updated` / `customer.deleted` | yes | `customer_events` / own routing key | NotificationService (push via registry) |
| `payment.received` | yes | default exchange | NotificationService (log-only) |
| `order.status.changed` | yes | default exchange | NotificationService (log-only) |
| `<queue>.retry` / `<queue>.dlq` | yes | default exchange (retry dead-letters back to `<queue>`) | broker-side for retry; DLQ is operator-facing |

Fan-out works as intended for `vehicle.reserved`: one publish, two queues
(NotificationService push + CustomerService CRM upsert).

## Code map

- Producer (vehicle domain): `VehicleService/Services/RabbitMQProducerService.cs`, keys in `VehicleService/Events/EventNames.cs`
- Producer (customer domain): `CustomerService/Services/RabbitMQProducerService.cs`, keys in `CustomerService/Events/EventNames.cs`
- Producer (sales): `SalesService/Services/RabbitMQMessagePublisher.cs` (`_publishLock` — singleton `IModel` is not thread-safe)
- Consumers: `NotificationService/Services/RabbitMQConsumerService.cs` (one channel per queue, declares+binds queues, dispatches to `Consumers/*` handlers), `CustomerService/Consumers/VehicleReservedEventConsumer.cs`
- Retry/DLQ policy: `NotificationService/Events/EventRetryPolicy.cs` + identical `CustomerService/Events/EventRetryPolicy.cs`; knobs `RabbitMQ:MaxDeliveryAttempts` (3), `RabbitMQ:RetryTtlMilliseconds` (5000)
- Contract/unit tests (Issue #31): `DealerSystem.Tests/NotificationService.Tests` —
  `EventContractTests.cs` round-trips every real producer event type (referenced
  by assembly, so a field rename on either side breaks the build) through the
  publisher's `JsonSerializer.Serialize` default options into the consumer DTO
  and asserts no field is silently dropped; the intentional gaps it encodes are
  `VehiclePrice`/`DealerId` on `vehicle.reserved` (consumer body renders
  name/quantity only) and the `DeviceToken` field on `order/quote/contract`
  (producers still never put it on the wire — the consumer resolves tokens
  out-of-band from the registry below, so the gap rows describe the payload,
  not dead fields; delete a row only if a producer starts publishing one).
  `EventRetryPolicyTests.cs`
  locks `RetryRounds` x-death semantics and the `.retry`/`.dlq` naming. `DeviceTokenRegistryTests.cs`
  pins the registry semantics (key spelling, upsert dedupe with a DETECTABLE
  UpdatedAt refresh, multi-device fan-out, per-subject cap, concurrent-
  registration 500-freedom, revoke incl. the concurrent-loser case, read-time
  fail-soft, and the UNIQUE `(Key, Token)` index) against real SQLite temp
  files. CI runs all three inside the "Build .NET services" job.

## Device-token registry (Issue #33)

The push consumers no longer die at a null `DeviceToken` payload field.
NotificationService owns a one-table SQLite store (`Models/DeviceToken.cs`,
`Data/NotificationDbContext.cs`, `Services/DeviceTokenRegistry.cs`) exposed at
`/api/DeviceTokens/{key}`:

- `PUT` `{"token": "…"}` — register/upsert (idempotent; refreshes `UpdatedAt`;
  concurrent duplicate registrations are absorbed by the UNIQUE index + one
  retry, so the endpoint never 500s). 409 once the subject holds
  `MaxTokensPerSubject` (20) tokens.
- `GET` — token COUNT + masked previews for the subject (registration UI
  check, ops probe). Raw tokens are deliberately not exported: whoever can
  read them can revoke the devices and enumerate device counts per customer.
- `DELETE ?token=…` — revoke one device (404 if unknown; a concurrent-revoke
  loser gets 404, not a 500)

Endpoints are anonymous, bounded by the cap above and, optionally, by
`DeviceTokens:RegistrationKey` — when that config value is non-empty, PUT and
DELETE require it as `X-Device-Registry-Key` (boot logs a warning when unset).
Full authenticated registration is the follow-up (see gap note below).

Tokens are keyed by **subject string**, built only via
`Services/NotificationSubjects.cs` — `NotificationSubjects.Customer(id)` →
`"customer:<id>"`. One spelling server-side; consumers look up exactly what
the registration API stored.

Resolution order in `order.created` / `quote.created` / `contract.created` /
`testdrive.scheduled` consumers: an in-band payload token **wins** (future
producers can send one without touching the registry); otherwise the consumer
fans the push out to **all** live tokens for `customer:<CustomerId>` via
`SendMulticastAsync`. The `customer.*` trio (Issue #35) is **registry-only**:
their payloads carry no `DeviceToken` field and the consumers have no
in-band branch — publishing a token field on those events would be silently
dropped. Zero tokens anywhere → the old log-only behavior
("Notification logged only"), which stays the honest fallback — the exact
subject string tried is logged so a key-spelling drift is visible.

The registry DB is volume-backed in compose
(`./NotificationService/data:/app/data`), so tokens survive container
restarts. It is fail-soft **end-to-end**: if `EnsureCreated` throws at boot the
service still serves, and a registry that dies at *read* time (file corrupted
mid-run) logs an error and returns empty — consumers degrade to log-only
instead of requeueing healthy events into retry→DLQ churn.

Remaining token gap: nothing registers real customer tokens yet — the
customer-facing app must `PUT` after FCM permission (tracked separately; the
portal has staff `User` accounts, and `User` carries no `CustomerId`, so the
subject-to-login mapping needs a product decision). Accepted interim risk:
an attacker who guesses a subject can plant their OWN token there and receive
that subject's pushes (they cannot remove or read other tokens). Authenticated
registration — the follow-up issue — replaces both the cap and the optional
`RegistrationKey` gate.

## Known gaps (tracked for next phases)

1. ~~**`customer.*` consumers not wired** — events are published but nothing binds
   `customer_events`. NotificationService should acknowledge new customers. (W4 follow-up)~~
   — **closed**: `customer.created/updated/deleted` queues bound; since Issue #35
   they are push-capable via the registry (`customer:<CustomerId>`). No client
   registers customer-subject tokens yet (portal is staff-only; Issue #36 adds
   authenticated registration), so live behavior stays log-only + logged
   subject — the plumbing, not the audience, was the gap.
2. ~~**`payment.received` / `order.status.changed` have no consumers** — published,
   accumulating; no notification content designed for them yet.~~
   — **closed**: `payment.received` + `order.status.changed` queues consumed
   log-only (payloads carry no CustomerEmail/DeviceToken yet).
3. ~~**`vehicle.created/updated/deleted` have no consumers** — topology keeps room
   for reporting/search indexing later.~~
   — **closed**: `vehicle.created/updated/deleted` queues bound + log-only
   consumers (payloads carry no DeviceToken yet). The event bus now has no
   unwired published events.
4. ~~**docker-compose ships only user/vehicle/sales + rabbitmq**~~ — **closed in
   W5**: all six services plus the gateway run in `docker-compose.yml` on
   `ev-dealer-network` with `RabbitMQ__HostName=rabbitmq`. The W4 retry
   topology is live in compose: `customer_vehicle_reserved(.retry/.dlq)` and
   notification's fourteen `<queue>(.retry/.dlq)` triplets materialize on the
   broker as events flow. Notification containers boot without a Firebase
   credential (secret, out of repo): push endpoints 500 and consumed events
   cycle retry→DLQ, which is the documented degraded state, not a regression.
