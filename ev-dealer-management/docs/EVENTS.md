# Event Topology (RabbitMQ)

Canonical event bus for the EV Dealer microservices. Every publish is **persistent**
(`Persistent = true`) and every consumer **acks manually** (`autoAck: false`, ack on
success, nack+requeue on failure).

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
| `customer.created` | CustomerService — `CustomerCreatedEvent` (CustomerId: int, Name, Email, Timestamp) | *(no bound queue yet)* |
| `customer.updated` | CustomerService — `CustomerUpdatedEvent` (CustomerId, Name, Email, Phone, Address, Status, Timestamp) | *(no bound queue yet)* |
| `customer.deleted` | CustomerService — `CustomerDeletedEvent` (CustomerId, Timestamp) | *(no bound queue yet)* |

NotificationService has DTOs for these (`CustomerCreatedEvent`) but **no consumer is
wired** to a bound queue yet — see "Known gaps".

### Default exchange (SalesService)

SalesService publishes with `basicProperties.Persistent = true` to the default
exchange; the routing key **is** the queue name (from `RabbitMQ:Queues:*` config):

| Routing key = queue | Producer (payload) | Consumer |
|---|---|---|
| `sales.completed` | SalesService OrdersController — `SaleCompletedEvent` (OrderId, CustomerName, Email, Phone, VehicleModel, TotalPrice, DealerId, CompletedAt, DeviceToken) | NotificationService `sales.completed` → push (skipped without DeviceToken) |
| `order.created` | SalesService OrdersController — `OrderCreatedEvent` | **unrouted** — NotificationService has `OrderCreatedConsumer` but it is not wired to a queue |
| `payment.received` | SalesService PaymentsController | no consumer |
| `order.status.changed` | SalesService OrdersController | no consumer |
| `quote.created` | SalesService QuotesController | **unrouted** — `QuoteCreatedConsumer` exists, not wired |

## Queue inventory

| Queue | Durable | Bound to | Consumed by |
|---|---|---|---|
| `vehicle.reserved` | yes | `vehicle_events` / `vehicle.reserved` | NotificationService |
| `testdrive.scheduled` | yes | `vehicle_events` / `testdrive.scheduled` | NotificationService |
| `customer_vehicle_reserved` | yes | `vehicle_events` / `vehicle.reserved` | CustomerService |
| `sales.completed` | yes | default exchange | NotificationService |
| `order.created`, `payment.received`, `order.status.changed`, `quote.created` | yes (declared by publisher) | default exchange | nobody — messages accumulate |

Fan-out works as intended for `vehicle.reserved`: one publish, two queues
(NotificationService push + CustomerService CRM upsert).

## Code map

- Producer (vehicle domain): `VehicleService/Services/RabbitMQProducerService.cs`, keys in `VehicleService/Events/EventNames.cs`
- Producer (customer domain): `CustomerService/Services/RabbitMQProducerService.cs`, keys in `CustomerService/Events/EventNames.cs`
- Producer (sales): `SalesService/Services/RabbitMQMessagePublisher.cs`
- Consumers: `NotificationService/Services/RabbitMQConsumerService.cs` (declares+binds queues, dispatches to `Consumers/*` handlers), `CustomerService/Consumers/VehicleReservedEventConsumer.cs`

## Known gaps (tracked for next phases)

1. **Unrouted sales consumers** — `OrderCreatedConsumer`, `QuoteCreatedConsumer`,
   `ContractCreatedConsumer` classes exist in NotificationService but are never
   registered or bound to a queue; their events pile up unconsumed. (W4)
2. **`contract.created` has no producer** — the DTO exists in SalesService but no
   code publishes it.
3. **No dead-letter queues** — a handler that always throws (poison message) is
   nack-requeued forever. W4 adds DLX + retry limits.
4. **`customer.*` consumers not wired** — events are published but nothing binds
   `customer_events`. NotificationService should acknowledge new customers. (W4)
5. **docker-compose ships only user/vehicle/sales + rabbitmq** — CustomerService and
   NotificationService run outside compose, hence `HostName: localhost` defaults;
   adding them to compose is part of the W5 e2e task.
6. **`vehicle.created/updated/deleted` have no consumers** — topology keeps room
   for reporting/search indexing later.
