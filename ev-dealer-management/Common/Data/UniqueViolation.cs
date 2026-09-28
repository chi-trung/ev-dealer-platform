using Microsoft.EntityFrameworkCore;

namespace Common.Data;

/// <summary>
/// Tells a unique-constraint violation apart from every other failed save.
/// </summary>
/// <remarks>
/// Issue #91 first hit this in <c>DeviceTokenRegistry</c> under a different
/// name; #150 needed the same detection in <c>UserService</c>, which cannot
/// reference NotificationService. The logic is here so both call one
/// implementation rather than two that drift.
///
/// The detection meets each provider on its own vocabulary. Matching on the
/// .NET type of the inner exception made the old helper a no-op under postgres:
/// SQLite raises <c>SqliteException</c> with error code 19
/// (SQLITE_CONSTRAINT), postgres raises <c>PostgresException</c> with SqlState
/// 23505. So the driver-specific error CODE is the signal on both, with the
/// english message as a fallback for a driver that surfaces no SqlState —
/// postgres localizes its message, so a populated SQLSTATE always wins.
/// </remarks>
public static class UniqueViolation
{
    /// <summary>
    /// True when <paramref name="ex"/> is a failed save caused by a UNIQUE
    /// constraint, as opposed to a NOT NULL violation, a type error, or a
    /// transport failure. Callers use this to turn the database's answer into
    /// a result the caller can act on, instead of a 500.
    /// </summary>
    public static bool IsUniqueViolation(Exception ex)
    {
        if (ex is not DbUpdateException) return false;
        var inner = ex.InnerException;
        if (inner is null) return false;

        // SQLite: SQLITE_CONSTRAINT (19) covers UNIQUE, NOT NULL and CHECK, so
        // a caller that uses this must have only UNIQUE constraints on the
        // path it guards.
        //
        // SQLITE_BUSY (5) is deliberately NOT matched. It means another
        // connection held the write lock — a concurrency condition, not a
        // statement about the data — and reporting it as "email already
        // exists" would tell a user their address is taken when the truth is
        // that the database was busy. Same reasoning as leaving a
        // NOT NULL violation out: the caller shows one message for this
        // boolean, so this must mean exactly one thing.
        if (inner is Microsoft.Data.Sqlite.SqliteException se)
            return se.SqliteErrorCode is 19;

        // Postgres: SQLSTATE 23505 = unique_violation.
        if (inner.GetType().FullName is { } typeName
            && typeName.StartsWith("Npgsql.", StringComparison.Ordinal))
        {
            var state = inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string;
            if (!string.IsNullOrEmpty(state)) return state == "23505";

            var msg = inner.Message ?? string.Empty;
            return msg.Contains("duplicate key value violates unique constraint", StringComparison.Ordinal);
        }

        return false;
    }
}
