using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NotificationService.Consumers;
using NotificationService.Data;
using NotificationService.DTOs;
using NotificationService.Services;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Issue #35: the customer.* consumers resolve tokens out-of-band from the
/// registry (payload has no DeviceToken by design) and fan the push out to
/// ALL live devices for customer:&lt;CustomerId&gt;. These tests pin the wiring
/// the docs promise: multicast with the registered tokens, throw-on-false
/// (so the bus retries a failed delivery), log-only when nothing is
/// registered — and the subject string the lookup uses, because a drift
/// there means "silently pushes to nobody" (the exact bug class the key
/// is pinned against in DeviceTokenRegistryTests).
/// Real registry (SQLite temp file) + a recording IFcmService fake; the
/// consumers are what's under test, not Firebase or EF.
/// </summary>
[Collection("sqlite")]
public class CustomerConsumerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<NotificationDbContext> _options;

    public CustomerConsumerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"customer_consumer_{Guid.NewGuid():N}.db");
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

    // ---- customer.created ----------------------------------------------------

    [Fact]
    public async Task Created_MulticastsToAllRegisteredDevices()
    {
        await RegisterAsync(5, "tok-phone", "tok-laptop");
        var fcm = Fcm();
        var consumer = new CustomerCreatedConsumer(fcm, Registry());

        await consumer.HandleAsync(Json(new CustomerCreatedEvent
        {
            CustomerId = 5, Name = "Probe", Email = "probe@example.com", Timestamp = DateTime.UtcNow,
        }));

        Assert.Equal(new[] { "tok-phone", "tok-laptop" }, fcm.LastMulticastTokens);
        Assert.Contains("Probe", fcm.LastMulticastBody);
        Assert.Equal("customer", fcm.LastMulticastData!["type"]);
        Assert.Equal("5", fcm.LastMulticastData["customerId"]);
    }

    [Fact]
    public async Task Created_ThrowWhenFcmReportsFailure()
    {
        // The false-return contract: IFcmService swallows send errors, so the
        // consumer must throw or the bus acks a lost notification (W4 lesson).
        await RegisterAsync(5, "tok-a");
        var fcm = Fcm();
        fcm.Result = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CustomerCreatedConsumer(fcm, Registry()).HandleAsync(Json(new CustomerCreatedEvent
            {
                CustomerId = 5, Name = "Probe", Email = "probe@example.com", Timestamp = DateTime.UtcNow,
            })));
    }

    [Fact]
    public async Task Created_NoRegisteredTokens_LogsOnlyAndDoesNotThrow()
    {
        // The honest fallback: unknown subject (nobody registered) must not
        // requeue a healthy event through retry->DLQ.
        var fcm = Fcm();
        await new CustomerCreatedConsumer(fcm, Registry()).HandleAsync(Json(new CustomerCreatedEvent
        {
            CustomerId = 99, Name = "Nobody", Email = "n@example.com", Timestamp = DateTime.UtcNow,
        }));

        Assert.Null(fcm.LastMulticastTokens); // never reached FCM
    }

    // ---- customer.updated / customer.deleted (same contract, less payload) --

    [Fact]
    public async Task Updated_MulticastsToRegistrySubject()
    {
        await RegisterAsync(7, "tok-x");
        var fcm = Fcm();
        await new CustomerUpdatedConsumer(fcm, Registry()).HandleAsync(Json(new CustomerUpdatedEvent
        {
            CustomerId = 7, Name = "Renamed", Email = "r@example.com", Status = "inactive", Timestamp = DateTime.UtcNow,
        }));

        Assert.Equal(new[] { "tok-x" }, fcm.LastMulticastTokens);
        Assert.Contains("Renamed", fcm.LastMulticastBody);
    }

    [Fact]
    public async Task Deleted_ResolvesSubjectFromCustomerIdOnly()
    {
        // CustomerDeletedEvent carries just CustomerId+Timestamp — the registry
        // key is exactly what the payload allows; prove it lands.
        await RegisterAsync(9, "tok-d");
        var fcm = Fcm();
        await new CustomerDeletedConsumer(fcm, Registry()).HandleAsync(Json(new CustomerDeletedEvent
        {
            CustomerId = 9, Timestamp = DateTime.UtcNow,
        }));

        Assert.Equal(new[] { "tok-d" }, fcm.LastMulticastTokens);
        Assert.Contains("#9", fcm.LastMulticastBody);
    }

    [Fact]
    public async Task Deleted_ThrowWhenFcmReportsFailure()
    {
        await RegisterAsync(9, "tok-d");
        var fcm = Fcm();
        fcm.Result = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CustomerDeletedConsumer(fcm, Registry()).HandleAsync(Json(new CustomerDeletedEvent
            {
                CustomerId = 9, Timestamp = DateTime.UtcNow,
            })));
    }

    /// <summary>Records the last multicast attempt and returns a canned result.
    /// Only SendMulticastAsync matters for push consumers; the topic methods
    /// exist on the interface but no customer consumer uses them. DeadTokens
    /// (Issue #44) is canned per-test to drive the revoke path.</summary>
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
            => throw new NotSupportedException("push consumers in this test only multicast");
        public Task<bool> SendToTopicAsync(string topic, string title, string body, Dictionary<string, string>? data = null)
            => throw new NotSupportedException();
        public Task<bool> SubscribeToTopicAsync(string deviceToken, string topic)
            => throw new NotSupportedException();
        public Task<bool> UnsubscribeFromTopicAsync(string deviceToken, string topic)
            => throw new NotSupportedException();
    }
}
