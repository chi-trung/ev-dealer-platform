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
| `vehicle.created` | VehicleService — `VehicleCreatedEvent` (VehicleId, Model, Type, Price, DealerId, CreatedAt) | `vehicle.created` (NotificationService → registry-only push via `dealer:<DealerId>`, Issue #38; payload carries no DeviceToken) |
| `vehicle.updated` | VehicleService — `VehicleUpdatedEvent` (VehicleId, Model, Type, Price, DealerId, UpdatedAt) | `vehicle.updated` (NotificationService → registry-only push via `dealer:<DealerId>`, Issue #38) |
| `vehicle.deleted` | VehicleService — `VehicleDeletedEvent` (VehicleId, DealerId — Issue #38, DeletedAt) | `vehicle.deleted` (NotificationService → registry-only push via `dealer:<DealerId>`, Issue #38) |
| `vehicle.reserved` | VehicleService — `VehicleReservedEvent` (VehicleId, VehicleName, VehiclePrice, DealerId, CustomerName/Email/Phone, ColorVariantId/Name, Quantity, Notes, ReservedAt, DeviceToken) | `vehicle.reserved` (NotificationService → push), `customer_vehicle_reserved` (CustomerService → create/update customer + purchase) |
| `testdrive.scheduled` | CustomerService — `TestDriveScheduledEvent` (TestDriveId, CustomerId, VehicleId, DealerId, CustomerEmail, CustomerName, VehicleModel*, ScheduledDate, DeviceToken*) | `testdrive.scheduled` (NotificationService → push; token via registry when payload carries none) |

\* `VehicleModel` is currently empty (CustomerService has no local vehicle catalog) and
`DeviceToken` is null (the booking API doesn't collect one yet) — the consumer falls
back to `#<VehicleId>` in the notification and resolves the token from the
device-token registry (`customer:<CustomerId>`, Issue #33) before skipping push.

The three vehicle lifecycle queues bind `vehicle_events` by their EVENT routing
key, declare their own `.retry`/`.dlq` triplet, and (since Issue #38) push to
**all live tokens for `dealer:<DealerId>`** via `SendMulticastAsync` —
registry-only like the `customer.*` trio (the payloads carry no `DeviceToken`
and the consumers have no in-band branch). Zero tokens → log-only with the
exact subject logged; a failed push throws for the retry/DLQ policy.
`vehicle.deleted` needed a payload change to route at all: `VehicleDeletedEvent`
gained a `DealerId` the producer fills from the vehicle it deletes.

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
| `sales.completed` | SalesService OrdersController — `SaleCompletedEvent` (OrderId, CustomerEmail, CustomerName, VehicleModel, TotalPrice, CompletedAt, DeviceToken?, CustomerId — Issue #37) | NotificationService `sales.completed` → push: payload DeviceToken wins, else registry fallback `customer:<CustomerId>` (Issue #37); log-only when neither yields a token |
| `order.created` | SalesService OrdersController — `OrderCreatedEvent` (no DeviceToken field) | NotificationService `order.created` → push via registry (`customer:<CustomerId>`, Issue #33); log-only when the customer has no registered device |
| `quote.created` | SalesService QuotesController — `QuoteCreatedEvent` (no DeviceToken field) | NotificationService `quote.created` → same registry resolution as `order.created` |
| `contract.created` | SalesService ContractsController — `ContractCreatedEvent` (ContractId, ContractNumber, OrderId, CustomerId, DealerId, SalespersonId, TotalAmount, PaymentStatus, Status, CreatedAt; DeviceToken always null — the API collects none) | NotificationService `contract.created` → same registry resolution as `order.created` |
| `payment.received` | SalesService PaymentsController — `PaymentReceivedEvent` (PaymentId, OrderId, Amount, PaymentMethod, Status, PaidDate, CreatedAt, CustomerId — resolved from the payment's Order at the publish site, Issue #37) | `payment.received` (NotificationService → registry-only push via `customer:<CustomerId>`, Issue #37; log-only when nothing is registered) |
| `order.status.changed` | SalesService OrdersController — `OrderStatusChangedEvent` (OrderId, OrderNumber, OldStatus, NewStatus, ChangedAt, CustomerId — Issue #37) | `order.status.changed` (NotificationService → registry-only push via `customer:<CustomerId>`, Issue #37; log-only when nothing is registered) |

## Queue inventory

| Queue | Durable | Bound to | Consumed by |
|---|---|---|---|
| `vehicle.reserved` | yes | `vehicle_events` / `vehicle.reserved` | NotificationService |
| `vehicle.created` / `vehicle.updated` / `vehicle.deleted` | yes | `vehicle_events` / own routing key | NotificationService (push via `dealer:<DealerId>`, Issue #38) |
| `testdrive.scheduled` | yes | `vehicle_events` / `testdrive.scheduled` | NotificationService |
| `customer_vehicle_reserved` | yes | `vehicle_events` / `vehicle.reserved` | CustomerService |
| `sales.completed` | yes | default exchange | NotificationService |
| `order.created` | yes | default exchange | NotificationService |
| `quote.created` | yes | default exchange | NotificationService |
| `contract.created` | yes | default exchange | NotificationService |
| `customer.created` / `customer.updated` / `customer.deleted` | yes | `customer_events` / own routing key | NotificationService (push via registry) |
| `payment.received` | yes | default exchange | NotificationService (push via registry, Issue #37) |
| `order.status.changed` | yes | default exchange | NotificationService (push via registry, Issue #37) |
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
  files; `CustomerConsumerTests.cs` pins the Issue #35 push wiring;
  `SalesPushConsumerTests.cs` pins the Issue #37 sales-push wiring
  (payload-token-wins vs registry fallback, throw-on-false, the pre-#37
  missing-CustomerId degradation); `VehiclePushConsumerTests.cs` pins the
  Issue #38 dealer-push wiring (dealer-subject multicast for all three
  lifecycle events, throw-on-false, cross-dealer and customer-vs-dealer
  subject negative controls, the pre-#38 missing-DealerId degradation);
  `VehicleDeleteProducerTests.cs` (Issue #38 review) runs the real
  VehicleService.DeleteVehicleAsync against a real ApplicationDbContext with a
  recording IMessageProducer, pinning that the published event carries the
  vehicle's own DealerId (the consumer-side tests hand-build the DTO, so the
  producer line is invisible to them; a zeroed assignment would log-only
  forever with a green suite);
  `ContractRejectionFkTests.cs` (Issue #37 review) runs the real
  ContractsController against a real SalesDbContext to pin that rejecting a
  contract with payments deletes the payments before the order (the Restrict
  FK makes the order delete fail otherwise);
  `DeviceTokensAuthTests.cs` (Issue #36) pins the controller's authorization
  decision table (own/dealer/foreign/missing-claim × PUT/GET/DELETE, incl.
  prefix-shaped negatives that kill a StartsWith mutant of the ownership
  rule); `DealerClaimLoginTests.cs` (Issue #41) pins the MINTING side of that
  same `dealer` claim — it runs the real `UserServiceImpl.LoginAsync` and reads
  the returned JWT's raw payload (base64url-decoded, so the assertion is
  independent of any reader's inbound claim-mapping — this test project's
  version-skewed `JwtSecurityTokenHandler` read-back empirically drops claims
  the wire carries) to assert the `dealer`/`id`/`unique_name`/`role` claims are
  present with the right values and that `dealer` is OMITTED (not empty) for a
  no-dealer account, so a dropped claim block in Program.cs can no longer leave
  the suite green while silently breaking #36 for real users. CI runs all ten
  test files inside the "Build .NET services" job.

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

Endpoints require a UserService JWT (`Authorization: Bearer`, validated with
the same `Jwt:Key/Issuer/Audience` as every other service — Issue #36), and a
caller may manage only their OWN subjects: `user:<id>` (the token's `id`
claim) and `dealer:<n>` when the token carries a matching `dealer` claim
(minted at login for accounts with a `DealerId`). Anything else — another
user's mailbox, an arbitrary `customer:…` — is 403. The interim
`DeviceTokens:RegistrationKey` header gate is deleted; the per-subject cap
and masked GET previews remain (auth bounds WHO may write a subject, not HOW
much one subject can hoard). The staff portal registers its FCM token after
login and on app load with a token (`user:<id>` + `dealer:<n>` when the
account has a dealer), so pushes reach staff devices; the customer-facing
audience remains unbuilt.

Tokens are keyed by **subject string**, built only via
`Services/NotificationSubjects.cs` — `NotificationSubjects.Customer(id)` →
`"customer:<id>"`, `User(id)` → `"user:<id>"`, `Dealer(id)` → `"dealer:<id>"`.
One spelling server-side; consumers look up exactly what the registration API
stored, and the Issue #36 write-scope check compares against the same builders
(the authorization decision and the storage key can never drift apart).

Resolution order in `order.created` / `quote.created` / `contract.created` /
`testdrive.scheduled` / `sales.completed` consumers: an in-band payload token
**wins** (future producers can send one without touching the registry);
otherwise the consumer fans the push out to **all** live tokens for
`customer:<CustomerId>` via `SendMulticastAsync` (`sales.completed` gained its
registry fallback with Issue #37; `CustomerId` is now on that payload). The
`customer.*` trio (Issue #35), the two sales status/payment events
`payment.received` / `order.status.changed` (Issue #37) and the three vehicle
lifecycle events `vehicle.created` / `vehicle.updated` / `vehicle.deleted`
(Issue #38, fanning out to `dealer:<DealerId>`) are **registry-only**:
their payloads carry no `DeviceToken` field and the consumers have no in-band
branch — publishing a token field on those events would be silently dropped.
Zero tokens anywhere → the old log-only behavior
("Notification logged only"), which stays the honest fallback — the exact
subject string tried is logged so a key-spelling drift is visible. A pre-#37
in-flight `payment.received`/`order.status.changed` message with no `CustomerId`
deserializes it to `0`, looks up `customer:0`, finds nothing, and degrades to
log-only rather than retry→DLQ.

The registry DB is volume-backed in compose
(`./NotificationService/data:/app/data`), so tokens survive container
restarts. It is fail-soft **end-to-end**: if `EnsureCreated` throws at boot the
service still serves, and a registry that dies at *read* time (file corrupted
mid-run) logs an error and returns empty — consumers degrade to log-only
instead of requeueing healthy events into retry→DLQ churn.

Remaining token gap: no *customer*-subject tokens are registered yet — the
portal is staff-only. `dealer:<id>` is now a live lookup key (Issue #38 fans
the vehicle lifecycle events out to it, and the staff portal registers that
subject at login when the JWT carries a dealer claim, Issue #36), so dealer
pushes are deliverable wherever a dealer's staff device has registered; `user:`
is registration-only for now, its consumers coming with whatever notifies a
staff user directly. The old "Issue #33 accepted risk" — anyone guessing a
subject could plant their own token there — is closed by Issue #36: writes
require a JWT whose `id`/`dealer` claims own the exact subject. `customer:<n>`
subjects are consequently write-protected from the API entirely (no claim maps
to one); they can only gain tokens if a future authenticated customer app adds
a customer-scoped rule here.

## Known gaps (tracked for next phases)

1. ~~**`customer.*` consumers not wired** — events are published but nothing binds
   `customer_events`. NotificationService should acknowledge new customers. (W4 follow-up)~~
   — **closed**: `customer.created/updated/deleted` queues bound; since Issue #35
   they are push-capable via the registry (`customer:<CustomerId>`). No client
   registers customer-subject tokens yet (portal is staff-only; Issue #36
   shipped authenticated registration — `user:`/`dealer:` subjects only), so
   live behavior stays log-only + logged
   subject — the plumbing, not the audience, was the gap.
2. ~~**`payment.received` / `order.status.changed` have no consumers** — published,
   accumulating; no notification content designed for them yet.~~
   — **closed**: `payment.received` + `order.status.changed` queues consumed;
   since Issue #37 they are push-capable via the registry
   (`customer:<CustomerId>` — the events gained a `CustomerId`, the payment one
   resolved from the Order at the publish site after `Payment.OrderId` became a
   real int FK; the old Guid-vs-int mismatch also made
   `ReportingService.GetPaymentsAsync` swallow a JsonException and return an
   empty payment list, which the same fix cures). No client registers
   customer-subject tokens yet (they are write-protected from the API, Issue
   #36), so live behavior stays log-only + logged subject until a customer app
   exists.
3. ~~**`vehicle.created/updated/deleted` have no consumers** — topology keeps room
   for reporting/search indexing later.~~
   — **closed**: `vehicle.created/updated/deleted` queues bound; since Issue #38
   they are push-capable via the registry, fanning out to the vehicle's owning
   dealer at `dealer:<DealerId>` (the portal staff register that subject when
   the login JWT carries a dealer claim, Issue #36 — these are the first
   consumers to use it). `vehicle.deleted` could not route before #38: its
   payload carried no DealerId at all, so the event gained one at the producer
   (available on the loaded vehicle in `DeleteVehicleAsync`). Live behavior on
   a broker with no dealer tokens stays log-only + logged subject until staff
   devices register.
4. ~~**docker-compose ships only user/vehicle/sales + rabbitmq**~~ — **closed in
   W5**: all six services plus the gateway run in `docker-compose.yml` on
   `ev-dealer-network` with `RabbitMQ__HostName=rabbitmq`. The W4 retry
   topology is live in compose: `customer_vehicle_reserved(.retry/.dlq)` and
   notification's fourteen `<queue>(.retry/.dlq)` triplets materialize on the
   broker as events flow. Notification containers boot without a Firebase
   credential (secret, out of repo): push endpoints 500 and consumed events
   cycle retry→DLQ, which is the documented degraded state, not a regression.
