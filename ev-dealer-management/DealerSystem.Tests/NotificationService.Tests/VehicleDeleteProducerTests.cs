using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VehicleService.Data;
using VehicleService.DTOs;
using VehicleService.Models;
using VehicleService.Services;
using Xunit;
// The service class is named VehicleService inside a VehicleService.*
// namespace hierarchy, so the bare name resolves to the namespace (CS0118).
using VehicleLifecycleService = global::VehicleService.Services.VehicleService;
// UserService (referenced for Issue #41) declares a global `Dealer`; the bare
// name here would bind to THAT one (Id/Name/Address only) and lose the
// VehicleService model's Region/Contact/Email (CS0117). Alias to disambiguate.
using VehicleDealer = VehicleService.Models.Dealer;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Issue #38 producer-site pin (adversarial review of PR #43): the contract
/// test round-trips a hand-built VehicleDeletedEvent and the consumer tests
/// hand-build the mirror DTO — neither executes DeleteVehicleAsync, so the
/// one line the whole dealer fan-out depends on ("DealerId = vehicle.DealerId")
/// could regress to DealerId=0 (→ dealer:0 → silent log-only, the exact
/// failure mode with no error and no DLQ) with a fully green suite. This file
/// runs the REAL VehicleService against a real SQLite context with a
/// recording IMessageProducer — same pattern as ContractRejectionFkTests —
/// so mutating that assignment fails here. Mutation check performed: setting
/// the initializer to DealerId = 0 fails DeletingVehicle_PublishesEventWithOwn
/// dealer's DealerId exactly.
/// </summary>
public class VehicleDeleteProducerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public VehicleDeleteProducerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"vehdel_prod_{Guid.NewGuid():N}.db");
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        using var db = new ApplicationDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task DeletingVehicle_PublishesEventWithOwningDealersId()
    {
        // DealerId 7 is a non-default sentinel: it differs from every seeded
        // dealer (1-4), so a dropped/zeroed assignment cannot pass by accident.
        int vehicleId;
        using (var db = new ApplicationDbContext(_options))
        {
            var dealer = new VehicleDealer { Id = 7, Name = "Probe Dealer", Region = "HN", Contact = "0", Email = "p@e.co", Address = "x" };
            var vehicle = new Vehicle
            {
                Model = "VF8 probe", Type = "suv", Price = 1, BatteryCapacity = 1,
                Range = 1, StockQuantity = 1, DealerId = 7,
            };
            db.Dealers.Add(dealer);
            db.Vehicles.Add(vehicle);
            await db.SaveChangesAsync();
            vehicleId = vehicle.Id;
        }

        var producer = new RecordingMessageProducer();
        using var svcDb = new ApplicationDbContext(_options);
        var service = new VehicleLifecycleService(svcDb, producer);

        var deleted = await service.DeleteVehicleAsync(vehicleId);

        Assert.True(deleted);
        var message = Assert.Single(producer.Messages);
        var evt = Assert.IsType<VehicleDeletedEvent>(message);
        Assert.Equal(vehicleId, evt.VehicleId);
        // THE pin: the event must carry the vehicle's own dealer — this is the
        // registry subject key consumers resolve (Issue #38).
        Assert.Equal(7, evt.DealerId);
        Assert.True(evt.DeletedAt > DateTime.MinValue);

        // The row is really gone (not just event-emitted).
        using var check = new ApplicationDbContext(_options);
        Assert.Null(await check.Vehicles.FindAsync(vehicleId));
    }

    [Fact]
    public async Task DeletingUnknownVehicle_PublishesNothing()
    {
        var producer = new RecordingMessageProducer();
        using var svcDb = new ApplicationDbContext(_options);
        var service = new VehicleLifecycleService(svcDb, producer);

        Assert.False(await service.DeleteVehicleAsync(9999));
        Assert.Empty(producer.Messages);
    }

    /// <summary>Records every PublishMessage call — the assertion target for
    /// what actually goes on the wire.</summary>
    private sealed class RecordingMessageProducer : IMessageProducer
    {
        public List<object> Messages { get; } = new();
        public void PublishMessage<T>(T message, string routingKey = "") => Messages.Add(message!);
    }
}
