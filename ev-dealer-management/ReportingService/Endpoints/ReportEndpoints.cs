using System.Text;
using System.Text.Json;
using System.IO;
using Microsoft.EntityFrameworkCore;
using ev_dealer_reporting.Data;
using ev_dealer_reporting.DTOs;
using ev_dealer_reporting.Models;
using ev_dealer_reporting.Services;

namespace ev_dealer_reporting.Endpoints;

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this WebApplication app)
    {
    app.MapGet("/api/reports/demand-forecast", async (IForecastingService forecastingService, string? from, string? to) =>
    {
        try
        {
            DateTime? fromDate = null;
            DateTime? toDate = null;
    
            if (!string.IsNullOrEmpty(from) && DateTime.TryParse(from, out var fd))
                fromDate = fd;
            if (!string.IsNullOrEmpty(to) && DateTime.TryParse(to, out var td))
                toDate = td;
    
            var forecast = await forecastingService.GenerateDemandForecastAsync(fromDate, toDate);
            return Results.Json(forecast);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in GET /api/reports/demand-forecast: {ex.Message}");
            return Results.Json(new { success = false, error = "An error occurred while generating the forecast.", details = ex.Message }, statusCode: 500);
        }
    })
    .WithName("GetDemandForecast")
    .WithOpenApi()
    .Produces<DemandForecastDto>(200)
    .Produces(500).RequireAuthorization();
    
    
    // New endpoint to trigger data synchronization manually
    app.MapPost("/api/reports/synchronize-data", async (IDataSynchronizationService dataSyncService) =>
    {
        try
        {
            await dataSyncService.SynchronizeAllDataAsync();
            return Results.Ok(new { success = true, message = "Data synchronization initiated successfully." });
        }
        catch (Exception ex)
        {
        Console.Error.WriteLine($"Error in POST /api/reports/synchronize-data: {ex.Message}");
            return Results.Json(new { success = false, error = "An error occurred during data synchronization.", details = ex.Message }, statusCode: 500);
        }
    })
    .WithName("SynchronizeData")
    .WithOpenApi()
    .Produces(200)
    .Produces(500).RequireAuthorization();
    
    // New endpoint for Debt Summary Report
    app.MapGet("/api/reports/debt-summary", async (ReportingDbContext db, int? dealerId, int? customerId, string? debtType, string? status, string? from, string? to) =>
    {
        try
        {
            var query = db.DebtSummaries.AsQueryable();
    
            if (dealerId.HasValue)
                query = query.Where(d => d.DealerId == dealerId.Value);
    
            if (customerId.HasValue)
                query = query.Where(d => d.CustomerId == customerId.Value);
    
            if (!string.IsNullOrWhiteSpace(debtType))
                query = query.Where(d => d.DebtType == debtType);
    
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(d => d.Status == status);
    
            DateTime? fromDate = null;
            DateTime? toDate = null;
    
            if (!string.IsNullOrEmpty(from) && DateTime.TryParse(from, out var fd))
                fromDate = fd;
            if (!string.IsNullOrEmpty(to) && DateTime.TryParse(to, out var td))
                toDate = td;
    
            if (fromDate.HasValue)
                query = query.Where(d => d.CreatedAt >= fromDate.Value);
            if (toDate.HasValue)
                query = query.Where(d => d.CreatedAt <= toDate.Value);
    
            var results = await query.OrderByDescending(d => d.CreatedAt).ToListAsync();
    
            return Results.Json(new
            {
                success = true,
                count = results.Count,
                data = results
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in GET /api/reports/debt-summary: {ex.Message}");
            return Results.Json(new { success = false, error = ex.Message }, statusCode: 500);
        }
    })
    .WithName("GetDebtSummary")
    .WithOpenApi()
    .Produces(200)
    .Produces(500).RequireAuthorization();
    
    // New endpoint for Dealer Debt Report (using ReportService)
    app.MapGet("/api/reports/debt-report", async (IReportService reportService, int? dealerId, string? from, string? to) =>
    {
        try
        {
            // Nếu không có dealerId, lấy dữ liệu cho tất cả đại lý (tổng hợp)
            var report = await reportService.GetDealerDebtReportAsync(dealerId);
            return Results.Json(new { success = true, data = report });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in GET /api/reports/debt-report: {ex.Message}");
            return Results.Json(new { success = false, error = "An error occurred while fetching the debt report.", details = ex.Message }, statusCode: 500);
        }
    })
    .WithName("GetDealerDebtReport")
    .WithOpenApi()
    .Produces<DealerDebtReportDto>(200)
    .Produces(500).RequireAuthorization();
    
    // New endpoint for Sales by Dealer (using ReportService)
    app.MapGet("/api/reports/sales-by-dealer", async (IReportService reportService, IVehicleDataService vehicleDataService, int? dealerId, string? period, DateTime? fromDate, DateTime? toDate) =>
    {
        try
        {
            // Mặc định period là "month" nếu không được cung cấp
            var periodValue = period ?? "month";
    
            if (dealerId.HasValue)
            {
                var report = await reportService.GetDealerSalesReportAsync(dealerId.Value, periodValue, fromDate, toDate);
                return Results.Json(new { success = true, data = report });
            }
            else
            {
                // Lấy tất cả dealers và tổng hợp
                var dealers = await vehicleDataService.GetDealersAsync();
    
                var allReports = new List<object>();
                foreach (var dealer in dealers.Take(10)) // Giới hạn 10 dealers để tránh quá tải
                {
                    try
                    {
                        var report = await reportService.GetDealerSalesReportAsync(dealer.Id, periodValue, fromDate, toDate);
                        allReports.Add(report);
                    }
                    catch
                    {
                        // Bỏ qua lỗi cho từng dealer
                    }
                }
    
                return Results.Json(new { success = true, data = allReports });
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in GET /api/reports/sales-by-dealer: {ex.Message}");
            return Results.Json(new { success = false, error = "An error occurred while fetching sales by dealer report.", details = ex.Message }, statusCode: 500);
        }
    })
    .WithName("GetSalesByDealer")
    .WithOpenApi()
    .Produces<DealerSalesReportDto>(200)
    .Produces(500).RequireAuthorization();
    
    // New endpoint for Inventory Trends (using ReportService)
    app.MapGet("/api/reports/inventory-trends", async (IReportService reportService) =>
    {
        try
        {
            var report = await reportService.GetInventoryAnalysisAsync();
            return Results.Json(new { success = true, data = report });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in GET /api/reports/inventory-trends: {ex.Message}");
            return Results.Json(new { success = false, error = "An error occurred while fetching inventory trends.", details = ex.Message }, statusCode: 500);
        }
    })
    .WithName("GetInventoryTrends")
    .WithOpenApi()
    .Produces<InventoryAnalysisDto>(200)
    .Produces(500).RequireAuthorization();
    
    // Endpoint for Sales by Staff (using ReportService)
    app.MapGet("/api/reports/sales-by-staff", async (IReportService reportService, string? from, string? to) =>
    {
        try
        {
            DateTime? fromDate = null;
            DateTime? toDate = null;
    
            if (!string.IsNullOrEmpty(from) && DateTime.TryParse(from, out var fd))
                fromDate = fd;
            if (!string.IsNullOrEmpty(to) && DateTime.TryParse(to, out var td))
                toDate = td;
    
            var report = await reportService.GetSalesByStaffAsync(fromDate, toDate);
            return Results.Json(new { success = true, data = report });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in GET /api/reports/sales-by-staff: {ex.Message}");
            return Results.Json(new { success = false, error = "An error occurred while fetching sales by staff report.", details = ex.Message }, statusCode: 500);
        }
    })
    .WithName("GetSalesByStaff")
    .WithOpenApi()
    .Produces(501).RequireAuthorization();
    
    
    // Summary endpoint - Tính toán từ dữ liệu thật trong database
    app.MapGet("/api/reports/summary", async (ReportingDbContext db, string? type, string? from, string? to) =>
    {
        try
        {
            DateTime? fromDate = null;
            DateTime? toDate = null;
    
            if (!string.IsNullOrEmpty(from) && DateTime.TryParse(from, out var fd))
                fromDate = fd;
            if (!string.IsNullOrEmpty(to) && DateTime.TryParse(to, out var td))
                toDate = td;
    
            // Query SalesSummaries với filter date nếu có
            var salesQuery = db.SalesSummaries.AsQueryable();
            if (fromDate.HasValue)
                salesQuery = salesQuery.Where(s => s.Date >= fromDate.Value);
            if (toDate.HasValue)
                salesQuery = salesQuery.Where(s => s.Date <= toDate.Value);
    
            var salesData = await salesQuery.ToListAsync();
    
            // Tính toán metrics từ dữ liệu thật
            var totalSales = salesData.Sum(s => s.TotalOrders);
            // Sum as decimal and convert at the boundary: a (double) cast inside
            // the Sum drops cents on every provider, not just SQLite, and
            // TotalRevenue is decimal(18,2) (Issue #103).
            var totalRevenue = (double)salesData.Sum(s => s.TotalRevenue);
    
            // Đếm số đại lý unique từ SalesSummaries và InventorySummaries
            var activeDealersFromSales = await salesQuery.Select(s => s.DealerId).Distinct().CountAsync();
            var activeDealersFromInventory = await db.InventorySummaries.Select(i => i.DealerId).Distinct().CountAsync();
            var activeDealers = Math.Max(activeDealersFromSales, activeDealersFromInventory);
    
            // Tổng số dealer: lấy từ tất cả unique dealers trong cả 2 bảng
            var allDealerIds = await db.SalesSummaries.Select(s => s.DealerId)
                .Union(db.InventorySummaries.Select(i => i.DealerId))
                .Distinct()
                .CountAsync();
            var totalDealers = allDealerIds; // Lấy giá trị thực tế từ database
    
            // Conversion rate: tính dựa trên tổng số orders và số lượng inventory (ước tính)
            var totalInventory = await db.InventorySummaries.SumAsync(i => i.StockCount);
            var conversionRate = totalInventory > 0 ? (double)totalSales / (totalSales + totalInventory) : 0.0;
    
            var metrics = new
            {
                totalSales,
                totalRevenue,
                activeDealers,
                totalDealers,
                conversionRate = Math.Round(conversionRate, 4)
            };
    
            var result = new
            {
                type = type ?? "sales",
                from,
                to,
                metrics
            };
    
            return Results.Json(result);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in GET /api/reports/summary: {ex.Message}");
            return Results.Json(new { success = false, error = ex.Message }, statusCode: 500);
        }
    })
    .WithName("GetReportSummary")
    .WithOpenApi()
    .Produces(200)
    .Produces(500).RequireAuthorization();
    
    // Sales by region (grouped by Region) - for bar chart
    app.MapGet("/api/reports/sales-by-region", async (ReportingDbContext db, string? from, string? to) =>
    {
        try
        {
            DateTime? fromDate = null;
            DateTime? toDate = null;
    
            if (!string.IsNullOrEmpty(from) && DateTime.TryParse(from, out var fd))
                fromDate = fd;
            if (!string.IsNullOrEmpty(to) && DateTime.TryParse(to, out var td))
                toDate = td;
    
            var query = db.SalesSummaries.AsQueryable();
            if (fromDate.HasValue)
                query = query.Where(s => s.Date >= fromDate.Value);
            if (toDate.HasValue)
                query = query.Where(s => s.Date <= toDate.Value);
    
            // Group by Region và tính tổng (bỏ qua records có Region null)
            var regionalGroups = await query
                .Where(s => !string.IsNullOrWhiteSpace(s.Region))
                .GroupBy(s => s.Region)
                .Select(g => new
                {
                    region = g.Key,
                    sales = g.Sum(s => s.TotalOrders),
                    // Materialise the decimals and aggregate them client-side
                    // as decimal; a (long) cast in the Sum truncated cents on
                    // every provider (Issue #103).
                    revenues = g.Select(s => s.TotalRevenue)
                })
                .ToListAsync();
    
            var salesByRegion = regionalGroups
                .Select(g => new
                {
                    g.region,
                    g.sales,
                    revenue = (double)g.revenues.Sum(v => v)
                })
                .OrderByDescending(x => x.revenue)
                .ToList();
    
            return Results.Json(salesByRegion);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in GET /api/reports/sales-by-region: {ex.Message}");
            return Results.Json(new { success = false, error = ex.Message }, statusCode: 500);
        }
    })
    .WithName("GetSalesByRegion")
    .WithOpenApi()
    .Produces(200)
    .Produces(500).RequireAuthorization();
    
    // Sales proportion by region - for donut chart
    app.MapGet("/api/reports/sales-proportion", async (ReportingDbContext db, string? from, string? to) =>
    {
        try
        {
            DateTime? fromDate = null;
            DateTime? toDate = null;
    
            if (!string.IsNullOrEmpty(from) && DateTime.TryParse(from, out var fd))
                fromDate = fd;
            if (!string.IsNullOrEmpty(to) && DateTime.TryParse(to, out var td))
                toDate = td;
    
            var query = db.SalesSummaries.AsQueryable();
            if (fromDate.HasValue)
                query = query.Where(s => s.Date >= fromDate.Value);
            if (toDate.HasValue)
                query = query.Where(s => s.Date <= toDate.Value);
    
            // Bỏ qua records có Region null
            var grouped = await query
                .Where(s => !string.IsNullOrWhiteSpace(s.Region))
                .GroupBy(s => s.Region)
                .Select(g => new
                {
                    region = g.Key,
                    sales = g.Sum(s => s.TotalOrders),
                    revenues = g.Select(s => s.TotalRevenue)
                })
                .ToListAsync();
    
            var salesByRegion = grouped
                .Select(g => new
                {
                    g.region,
                    g.sales,
                    // Keep decimal through the aggregate; a (long) cast here
                    // truncated cents on every provider (Issue #103).
                    revenue = g.revenues.Sum(v => v)
                })
                .ToList();
    
            var totalSales = salesByRegion.Sum(x => x.sales);
            var totalRevenue = salesByRegion.Sum(x => (double)x.revenue);
    
            var result = salesByRegion.Select(x => new
            {
                region = x.region,
                sales = x.sales,
                // The wire value is double; convert once at the boundary.
                revenue = (double)x.revenue,
                salesPercentage = totalSales > 0 ? Math.Round((double)x.sales / totalSales * 100, 1) : 0,
                revenuePercentage = totalRevenue > 0 ? Math.Round((double)x.revenue / totalRevenue * 100, 1) : 0
            }).OrderByDescending(x => x.sales).ToList();
    
            return Results.Json(result);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in GET /api/reports/sales-proportion: {ex.Message}");
            return Results.Json(new { success = false, error = ex.Message }, statusCode: 500);
        }
    })
    .WithName("GetSalesProportion")
    .WithOpenApi()
    .Produces(200)
    .Produces(500).RequireAuthorization();
    
    // Top vehicles - Lấy từ InventorySummaries, sắp xếp theo StockCount
    app.MapGet("/api/reports/top-vehicles", async (ReportingDbContext db, int? limit) =>
    {
        try
        {
            // Tính average revenue per order từ SalesSummaries để ước tính revenue cho vehicles
            // Sum as decimal: casting inside the EF-translated expression made
            // this a server-side CAST(... AS bigint) under Postgres, truncating
            // cents at the database before the client saw the value. Summing
            // client-side as decimal keeps the cents (Issue #103).
            var totalOrders = await db.SalesSummaries.SumAsync(s => (long)s.TotalOrders);
            var revenueRows = await db.SalesSummaries
                .Select(s => s.TotalRevenue)
                .ToListAsync();
            var totalRevenue = (double)revenueRows.Sum(v => v);
            var avgRevenuePerOrder = totalOrders > 0 ? totalRevenue / totalOrders : 0;
    
            var query = db.InventorySummaries
                .GroupBy(i => i.VehicleName)
                .Select(g => new
                {
                    model = g.Key,
                    stockCount = g.Sum(i => i.StockCount),
                    estimatedRevenue = (long)Math.Round(g.Sum(i => i.StockCount) * avgRevenuePerOrder)
                })
                .OrderByDescending(x => x.stockCount);
    
            var l = limit.HasValue && limit.Value > 0 ? limit.Value : 10;
            var topVehicles = await query
                .Take(l)
                .Select(x => new
                {
                    x.model,
                    stockCount = x.stockCount,
                    // Backwards compatibility for existing frontend expecting "sales" + "revenue"
                    sales = x.stockCount,
                    revenue = x.estimatedRevenue,
                    estimatedRevenue = x.estimatedRevenue
                })
                .ToListAsync();
    
            return Results.Json(topVehicles);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in GET /api/reports/top-vehicles: {ex.Message}");
            return Results.Json(new { success = false, error = ex.Message }, statusCode: 500);
        }
    })
    .WithName("GetTopVehicles")
    .WithOpenApi()
    .Produces(200)
    .Produces(500).RequireAuthorization();
    
    // Export endpoint - Export dữ liệu thật từ database
    app.MapPost("/api/reports/export", async (HttpRequest req, ReportingDbContext db) =>
    {
        try
        {
            using var sr = new StreamReader(req.Body, Encoding.UTF8);
            var body = await sr.ReadToEndAsync();
    
            // Parse payload for type/format
            string type = "sales";
            string format = "csv";
            DateTime? fromDate = null;
            DateTime? toDate = null;
    
            try
            {
                if (!string.IsNullOrEmpty(body))
                {
                    var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("type", out var t)) type = t.GetString() ?? type;
                    if (doc.RootElement.TryGetProperty("format", out var f)) format = f.GetString() ?? format;
                    if (doc.RootElement.TryGetProperty("from", out var fr) && fr.GetString() is string frs && DateTime.TryParse(frs, out var fd)) fromDate = fd;
                    if (doc.RootElement.TryGetProperty("to", out var toEl) && toEl.GetString() is string tos && DateTime.TryParse(tos, out var td)) toDate = td;
                }
            }
            catch
            {
                // ignore parse errors and use defaults
            }
    
            var csvBuilder = new StringBuilder();
            byte[] bytes;
            string filename;
    
            if (type.ToLower() == "sales")
            {
                // Export sales data
                var salesQuery = db.SalesSummaries.AsQueryable();
                if (fromDate.HasValue)
                    salesQuery = salesQuery.Where(s => s.Date >= fromDate.Value);
                if (toDate.HasValue)
                    salesQuery = salesQuery.Where(s => s.Date <= toDate.Value);
    
                var salesData = await salesQuery.OrderByDescending(s => s.Date).ToListAsync();
    
                csvBuilder.AppendLine("Date,DealerName,SalespersonName,TotalOrders,TotalRevenue");
                foreach (var s in salesData)
                {
                    csvBuilder.AppendLine($"{s.Date:yyyy-MM-dd},\"{s.DealerName}\",\"{s.SalespersonName}\",{s.TotalOrders},{s.TotalRevenue}");
                }
    
                filename = $"sales_report_{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
            }
            else if (type.ToLower() == "inventory")
            {
                // Export inventory data
                var inventoryData = await db.InventorySummaries
                    .OrderByDescending(i => i.LastUpdatedAt)
                    .ToListAsync();
    
                csvBuilder.AppendLine("VehicleName,DealerName,StockCount,LastUpdatedAt");
                foreach (var i in inventoryData)
                {
                    csvBuilder.AppendLine($"{i.VehicleId},\"{i.DealerName}\",{i.StockCount},{i.LastUpdatedAt:yyyy-MM-dd HH:mm:ss}");
                }
    
                filename = $"inventory_report_{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
            }
            else
            {
                // Default: export both
                var salesData = await db.SalesSummaries.OrderByDescending(s => s.Date).ToListAsync();
                var inventoryData = await db.InventorySummaries.OrderByDescending(i => i.LastUpdatedAt).ToListAsync();
    
                csvBuilder.AppendLine("Type,Date,DealerName,Details,Count,Revenue");
                foreach (var s in salesData)
                {
                    csvBuilder.AppendLine($"Sales,{s.Date:yyyy-MM-dd},\"{s.DealerName}\",\"{s.SalespersonName}\",{s.TotalOrders},{s.TotalRevenue}");
                }
                foreach (var i in inventoryData)
                {
                    csvBuilder.AppendLine($"Inventory,{i.LastUpdatedAt:yyyy-MM-dd},\"{i.DealerName}\",\"{i.VehicleName}\",{i.StockCount},0");
                }
    
                filename = $"full_report_{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
            }
    
                        // --- SỬA: Thêm BOM vào đầu chuỗi byte để Excel nhận diện tiếng Việt ---
                var contentBytes = Encoding.UTF8.GetBytes(csvBuilder.ToString());
                var bom = Encoding.UTF8.GetPreamble(); // Lấy mã BOM (EF BB BF)
                bytes = bom.Concat(contentBytes).ToArray(); // Ghép BOM + Dữ liệu
                // --------------------------------------------------------------------
    
            // Persist ReportRequest and ReportExport record
            try
            {
                var reqEntity = new ReportRequest
                {
                    Type = type,
                    From = fromDate,
                    To = toDate,
                    RequestedBy = "system",
                    Status = "Completed",
                    CreatedAt = DateTime.UtcNow,
                    CompletedAt = DateTime.UtcNow
                };
                db.ReportRequests.Add(reqEntity);
                await db.SaveChangesAsync();
    
                var exportEntity = new ReportExport
                {
                    ReportRequestId = reqEntity.Id,
                    FileName = filename,
                    ContentType = "text/csv",
                    SizeBytes = bytes.Length,
                    CreatedAt = DateTime.UtcNow
                };
                db.ReportExports.Add(exportEntity);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: failed to save export metadata: {ex.Message}");
            }
    
            return Results.File(bytes, "text/csv", filename);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in POST /api/reports/export: {ex.Message}");
            return Results.Json(new { success = false, error = ex.Message }, statusCode: 500);
        }
    })
    .WithName("ExportReport")
    .WithOpenApi()
    .Produces(200)
    .Produces(500).RequireAuthorization();
    
    
    // GET /api/reports/sales-summary - Lấy tất cả dữ liệu tổng hợp doanh số
    app.MapGet("/api/reports/sales-summary", async (ReportingDbContext db, DateTime? fromDate, DateTime? toDate, int? dealerId) => // Changed Guid? to int?
    {
        try
        {
            var query = db.SalesSummaries.AsQueryable();
    
            if (dealerId.HasValue)
                query = query.Where(s => s.DealerId == dealerId.Value);
    
            if (fromDate.HasValue)
                query = query.Where(s => s.Date >= fromDate.Value);
    
            if (toDate.HasValue)
                query = query.Where(s => s.Date <= toDate.Value);
    
            var results = await query.OrderByDescending(s => s.Date).ToListAsync();
    
            return Results.Json(new
            {
                success = true,
                count = results.Count,
                data = results
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in GET /api/reports/sales-summary: {ex.Message}");
            return Results.Json(new { success = false, error = ex.Message }, statusCode: 500);
        }
    })
    .WithName("GetSalesSummary")
    .WithOpenApi()
    .Produces(200)
    .Produces(500).RequireAuthorization();
    
    // GET /api/reports/sales-summary/{id} - Lấy chi tiết một doanh số
    app.MapGet("/api/reports/sales-summary/{id}", async (Guid id, ReportingDbContext db) =>
    {
        try
        {
            var salesSummary = await db.SalesSummaries.FirstOrDefaultAsync(s => s.Id == id);
    
            if (salesSummary == null)
                return Results.NotFound(new { message = "Sales summary not found" });
    
            return Results.Json(new { success = true, data = salesSummary });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in GET /api/reports/sales-summary/{id}: {ex.Message}");
            return Results.Json(new { success = false, error = ex.Message }, statusCode: 500);
        }
    })
    .WithName("GetSalesSummaryById")
    .WithOpenApi()
    .Produces(200)
    .Produces(404)
    .Produces(500).RequireAuthorization();
    
    // GET /api/reports/inventory-summary - Lấy tất cả dữ liệu tồn kho tổng hợp
    app.MapGet("/api/reports/inventory-summary", async (ReportingDbContext db, int? dealerId, int? vehicleId) => // Changed Guid? to int? for both
    {
        try
        {
            var query = db.InventorySummaries.AsQueryable();
    
            if (dealerId.HasValue)
                query = query.Where(i => i.DealerId == dealerId.Value);
    
            if (vehicleId.HasValue)
                query = query.Where(i => i.VehicleId == vehicleId.Value);
    
            var results = await query.OrderByDescending(i => i.LastUpdatedAt).ToListAsync();
    
            return Results.Json(new
            {
                success = true,
                count = results.Count,
                data = results
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in GET /api/reports/inventory-summary: {ex.Message}");
            return Results.Json(new { success = false, error = ex.Message }, statusCode: 500);
        }
    })
    .WithName("GetInventorySummary")
    .WithOpenApi()
    .Produces(200)
    .Produces(500).RequireAuthorization();
    
    // GET /api/reports/inventory-summary/{id} - Lấy chi tiết một tồn kho
    app.MapGet("/api/reports/inventory-summary/{id}", async (Guid id, ReportingDbContext db) =>
    {
        try
        {
            var inventorySummary = await db.InventorySummaries.FirstOrDefaultAsync(i => i.Id == id);
    
            if (inventorySummary == null)
                return Results.NotFound(new { message = "Inventory summary not found" });
    
            return Results.Json(new { success = true, data = inventorySummary });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in GET /api/reports/inventory-summary/{id}: {ex.Message}");
            return Results.Json(new { success = false, error = ex.Message }, statusCode: 500);
        }
    })
    .WithName("GetInventorySummaryById")
    .WithOpenApi()
    .Produces(200)
    .Produces(404)
    .Produces(500).RequireAuthorization();
    
    // POST /api/reports/sales-summary - Thêm dữ liệu tổng hợp doanh số mới (cho test)
    app.MapPost("/api/reports/sales-summary", async (ReportingDbContext db, SalesSummary salesSummary) =>
    {
        try
        {
            if (string.IsNullOrWhiteSpace(salesSummary.DealerName) || string.IsNullOrWhiteSpace(salesSummary.SalespersonName))
                return Results.BadRequest(new { message = "DealerName and SalespersonName are required" });
    
            if (string.IsNullOrWhiteSpace(salesSummary.Region))
                return Results.BadRequest(new { message = "Region is required (Miền Bắc, Miền Trung, or Miền Nam)" });
    
            salesSummary.Id = Guid.NewGuid();
            salesSummary.LastUpdatedAt = DateTime.UtcNow;
    
            db.SalesSummaries.Add(salesSummary);
            await db.SaveChangesAsync();
    
            return Results.Created($"/api/reports/sales-summary/{salesSummary.Id}", new { success = true, data = salesSummary });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in POST /api/reports/sales-summary: {ex.Message}");
            return Results.Json(new { success = false, error = ex.Message }, statusCode: 500);
        }
    })
    .WithName("CreateSalesSummary")
    .WithOpenApi()
    .Produces(201)
    .Produces(400)
    .Produces(500).RequireAuthorization();
    
    // POST /api/reports/inventory-summary - Thêm dữ liệu tồn kho mới (cho test)
    app.MapPost("/api/reports/inventory-summary", async (ReportingDbContext db, InventorySummary inventorySummary) =>
    {
        try
        {
            if (string.IsNullOrWhiteSpace(inventorySummary.VehicleName) || string.IsNullOrWhiteSpace(inventorySummary.DealerName))
                return Results.BadRequest(new { message = "VehicleName and DealerName are required" });
    
            if (string.IsNullOrWhiteSpace(inventorySummary.Region))
                return Results.BadRequest(new { message = "Region is required (Miền Bắc, Miền Trung, or Miền Nam)" });
    
            inventorySummary.Id = Guid.NewGuid();
            inventorySummary.LastUpdatedAt = DateTime.UtcNow;
    
            db.InventorySummaries.Add(inventorySummary);
            await db.SaveChangesAsync();
    
            return Results.Created($"/api/reports/inventory-summary/{inventorySummary.Id}", new { success = true, data = inventorySummary });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in POST /api/reports/inventory-summary: {ex.Message}");
            return Results.Json(new { success = false, error = ex.Message }, statusCode: 500);
        }
    })
    .WithName("CreateInventorySummary")
    .WithOpenApi()
    .Produces(201)
    .Produces(400)
    .Produces(500).RequireAuthorization();
    // Sample weather endpoint kept for template parity. Both it and
    // WeatherForecast moved here with the routes: the array it indexes was
    // declared in Program.cs between the route groups, which is not a
    // dependency Program.cs still has any reason to keep.
var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast")
    .WithName("GetWeatherForecast")
    .WithOpenApi();
    }
}

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}