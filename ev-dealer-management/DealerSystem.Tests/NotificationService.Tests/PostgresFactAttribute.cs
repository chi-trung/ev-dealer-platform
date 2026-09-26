using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> that SKIPS (rather than silently passes) when
/// no real Postgres is available.
///
/// WHY NOT JUST <c>if (no connection) return;</c> inside the test body: a test
/// that returns early reports PASSED, so a green suite would stop meaning
/// "the Postgres path works" and start meaning "the Postgres path did not run".
/// That is the same false-confidence shape the plan exists to remove
/// (see test-all-flows.ps1 printing ✅ without asserting anything). Setting
/// <see cref="FactAttribute.Skip"/> keeps the test in the run with an explicit
/// "skipped" reason, so a machine without Postgres shows 4 skipped instead of
/// a misleading 4 passed.
///
/// xUnit v2 has no built-in runtime skip; v3 does, and the repo is on 2.9.2.
/// The SkippableFact NuGet package would also work but is not in the local
/// package cache, and adding a dependency for one attribute is not worth it.
///
/// Set <c>EVM_TEST_POSTGRES</c> to run them, e.g.
///   $env:EVM_TEST_POSTGRES = "Host=localhost;Port=5432;Database=evm_core;Username=postgres;Password=postgres"
/// against the docker-compose Postgres. The ReportingService migrations are
/// applied by the test itself via <c>Migrate()</c>.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public const string ConnectionStringVariable = "EVM_TEST_POSTGRES";

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringVariable)))
        {
            Skip = $"{ConnectionStringVariable} is not set — no real Postgres to test against";
        }
    }
}
