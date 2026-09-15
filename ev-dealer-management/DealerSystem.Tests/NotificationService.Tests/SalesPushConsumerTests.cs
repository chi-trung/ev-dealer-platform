using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NotificationService.Consumers;
using NotificationService.Data;
using NotificationService.DTOs;
using NotificationService.Services;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Issue #37: the sales.completed / payment.received / order.status.changed
/// consumers became push-capable. payment.received and order.status.changed
/// are registry-only (payload carries no DeviceToken by design);
/// sales.completed keeps the payload-token-wins contract and adds the
/// registry fallback. Same pinned behaviors as CustomerConsumerTests: multicast
/// to every live token for customer:&lt;CustomerId&gt;, throw-on-false (so the
/// bus retries a failed delivery), and the honest log-only fallback — which
/// also covers pre-#37 messages whose missing CustomerId deserializes to 0
/// (subject customer:0 exists by construction, and nothing registers to it).
/// Real registry (SQLite temp file) + a recording IFcmService fake; the
/// consumers are what's under test, not Firebase or EF.
/// </summary>
[Collection("sqlite")]
public class SalesPushConsumerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<NotificationDbContext> _options;

    public SalesPushConsumerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"sales_push_{Guid.NewGuid():N}.db");
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

    private async Task RegisterAsync(int customerId, params string[] tokens)
    {
        var reg = Registry();
        foreach (var t in tokens)
            await reg.RegisterAsync(NotificationSubjects.Customer(customerId), t);
    }

    private static SaleCompletedEvent Sale(string? deviceToken = null) => new()
    {
        OrderId = "11", CustomerEmail = "d@example.com", CustomerName = "Pham Van D",
        VehicleModel = "VF8", TotalPrice = 640000000m, CompletedAt = DateTime.UtcNow,
        DeviceToken = deviceToken, CustomerId = 5,
    };

    private static PaymentReceivedEvent Payment() => new()
    {
        PaymentId = "p-1", OrderId = "11", Amount = 10000000m, PaymentMethod = "Cash",
        Status = "Paid", PaidDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, CustomerId = 5,
    };

    private static OrderStatusChangedEvent StatusChanged() => new()
    {
        OrderId = "11", OrderNumber = "ORD-2026-0913", OldStatus = "Pending",
        NewStatus = "Confirmed", ChangedAt = DateTime.UtcNow, CustomerId = 5,
    };

    // ---- sales.completed: payload-token-wins + registry fallback ------------

    [Fact]
    public async Task SaleCompleted_PayloadTokenWins_RegistryNeverUsed()
    {
        // Customer 5 also has registry tokens — the in-band token must still
        // win and the push must go ONLY to it (the pre-#37 contract, kept).
        await RegisterAsync(5, "tok-registry");
        var fcm = Fcm();
        await new SaleCompletedConsumer(fcm, Registry()).HandleAsync(Json(Sale("tok-payload")));

        Assert.Equal(new[] { "tok-payload" }, fcm.LastMulticastTokens);
    }

    [Fact]
    public async Task SaleCompleted_NoPayloadToken_FallsBackToRegistry()
    {
        await RegisterAsync(5, "tok-phone", "tok-laptop");
        var fcm = Fcm();
        await new SaleCompletedConsumer(fcm, Registry()).HandleAsync(Json(Sale(deviceToken: null)));

        Assert.Equal(new[] { "tok-laptop", "tok-phone" }, fcm.LastMulticastTokens?.OrderBy(t => t).ToList());
        Assert.Equal("sale", fcm.LastMulticastData!["type"]);
        Assert.Equal("5", fcm.LastMulticastData["customerId"]);
    }

    [Fact]
    public async Task SaleCompleted_ThrowWhenFcmReportsFailure()
    {
        await RegisterAsync(5, "tok-a");
        var fcm = Fcm();
        fcm.Result = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SaleCompletedConsumer(fcm, Registry()).HandleAsync(Json(Sale())));
    }

    [Fact]
    public async Task SaleCompleted_NoWhereToSend_LogsOnlyAndDoesNotThrow()
    {
        // No payload token AND nothing registered for customer:99.
        var fcm = Fcm();
        await new SaleCompletedConsumer(fcm, Registry()).HandleAsync(Json(new SaleCompletedEvent
        {
            OrderId = "12", CustomerEmail = "n@example.com", CustomerName = "Nobody",
            VehicleModel = "VF e34", TotalPrice = 1m, CompletedAt = DateTime.UtcNow,
            DeviceToken = null, CustomerId = 99,
        }));

        Assert.Null(fcm.LastMulticastTokens); // never reached FCM
    }

    // ---- payment.received (registry-only) -----------------------------------

    [Fact]
    public async Task PaymentReceived_MulticastsToRegistrySubject()
    {
        await RegisterAsync(5, "tok-phone");
        var fcm = Fcm();
        await new PaymentReceivedConsumer(fcm, Registry()).HandleAsync(Json(Payment()));

        Assert.Equal(new[] { "tok-phone" }, fcm.LastMulticastTokens);
        // The consumer renders the amount with {Amount:N0} under
        // CultureInfo.CurrentCulture, so the expected grouping must be computed
        // the same way — a hardcoded "10,000,000" would fail on vi-VN/de-DE
        // hosts ("10.000.000") without any product regression. Pins the
        // N0-grouping intent culture-symmetrically (review of PR #42).
        Assert.Contains(10000000m.ToString("N0"), fcm.LastMulticastBody);
        Assert.Equal("payment", fcm.LastMulticastData!["type"]);
        Assert.Equal("11", fcm.LastMulticastData["orderId"]);
        Assert.Equal("5", fcm.LastMulticastData["customerId"]);
    }

    [Fact]
    public async Task PaymentReceived_ThrowWhenFcmReportsFailure()
    {
        await RegisterAsync(5, "tok-a");
        var fcm = Fcm();
        fcm.Result = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PaymentReceivedConsumer(fcm, Registry()).HandleAsync(Json(Payment())));
    }

    [Fact]
    public async Task PaymentReceived_PreIssue37Message_LogsOnlyAndDoesNotThrow()
    {
        // Simulates a message from a producer that predates #37: the JSON has
        // no CustomerId, so the mirror DTO defaults it to 0 and the lookup
        // (customer:0) finds nothing — log-only, not a retry/DLQ spiral.
        var legacyPayload = Json(new
        {
            PaymentId = "p-old", OrderId = "11", Amount = 5m, PaymentMethod = "Cash",
            Status = "Paid", PaidDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
        });
        var fcm = Fcm();
        await new PaymentReceivedConsumer(fcm, Registry()).HandleAsync(legacyPayload);

        Assert.Null(fcm.LastMulticastTokens);
    }

    // ---- order.status.changed (registry-only) -------------------------------

    [Fact]
    public async Task OrderStatusChanged_MulticastsToRegistrySubject()
    {
        await RegisterAsync(5, "tok-a", "tok-b");
        var fcm = Fcm();
        await new OrderStatusChangedConsumer(fcm, Registry()).HandleAsync(Json(StatusChanged()));

        Assert.Equal(new[] { "tok-a", "tok-b" }, fcm.LastMulticastTokens);
        Assert.Contains("ORD-2026-0913", fcm.LastMulticastBody);
        Assert.Contains("Confirmed", fcm.LastMulticastBody);
        Assert.Equal("orderStatus", fcm.LastMulticastData!["type"]);
        Assert.Equal("5", fcm.LastMulticastData["customerId"]);
    }

    [Fact]
    public async Task OrderStatusChanged_ThrowWhenFcmReportsFailure()
    {
        await RegisterAsync(5, "tok-a");
        var fcm = Fcm();
        fcm.Result = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new OrderStatusChangedConsumer(fcm, Registry()).HandleAsync(Json(StatusChanged())));
    }

    [Fact]
    public async Task OrderStatusChanged_TokenForOtherCustomer_NotUsed()
    {
        // Subject-spelling pin: customer 6's token must never receive
        // customer 5's status change — a subject drift is visible this way.
        await RegisterAsync(6, "tok-wrong");
        var fcm = Fcm();
        await new OrderStatusChangedConsumer(fcm, Registry()).HandleAsync(Json(StatusChanged()));

        Assert.Null(fcm.LastMulticastTokens);
    }

    // ---- dead-token revocation wiring (Issue #44) ----------------------------

    [Fact]
    public async Task OrderStatusChanged_DeadTokenIsRevokedOnSuccessfulDelivery()
    {
        // The wedge this issue is about: a stale row stays in the shared
        // mailbox forever unless someone revokes what FCM rejected. tok-a is
        // reported dead even though the delivery overall succeeded (tok-b
        // got it) — the revoke must run on the SUCCESS path, not only when
        // the consumer is about to throw anyway.
        await RegisterAsync(5, "tok-a", "tok-b");
        var fcm = Fcm();
        fcm.DeadTokens = new List<string> { "tok-a" };
        var reg = Registry();

        await new OrderStatusChangedConsumer(fcm, reg).HandleAsync(Json(StatusChanged()));

        Assert.Equal(new[] { "tok-b" }, await reg.GetTokensAsync(NotificationSubjects.Customer(5)));
    }

    [Fact]
    public async Task OrderStatusChanged_DeadTokenRevoked_BeforeTotalFailureThrow()
    {
        // Zero devices reached: the cleanup still happens (the row is dead
        // regardless of why nothing landed), and the throw contract stands —
        // the bus retries and eventually DLQs.
        await RegisterAsync(5, "tok-a");
        var fcm = Fcm();
        fcm.Result = false;
        fcm.DeadTokens = new List<string> { "tok-a" };
        var reg = Registry();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new OrderStatusChangedConsumer(fcm, reg).HandleAsync(Json(StatusChanged())));

        Assert.Empty(await reg.GetTokensAsync(NotificationSubjects.Customer(5)));
    }

    [Fact]
    public async Task OrderStatusChanged_TransientFailure_KeepsEveryRow()
    {
        // The asymmetry that matters: a send that failed for transient
        // reasons reports NO dead tokens, so every row must stay. Revoking
        // here would mass-unregister the fleet during an outage.
        await RegisterAsync(5, "tok-a", "tok-b");
        var fcm = Fcm();
        fcm.Result = false; // DeadTokens stays empty = "we don't know"
        var reg = Registry();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new OrderStatusChangedConsumer(fcm, reg).HandleAsync(Json(StatusChanged())));

        Assert.Equal(2, (await reg.GetTokensAsync(NotificationSubjects.Customer(5))).Count);
    }

    [Fact]
    public async Task SaleCompleted_PayloadTokenPath_RevokeSkipped()
    {
        // Payload token wins → registrySubject is null → a dead report about
        // the in-band token must NOT touch the customer's registry rows.
        // tok-payload is ALSO a registered row (another device, or a stale
        // duplicate string): with the subject wrongly passed on this path,
        // that row would be revoked while the registry lookup never even ran.
        await RegisterAsync(5, "tok-payload", "tok-registry");
        var fcm = Fcm();
        fcm.DeadTokens = new List<string> { "tok-payload" };
        var reg = Registry();

        await new SaleCompletedConsumer(fcm, reg).HandleAsync(Json(Sale("tok-payload")));

        Assert.Equal(new[] { "tok-payload", "tok-registry" },
            (await reg.GetTokensAsync(NotificationSubjects.Customer(5))).OrderBy(t => t).ToList());
    }

    [Fact]
    public async Task SaleCompleted_RegistryPath_DeadTokenIsRevoked()
    {
        // Same wiring check for the payload-token-wins consumer's registry
        // branch: revoke runs when the subject actually supplied the tokens.
        await RegisterAsync(5, "tok-a", "tok-b");
        var fcm = Fcm();
        fcm.DeadTokens = new List<string> { "tok-b" };
        var reg = Registry();

        await new SaleCompletedConsumer(fcm, reg).HandleAsync(Json(Sale(deviceToken: null)));

        Assert.Equal(new[] { "tok-a" }, await reg.GetTokensAsync(NotificationSubjects.Customer(5)));
    }

    /// <summary>Records the last multicast attempt and returns a canned result.
    /// All #37 push paths fan out via SendMulticastAsync (payload tokens ride
    /// the same list), so the remaining IFcmService methods are not reached.
    /// DeadTokens (Issue #44) is canned per-test to drive the revoke path.</summary>
    private sealed class RecordingFcm : IFcmService
    {
        public bool Result { get; set; } = true;
        public List<string> DeadTokens { get; set; } = new();
        public List<string>? LastMulticastTokens { get; private set; }
        public string? LastMulticastTitle { get; private set; }
        public string? LastMulticastBody { get; private set; }
        public Dictionary<string, string>? LastMulticastData { get; private set; }

        public Task<MulticastResult> SendMulticastAsync(List<string> deviceTokens, string title, string body, Dictionary<string, string>? data = null)
        {
            LastMulticastTokens = deviceTokens;
            LastMulticastTitle = title;
            LastMulticastBody = body;
            LastMulticastData = data;
            return Task.FromResult(new MulticastResult(Result, DeadTokens));
        }

        public Task<bool> SendNotificationAsync(string deviceToken, string title, string body, Dictionary<string, string>? data = null)
            => throw new NotSupportedException("#37 push paths only multicast");
        public Task<bool> SendToTopicAsync(string topic, string title, string body, Dictionary<string, string>? data = null)
            => throw new NotSupportedException();
        public Task<bool> SubscribeToTopicAsync(string deviceToken, string topic)
            => throw new NotSupportedException();
        public Task<bool> UnsubscribeFromTopicAsync(string deviceToken, string topic)
            => throw new NotSupportedException();
    }
}
