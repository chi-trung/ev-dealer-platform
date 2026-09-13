using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;

namespace CustomerService.Events;

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
/// (Duplicate of NotificationService/Events/EventRetryPolicy.cs - services share
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
    /// queue via the default exchange) and &lt;queue&gt;.dlq parking queue on a
    /// THROWAWAY channel and swallows declare failures: redeclaring an existing
    /// queue with a different x-message-ttl is a channel-level
    /// PRECONDITION_FAILED soft error, and both consumers call this inside
    /// their single top-level try. On the consumer's own channel an operator's
    /// RabbitMQ:RetryTtlMilliseconds edit would therefore kill every later
    /// queue (NotificationService) or the whole consumer thread
    /// (CustomerService) while /health keeps returning 200. Fail-soft means the
    /// broker keeps its existing TTL and the consumer keeps running; the
    /// warning names the remedy. Takes the connection, not the consumer's
    /// channel: IModel (6.x) exposes no Connection property.
    /// </summary>
    public static void DeclareRetryTopology(IConnection connection, string queue, int retryTtlMs)
    {
        var retryQueue = RetryQueueFor(queue);
        try
        {
            using var tempChannel = connection.CreateModel();
            // The .dlq takes no arguments, so this declare is always idempotent.
            tempChannel.QueueDeclare(
                queue: DeadLetterQueueFor(queue),
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);
            tempChannel.QueueDeclare(
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
        }
        catch (Exception ex)
        {
            // Console.Error, not a logger: this static helper is shared by
            // both consumers and carries no ILogger; stderr lands in
            // `docker compose logs` either way.
            Console.Error.WriteLine(
                $"[EventRetryPolicy] WARNING: could not (re)declare {retryQueue}: {ex.Message}. " +
                "If this is PRECONDITION_FAILED, the broker's x-message-ttl for that queue differs from " +
                "RabbitMQ:RetryTtlMilliseconds; the existing queue keeps its old TTL and processing " +
                $"continues. To apply the new value: docker exec evm_rabbitmq rabbitmqctl delete_queue {retryQueue} " +
                "(drops any messages waiting to retry) and restart the consumer, or revert the setting.");
        }
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
    /// The topology declare goes through the same throwaway-channel guard as
    /// boot: a TTL mismatch must not close this consumer channel mid-delivery
    /// (publish then throws and the caller falls back to nack+requeue).
    /// </summary>
    public static void ScheduleRetry(IConnection connection, IModel channel, BasicDeliverEventArgs ea, string queue, int retryTtlMs)
    {
        DeclareRetryTopology(connection, queue, retryTtlMs);
        var properties = ea.BasicProperties ?? channel.CreateBasicProperties();
        channel.BasicPublish("", RetryQueueFor(queue), properties, ea.Body.ToArray());
        channel.BasicAck(ea.DeliveryTag, multiple: false);
    }

    /// <summary>
    /// Parks the delivery in the dead-letter queue for operators to inspect
    /// (and re-publish by hand once fixed) and acks the original. The body is
    /// retained - unlike the old ack-and-discard policy for malformed payloads.
    /// Only the .dlq is declared here (it takes no arguments, so the declare
    /// is always idempotent): redeclaring .retry with DefaultRetryTtlMs when
    /// the broker has it under a configured TTL would PRECONDITION_FAILED
    /// and close the consumer channel.
    /// </summary>
    public static void ParkInDeadLetterQueue(IModel channel, BasicDeliverEventArgs ea, string queue)
    {
        channel.QueueDeclare(
            queue: DeadLetterQueueFor(queue),
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);
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
