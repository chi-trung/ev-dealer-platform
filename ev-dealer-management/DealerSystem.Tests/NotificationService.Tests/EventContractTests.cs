using System.Reflection;
using System.Text.Json;
using Xunit;
using CS = CustomerService.DTOs;
using NS = NotificationService.DTOs;
using SA = SalesService.DTOs;
using VS = VehicleService.DTOs;

namespace NotificationService.Tests;

/// <summary>
/// Producer↔consumer payload contract tests (Issue #31).
///
/// Every publisher serializes with plain JsonSerializer.Serialize(message)
/// — default options, PascalCase — and every consumer deserializes
/// case-sensitively into its mirror DTO. A renamed or mis-cased property on
/// one side does NOT throw: it silently lands as 0 / null / empty string in
/// the notification body (the exact failure the payment probe exposed:
/// Payment.OrderId Guid vs Order.OrderId int drifted unnoticed until a live
/// 400). These tests close that gap offline:
///
///   1. construct the REAL producer type from its own assembly (renaming a
///      field there breaks this file at compile time);
///   2. serialize exactly like the publisher services do (default options);
///   3. deserialize into the consumer DTO;
///   4. reflect over every producer property and assert the consumer side
///      received a non-default value — i.e. no field is silently dropped.
///
/// Known intentional gaps are enumerated in ProducerOnlyFields below; any
/// OTHER mismatch fails, and any consumer field the producer never sends is
/// flagged (it would deserialize to a default forever).
/// </summary>
public class EventContractTests
{
    /// <summary>
    /// Producer fields that intentionally have no consumer counterpart.
    /// VehiclePrice/DealerId on vehicle.reserved: the consumer notification
    /// body (VehicleReservedConsumer) renders name/quantity only — the price
    /// was in the W2 payload but the push text dropped it, and docs/EVENTS.md
    /// catalogs it for future reporting consumers. If you add here, say why.
    /// </summary>
    private static readonly HashSet<(Type Producer, string Field)> ProducerOnlyFields = new()
    {
        (typeof(VS.VehicleReservedEvent), nameof(VS.VehicleReservedEvent.VehiclePrice)),
        (typeof(VS.VehicleReservedEvent), nameof(VS.VehicleReservedEvent.DealerId)),
    };

    /// <summary>
    /// Consumer fields the producer intentionally does not send yet: the
    /// placeholder DeviceToken on order/quote/contract (the sales APIs
    /// collect no token — docs/EVENTS.md "DeviceToken always null"). When the
    /// API starts sending one, delete the row; the test then enforces the
    /// pairing again.
    /// </summary>
    private static readonly HashSet<(Type Consumer, string Field)> ConsumerOnlyFields = new()
    {
        (typeof(NS.OrderCreatedEvent), nameof(NS.OrderCreatedEvent.DeviceToken)),
        (typeof(NS.QuoteCreatedEvent), nameof(NS.QuoteCreatedEvent.DeviceToken)),
        (typeof(NS.ContractCreatedEvent), nameof(NS.ContractCreatedEvent.DeviceToken)),
    };

    private static void AssertContractSpirits(object producerPayload, Type consumerDto)
    {
        // Same call the three publisher services make (VehicleService/
        // CustomerService/NotificationService RabbitMQProducerService +
        // SalesService RabbitMQMessagePublisher): default options, PascalCase.
        var json = JsonSerializer.Serialize(producerPayload);
        var consumer = JsonSerializer.Deserialize(json, consumerDto);
        Assert.NotNull(consumer);

        var producerType = producerPayload.GetType();
        var consumerProps = consumerDto
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(p => p.Name, p => p);

        // 1) every producer field must exist on the consumer (or be a listed
        //    intentional gap) AND must have survived the round-trip non-default.
        foreach (var prop in producerType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var inGap = ProducerOnlyFields.Contains((producerType, prop.Name));
            Assert.True(
                consumerProps.ContainsKey(prop.Name) || inGap,
                $"Producer {producerType.Name}.{prop.Name} has no consumer counterpart on " +
                $"{consumerDto.Name} and is not listed as an intentional gap — a consumer " +
                $"would silently never see it. Either add the property or document it above.");

            if (inGap)
            {
                // Listed gap: producer sends it, consumer intentionally drops
                // it. Nothing to round-trip-check; the consumer just must not
                // grow a half-baked counterpart unnoticed.
                continue;
            }

            var producerValue = prop.GetValue(producerPayload)!;
            var consumerValue = consumerProps[prop.Name].GetValue(consumer)!;
            Assert.Equal(producerValue, consumerValue);
        }

        // 2) every consumer field must be sent by the producer, otherwise it
        //    deserializes to default forever (dead weight, misleading DTO).
        foreach (var prop in consumerDto.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var onProducer = producerType.GetProperty(prop.Name) != null;
            var inGap = ConsumerOnlyFields.Contains((consumerDto, prop.Name));
            Assert.True(
                onProducer || inGap,
                $"Consumer {consumerDto.Name}.{prop.Name} is never published by " +
                $"{producerType.Name} and is not listed as an intentional gap — it always " +
                $"deserializes to its default. Remove it, publish it, or document it above.");
            // A listed consumer-only placeholder is by definition never in the
            // JSON, so it holds its default; loop 1 above already proves no
            // producer field was sent under a different name.
        }
    }

