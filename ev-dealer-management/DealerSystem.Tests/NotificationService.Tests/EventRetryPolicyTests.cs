using NotificationService.Events;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace NotificationService.Tests;

/// <summary>
/// Pure-logic tests for the W4 retry policy (docs/EVENTS.md). The broker
/// round-trip itself is covered by live compose runs; what regressed in the
/// past (and what these lock down) is the x-death parsing and the queue-name
/// conventions every .retry/.dlq triplet depends on.
/// </summary>
public class EventRetryPolicyTests
{
    // ---- queue naming ------------------------------------------------------

    [Fact]
    public void RetryQueueFor_AppendsDotRetry()
    {
        Assert.Equal("vehicle.created.retry", EventRetryPolicy.RetryQueueFor("vehicle.created"));
    }

    [Fact]
    public void DeadLetterQueueFor_AppendsDotDlq()
    {
        Assert.Equal("vehicle.created.dlq", EventRetryPolicy.DeadLetterQueueFor("vehicle.created"));
    }

    [Fact]
    public void Defaults_MatchDocumentedKnobs()
    {
        // docs/EVENTS.md + appsettings ship 3 attempts / 5000ms; a silent
        // change here changes live broker behavior for every consumer.
        Assert.Equal(3, EventRetryPolicy.DefaultMaxAttempts);
        Assert.Equal(5000, EventRetryPolicy.DefaultRetryTtlMs);
    }

    // ---- RetryRounds -------------------------------------------------------

    // RabbitMQ.Client 6.x marks Framing.BasicProperties internal (production
    // code only ever gets one via channel.CreateBasicProperties()); the
    // constructor is public, so reflection instantiates it without the test
    // assembly needing an InternalsVisibleTo on a vendor package.
    private static IBasicProperties NewProperties()
    {
        var type = typeof(IBasicProperties).Assembly
            .GetType("RabbitMQ.Client.Framing.BasicProperties", throwOnError: true)!;
        return (IBasicProperties)Activator.CreateInstance(type)!;
    }

    private static BasicDeliverEventArgs DeliveryWithHeaders(
        params Dictionary<string, object>[] xDeathEntries)
    {
        var props = NewProperties();
        props.Headers = new Dictionary<string, object>
        {
            ["x-death"] = xDeathEntries.Cast<object>().ToList(),
        };
        return new BasicDeliverEventArgs { BasicProperties = props };
    }

    [Fact]
    public void RetryRounds_NoHeaders_IsZero()
    {
        var args = new BasicDeliverEventArgs();
        Assert.Equal(0, EventRetryPolicy.RetryRounds(args, "payment.received"));
    }

    [Fact]
    public void RetryRounds_HappyPath_CountsExpiredRoundsForOwnRetryQueue()
    {
        var args = DeliveryWithHeaders(new Dictionary<string, object>
        {
            ["queue"] = "payment.received.retry",
            ["reason"] = "expired",
            ["count"] = 2L, // broker sends long
        });
        Assert.Equal(2, EventRetryPolicy.RetryRounds(args, "payment.received"));
    }

    [Fact]
    public void RetryRounds_DeathFromAnotherQueue_IsIgnored()
    {
        // Two bindings on one exchange (the vehicle.reserved fan-out) share a
        // broker; x-death entries for foreign queues must not count here.
        var args = DeliveryWithHeaders(new Dictionary<string, object>
        {
            ["queue"] = "customer_vehicle_reserved.retry",
            ["reason"] = "expired",
            ["count"] = 5L,
        });
        Assert.Equal(0, EventRetryPolicy.RetryRounds(args, "vehicle.reserved"));
    }

    [Fact]
    public void RetryRounds_RejectedNotExpired_IsIgnored()
    {
        // reason=rejected means someone nack'd it elsewhere; the TTL loop
        // (reason=expired) is the only thing that counts as a retry round.
        var args = DeliveryWithHeaders(new Dictionary<string, object>
        {
            ["queue"] = "order.created.retry",
            ["reason"] = "rejected",
            ["count"] = 9L,
        });
        Assert.Equal(0, EventRetryPolicy.RetryRounds(args, "order.created"));
    }

    [Fact]
    public void RetryRounds_QueueNameAsUtf8Bytes_IsNormalized()
    {
        // RabbitMQ.Client 6.x returns table strings as string OR byte[]
        // depending on field encoding — both must work (the AsString branch).
        var args = DeliveryWithHeaders(new Dictionary<string, object>
        {
            ["queue"] = System.Text.Encoding.UTF8.GetBytes("quote.created.retry"),
            ["reason"] = System.Text.Encoding.UTF8.GetBytes("expired"),
            ["count"] = 1L,
        });
        Assert.Equal(1, EventRetryPolicy.RetryRounds(args, "quote.created"));
    }

    [Fact]
    public void RetryRounds_MissingCount_IsZero()
    {
        var args = DeliveryWithHeaders(new Dictionary<string, object>
        {
            ["queue"] = "contract.created.retry",
            ["reason"] = "expired",
        });
        Assert.Equal(0, EventRetryPolicy.RetryRounds(args, "contract.created"));
    }

    [Fact]
    public void RetryRounds_CountNotLong_IsZero()
    {
        // int-boxed count is not what the broker sends; accepting it silently
        // would mask an upstream change. Explicit int must NOT match `is long`.
        var args = DeliveryWithHeaders(new Dictionary<string, object>
        {
            ["queue"] = "sales.completed.retry",
            ["reason"] = "expired",
            ["count"] = 3, // int, not long
        });
        Assert.Equal(0, EventRetryPolicy.RetryRounds(args, "sales.completed"));
    }

    [Fact]
    public void RetryRounds_PicksOwnEntryAmongDeaths()
    {
        // x-death may hold several entries; only ours counts.
        var args = DeliveryWithHeaders(
            new Dictionary<string, object>
            {
                ["queue"] = "other.queue.retry",
                ["reason"] = "expired",
                ["count"] = 7L,
            },
            new Dictionary<string, object>
            {
                ["queue"] = "testdrive.scheduled.retry",
                ["reason"] = "expired",
                ["count"] = 2L,
            });
        Assert.Equal(2, EventRetryPolicy.RetryRounds(args, "testdrive.scheduled"));
    }
}
