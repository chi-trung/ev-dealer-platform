using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace ev_dealer_reporting.Services;

public class VehicleDataService : IVehicleDataService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<VehicleDataService> _logger;

    public VehicleDataService(HttpClient httpClient, IConfiguration configuration, ILogger<VehicleDataService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        
        // Fallback must match VehicleService's real http profile port
        // (Properties/launchSettings.json). docker compose overrides this with
        // Services__VehicleService=http://vehicleservice:8080, so this default
        // only matters for a bare `dotnet run` ReportingService -- where the
        // old 5002 pointed at a port nothing listens on.
        var vehicleServiceUrl = _configuration["Services:VehicleService"] ?? "http://localhost:5068";
        _httpClient.BaseAddress = new Uri(vehicleServiceUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<List<VehicleInventoryDto>> GetVehiclesAsync(int? dealerId = null)
    {
        try
        {
            var queryString = dealerId.HasValue ? $"?dealerId={dealerId.Value}" : "";
            var response = await _httpClient.GetAsync($"/api/vehicles{queryString}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch vehicles from VehicleService: {StatusCode}", response.StatusCode);
                return new List<VehicleInventoryDto>();
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(jsonContent);
            
            // VehicleService returns paginated result with data array
            List<VehicleInventoryDto> vehicles = new();
            
            if (jsonDoc.RootElement.TryGetProperty("data", out var dataArray))
            {
                foreach (var vehicle in dataArray.EnumerateArray())
                {
                    vehicles.Add(MapVehicle(vehicle));
                }
            }
            else if (jsonDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var vehicle in jsonDoc.RootElement.EnumerateArray())
                {
                    vehicles.Add(MapVehicle(vehicle));
                }
            }

            return vehicles;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching vehicles from VehicleService");
            return new List<VehicleInventoryDto>();
        }
    }

    public async Task<VehicleInventoryDto?> GetVehicleByIdAsync(int vehicleId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/vehicles/{vehicleId}");
            if (!response.IsSuccessStatusCode)
                return null;

            var jsonContent = await response.Content.ReadAsStringAsync();
            var vehicle = JsonDocument.Parse(jsonContent).RootElement;

            return MapVehicle(vehicle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching vehicle {VehicleId} from VehicleService", vehicleId);
            return null;
        }
    }

    public async Task<List<DealerDto>> GetDealersAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/dealers");
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch dealers from VehicleService: {StatusCode}", response.StatusCode);
                return new List<DealerDto>();
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            var dealers = JsonSerializer.Deserialize<List<JsonElement>>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (dealers == null) return new List<DealerDto>();

            return dealers.Select(d => new DealerDto
            {
                Id = d.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                Name = d.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                Address = d.TryGetProperty("address", out var addr) ? addr.GetString() : null,
                Region = d.TryGetProperty("region", out var reg) ? reg.GetString() : null
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching dealers from VehicleService");
            return new List<DealerDto>();
        }
    }

    private VehicleInventoryDto MapVehicle(JsonElement vehicle)
    {
        return new VehicleInventoryDto
        {
            Id = vehicle.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
            Model = vehicle.TryGetProperty("model", out var model) ? model.GetString() ?? "" : "",
            DealerId = vehicle.TryGetProperty("dealerId", out var did) ? did.GetInt32() : 0,
            DealerName = vehicle.TryGetProperty("dealerName", out var dname) ? dname.GetString() ?? "" : "",
            StockQuantity = vehicle.TryGetProperty("stockQuantity", out var sq) ? sq.GetInt32() : 0,
            Price = vehicle.TryGetProperty("price", out var price) ? price.GetDecimal() : 0,
            CreatedAt = vehicle.TryGetProperty("createdAt", out var ca) && DateTime.TryParse(ca.GetString(), out var cdt) ? cdt : DateTime.UtcNow,
            UpdatedAt = vehicle.TryGetProperty("updatedAt", out var ua) && DateTime.TryParse(ua.GetString(), out var udt) ? udt : DateTime.UtcNow
        };
    }
}


