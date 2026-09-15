using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NotificationService.Consumers;
using NotificationService.Data;
using NotificationService.DTOs;
using NotificationService.Services;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Issue #56 end-to-end at the two real user-targeted fan-out points: the
/// Quote/Contract consumers' salesperson audience. The policy unit tests
/// prove the decision table; these prove the WIRING — that a muted
/// salesperson's push is skipped while the customer's primary push is
/// byte-identical to the pre-#56 behavior, that an unmuted salesperson
/// actually receives the audience:"salesperson" multicast, and that the
/// secondary push is best-effort: an exception inside it (muted-channel
/// path never throws, but the token lookup or send can) must NOT bubble out
/// of HandleAsync, because the bus retrying the event would double-push
/// the already-delivered customer.
///
/// Real registry + real preferences store (temp-file SQLite), real policy;
/// only FCM is faked — and faked with a recorder that keeps EVERY send,
/// unlike the shared RecordingFcm (last-only), because these tests assert
/// on the customer AND salesperson multicasts in one event.
/// </summary>
[Collection("sqlite")]
public class PreferenceFanoutConsumerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<NotificationDbContext> _options;

    public PreferenceFanoutConsumerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"pref_fanout_{Guid.NewGuid():N}.db");
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
    private NotificationPreferencesStore Store() => new(new NotificationDbContext(_options));
    private NotificationPreferencePolicy Policy() => new(Store());

    private static string Json(object payload) =>
        System.Text.Json.JsonSerializer.Serialize(payload);

    private async Task RegisterAsync(string subject, params string[] tokens)
    {
        var reg = Registry();
        foreach (var t in tokens)
            await reg.RegisterAsync(subject, t);
    }

    private Task SavePrefsAsync(string subject, NotificationPreferencesDto prefs)
        => Store().PutAsync(subject, prefs);

    private static NotificationPreferencesDto Prefs(
        bool inApp = true, bool orders = true) => new()
        {
            EmailNotifications = true,
            SmsNotifications = false,
            InAppNotifications = inApp,
            Orders = orders,
            Deliveries = true,
            Payments = true,
        };

    private static QuoteCreatedEvent Quote(int salespersonId = 1) => new()
    {
        QuoteId = "Q-77",
        CustomerId = 5,
        DealerId = 2,
        SalespersonId = salespersonId,
        VehicleId = 9,
        Quantity = 1,
        TotalBasePrice = 555_000_000m,
        Status = "Draft",
        CreatedAt = DateTime.UtcNow,
    };

    private static ContractCreatedEvent Contract(int salespersonId = 1) => new()
    {
        ContractId = "c-1",
        ContractNumber = "HD-001",
        OrderId = 3,
        CustomerId = 5,
        DealerId = 2,
        SalespersonId = salespersonId,
        TotalAmount = 600_000_000m,
        Status = "Draft",
        CreatedAt = DateTime.UtcNow,
    };

    // ---- QuoteCreated ---------------------------------------------------------

    [Fact]
    public async Task Quote_SalespersonMutesOrders_CustomerDeliveredSalespersonSuppressed()
    {
        // The acceptance pair, wiring half: muted type ⇒ NOT delivered...
        await RegisterAsync("customer:5", "tok-cust");
        await RegisterAsync("user:1", "tok-sales");
        await SavePrefsAsync("user:1", Prefs(orders: false));
        var fcm = new AllSendsFcm();

        await new QuoteCreatedConsumer(fcm, Registry(), Policy())
            .HandleAsync(Json(Quote(salespersonId: 1)));

        var send = Assert.Single(fcm.Sends);
        Assert.Equal(new[] { "tok-cust" }, send.Tokens);
        Assert.Equal("quote", send.Data["type"]);
        Assert.False(send.Data.ContainsKey("audience")); // customer push shape unchanged
    }

    [Fact]
    public async Task Quote_SalespersonUnmuted_BothAudiencesDelivered()
    {
        // ...and unmuted ⇒ delivered, as a SECOND multicast tagged for the
        // salesperson on their own subject's tokens.
        await RegisterAsync("customer:5", "tok-cust");
        await RegisterAsync("user:1", "tok-sales");
        await SavePrefsAsync("user:1", Prefs(orders: true));
        var fcm = new AllSendsFcm();

        await new QuoteCreatedConsumer(fcm, Registry(), Policy())
            .HandleAsync(Json(Quote(salespersonId: 1)));

        Assert.Equal(2, fcm.Sends.Count);
        var cust = fcm.Sends.First(s => s.Tokens.Contains("tok-cust"));
        var sales = fcm.Sends.First(s => s.Tokens.Contains("tok-sales"));
        Assert.False(cust.Data.ContainsKey("audience"));
        Assert.Equal(new[] { "tok-sales" }, sales.Tokens);
        Assert.Equal("salesperson", sales.Data["audience"]);
        Assert.Equal("quote", sales.Data["type"]);
    }

    [Fact]
    public async Task Quote_InAppChannelMuted_SalespersonSuppressedCustomerUnaffected()
    {
        await RegisterAsync("customer:5", "tok-cust");
        await RegisterAsync("user:1", "tok-sales");
        await SavePrefsAsync("user:1", Prefs(inApp: false, orders: true));
        var fcm = new AllSendsFcm();

        await new QuoteCreatedConsumer(fcm, Registry(), Policy())
            .HandleAsync(Json(Quote()));

        Assert.Single(fcm.Sends);
        Assert.Equal(new[] { "tok-cust" }, fcm.Sends[0].Tokens);
    }

    [Fact]
    public async Task Quote_NeverSavedSalesperson_DefaultsDeliver()
    {
        // No preferences row at all: GetAsync contract ⇒ shared defaults
        // (orders ON) ⇒ salesperson gets the push. Backward-compatible:
        // pre-#56 users are opted in to orders-shaped traffic.
        await RegisterAsync("customer:5", "tok-cust");
        await RegisterAsync("user:1", "tok-sales");
        var fcm = new AllSendsFcm();

        await new QuoteCreatedConsumer(fcm, Registry(), Policy())
            .HandleAsync(Json(Quote()));

        Assert.Equal(2, fcm.Sends.Count);
    }

    [Fact]
    public async Task Quote_NoSalespersonId_CustomerOnly_BackwardCompat()
    {
        // Old in-flight messages without the field (deserializes to 0):
        // exactly the pre-#56 single-push behavior, no policy read.
        await RegisterAsync("customer:5", "tok-cust");
        await RegisterAsync("user:0", "tok-ghost"); // must NOT be touched
        var fcm = new AllSendsFcm();

        await new QuoteCreatedConsumer(fcm, Registry(), Policy())
            .HandleAsync(Json(Quote(salespersonId: 0)));

        var send = Assert.Single(fcm.Sends);
        Assert.Equal(new[] { "tok-cust" }, send.Tokens);
    }

    [Fact]
    public async Task Quote_SalespersonMutedButCustomerPushFails_StillThrows()
    {
        // #56 must not soften the PRIMARY contract: customer-side FCM
        // failure still throws so the bus retries/DLQs — suppression of the
        // secondary audience changes nothing about that path.
        await RegisterAsync("customer:5", "tok-cust");
        await RegisterAsync("user:1", "tok-sales");
        await SavePrefsAsync("user:1", Prefs(orders: false));
        var fcm = new AllSendsFcm { Result = false };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new QuoteCreatedConsumer(fcm, Registry(), Policy()).HandleAsync(Json(Quote())));
    }

    [Fact]
    public async Task Quote_SecondaryPushThrows_CustomerPushAlreadySentAndNoRethrow()
    {
        // The best-effort guarantee: everything primary succeeded, then the
        // salesperson send itself fails (Success=false inside the block is
        // logged, not thrown). HandleAsync completes, no double-push.
        await RegisterAsync("customer:5", "tok-cust");
        await RegisterAsync("user:1", "tok-sales");
        var fcm = new AllSendsFcm { FailOnSalespersonSend = true };

        await new QuoteCreatedConsumer(fcm, Registry(), Policy())
            .HandleAsync(Json(Quote()));

        Assert.Single(fcm.Sends); // only the customer send reached the recorder
    }

    [Fact]
    public async Task Quote_SecondaryPushThrowsException_AbsorbedExactlyOneCustomerPush()
    {
        // The inner catch of the #56 block IS the double-push guard, and
        // only an EXCEPTION proves it: the Success=false path above rides
        // the log branch instead. A throw genuinely escapes in production
        // (DeviceTokenRegistry.GetTokensAsync fail-s-soft only for
        // DbUpdate/Sqlite/IO/UnauthorizedAccessException — a cancellation
        // on shutdown escapes that filter into this block). If the catch
        // were ever dropped, the bus retries the whole event and the
        // already-delivered customer is pushed twice — this test fails
        // loudly in that refactor because HandleAsync would rethrow.
        await RegisterAsync("customer:5", "tok-cust");
        await RegisterAsync("user:1", "tok-sales");
        var fcm = new AllSendsFcm { ThrowOnSalespersonSend = true };

        await new QuoteCreatedConsumer(fcm, Registry(), Policy())
            .HandleAsync(Json(Quote())); // must NOT throw

        var send = Assert.Single(fcm.Sends); // the customer's, exactly once
        Assert.Equal(new[] { "tok-cust" }, send.Tokens);
    }

    // ---- ContractCreated ------------------------------------------------------

    [Fact]
    public async Task Contract_SalespersonMutesOrders_CustomerDeliveredSalespersonSuppressed()
    {
        await RegisterAsync("customer:5", "tok-cust");
        await RegisterAsync("user:1", "tok-sales");
        await SavePrefsAsync("user:1", Prefs(orders: false));
        var fcm = new AllSendsFcm();

        await new ContractCreatedConsumer(fcm, Registry(), Policy())
            .HandleAsync(Json(Contract()));

        var send = Assert.Single(fcm.Sends);
        Assert.Equal(new[] { "tok-cust" }, send.Tokens);
        Assert.Equal("contract", send.Data["type"]);
    }

    [Fact]
    public async Task Contract_SalespersonUnmuted_BothAudiencesDelivered()
    {
        await RegisterAsync("customer:5", "tok-cust");
        await RegisterAsync("user:1", "tok-sales");
        await SavePrefsAsync("user:1", Prefs(orders: true));
        var fcm = new AllSendsFcm();

        await new ContractCreatedConsumer(fcm, Registry(), Policy())
            .HandleAsync(Json(Contract()));

        Assert.Equal(2, fcm.Sends.Count);
        var sales = fcm.Sends.First(s => s.Tokens.Contains("tok-sales"));
        Assert.Equal("salesperson", sales.Data["audience"]);
        Assert.Equal("contract", sales.Data["type"]);
        Assert.Equal("HD-001", sales.Data["contractNumber"]);
    }

    [Fact]
    public async Task Contract_CustomerSubjectCollision_PrefsUnderCustomerKeyCannotMutePrimary()
    {
        // The id-space-collision guard, proven at the wiring level: a fully
        // muted document saved under "customer:5" (the primary audience's
        // exact registry key) must not suppress the customer push. A LIVE
        // salesperson keeps the run honest: with salespersonId=0 the policy
        // is never invoked here at all (the branch above it is skipped), so
        // a scope-loosening regression in IsUserSubject would sail through.
        // With it >0 the store read happens mid-handler, and if the policy
        // ever filtered customer: keys, the customer multicast disappears
        // and this fails.
        await RegisterAsync("customer:5", "tok-cust");
        await RegisterAsync("user:1", "tok-sales");
        await SavePrefsAsync("customer:5", Prefs(inApp: false, orders: false));
        var fcm = new AllSendsFcm();

        await new ContractCreatedConsumer(fcm, Registry(), Policy())
            .HandleAsync(Json(Contract(salespersonId: 1)));

        Assert.Equal(2, fcm.Sends.Count); // customer NOT suppressed despite the row at its key
        Assert.Contains(fcm.Sends, s => s.Tokens.SequenceEqual(new[] { "tok-cust" }));
        Assert.Contains(fcm.Sends, s => s.Data.TryGetValue("audience", out var a) && a == "salesperson");
    }

    /// <summary>Keeps EVERY multicast (the shared RecordingFcm keeps only
    /// the last, but a fan-out event sends twice). The two FailOn/ThrowOn
    /// knobs reproduce secondary-push failures from both angles: a
    /// reported-failure result (the !Success log branch) and a genuine
    /// exception escaping the send (what the block's catch actually
    /// defends, and the only shape that catches a dropped-catch refactor).</summary>
    private sealed class AllSendsFcm : IFcmService
    {
        public sealed record Send(IReadOnlyList<string> Tokens, string Title, string Body, Dictionary<string, string> Data);

        public List<Send> Sends { get; } = new();
        public bool Result { get; set; } = true;
        public bool FailOnSalespersonSend { get; set; }
        public bool ThrowOnSalespersonSend { get; set; }

        public Task<MulticastResult> SendMulticastAsync(List<string> deviceTokens, string title, string body, Dictionary<string, string>? data = null)
        {
            var audienceSalesperson = data is not null && data.TryGetValue("audience", out var a) && a == "salesperson";
            if (audienceSalesperson && ThrowOnSalespersonSend)
                throw new InvalidOperationException("simulated throw inside the #56 secondary push");
            if (audienceSalesperson && FailOnSalespersonSend)
                return Task.FromResult(new MulticastResult(false, Array.Empty<string>()));

            Sends.Add(new Send(deviceTokens, title, body, data ?? new()));
            return Task.FromResult(new MulticastResult(Result, Array.Empty<string>()));
        }

        public Task<bool> SendNotificationAsync(string deviceToken, string title, string body, Dictionary<string, string>? data = null)
            => throw new NotSupportedException("#56 fan-out paths only multicast");
        public Task<bool> SendToTopicAsync(string topic, string title, string body, Dictionary<string, string>? data = null)
            => throw new NotSupportedException();
        public Task<bool> SubscribeToTopicAsync(string deviceToken, string topic)
            => throw new NotSupportedException();
        public Task<bool> UnsubscribeFromTopicAsync(string deviceToken, string topic)
            => throw new NotSupportedException();
    }
}
