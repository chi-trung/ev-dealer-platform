using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SalesService.Controllers;
using SalesService.Data;
using SalesService.DTOs;
using SalesService.Models;
using SalesService.Services;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Issue #37 (PR #42 adversarial review): making Payment.OrderId a real
/// NOT NULL FK with DeleteBehavior.Restrict broke the contract-rejection
/// flow — ContractsController.UpdateContractStatus("Rejected") deletes the
/// Order, and SQLite's FK enforcement refused the DELETE whenever the order
/// had payment rows (pre-#37 the Guid/shadow-OrderId1 link constrained
/// nothing, so that delete silently succeeded with orphaned payments). The
/// Rejected branch now deletes the order's payments explicitly.
///
/// These run the REAL controller against a REAL SalesDbContext on a temp
/// SQLite file — the only shipped coverage that the publish sites and FK
/// wiring work end-to-end at the EF layer. The fake publisher stays unused
/// on the rejection path (it publishes nothing), so no broker is involved.
/// Mutation check: dropping the RemoveRange(payments) line makes the first
/// test fail with "FOREIGN KEY constraint failed"; the second test proves
/// the common payment-less rejection still behaves exactly as before.
/// </summary>
[Collection("sqlite")]
public class ContractRejectionFkTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<SalesDbContext> _options;

    public ContractRejectionFkTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"contract_reject_{Guid.NewGuid():N}.db");
        _options = new DbContextOptionsBuilder<SalesDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        using var db = new SalesDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private sealed class NoopPublisher : IMessagePublisher
    {
        public void PublishMessage<T>(string queueName, T message) { }
        public Task PublishMessageAsync<T>(string queueName, T message) => Task.CompletedTask;
    }

    private static ContractsController Controller(SalesDbContext db) =>
        new(db, NullLogger<ContractsController>.Instance, new NoopPublisher(),
            new ConfigurationBuilder().Build());

    private static Order NewOrder(int orderId) => new()
    {
        OrderId = orderId,
        QuoteId = 1,
        CustomerId = 7,
        DealerId = 1,
        SalespersonId = 3,
        OrderNumber = $"ORD-TEST-{orderId}",
        VehicleId = 2,
        VariantId = 1,
        ColorId = 1,
        Quantity = 1,
        UnitPrice = 640000000m,
        SubTotal = 640000000m,
        TotalDiscount = 0m,
        TotalPrice = 640000000m,
        PaymentMethod = "Trả thẳng",
        PaymentForm = "Chuyển khoản",
        DeliveryPreferredDate = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc),
        DeliveryExpectedDate = new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc),
        Status = "Pending",
    };

    private static Contract NewContract(int contractId, int orderId) => new()
    {
        ContractId = contractId,
        OrderId = orderId,
        CustomerId = 7,
        DealerId = 1,
        SalespersonId = 3,
        ContractNumber = $"CTR-TEST-{contractId}",
        TotalAmount = 640000000m,
        PaymentStatus = "Unpaid",
        Status = "PendingApproval",
    };

    private static Payment NewPayment(int orderId) => new()
    {
        PaymentId = Guid.NewGuid(),
        OrderId = orderId,
        Amount = 10000000m,
        Method = "Cash",
        Status = "Paid",
    };

    [Fact]
    public async Task RejectingContractWithPayments_RemovesOrderAndPayments()
    {
        // Seed order 11 + contract 5 + ONE payment — exactly the state the
        // review said the Restrict FK made un-rejectable (500).
        using (var db = new SalesDbContext(_options))
        {
            db.Orders.Add(NewOrder(11));
            db.Contracts.Add(NewContract(5, 11));
            db.Payments.Add(NewPayment(11));
            db.Payments.Add(NewPayment(11)); // multi-payment order too
            await db.SaveChangesAsync();
        }

        IActionResult result;
        using (var db = new SalesDbContext(_options))
        {
            result = await Controller(db).UpdateContractStatus(5, new UpdateStatusRequest { Status = "Rejected" });
        }

        var ok = Assert.IsType<OkObjectResult>(result); // was: StatusCode(500, ...)
        Assert.Equal(200, ok.StatusCode);

        using (var db = new SalesDbContext(_options))
        {
            Assert.Null(await db.Orders.FindAsync(11));
            Assert.Null(await db.Contracts.FindAsync(5));
            // The payment rows must be gone too — that is what unblocks the
            // order delete; if RemoveRange were a no-op the FK would 500 us.
            Assert.Empty(await db.Payments.Where(p => p.OrderId == 11).ToListAsync());
        }
    }

    [Fact]
    public async Task RejectingContractWithoutPayments_StillSucceeds()
    {
        // The common case (no payment recorded yet) must be untouched by the
        // fix — guards against the new Payments query throwing on empty.
        using (var db = new SalesDbContext(_options))
        {
            db.Orders.Add(NewOrder(12));
            db.Contracts.Add(NewContract(6, 12));
            await db.SaveChangesAsync();
        }

        IActionResult result;
        using (var db = new SalesDbContext(_options))
        {
            result = await Controller(db).UpdateContractStatus(6, new UpdateStatusRequest { Status = "Rejected" });
        }

        Assert.IsType<OkObjectResult>(result);
        using (var db = new SalesDbContext(_options))
        {
            Assert.Null(await db.Orders.FindAsync(12));
            Assert.Null(await db.Contracts.FindAsync(6));
        }
    }
}
