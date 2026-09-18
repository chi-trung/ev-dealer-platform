using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.Linq;

namespace ev_dealer_reporting.Services
{
    public class CustomerDataService : ICustomerDataService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<CustomerDataService> _logger;

        public CustomerDataService(HttpClient httpClient, IConfiguration configuration, ILogger<CustomerDataService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;

            // Fallback must match CustomerService's real http profile port
            // (Properties/launchSettings.json). docker compose overrides this
            // with Services__CustomerService=http://customerservice:80; the old
            // 5004 only mattered for a bare `dotnet run` and was wrong there.
            var customerServiceUrl = _configuration["Services:CustomerService"] ?? "http://localhost:5039";
            _httpClient.BaseAddress = new Uri(customerServiceUrl);
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        // Removed GetPurchasesAsync as the endpoint does not exist in CustomerService
        // public async Task<List<PurchaseDataDto>> GetPurchasesAsync(DateTime? fromDate, DateTime? toDate, int? customerId = null)
        // {
        //     try
        //     {
        //         var queryParams = new List<string>();
        //         if (fromDate.HasValue)
        //             queryParams.Add($"fromDate={fromDate.Value:yyyy-MM-dd}");
        //         if (toDate.HasValue)
        //             queryParams.Add($"toDate={toDate.Value:yyyy-MM-dd}");
        //         if (customerId.HasValue)
        //             queryParams.Add($"customerId={customerId.Value}");

        //         var queryString = queryParams.Any() ? "?" + string.Join("&", queryParams) : "";
        //         var response = await _httpClient.GetAsync($"/api/customers/purchases{queryString}");

        //         if (!response.IsSuccessStatusCode)
        //         {
        //             _logger.LogWarning("Failed to fetch purchases from CustomerService: {StatusCode}", response.StatusCode);
        //             return new List<PurchaseDataDto>();
        //         }

        //         var jsonContent = await response.Content.ReadAsStringAsync();
        //         var purchases = JsonSerializer.Deserialize<List<JsonElement>>(jsonContent, new JsonSerializerOptions
        //         {
        //             PropertyNameCaseInsensitive = true
        //         });

        //         if (purchases == null) return new List<PurchaseDataDto>();

        //         return purchases.Select(p => new PurchaseDataDto
        //         {
        //             Id = p.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
        //             CustomerId = p.TryGetProperty("customerId", out var cid) ? cid.GetInt32() : 0,
        //             Vehicle = p.TryGetProperty("vehicle", out var v) ? v.GetString() ?? "" : "",
        //             Amount = p.TryGetProperty("amount", out var amt) ? amt.GetDecimal() : 0,
        //             PurchaseDate = p.TryGetProperty("purchaseDate", out var pd) && DateTime.TryParse(pd.GetString(), out var pdt) ? pdt : DateTime.MinValue
        //         }).ToList();
        //     }
        //     catch (Exception ex)
        //     {
        //         _logger.LogError(ex, "Error fetching purchases from CustomerService");
        //         return new List<PurchaseDataDto>();
        //     }
        // }

        public async Task<List<CustomerDataDto>> GetCustomersAsync(int? customerId = null)
        {
            try
            {
                var queryParams = new List<string>();
                if (customerId.HasValue)
                    queryParams.Add($"customerId={customerId.Value}");

                var queryString = queryParams.Any() ? "?" + string.Join("&", queryParams) : "";
                var response = await _httpClient.GetAsync($"/api/customers{queryString}");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to fetch customers from CustomerService: {StatusCode}", response.StatusCode);
                    return new List<CustomerDataDto>();
                }

                var jsonContent = await response.Content.ReadAsStringAsync();
                // Handle ReferenceHandler.Preserve format if present
                using var doc = JsonDocument.Parse(jsonContent);
                JsonElement root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("$values", out JsonElement valuesElement))
                {
                    root = valuesElement; // Use the $values array
                }

                var customers = JsonSerializer.Deserialize<List<JsonElement>>(root.GetRawText(), new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (customers == null) return new List<CustomerDataDto>();

                return customers.Select(c => new CustomerDataDto
                {
                    Id = c.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                    Name = c.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                    Email = c.TryGetProperty("email", out var email) ? email.GetString() ?? "" : "",
                    Phone = c.TryGetProperty("phone", out var phone) ? phone.GetString() ?? "" : "",
                    DealerId = c.TryGetProperty("dealerId", out var did) ? did.GetInt32() : 0
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching customers from CustomerService");
                return new List<CustomerDataDto>();
            }
        }
    }
}
