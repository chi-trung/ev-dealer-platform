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
| `vehicle.created` | VehicleService — `VehicleCreatedEvent` | *(none yet — reserved for search/cache indexing)* |
| `vehicle.updated` | VehicleService — `VehicleUpdatedEvent` | *(none yet)* |
| `vehicle.deleted` | VehicleService — `VehicleDeletedEvent` | *(none yet)* |
| `vehicle.reserved` | VehicleService — `VehicleReservedEvent` (VehicleId, VehicleName, VehiclePrice, DealerId, CustomerName/Email/Phone, ColorVariantId/Name, Quantity, Notes, ReservedAt, DeviceToken) | `vehicle.reserved` (NotificationService → push), `customer_vehicle_reserved` (CustomerService → create/update customer + purchase) |
| `testdrive.scheduled` | CustomerService — `TestDriveScheduledEvent` (TestDriveId, CustomerId, VehicleId, DealerId, CustomerEmail, CustomerName, VehicleModel*, ScheduledDate, DeviceToken*) | `testdrive.scheduled` (NotificationService → push) |

\* `VehicleModel` is currently empty (CustomerService has no local vehicle catalog) and
`DeviceToken` is null (the booking API doesn't collect one yet) — the consumer falls
back to `#<VehicleId>` in the notification and skips push without a token.

### `customer_events` (topic)

| Routing key | Producer (payload) | Consumer queues |
|---|---|---|
| `customer.created` | CustomerService — `CustomerCreatedEvent` (CustomerId: int, Name, Email, Timestamp) | `customer.created` (NotificationService → log-only; payload carries no DeviceToken) |
| `customer.updated` | CustomerService — `CustomerUpdatedEvent` (CustomerId, Name, Email, Phone, Address, Status, Timestamp) | `customer.updated` (NotificationService → log-only; same) |
| `customer.deleted` | CustomerService — `CustomerDeletedEvent` (CustomerId, Timestamp) | `customer.deleted` (NotificationService → log-only; same) |

Each queue binds `customer_events` by its EVENT routing key (never the queue
name — renames via `RabbitMQ:Queues:*` must not unbind), declares its own
`.retry`/`.dlq` triplet via the shared `EventRetryPolicy`, and dispatches to
`Consumers/Customer{Created,Updated,Deleted}Consumer.cs` (log-only, no
`IFcmService` dependency — adding push later means taking the token plus a
`throw on success == false` block like `TestDriveScheduledConsumer`).

### Default exchange (SalesService)

SalesService publishes with `basicProperties.Persistent = true` to the default
exchange; the routing key **is** the queue name (from `RabbitMQ:Queues:*` config):

| Routing key = queue | Producer (payload) | Consumer |
|---|---|---|
| `sales.completed` | SalesService OrdersController — `SaleCompletedEvent` (OrderId, CustomerEmail, CustomerName, VehicleModel, TotalPrice, CompletedAt, DeviceToken?) | NotificationService `sales.completed` → push (skipped without DeviceToken) |
| `order.created` | SalesService OrdersController — `OrderCreatedEvent` (no DeviceToken field) | NotificationService `order.created` → log-only today: push needs a DeviceToken the payload never carries (see Known gaps) |
| `quote.created` | SalesService QuotesController — `QuoteCreatedEvent` (no DeviceToken field) | NotificationService `quote.created` → log-only today: same as `order.created` |
| `contract.created` | SalesService ContractsController — `ContractCreatedEvent` (ContractId, ContractNumber, OrderId, CustomerId, DealerId, SalespersonId, TotalAmount, PaymentStatus, Status, CreatedAt; DeviceToken always null — the API collects none) | NotificationService `contract.created` → log-only today: DeviceToken is never populated |
| `payment.received` | SalesService PaymentsController — `PaymentReceivedEvent` (PaymentId, OrderId, Amount, PaymentMethod, Status, PaidDate, CreatedAt) | `payment.received` (NotificationService → log-only; payload carries no CustomerEmail/DeviceToken) |
| `order.status.changed` | SalesService OrdersController — `OrderStatusChangedEvent` (OrderId, OrderNumber, OldStatus, NewStatus, ChangedAt) | `order.status.changed` (NotificationService → log-only; same) |

## Queue inventory

| Queue | Durable | Bound to | Consumed by |
|---|---|---|---|
| `vehicle.reserved` | yes | `vehicle_events` / `vehicle.reserved` | NotificationService |
| `testdrive.scheduled` | yes | `vehicle_events` / `testdrive.scheduled` | NotificationService |
| `customer_vehicle_reserved` | yes | `vehicle_events` / `vehicle.reserved` | CustomerService |
| `sales.completed` | yes | default exchange | NotificationService |
| `order.created` | yes | default exchange | NotificationService |
| `quote.created` | yes | default exchange | NotificationService |
| `contract.created` | yes | default exchange | NotificationService |
| `customer.created` / `customer.updated` / `customer.deleted` | yes | `customer_events` / own routing key | NotificationService (log-only) |
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

## Known gaps (tracked for next phases)

1. ~~**`customer.*` consumers not wired** — events are published but nothing binds
   `customer_events`. NotificationService should acknowledge new customers. (W4 follow-up)~~
   — **closed**: `customer.created/updated/deleted` queues bound + log-only
   consumers (no DeviceToken in payloads yet; push wiring is the remaining
   follow-up when the API collects tokens).
2. ~~**`payment.received` / `order.status.changed` have no consumers** — published,
   accumulating; no notification content designed for them yet.~~
   — **closed**: `payment.received` + `order.status.changed` queues consumed
   log-only (payloads carry no CustomerEmail/DeviceToken yet).
3. **`vehicle.created/updated/deleted` have no consumers** — topology keeps room
   for reporting/search indexing later.
4. ~~**docker-compose ships only user/vehicle/sales + rabbitmq**~~ — **closed in
   W5**: all six services plus the gateway run in `docker-compose.yml` on
   `ev-dealer-network` with `RabbitMQ__HostName=rabbitmq`. The W4 retry
   topology is live in compose: `customer_vehicle_reserved(.retry/.dlq)` and
   notification's eleven `<queue>(.retry/.dlq)` triplets materialize on the
   broker as events flow. Notification containers boot without a Firebase
   credential (secret, out of repo): push endpoints 500 and consumed events
   cycle retry→DLQ, which is the documented degraded state, not a regression.
