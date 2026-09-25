using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ev_dealer_reporting.Data;
using ev_dealer_reporting.DTOs;
using ev_dealer_reporting.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ev_dealer_reporting.Services
{
    public class DataSynchronizationService : IDataSynchronizationService
    {
        private readonly ISalesDataService _salesDataService;
        private readonly IVehicleDataService _vehicleDataService;
        private readonly ICustomerDataService _customerDataService; // Add CustomerDataService
        private readonly ReportingDbContext _dbContext;
        private readonly ILogger<DataSynchronizationService> _logger;

        public DataSynchronizationService(
            ISalesDataService salesDataService,
            IVehicleDataService vehicleDataService,
            ICustomerDataService customerDataService, // Inject CustomerDataService
            ReportingDbContext dbContext,
            ILogger<DataSynchronizationService> logger)
        {
            _salesDataService = salesDataService;
            _vehicleDataService = vehicleDataService;
            _customerDataService = customerDataService; // Assign CustomerDataService
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task SynchronizeSalesDataAsync()
        {
            _logger.LogInformation("Starting sales data synchronization...");

            try
            {
                // Clear existing SalesSummaries to ensure fresh data
                _dbContext.SalesSummaries.RemoveRange(_dbContext.SalesSummaries);
                await _dbContext.SaveChangesAsync();

                // Lấy tất cả orders từ SalesService
                var orders = await _salesDataService.GetOrdersAsync(null, null);
                var dealers = await _vehicleDataService.GetDealersAsync(); // Lấy thông tin dealer để có Region

                var dealerRegionMap = dealers.ToDictionary(d => d.Id, d => d.Region ?? "Unknown");
                var dealerNameMap = dealers.ToDictionary(d => d.Id, d => d.Name);

                foreach (var order in orders)
                {
                    // Group by Date and DealerId to create SalesSummary.
                    //
                    // Issue #92 (P2): this used to query the DATABASE for an
                    // existing row, but rows created earlier in this same loop
                    // only exist in the change tracker until SaveChangesAsync at
                    // the bottom. So the second order of the same day+dealer did
                    // not find the first one and created a DUPLICATE row instead
                    // of folding into it — the else branch below was effectively
                    // dead, and TotalOrders/TotalRevenue were split across two
                    // rows per day per dealer. Caught by
                    // PostgresDataSynchronizationTests, which expects 2 rows and
                    // got 3.
                    //
                    // The database is empty by now (cleared three lines above),
                    // so this only ever matches a row this same loop created.
                    //
                    // The .Local fallback is restricted to state Added on
                    // purpose. The clearing above does RemoveRange +
                    // SaveChanges, which leaves the removed entities TRACKED in
                    // state Deleted — and .Local happily returns those
                    // tombstones. Matching one of those made this loop add the
                    // new order's quantity onto a row that was about to stay
                    // deleted, so the fresh row was never created and the
                    // numbers came out as stale_value + new_value.
                    //
                    // DateTime.Date strips the Kind (it always returns
                    // Unspecified), so re-stamp UTC — see TimestampParser in
                    // SalesDataService for the Npgsql reason.
                    var day = DateTime.SpecifyKind(order.CreatedAt.Date, DateTimeKind.Utc);
                    var existingSummary = await _dbContext.SalesSummaries
                        .FirstOrDefaultAsync(s => s.Date == day && s.DealerId == order.DealerId)
                        ?? _dbContext.SalesSummaries.Local
                            .FirstOrDefault(s => s.Date == day
                                              && s.DealerId == order.DealerId
                                              && _dbContext.Entry(s).State == EntityState.Added);

                    if (existingSummary == null)
                    {
                        var newSummary = new SalesSummary
                        {
                            Id = Guid.NewGuid(),
                            Date = day,
                            DealerId = order.DealerId,
                            DealerName = dealerNameMap.GetValueOrDefault(order.DealerId, $"Dealer {order.DealerId}"),
                            Region = dealerRegionMap.GetValueOrDefault(order.DealerId, "Unknown"),
                            SalespersonId = order.SalespersonId,
                            SalespersonName = $"Salesperson {order.SalespersonId}", // Placeholder, ideally from UserService
                            TotalOrders = order.Quantity,
                            TotalRevenue = order.TotalPrice,
                            LastUpdatedAt = DateTime.UtcNow
                        };
                        _dbContext.SalesSummaries.Add(newSummary);
                    }
                    else
                    {
                        existingSummary.TotalOrders += order.Quantity;
                        existingSummary.TotalRevenue += order.TotalPrice;
                        existingSummary.LastUpdatedAt = DateTime.UtcNow;
                        // Deliberately NO .Update() call here (Issue #92).
                        // Update() forces state Modified, and the row matched
                        // above can be one this same loop only just Add()ed —
                        // whose state was Added. Forcing Modified makes EF issue
                        // an UPDATE for a row that does not exist yet, which
                        // fails with DbUpdateConcurrencyException ("expected to
                        // affect 1 row(s), but actually affected 0"). Both
                        // sources (FirstOrDefaultAsync and .Local) return
                        // TRACKED entities, and the change tracker picks the
                        // property mutations up on its own.
                    }
                }

                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Sales data synchronization completed. {Count} orders processed.", orders.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during sales data synchronization.");
            }
        }

        public async Task SynchronizeInventoryDataAsync()
        {
            _logger.LogInformation("Starting inventory data synchronization...");

            try
            {
                // Clear existing InventorySummaries to ensure fresh data
                _dbContext.InventorySummaries.RemoveRange(_dbContext.InventorySummaries);
                await _dbContext.SaveChangesAsync();

                // Lấy tất cả vehicles từ VehicleService
                var vehicles = await _vehicleDataService.GetVehiclesAsync();
                var dealers = await _vehicleDataService.GetDealersAsync();

                var dealerRegionMap = dealers.ToDictionary(d => d.Id, d => d.Region ?? "Unknown");

                foreach (var vehicle in vehicles)
                {
                    // Check if InventorySummary already exists for this dealer and vehicle
                    var existingSummary = await _dbContext.InventorySummaries
                        .FirstOrDefaultAsync(i => i.DealerId == vehicle.DealerId && i.VehicleId == vehicle.Id);

                    if (existingSummary == null)
                    {
                        var newSummary = new InventorySummary
                        {
                            Id = Guid.NewGuid(),
                            VehicleId = vehicle.Id,
                            VehicleName = vehicle.Model,
                            DealerId = vehicle.DealerId,
                            DealerName = vehicle.DealerName,
                            Region = dealerRegionMap.GetValueOrDefault(vehicle.DealerId, "Unknown"),
                            StockCount = vehicle.StockQuantity,
                            LastUpdatedAt = DateTime.UtcNow
                        };
                        _dbContext.InventorySummaries.Add(newSummary);
                    }
                    else
                    {
                        existingSummary.StockCount = vehicle.StockQuantity;
                        existingSummary.LastUpdatedAt = DateTime.UtcNow;
                        // No .Update() — see the note in SynchronizeSalesDataAsync.
                        // (Here the else branch is in practice unreachable: the
                        // PK is a fresh Guid.NewGuid() and the table was cleared
                        // two lines above, so the query never matches. Kept as a
                        // guard for the day the clearing changes.)
                    }
                }

                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Inventory data synchronization completed. {Count} vehicles processed.", vehicles.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during inventory data synchronization.");
            }
        }

        public async Task SynchronizeDebtDataAsync()
        {
            _logger.LogInformation("Starting debt data synchronization...");

            try
            {
                // Clear existing DebtSummaries to ensure fresh data
                _dbContext.DebtSummaries.RemoveRange(_dbContext.DebtSummaries);
                await _dbContext.SaveChangesAsync();

                var currentTime = DateTime.UtcNow;

                // Fetch all necessary data
                var contracts = await _salesDataService.GetContractsAsync(null, null);
                var orders = await _salesDataService.GetOrdersAsync(null, null);
                var payments = await _salesDataService.GetPaymentsAsync(null, null);
                // GetPurchasesAsync đã bị xóa vì endpoint /api/customers/purchases không tồn tại trong CustomerService
                // var purchases = await _customerDataService.GetPurchasesAsync(null, null);
                var customers = await _customerDataService.GetCustomersAsync();
                var dealers = await _vehicleDataService.GetDealersAsync();

                var customerNameMap = customers.ToDictionary(c => c.Id, c => c.Name);
                var dealerNameMap = dealers.ToDictionary(d => d.Id, d => d.Name);

                // Map payments to orders for easier calculation of outstanding amounts
                var paymentsByOrderId = payments.GroupBy(p => p.OrderId)
                                                .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

                // 1. Dealer to Manufacturer Debt (from Contracts)
                foreach (var contract in contracts)
                {
                    // Assuming "Pending" or "Unpaid" status indicates outstanding debt
                    if (contract.PaymentStatus != "Paid")
                    {
                        var debt = new DebtSummary
                        {
                            Id = Guid.NewGuid(),
                            DealerId = contract.DealerId,
                            DealerName = dealerNameMap.GetValueOrDefault(contract.DealerId, $"Dealer {contract.DealerId}"),
                            DebtType = "DealerToManufacturer",
                            ReferenceType = "Contract",
                            ReferenceId = contract.ContractId.ToString(),
                            TotalAmount = contract.TotalAmount,
                            OutstandingAmount = contract.TotalAmount, // Simplified: assuming full amount is outstanding if not paid
                            Status = contract.PaymentStatus,
                            // DateOnly.ToDateTime always yields Kind=Unspecified,
                            // which Npgsql rejects on a timestamptz column.
                            DueDate = DateTime.SpecifyKind(
                                contract.SignedDate.ToDateTime(TimeOnly.MinValue).AddMonths(1),
                                DateTimeKind.Utc), // Placeholder due date
                            CreatedAt = contract.CreatedAt,
                            LastUpdatedAt = currentTime
                        };
                        _dbContext.DebtSummaries.Add(debt);
                    }
                }

                // 2. Customer to Dealer Debt (from Orders with installment payments)
                foreach (var order in orders)
                {
                    if (order.PaymentMethod == "Trả góp")
                    {
                        var paidAmount = paymentsByOrderId.GetValueOrDefault(order.OrderId, 0m);
                        var outstandingAmount = order.TotalPrice - paidAmount;

                        if (outstandingAmount > 0)
                        {
                            var debt = new DebtSummary
                            {
                                Id = Guid.NewGuid(),
                                DealerId = order.DealerId,
                                DealerName = dealerNameMap.GetValueOrDefault(order.DealerId, $"Dealer {order.DealerId}"),
                                CustomerId = order.CustomerId,
                                CustomerName = customerNameMap.GetValueOrDefault(order.CustomerId, $"Customer {order.CustomerId}"),
                                DebtType = "CustomerToDealer",
                                ReferenceType = "Order",
                                ReferenceId = order.OrderId.ToString(),
                                TotalAmount = order.TotalPrice,
                                OutstandingAmount = outstandingAmount,
                                Status = "Outstanding", // Or derive from order.Status
                                DueDate = order.CreatedAt.AddMonths(order.LoanTermMonths ?? 1), // Placeholder due date
                                CreatedAt = order.CreatedAt,
                                LastUpdatedAt = currentTime
                            };
                            _dbContext.DebtSummaries.Add(debt);
                        }
                    }
                }

                // 3. Customer to Dealer Debt (from Purchases in CustomerService)
                // Phần này đã bị comment out vì endpoint /api/customers/purchases không tồn tại trong CustomerService
                // Công nợ từ khách hàng đã được xử lý đầy đủ ở phần 2 (từ Orders với PaymentMethod = "Trả góp")
                /*
                foreach (var purchase in purchases)
                {
                    // Assuming all purchases from CustomerService are outstanding debt unless explicitly paid
                    var debt = new DebtSummary
                    {
                        Id = Guid.NewGuid(),
                        CustomerId = purchase.CustomerId,
                        CustomerName = customerNameMap.GetValueOrDefault(purchase.CustomerId, $"Customer {purchase.CustomerId}"),
                        // DealerId is not directly available in PurchaseDataDto, might need to fetch from CustomerDataDto
                        // For now, leaving DealerId as null or trying to infer if possible
                        DebtType = "CustomerToDealer",
                        ReferenceType = "Purchase",
                        ReferenceId = purchase.Id.ToString(),
                        TotalAmount = purchase.Amount,
                        OutstandingAmount = purchase.Amount, // Simplified: assuming full amount is outstanding
                        Status = "Outstanding",
                        DueDate = purchase.PurchaseDate.AddMonths(1), // Placeholder due date
                        CreatedAt = purchase.PurchaseDate,
                        LastUpdatedAt = currentTime
                    };
                    _dbContext.DebtSummaries.Add(debt);
                }
                */

                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Debt data synchronization completed. {Count} debt entries processed.", _dbContext.DebtSummaries.Count());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during debt data synchronization.");
            }
        }

        public async Task SynchronizeAllDataAsync()
        {
            await SynchronizeSalesDataAsync();
            await SynchronizeInventoryDataAsync();
            await SynchronizeDebtDataAsync(); // Call new debt synchronization
            _logger.LogInformation("All data synchronization completed.");
        }
    }
}
