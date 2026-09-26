using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> that SKIPS (rather than silently passes) when
/// no real RabbitMQ broker is reachable.
///
/// Same reasoning as <see cref="PostgresFactAttribute"/>, and it matters MORE
/// here: the tests that use it assert the live queue topology, so a body that
/// returned early would report "the topology is correct" on a machine with no
/// broker at all. A topology test that cannot reach the broker and passes
/// anyway is the exact false-confidence shape this suite exists to remove.
///
/// Set <c>EVM_TEST_RABBITMQ_HOST</c> (default port 5672, user/pass
/// <c>guest</c>/<c>guest</c>) to run them, e.g.
///   $env:EVM_TEST_RABBITMQ_HOST = "localhost"
/// against the docker-compose RabbitMQ.
/// </summary>
public sealed class BrokerFactAttribute : FactAttribute
{
    public const string HostVariable = "EVM_TEST_RABBITMQ_HOST";

    public BrokerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HostVariable)))
        {
            Skip = $"{HostVariable} is not set — no real broker to test the topology against";
        }
    }
}
