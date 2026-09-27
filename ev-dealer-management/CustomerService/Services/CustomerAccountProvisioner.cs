using System.Net.Http.Json;
using Common.Auth;
using Microsoft.Extensions.Logging;

namespace CustomerService.Services;

/// <summary>
/// Creates the UserService account a customer logs in with (issue #150), and
/// returns its id so the caller can link it to the customer row.
/// </summary>
/// <remarks>
/// WHY THIS EXISTS SEPARATELY FROM THE CUSTOMER ROW
/// Customers and Users are the same person but not the same identity:
/// Customers.Id is its own sequence and six tables point at it, so the two id
/// spaces cannot be collapsed. This is the only thing that bridges them, and it
/// runs at customer-creation time — before this, Customers.UserId (added in
/// #149) had no writer at all and every notification stayed log-only.
///
/// WHY THE FAILURE IS OPEN, AND WHY THAT IS CORRECT
/// If UserService is unreachable, the customer is still created, unlinked, and
/// a warning is logged. The alternative — failing the whole request — means one
/// service's outage stops a dealer from recording a walk-in, and loses the
/// customer record entirely. Customers.UserId is nullable and uniquely indexed
/// precisely so this state is representable, and the notification consumers
/// treat a null link as "log and carry on" rather than throwing. An unlinked
/// customer is the state the system has been in since #149; this does not make
/// it worse, it just stops it being the only state.
///
/// The known limit, stated rather than hidden: if UserService creates the
/// account and this service then fails to save the link, the account is
/// orphaned. A retry hits "Username already exists". Proper handling needs an
/// idempotency key, which is out of scope here.
/// </remarks>
public interface ICustomerAccountProvisioner
{
    /// <summary>
    /// Creates the account. Returns the new user's id, or null when the
    /// account could not be created — which the caller must treat as "customer
    /// saved, not linked", never as a fatal error.
    /// </summary>
    Task<int?> ProvisionAsync(
        string username,
        string email,
        string fullName,
        string password,
        int? dealerId,
        CancellationToken cancellationToken = default);
}

public sealed class CustomerAccountProvisioner : ICustomerAccountProvisioner
{
    private readonly HttpClient _http;
    private readonly ILogger<CustomerAccountProvisioner> _logger;

    public CustomerAccountProvisioner(HttpClient http, ILogger<CustomerAccountProvisioner> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<int?> ProvisionAsync(
        string username,
        string email,
        string fullName,
        string password,
        int? dealerId,
        CancellationToken cancellationToken = default)
    {
        // Same reasoning as DealerIdValidator.GetDealersAsync: the request below
        // is a relative URI, so without a BaseAddress GetFromJsonAsync throws
        // before touching the network. Failing loudly here names the missing
        // config; falling through would make every customer silently unlinked.
        if (_http.BaseAddress is null)
        {
            _logger.LogError(
                "Services:UserService is not configured: HttpClient BaseAddress is empty, so the " +
                "relative /api/internal/customer-accounts request cannot be built. Customer will be " +
                "created WITHOUT a login account. Set Services__UserService (see render.yaml / " +
                "docker-compose.yml).");
            return null;
        }

        try
        {
            var response = await _http.PostAsJsonAsync(
                "/api/internal/customer-accounts",
                new
                {
                    username,
                    email,
                    fullName,
                    password,
                    dealerId
                },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // The status, not the body: the body may echo fields back and
                // this log ships wherever the service's logs go. 409 is the
                // expected one (email already has an account) and is a
                // business outcome, not a defect.
                _logger.LogWarning(
                    "Could not provision a login account for customer {Email}: UserService returned {StatusCode}. " +
                    "The customer was created but is NOT linked to an account, so their notifications will not be delivered.",
                    email,
                    (int)response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<CustomerAccountResponse>(
                cancellationToken: cancellationToken);

            if (result?.UserId is not { } userId)
            {
                // A 200 without an id is worse than a failure: the account may
                // exist and we simply do not know its id, so the customer is
                // unlinked and a retry would collide on username.
                _logger.LogError(
                    "UserService accepted the customer account for {Email} but returned no user id. " +
                    "The account exists and is NOT linked; a retry will report the username as taken.",
                    email);
                return null;
            }

            return userId;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Narrow on purpose: a DbUpdateException or a programming error
            // must not be swallowed into a silently unlinked customer, because
            // those indicate a defect rather than an outage.
            _logger.LogWarning(ex,
                "Could not reach UserService to provision a login account for customer {Email}. " +
                "The customer was created but is NOT linked to an account, so their notifications will not be delivered.",
                email);
            return null;
        }
    }

    private sealed record CustomerAccountResponse(int? UserId, string? Message);
}
