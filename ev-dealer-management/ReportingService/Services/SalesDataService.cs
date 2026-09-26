using System.Net.Http.Json;
using System.Text.Json;
using System.Linq;
using System.Globalization;
using ev_dealer_reporting.DTOs;
using Microsoft.Extensions.Configuration;

namespace ev_dealer_reporting.Services;

/// <summary>
/// Parses a timestamp coming from a sibling service's JSON into a DateTime that
/// Npgsql will actually accept on a <c>timestamp with time zone</c> column.
///
/// WHY THIS EXISTS (Issue #92 / P2, found by PostgresDataSynchronizationTests)
/// Every date in this file was read with <c>DateTime.TryParse</c>, which
/// returns <see cref="DateTimeKind.Unspecified"/> for a string with no offset
/// (which is what ASP.NET emits for a <c>DateTime</c> read from a UTC
/// database). DataSynchronizationService then assigns that value straight to a
/// summary entity, and Npgsql refuses it:
///
///   ArgumentException: Cannot write DateTime with Kind=Unspecified to
///   PostgreSQL type 'timestamp with time zone', only UTC is supported.
///
/// The throw happens inside SaveChangesAsync, which
/// DataSynchronizationService wraps in <c>catch (Exception) { LogError }</c>
/// (DataSynchronizationService.cs:265) — so the debt report silently stayed
/// EMPTY on Postgres, and the endpoint still answered 200. Re-stamping the
/// Kind to UTC is the fix; the wall-clock value is unchanged, because every
/// writer upstream already uses <c>DateTime.UtcNow</c>.
///
/// Note the read path was never broken: Npgsql accepts a Kind=Unspecified
/// value as a query PARAMETER (it sends it as timestamp-without-time-zone and
/// Postgres compares it fine), which is why the from/to filters in
/// ReportEndpoints.cs kept working and only the WRITES failed. Fixing only
/// the writes is therefore not papering over a second bug.
/// </summary>
internal static class TimestampParser
{
    public static DateTime Utc(string? value, DateTime fallback) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture,
                          DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                          out var parsed)
            ? parsed
            : fallback;
}

