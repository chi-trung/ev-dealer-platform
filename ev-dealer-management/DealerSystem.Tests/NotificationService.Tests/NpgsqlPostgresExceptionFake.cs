// Deliberately in namespace Npgsql: the provider-neutral detection keys off
// GetType().FullName.StartsWith("Npgsql."), so the fake has to live there to
// exercise the branch. Named *Fake so nobody mistakes it for the driver.
namespace Npgsql;

internal sealed class PostgresExceptionFake : Exception
{
    public string? SqlState { get; }
    public PostgresExceptionFake(string message, string? sqlState)
        : base(message) => SqlState = sqlState;
}

