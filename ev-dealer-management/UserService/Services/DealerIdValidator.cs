using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using UserService.Data;
using UserService.DTOs;
using UserService.Models;

namespace UserService.Services;
// Falls open on failure: an unreachable VehicleService must not block user
// registration entirely -- it returns true so the request proceeds, matching
// the pre-#121 behaviour where a missing table row was the only guard.
// Callers that need a hard guarantee can check the returned list instead.
public class DealerIdValidator
{
    private readonly HttpClient _http;
    private readonly ILogger<DealerIdValidator> _logger;
    // Short-lived cache: a signup should not cost a network round-trip every
    // time, but dealers are admin-managed so a long TTL would go stale.
    // This component is a SINGLETON (AddSingleton + AddHttpClient<T>), so the
    // cache fields are hit by every concurrent registration in the process.
    // A plain List<> reference + a separately-written DateTime is not atomic:
    // two threads can race the null/TTL check and stampede the endpoint, and
    // a reader can see _cacheAt advanced past a _cache that is still the old
    // list (torn read). The pair is stored and read atomically instead.
    private volatile Tuple<List<Dealer>, DateTime>? _cache;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

    public DealerIdValidator(HttpClient http, ILogger<DealerIdValidator> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<List<Dealer>> GetDealersAsync()
    {
        // BaseAddress is required: the request below is a RELATIVE URI, so
        // without it GetFromJsonAsync throws before any network call and the
        // caller sees an empty dealer list. Fail loudly here -- the proxy's
        // own /api/dealers endpoint catches and 503s -- rather than returning
        // [] and having every DealerId validation silently reject.
        var baseUri = _http.BaseAddress?.ToString().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUri))
            throw new InvalidOperationException(
                "Services:VehicleService is not configured: HttpClient BaseAddress is empty, " +
                "so the relative /api/dealers request cannot be built. Set Services__VehicleService " +
                "(see render.yaml / docker-compose.yml).");

        var dealers = await _http.GetFromJsonAsync<List<Dealer>>("/api/dealers")
            ?? new List<Dealer>();
        // Write the pair atomically: a concurrent IsValidAsync reader either
        // gets the old snapshot or this one, never a mismatched combination.
        _cache = Tuple.Create(dealers, DateTime.UtcNow);
        return dealers;
    }

    public async Task<bool> IsValidAsync(int dealerId)
    {
        var dealers = await GetCachedOrFreshAsync();
        return dealers.Any(d => d.Id == dealerId);
    }

    private async Task<List<Dealer>> GetCachedOrFreshAsync()
    {
        // Single atomic read of the cached pair.
        var snapshot = _cache;
        if (snapshot is not null && DateTime.UtcNow - snapshot.Item2 < CacheTtl)
            return snapshot.Item1;
        try { return await GetDealersAsync(); }
        catch (Exception ex)
        {
            // Fail OPEN, with a log line: a broker/dealer-service outage must
            // not lock out registration. The FK no longer exists in this
            // context, so there is nothing else to keep the row consistent.
            _logger.LogWarning(ex, "Could not reach VehicleService to validate dealer ids; accepting DealerId {Id} unchecked", 0);
            return snapshot?.Item1 ?? new List<Dealer>();
        }
    }
}
