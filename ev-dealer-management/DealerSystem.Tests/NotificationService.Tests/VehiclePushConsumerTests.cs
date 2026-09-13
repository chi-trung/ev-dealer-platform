using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NotificationService.Consumers;
using NotificationService.Data;
using NotificationService.DTOs;
using NotificationService.Services;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Issue #38: the vehicle.created / vehicle.updated / vehicle.deleted
/// consumers became push-capable. All three are registry-only (payloads carry
/// no DeviceToken by design) and fan out to dealer:&lt;DealerId&gt; — the
/// vehicle's owning dealer's staff devices (#36 made that subject
/// registrable via the login JWT's dealer claim). Same pinned behaviors as
/// SalesPushConsumerTests/CustomerConsumerTests: multicast to every live
/// token for the subject, throw-on-false (so the bus retries a failed
/// delivery), and the honest log-only fallback — which also covers a pre-#38
/// in-flight vehicle.deleted message whose missing DealerId deserializes to 0
/// (subject dealer:0 exists by construction, and nothing registers to it).
/// Real registry (SQLite temp file) + a recording IFcmService fake; the
/// consumers are what's under test, not Firebase or EF.
/// </summary>
public class VehiclePushConsumerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<NotificationDbContext> _options;

    public VehiclePushConsumerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"vehicle_push_{Guid.NewGuid():N}.db");
        _options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        using var db = new NotificationDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private DeviceTokenRegistry Registry() => new(new NotificationDbContext(_options));
    private RecordingFcm Fcm() => new();

    private static string Json(object payload) =>
        System.Text.Json.JsonSerializer.Serialize(payload);

    private async Task RegisterAsync(int dealerId, params string[] tokens)
    {
        var reg = Registry();
        foreach (var t in tokens)
            await reg.RegisterAsync(NotificationSubjects.Dealer(dealerId), t);
    }

    private static VehicleCreatedEvent Created() => new()
    {
        VehicleId = 6, Model = "VinFast VF8", Type = "SUV", Price = 640000000m,
        DealerId = 3, CreatedAt = DateTime.UtcNow,
    };

    private static VehicleUpdatedEvent Updated() => new()
    {
        VehicleId = 6, Model = "VinFast VF9", Type = "SUV", Price = 740000000m,
        DealerId = 3, UpdatedAt = DateTime.UtcNow,
    };

    private static VehicleDeletedEvent Deleted() => new()
    {
        VehicleId = 6, DealerId = 3, DeletedAt = DateTime.UtcNow,
    };

    // ---- vehicle.created ------------------------------------------------------

    [Fact]
    public async Task VehicleCreated_MulticastsToDealerSubject()
    {
        await RegisterAsync(3, "tok-phone", "tok-laptop");
        var fcm = Fcm();
        await new VehicleCreatedConsumer(fcm, Registry()).HandleAsync(Json(Created()));

        Assert.Equal(new[] { "tok-laptop", "tok-phone" }, fcm.LastMulticastTokens?.OrderBy(t => t).ToList());
        Assert.Equal("vehicleCreated", fcm.LastMulticastData!["type"]);
        Assert.Equal("6", fcm.LastMulticastData["vehicleId"]);
        Assert.Equal("3", fcm.LastMulticastData["dealerId"]);
        Assert.Contains("VF8", fcm.LastMulticastBody);
    }

    [Fact]
    public async Task VehicleCreated_ThrowWhenFcmReportsFailure()
    {
        await RegisterAsync(3, "tok-a");
        var fcm = Fcm();
        fcm.Result = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new VehicleCreatedConsumer(fcm, Registry()).HandleAsync(Json(Created())));
    }

    [Fact]
    public async Task VehicleCreated_NoWhereToSend_LogsOnlyAndDoesNotThrow()
    {
        // Nothing registered for dealer:99 — the honest log-only fallback.
        var fcm = Fcm();
        await new VehicleCreatedConsumer(fcm, Registry()).HandleAsync(Json(new VehicleCreatedEvent
        {
            VehicleId = 7, Model = "VF e34", Type = "SUV", Price = 1m,
            DealerId = 99, CreatedAt = DateTime.UtcNow,
        }));

        Assert.Null(fcm.LastMulticastTokens); // never reached FCM
    }

    // ---- vehicle.updated ------------------------------------------------------

    [Fact]
    public async Task VehicleUpdated_MulticastsToDealerSubject()
    {
        await RegisterAsync(3, "tok-a", "tok-b");
        var fcm = Fcm();
        await new VehicleUpdatedConsumer(fcm, Registry()).HandleAsync(Json(Updated()));

        Assert.Equal(new[] { "tok-a", "tok-b" }, fcm.LastMulticastTokens);
        Assert.Contains("VF9", fcm.LastMulticastBody);
        Assert.Equal("vehicleUpdated", fcm.LastMulticastData!["type"]);
        Assert.Equal("3", fcm.LastMulticastData["dealerId"]);
    }

    [Fact]
    public async Task VehicleUpdated_ThrowWhenFcmReportsFailure()
    {
        await RegisterAsync(3, "tok-a");
        var fcm = Fcm();
        fcm.Result = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new VehicleUpdatedConsumer(fcm, Registry()).HandleAsync(Json(Updated())));
    }

    [Fact]
    public async Task VehicleUpdated_NoWhereToSend_LogsOnlyAndDoesNotThrow()
    {
        // Nothing registered for dealer:3 — pins the log-only guard here too.
        // Without it, the "registered != null" mutant (empty list reaches FCM,
        // SendMulticastAsync returns false on empty input, consumer throws ->
        // retry/DLQ churn for healthy events) would pass this consumer's
        // section: the created/deleted sections each pin the guard, and the
        // class header claims the fallback as a pinned behavior for all three.
        var fcm = Fcm();
        await new VehicleUpdatedConsumer(fcm, Registry()).HandleAsync(Json(Updated()));

        Assert.Null(fcm.LastMulticastTokens);
    }

    // ---- vehicle.deleted ------------------------------------------------------

    [Fact]
    public async Task VehicleDeleted_MulticastsToDealerSubject()
    {
        await RegisterAsync(3, "tok-phone");
        var fcm = Fcm();
        await new VehicleDeletedConsumer(fcm, Registry()).HandleAsync(Json(Deleted()));

        Assert.Equal(new[] { "tok-phone" }, fcm.LastMulticastTokens);
        Assert.Contains("#6", fcm.LastMulticastBody);
        Assert.Equal("vehicleDeleted", fcm.LastMulticastData!["type"]);
        Assert.Equal("6", fcm.LastMulticastData["vehicleId"]);
        Assert.Equal("3", fcm.LastMulticastData["dealerId"]);
    }

    [Fact]
    public async Task VehicleDeleted_ThrowWhenFcmReportsFailure()
    {
        await RegisterAsync(3, "tok-a");
        var fcm = Fcm();
        fcm.Result = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new VehicleDeletedConsumer(fcm, Registry()).HandleAsync(Json(Deleted())));
    }

    [Fact]
    public async Task VehicleDeleted_PreIssue38Message_LogsOnlyAndDoesNotThrow()
    {
        // Simulates a message from a producer that predates #38: the JSON has
        // no DealerId (the event didn't have one), so the mirror DTO defaults
        // it to 0 and the lookup (dealer:0) finds nothing — log-only, not a
        // retry/DLQ spiral. Pinning this is what keeps a rolling deploy safe.
        var legacyPayload = Json(new
        {
            VehicleId = 6, DeletedAt = DateTime.UtcNow,
        });
        var fcm = Fcm();
        await new VehicleDeletedConsumer(fcm, Registry()).HandleAsync(legacyPayload);

        Assert.Null(fcm.LastMulticastTokens);
    }

    // ---- subject-spelling pins across all three -------------------------------

    [Fact]
    public async Task VehicleDeleted_TokenForOtherDealer_NotUsed()
    {
        // dealer 4's token must never receive dealer 3's delete — a subject
        // drift is visible this way (same pin style as OrderStatusChanged_...).
        await RegisterAsync(4, "tok-wrong");
        var fcm = Fcm();
        await new VehicleDeletedConsumer(fcm, Registry()).HandleAsync(Json(Deleted()));

        Assert.Null(fcm.LastMulticastTokens);
    }

    [Fact]
    public async Task VehicleCreated_CustomerSubjectToken_NotUsed()
    {
        // The vehicle events are DEALER-scoped: a token planted on
        // customer:3 (the #35/#37 spelling) must not catch dealer 3's event —
        // pins that these consumers use NotificationSubjects.Dealer, not a
        // same-number Customer subject.
        var reg = Registry();
        await reg.RegisterAsync(NotificationSubjects.Customer(3), "tok-customer3");
        var fcm = Fcm();
        await new VehicleCreatedConsumer(fcm, reg).HandleAsync(Json(Created()));

        Assert.Null(fcm.LastMulticastTokens);
    }

    /// <summary>Records the last multicast attempt and returns a canned result.
    /// All #38 push paths fan out via SendMulticastAsync.</summary>
    private sealed class RecordingFcm : IFcmService
    {
        public bool Result { get; set; } = true;
        public List<string>? LastMulticastTokens { get; private set; }
        public string? LastMulticastTitle { get; private set; }
        public string? LastMulticastBody { get; private set; }
        public Dictionary<string, string>? LastMulticastData { get; private set; }

        public Task<bool> SendMulticastAsync(List<string> deviceTokens, string title, string body, Dictionary<string, string>? data = null)
        {
            LastMulticastTokens = deviceTokens;
            LastMulticastTitle = title;
            LastMulticastBody = body;
            LastMulticastData = data;
            return Task.FromResult(Result);
        }

        public Task<bool> SendNotificationAsync(string deviceToken, string title, string body, Dictionary<string, string>? data = null)
            => throw new NotSupportedException("#38 push paths only multicast");
        public Task<bool> SendToTopicAsync(string topic, string title, string body, Dictionary<string, string>? data = null)
            => throw new NotSupportedException();
        public Task<bool> SubscribeToTopicAsync(string deviceToken, string topic)
            => throw new NotSupportedException();
        public Task<bool> UnsubscribeFromTopicAsync(string deviceToken, string topic)
            => throw new NotSupportedException();
    }
}