public class SalesDataService : ISalesDataService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SalesDataService> _logger;

    public SalesDataService(HttpClient httpClient, IConfiguration configuration, ILogger<SalesDataService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        
        var salesServiceUrl = _configuration["Services:SalesService"] ?? "http://localhost:5003";
        _httpClient.BaseAddress = new Uri(salesServiceUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<List<QuoteDataDto>> GetQuotesAsync(DateTime? fromDate, DateTime? toDate, int? dealerId = null)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/Quotes");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch quotes from SalesService: {StatusCode}", response.StatusCode);
                return new List<QuoteDataDto>();
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonContent);
            JsonElement root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("$values", out JsonElement valuesElement))
            {
                root = valuesElement;
            }

            if (root.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("Unexpected quotes response format from SalesService.");
                return new List<QuoteDataDto>();
            }

            var quotes = root.EnumerateArray().Select(q => new QuoteDataDto
            {
                QuoteId = q.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                DealerId = q.TryGetProperty("dealerId", out var did) ? did.GetInt32() : 0,
                SalespersonId = q.TryGetProperty("salespersonId", out var sid) ? sid.GetInt32() : 0,
                CustomerId = q.TryGetProperty("customerId", out var cid) ? cid.GetInt32() : 0,
                VehicleId = q.TryGetProperty("vehicleId", out var vid) ? vid.GetInt32() : 0,
                Quantity = q.TryGetProperty("quantity", out var qty) ? qty.GetInt32() : 0,
                TotalBasePrice = q.TryGetProperty("totalBasePrice", out var tbp) ? tbp.GetDecimal() : 0,
                Status = q.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "",
                CreatedAt = q.TryGetProperty("createdAt", out var ca)
                    ? TimestampParser.Utc(ca.GetString(), DateTime.UtcNow)
                    : DateTime.UtcNow
            }).ToList();

            if (fromDate.HasValue)
                quotes = quotes.Where(q => q.CreatedAt >= fromDate.Value).ToList();
            if (toDate.HasValue)
                quotes = quotes.Where(q => q.CreatedAt <= toDate.Value).ToList();
            if (dealerId.HasValue)
                quotes = quotes.Where(q => q.DealerId == dealerId.Value).ToList();

            return quotes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching quotes from SalesService");
            return new List<QuoteDataDto>();
        }
    }

    public async Task<List<OrderDataDto>> GetOrdersAsync(DateTime? fromDate, DateTime? toDate, int? dealerId = null)
    {
        try
        {
            var queryParams = new List<string>();
            if (fromDate.HasValue)
                queryParams.Add($"fromDate={fromDate.Value:yyyy-MM-dd}");
            if (toDate.HasValue)
                queryParams.Add($"toDate={toDate.Value:yyyy-MM-dd}");
            if (dealerId.HasValue)
                queryParams.Add($"dealerId={dealerId.Value}");

            var queryString = queryParams.Any() ? "?" + string.Join("&", queryParams) : "";
            var response = await _httpClient.GetAsync($"/api/Orders{queryString}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch orders from SalesService: {StatusCode}", response.StatusCode);
                return new List<OrderDataDto>();
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            // Handle ReferenceHandler.Preserve format if present
            using var doc = JsonDocument.Parse(jsonContent);
            JsonElement root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("$values", out JsonElement valuesElement))
            {
                root = valuesElement; // Use the $values array
            }

            var orders = JsonSerializer.Deserialize<List<JsonElement>>(root.GetRawText(), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (orders == null) return new List<OrderDataDto>();

            return orders.Select(o => new OrderDataDto
            {
                OrderId = o.TryGetProperty("orderId", out var oid) ? oid.GetInt32() : 0,
                OrderNumber = o.TryGetProperty("orderNumber", out var on) ? on.GetString() ?? "" : "",
                DealerId = o.TryGetProperty("dealerId", out var did) ? did.GetInt32() : 0,
                SalespersonId = o.TryGetProperty("salespersonId", out var sid) ? sid.GetInt32() : 0,
                CustomerId = o.TryGetProperty("customerId", out var cid) ? cid.GetInt32() : 0,
                VehicleId = o.TryGetProperty("vehicleId", out var vid) ? vid.GetInt32() : 0,
                Quantity = o.TryGetProperty("quantity", out var qty) ? qty.GetInt32() : 0,
                SubTotal = o.TryGetProperty("subTotal", out var subT) ? subT.GetDecimal() : 0,
                TotalDiscount = o.TryGetProperty("totalDiscount", out var td) ? td.GetDecimal() : 0,
                TotalPrice = o.TryGetProperty("totalPrice", out var tp) ? tp.GetDecimal() : 0,
                PaymentMethod = o.TryGetProperty("paymentMethod", out var pm) ? pm.GetString() ?? "" : "",
                DepositAmount = o.TryGetProperty("depositAmount", out var da) && da.ValueKind != JsonValueKind.Null ? da.GetDecimal() : null,
                LoanTermMonths = o.TryGetProperty("loanTermMonths", out var ltm) && ltm.ValueKind != JsonValueKind.Null ? ltm.GetInt32() : null,
                InterestRateYearly = o.TryGetProperty("interestRateYearly", out var iry) && iry.ValueKind != JsonValueKind.Null ? iry.GetDecimal() : null,
                Status = o.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "",
                CreatedAt = o.TryGetProperty("createdAt", out var ca)
                    ? TimestampParser.Utc(ca.GetString(), DateTime.UtcNow)
                    : DateTime.UtcNow
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching orders from SalesService");
            return new List<OrderDataDto>();
        }
    }

    public async Task<List<PaymentDataDto>> GetPaymentsAsync(DateTime? fromDate, DateTime? toDate, int? orderId = null)
    {
        try
        {
            var queryParams = new List<string>();
            if (fromDate.HasValue)
                queryParams.Add($"fromDate={fromDate.Value:yyyy-MM-dd}");
            if (toDate.HasValue)
                queryParams.Add($"toDate={toDate.Value:yyyy-MM-dd}");
            if (orderId.HasValue)
                queryParams.Add($"orderId={orderId.Value}");

            var queryString = queryParams.Any() ? "?" + string.Join("&", queryParams) : "";
            var response = await _httpClient.GetAsync($"/api/payments{queryString}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch payments from SalesService: {StatusCode}", response.StatusCode);
                return new List<PaymentDataDto>();
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            
            // Handle ReferenceHandler.Preserve format: {"$id":"1","$values":[]}
            // Also handle direct array format: [...]
            using var doc = JsonDocument.Parse(jsonContent);
            JsonElement root = doc.RootElement;

            List<JsonElement> payments;
            
            if (root.ValueKind == JsonValueKind.Array)
            {
                // Direct array format
                payments = root.EnumerateArray().ToList();
            }
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("$values", out JsonElement valuesElement))
            {
                // ReferenceHandler.Preserve format: {"$id":"1","$values":[]}
                if (valuesElement.ValueKind == JsonValueKind.Array)
                {
                    payments = valuesElement.EnumerateArray().ToList();
                }
                else
                {
                    _logger.LogWarning("Unexpected format in payments response: $values is not an array");
                    return new List<PaymentDataDto>();
                }
            }
            else
            {
                _logger.LogWarning("Unexpected format in payments response: root is neither array nor object with $values");
                return new List<PaymentDataDto>();
            }

            if (payments == null || payments.Count == 0) return new List<PaymentDataDto>();

            return payments.Select(p => new PaymentDataDto
            {
                PaymentId = p.TryGetProperty("id", out var pid) && Guid.TryParse(pid.GetString(), out var gid) ? gid : Guid.Empty,
                OrderId = p.TryGetProperty("orderId", out var oid) ? oid.GetInt32() : 0,
                Amount = p.TryGetProperty("amount", out var amt) ? amt.GetDecimal() : 0,
                Method = p.TryGetProperty("paymentMethod", out var meth) ? meth.GetString() ?? "" : "",
                Status = p.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "",
                PaidDate = p.TryGetProperty("paymentDate", out var pd) && DateTime.TryParse(pd.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var pdt) ? pdt : null,
                CreatedAt = p.TryGetProperty("createdAt", out var ca)
                    ? TimestampParser.Utc(ca.GetString(), DateTime.UtcNow)
                    : DateTime.UtcNow
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching payments from SalesService");
            return new List<PaymentDataDto>();
        }
    }

    public async Task<List<ContractDataDto>> GetContractsAsync(DateTime? fromDate, DateTime? toDate, int? dealerId = null)
    {
        try
        {
            var queryParams = new List<string>();
            if (fromDate.HasValue)
                queryParams.Add($"fromDate={fromDate.Value:yyyy-MM-dd}");
            if (toDate.HasValue)
                queryParams.Add($"toDate={toDate.Value:yyyy-MM-dd}");
            if (dealerId.HasValue)
                queryParams.Add($"dealerId={dealerId.Value}");

            var queryString = queryParams.Any() ? "?" + string.Join("&", queryParams) : "";
            var response = await _httpClient.GetAsync($"/api/Contracts{queryString}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch contracts from SalesService: {StatusCode}", response.StatusCode);
                return new List<ContractDataDto>();
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            // Handle ReferenceHandler.Preserve format if present
            using var doc = JsonDocument.Parse(jsonContent);
            JsonElement root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("$values", out JsonElement valuesElement))
            {
                root = valuesElement; // Use the $values array
            }

            var contracts = JsonSerializer.Deserialize<List<JsonElement>>(root.GetRawText(), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (contracts == null) return new List<ContractDataDto>();

            return contracts.Select(c => new ContractDataDto
            {
                ContractId = c.TryGetProperty("contractId", out var cid) ? cid.GetInt32() : 0,
                OrderId = c.TryGetProperty("orderId", out var oid) ? oid.GetInt32() : 0,
                CustomerId = c.TryGetProperty("customerId", out var custid) ? custid.GetInt32() : 0,
                DealerId = c.TryGetProperty("dealerId", out var did) ? did.GetInt32() : 0,
                SalespersonId = c.TryGetProperty("salespersonId", out var sid) ? sid.GetInt32() : 0,
                ContractNumber = c.TryGetProperty("contractNumber", out var cn) ? cn.GetString() ?? "" : "",
                SignedDate = c.TryGetProperty("signedDate", out var sd) && DateOnly.TryParse(sd.GetString(), out var dOnly) ? dOnly : DateOnly.MinValue,
                TotalAmount = c.TryGetProperty("totalAmount", out var ta) ? ta.GetDecimal() : 0,
                PaymentStatus = c.TryGetProperty("paymentStatus", out var ps) ? ps.GetString() ?? "" : "",
                Status = c.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "",
                CreatedAt = c.TryGetProperty("createdAt", out var ca)
                    ? TimestampParser.Utc(ca.GetString(), DateTime.UtcNow)
                    : DateTime.UtcNow,
                UpdatedAt = c.TryGetProperty("updatedAt", out var ua)
                    ? TimestampParser.Utc(ua.GetString(), DateTime.UtcNow)
                    : DateTime.UtcNow
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching contracts from SalesService");
            return new List<ContractDataDto>();
        }
    }

    public async Task<OrderDataDto?> GetOrderByIdAsync(int orderId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/sales/orders/{orderId}");
            if (!response.IsSuccessStatusCode)
                return null;

            var jsonContent = await response.Content.ReadAsStringAsync();
            var order = JsonSerializer.Deserialize<JsonElement>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (order.ValueKind == JsonValueKind.Null) return null;

            return new OrderDataDto
            {
                OrderId = order.TryGetProperty("orderId", out var oid) ? oid.GetInt32() : 0,
                OrderNumber = order.TryGetProperty("orderNumber", out var on) ? on.GetString() ?? "" : "",
                DealerId = order.TryGetProperty("dealerId", out var did) ? did.GetInt32() : 0,
                SalespersonId = order.TryGetProperty("salespersonId", out var sid) ? sid.GetInt32() : 0,
                CustomerId = order.TryGetProperty("customerId", out var cid) ? cid.GetInt32() : 0,
                VehicleId = order.TryGetProperty("vehicleId", out var vid) ? vid.GetInt32() : 0,
                Quantity = order.TryGetProperty("quantity", out var qty) ? qty.GetInt32() : 0,
                SubTotal = order.TryGetProperty("subTotal", out var subT) ? subT.GetDecimal() : 0,
                TotalDiscount = order.TryGetProperty("totalDiscount", out var td) ? td.GetDecimal() : 0,
                TotalPrice = order.TryGetProperty("totalPrice", out var tp) ? tp.GetDecimal() : 0,
                PaymentMethod = order.TryGetProperty("paymentMethod", out var pm) ? pm.GetString() ?? "" : "",
                DepositAmount = order.TryGetProperty("depositAmount", out var da) && da.ValueKind != JsonValueKind.Null ? da.GetDecimal() : null,
                LoanTermMonths = order.TryGetProperty("loanTermMonths", out var ltm) && ltm.ValueKind != JsonValueKind.Null ? ltm.GetInt32() : null,
                InterestRateYearly = order.TryGetProperty("interestRateYearly", out var iry) && iry.ValueKind != JsonValueKind.Null ? iry.GetDecimal() : null,
                Status = order.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "",
                CreatedAt = order.TryGetProperty("createdAt", out var ca)
                    ? TimestampParser.Utc(ca.GetString(), DateTime.UtcNow)
                    : DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching order {OrderId} from SalesService", orderId);
            return null;
        }
    }
}