    // One test per routing key in docs/EVENTS.md. Sentinels are deliberately
    // distinct per field so a transposition (Model↔Type) also fails.

    private static readonly DateTime When = new(2026, 9, 13, 7, 30, 0, DateTimeKind.Utc);

    [Fact] public void VehicleCreated_Contract_Holds() => AssertContractSpirits(
        new VS.VehicleCreatedEvent { VehicleId = 6, Model = "VinFast VF8", Type = "SUV", Price = 640000000m, DealerId = 1, CreatedAt = When },
        typeof(NS.VehicleCreatedEvent));

    [Fact] public void VehicleUpdated_Contract_Holds() => AssertContractSpirits(
        new VS.VehicleUpdatedEvent { VehicleId = 6, Model = "VinFast VF9", Type = "SUV", Price = 740000000m, DealerId = 1, UpdatedAt = When },
        typeof(NS.VehicleUpdatedEvent));

    [Fact] public void VehicleDeleted_Contract_Holds() => AssertContractSpirits(
        new VS.VehicleDeletedEvent { VehicleId = 6, DeletedAt = When },
        typeof(NS.VehicleDeletedEvent));

    [Fact] public void VehicleReserved_Contract_Holds() => AssertContractSpirits(
        new VS.VehicleReservedEvent
        {
            VehicleId = 2, VehicleName = "VF e34", VehiclePrice = 30000000m, DealerId = 1,
            CustomerName = "Nguyen Van A", CustomerEmail = "a@example.com", CustomerPhone = "0900000001",
            ColorVariantId = 3, ColorVariantName = "Xanh", Quantity = 1, Notes = "giao Q1", ReservedAt = When,
            DeviceToken = "tok-veh-reserved",
        },
        typeof(NS.VehicleReservedEvent));

    [Fact] public void TestDriveScheduled_Contract_Holds() => AssertContractSpirits(
        new CS.TestDriveScheduledEvent
        {
            TestDriveId = 5, CustomerId = 7, VehicleId = 2, DealerId = 1,
            CustomerEmail = "b@example.com", CustomerName = "Tran Van B",
            VehicleModel = "VF e34", ScheduledDate = When, DeviceToken = null,
        },
        typeof(NS.TestDriveScheduledEvent));

    [Fact] public void CustomerCreated_Contract_Holds() => AssertContractSpirits(
        new CS.CustomerCreatedEvent { CustomerId = 42, Name = "Ly Van C", Email = "c@example.com", Timestamp = When },
        typeof(NS.CustomerCreatedEvent));

    [Fact] public void CustomerUpdated_Contract_Holds() => AssertContractSpirits(
        new CS.CustomerUpdatedEvent { CustomerId = 42, Name = "Ly Van C", Email = "c@example.com", Phone = "0900000002", Address = "Ha Noi", Status = "active", Timestamp = When },
        typeof(NS.CustomerUpdatedEvent));

    [Fact] public void CustomerDeleted_Contract_Holds() => AssertContractSpirits(
        new CS.CustomerDeletedEvent { CustomerId = 42, Timestamp = When },
        typeof(NS.CustomerDeletedEvent));

    [Fact] public void SaleCompleted_Contract_Holds() => AssertContractSpirits(
        new SA.SaleCompletedEvent { OrderId = "11", CustomerEmail = "d@example.com", CustomerName = "Pham Van D", VehicleModel = "VF8", TotalPrice = 640000000m, CompletedAt = When, DeviceToken = "tok-sale" },
        typeof(NS.SaleCompletedEvent));

