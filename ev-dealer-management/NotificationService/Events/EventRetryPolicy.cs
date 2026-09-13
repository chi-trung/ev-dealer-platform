using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;

namespace NotificationService.Events;

/// <summary>
/// Dead-letter retry topology shared by all consumers (docs/EVENTS.md):
///
///   main queue --(handler throws)--> "&lt;queue&gt;.retry" (x-message-ttl)
///       ^                                  | TTL expires
///       +-------------(dead-letter)--------+
///
///   "&lt;queue&gt;.retry" --(attempts exhausted)--> "&lt;queue&gt;.dlq" (parked for ops)
///
/// The MAIN queues are untouched: they already exist on live brokers without
/// x-dead-letter arguments, and RabbitMQ refuses to redeclare an existing
/// queue with different arguments. So the retry hop is done by re-publishing
/// to the retry queue and acking the original delivery, and the round counter
/// is the x-death header the broker maintains on the retry queue itself.
/// (Duplicate of CustomerService/Events/EventRetryPolicy.cs - services share
/// no assembly, same convention as EventNames.cs.)
/// </summary>
public static class EventRetryPolicy
{
    public const int DefaultRetryTtlMs = 5000;
    public const int DefaultMaxAttempts = 3;

    public static string RetryQueueFor(string queue) => $"{queue}.retry";
    public static string DeadLetterQueueFor(string queue) => $"{queue}.dlq";

    /// <summary>
    /// Declares the &lt;queue&gt;.retry (TTL, dead-letters back into the main
    /// queue via the default exchange) and &lt;queue&gt;.dlq parking queue.
    /// Idempotent: same arguments every time, so redeploying is safe.
    /// </summary>
    public static void DeclareRetryTopology(IModel channel, string queue, int retryTtlMs)
    {
        channel.QueueDeclare(
            queue: RetryQueueFor(queue),
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object>
            {
                ["x-message-ttl"] = retryTtlMs,
                ["x-dead-letter-exchange"] = "",
                ["x-dead-letter-routing-key"] = queue,
            });

        channel.QueueDeclare(
            queue: DeadLetterQueueFor(queue),
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);
    }

    /// <summary>
    /// How many times this message already circled through the retry queue
    /// (0 on first delivery). Reads the broker-maintained x-death header.
    /// </summary>
    public static int RetryRounds(BasicDeliverEventArgs ea, string queue)
    {
        var headers = ea.BasicProperties?.Headers;
        if (headers == null || !headers.TryGetValue("x-death", out var raw))
        {
            return 0;
        }

        if (raw is not IEnumerable<object> deaths)
        {
            return 0;
        }

        foreach (var death in deaths)
        {
            if (death is not IDictionary<string, object> entry) continue;
            if (!entry.TryGetValue("queue", out var queueValue)) continue;
            if (AsString(queueValue) != RetryQueueFor(queue)) continue;
            if (!entry.TryGetValue("reason", out var reasonValue)) continue;
            if (AsString(reasonValue) != "expired") continue;
            if (entry.TryGetValue("count", out var countValue) && countValue is long count)
            {
                return (int)count;
            }
        }

        return 0;
    }

    /// <summary>
    /// Re-publishes the delivery to the retry queue (original properties and
    /// body preserved, so x-death keeps accumulating) and acks the original.
    /// </summary>
    public static void ScheduleRetry(IModel channel, BasicDeliverEventArgs ea, string queue, int retryTtlMs)
    {
        DeclareRetryTopology(channel, queue, retryTtlMs);
        var properties = ea.BasicProperties ?? channel.CreateBasicProperties();
        channel.BasicPublish("", RetryQueueFor(queue), properties, ea.Body.ToArray());
        channel.BasicAck(ea.DeliveryTag, multiple: false);
    }

    /// <summary>
    /// Parks the delivery in the dead-letter queue for operators to inspect
    /// (and re-publish by hand once fixed) and acks the original. The body is
    /// retained - unlike the old ack-and-discard policy for malformed payloads.
    /// </summary>
    public static void ParkInDeadLetterQueue(IModel channel, BasicDeliverEventArgs ea, string queue)
    {
        DeclareRetryTopology(channel, queue, DefaultRetryTtlMs);
        var properties = ea.BasicProperties ?? channel.CreateBasicProperties();
        channel.BasicPublish("", DeadLetterQueueFor(queue), properties, ea.Body.ToArray());
        channel.BasicAck(ea.DeliveryTag, multiple: false);
    }

    // RabbitMQ.Client returns table strings as either string or byte[]
    // depending on the field encoding; normalize both.
    private static string AsString(object? value) => value switch
    {
        string s => s,
        byte[] b => Encoding.UTF8.GetString(b),
        _ => value?.ToString() ?? "",
    };
}
