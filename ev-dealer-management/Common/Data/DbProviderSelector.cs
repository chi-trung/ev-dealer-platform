using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Common.Data;

// Issue #89: single source of truth for the provider switch.
//
// Two claims from the original recon were probed and refuted before this was
// written, and both are worth recording because they will look true to anyone
// reading the migrations:
//
// 1. "EnsureCreated() skips HasData seed rows." FALSE under Microsoft.Data.Sqlite
//    — a probe against a real SQLite FILE (not :memory:) produced
//    VehicleTypes 6 / Dealers 4 / Vehicles 5 / Specs 5 / Images 11 / Colors 14
//    from EnsureCreated() alone. The rule is provider-specific folklore, and
//    VehicleService's seeding is fine as shipped (#88, closed invalid).
//
// 2. "The Sqlite:Autoincrement annotations break int PKs on Postgres — Npgsql
//    ignores them so no sequence is created and every INSERT throws 23502."
//    HALF FALSE, and the false half was here until P2 measured it (issue #92).
//    The Sqlite: annotation really is irrelevant under Npgsql and does not need
//    removing. But the SECOND half of the claim — that no dual annotation is
//    required — does not hold for MIGRATIONS, which is the only place these
//    annotations exist.
//
//    The original probe was GenerateCreateScript(), which renders DDL from the
//    model at RUN time under whatever provider is active, so Npgsql's
//    convention layer really does add IdentityByDefaultColumn by itself there.
//    A migration is different: its DDL is fixed C# emitted at DESIGN time,
//    under whichever provider was active then — Sqlite for this repo — and
//    Npgsql re-renders none of it. Measured against a real postgres:16 in
//    P2, applying NotificationService's baseline and inspecting the result:
//
//      without Npgsql:ValueGenerationStrategy:
//        is_identity = NO, column_default empty
//        INSERT omitting Id -> ERROR 23502-style
//                             "null value in column \"Id\" ... violates
//                              not-null constraint"
//      with it:
//        is_identity = YES
//        INSERT omitting Id -> Id = 1
//
//    So every service's migrations DO need the annotation, and all five
//    existing ones already carry it (UserService x2, VehicleService x7,
//    CustomerService x6, SalesService x3, ReportingService x2) — they were
//    not redundant, and removing them would have broken every INSERT on the
//    Postgres deploy.
//
// So the migration files stay UNTOUCHED. What actually differs per provider is
// only the connection setup, which is what this helper centralises.
public static class DbProviderSelector
{
    // "sqlite" (or unset) keeps every existing local, CI and compose path
    // byte-identical. "postgres" switches the provider AND hard-fails if the
    // server is unreachable — deliberately NOT ReportingService's old
    // probe-and-silently-fall-back-to-SQLite pattern (removed in #89/#90,
    // when the switch moved here; naming the issue, not a line number,
    // because the cited lines no longer exist — see #131): in
    // production a silent fallback means a misconfigured service quietly boots
    // on SQLite and loses everything written to the container layer on the next
    // redeploy. Failing loudly is the correct failure mode.
    public static void AddApplicationDbContext<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName = "DefaultConnection",
        string? sqliteFallback = null,
        Action<DbContextOptionsBuilder>? configureOptions = null)
        where TContext : DbContext
    {
        var provider = (configuration["DB_PROVIDER"] ?? "sqlite").Trim()
                          .ToLowerInvariant();
        var connectionString = configuration.GetConnectionString(connectionStringName);

        // Unknown values do NOT fall through to SQLite. The whole point of this
        // helper (see the class comment) is that a misconfigured service must
        // fail loudly instead of quietly booting on SQLite and losing data on
        // the next redeploy — a typo like "postgre" or a future "sqlserver"
        // taking a silent SQLite path would be exactly that failure mode.
        switch (provider)
        {
            case "postgres":
            case "postgresql":
                if (string.IsNullOrWhiteSpace(connectionString))
                    throw new InvalidOperationException(
                        $"DB_PROVIDER=postgres but ConnectionStrings:{connectionStringName} is unset. " +
                        "Set it to a PostgreSQL connection string (Host=...;Database=...).");

                services.AddDbContext<TContext>(options =>
                {
                    options.UseNpgsql(connectionString);
                    configureOptions?.Invoke(options);
                });
                Console.WriteLine($"DB_PROVIDER=postgres: {typeof(TContext).Name} on PostgreSQL");
                break;

            case "sqlite":
                if (string.IsNullOrWhiteSpace(connectionString))
                    connectionString = sqliteFallback;
                if (string.IsNullOrWhiteSpace(connectionString))
                    throw new InvalidOperationException(
                        $"ConnectionStrings:{connectionStringName} is unset and no SQLite fallback was supplied.");

                services.AddDbContext<TContext>(options =>
                {
                    options.UseSqlite(connectionString);
                    configureOptions?.Invoke(options);
                });
                break;

            default:
                throw new InvalidOperationException(
                    $"DB_PROVIDER='{provider}' is not a supported value. " +
                    "Use 'sqlite' (default) or 'postgres'.");
        }
    }
}