    [Fact] public void OrderCreated_Contract_Holds() => AssertContractSpirits(
        new SA.OrderCreatedEvent { OrderId = "11", OrderNumber = "ORD-2026-0913", CustomerId = 7, DealerId = 1, VehicleId = 2, Quantity = 1, TotalPrice = 640000000m, PaymentMethod = "Bank", Status = "Pending", CreatedAt = When },
        typeof(NS.OrderCreatedEvent));

    [Fact] public void PaymentReceived_Contract_Holds() => AssertContractSpirits(
        new SA.PaymentReceivedEvent { PaymentId = "p-1", OrderId = "11", Amount = 10000000m, PaymentMethod = "Cash", Status = "Paid", PaidDate = When, CreatedAt = When },
        typeof(NS.PaymentReceivedEvent));

    [Fact] public void OrderStatusChanged_Contract_Holds() => AssertContractSpirits(
        new SA.OrderStatusChangedEvent { OrderId = "11", OrderNumber = "ORD-2026-0913", OldStatus = "Pending", NewStatus = "Confirmed", ChangedAt = When },
        typeof(NS.OrderStatusChangedEvent));

    [Fact] public void QuoteCreated_Contract_Holds() => AssertContractSpirits(
        new SA.QuoteCreatedEvent { QuoteId = "q-1", CustomerId = 7, DealerId = 1, SalespersonId = 3, VehicleId = 2, Quantity = 1, TotalBasePrice = 500000000m, Status = "Draft", CreatedAt = When },
        typeof(NS.QuoteCreatedEvent));

    [Fact] public void ContractCreated_Contract_Holds() => AssertContractSpirits(
        new SA.ContractCreatedEvent { ContractId = "ct-1", ContractNumber = "CTR-2026-0913", OrderId = 11, CustomerId = 7, DealerId = 1, SalespersonId = 3, TotalAmount = 640000000m, PaymentStatus = "Unpaid", Status = "Draft", CreatedAt = When },
        typeof(NS.ContractCreatedEvent));

    // ---- negative controls: the harness itself must be able to fail -------

    [Fact]
    public void Harness_DetectsRenamedProducerField()
    {
        // Guards the guard: if these two scenarios ever pass, the reflection
        // check above has rotted. Anonymous type stands in for "producer
        // renamed OrderNumber to OrderRef" and "consumer renamed Amount".
        var producer = new SA.OrderStatusChangedEvent { OrderId = "1", OrderNumber = "N", OldStatus = "A", NewStatus = "B", ChangedAt = When };

        Assert.ThrowsAny<Exception>(() => AssertContractSpirits(
            new { OrderId = "1", OrderRef = "N", OldStatus = "A", NewStatus = "B", ChangedAt = When }, // renamed on producer side
            typeof(NS.OrderStatusChangedEvent)));

        Assert.ThrowsAny<Exception>(() => AssertContractSpirits(
            new SA.PaymentReceivedEvent { PaymentId = "p", OrderId = "1", Amount = 5m, PaymentMethod = "Cash", Status = "Paid", PaidDate = When, CreatedAt = When },
            typeof(PaymentMissingFieldConsumerDto))); // consumer dropped PaymentMethod
    }

    internal class PaymentMissingFieldConsumerDto
    {
        public string PaymentId { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime PaidDate { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // ---- case-sensitivity tripwire ----------------------------------------

    [Fact]
    public void Consumer_Deserialization_IsCaseSensitive_NoImplicitPascalToFallback()
    {
        // Documents WHY the contract test exists: camelCase (what a future
        // producer using camelCase options would emit) silently produces
        // default values into a PascalCase DTO under default options.
        var payload = JsonSerializer.Serialize(new { VehicleId = 6, Model = "VF8", Type = "SUV", Price = 1m, DealerId = 1, CreatedAt = When });
        var pascal = JsonSerializer.Deserialize<NS.VehicleCreatedEvent>(payload)!;
        Assert.Equal("VF8", pascal.Model);

        var camel = JsonSerializer.Serialize(new { vehicleId = 6, model = "VF8", type = "SUV", price = 1m, dealerId = 1, createdAt = When });
        var dropped = JsonSerializer.Deserialize<NS.VehicleCreatedEvent>(camel)!;
        Assert.Equal(0, dropped.VehicleId);      // silently defaulted — the bug class
        Assert.Equal("", dropped.Model);         // silently defaulted — the bug class
    }
}
