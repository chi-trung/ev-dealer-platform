using ev_dealer_reporting.Data;
using ev_dealer_reporting.DTOs;
using ev_dealer_reporting.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DealerSystem.Tests.NotificationService.Tests;

/// <summary>
/// Issue #92 (P2): integration test for <c>DataSynchronizationService</c> —
/// the HTTP fan-out behind <c>POST /api/reports/synchronize-data</c> — against a
/// REAL Postgres.
///
/// Why a real database and not SQLite: the entire class of bug this phase
/// fixed is provider-specific. Every baseline migration in the repo was
/// scaffolded under <c>DB_PROVIDER</c> unset (which means sqlite, see
/// <c>Common/Data/DbProviderSelector.cs</c>), so the migrations emit fixed C#
/// declaring date columns as <c>type: "TEXT"</c> and booleans as
/// <c>type: "INTEGER"</c>. On a SQLite file that is honest. On Postgres it
/// means <c>SalesSummary.Date</c> was a real <c>text</c> column while the
/// running model treated it as <c>timestamptz</c> — so the GROUP-BY at
/// DataSynchronizationService.cs:57 and the inserts at :74 ran against the
/// wrong type. A SQLite-backed test cannot see any of it: SQLite has no
/// opinion on <c>text</c> vs <c>timestamptz</c>.
///
/// WHY THE ASSERTIONS ARE ON DATABASE STATE, NOT ON "NO EXCEPTION"
/// All three Synchronize* methods wrap their whole body in
/// <c>catch (Exception ex) { _logger.LogError(ex, ...); }</c>
/// (DataSynchronizationService.cs:88, :143, :265). A test that only asserted
/// the call returned would pass even if every insert failed — the service
/// cannot report failure, it only logs it. So each test asserts the rows
/// actually landed, with the right values. That is the only assertion shape
/// that can tell the difference between "synchronized" and "swallowed a
/// Postgres error".
///
/// CONNECTION STRING
/// Taken from <c>EVM_TEST_POSTGRES</c>; the tests SKIP when it is unset, so
/// the suite still runs green on a machine with no Postgres. When it IS set
/// the database must be reachable and the ReportingService migrations must be
/// applied — <see cref="EnsureReportingSchema"/> does that itself with
/// <c>Migrate()</c>, exactly as the service does at boot.
/// </summary>
public class PostgresDataSynchronizationTests
{
    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("EVM_TEST_POSTGRES");

    /// <summary>
    /// Applies ReportingService's own migrations to the shared database. Safe
    /// against a database the running stack already migrated: <c>Migrate()</c>
    /// is a no-op once <c>__EFMigrationsHistory</c> has the ids.
    /// </summary>
    private static ReportingDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ReportingDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        var db = new ReportingDbContext(options);
        db.Database.Migrate();
        return db;
    }

    /// <summary>
    /// Captures what the service logged. All three Synchronize* methods
    /// swallow their exceptions into <c>_logger.LogError</c>
    /// (DataSynchronizationService.cs:88, :143, :265), so a NullLogger turns
    /// any real Postgres failure into a bare "expected 2, got 1" with no cause.
    /// Recording the log and attaching it to the failure is the difference
    /// between a test that diagnoses itself and one that sends you to the
    /// service code to guess.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<string> _lines = new();

        public string Transcript =>
            _lines.Count == 0 ? "(the service logged nothing)" : string.Join(" | ", _lines);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // Walk the whole InnerException chain: EF's DbUpdateException
            // carries the actual Postgres error (SqlState, column, constraint)
            // one level down, and stopping at the wrapper is what turns a
            // one-line diagnosis into a guessing trip.
            var detail = "";
            for (var e = exception; e is not null; e = e.InnerException)
            {
                detail += $" :: {e.GetType().Name}: {e.Message}";
            }
            _lines.Add($"[{logLevel}] {formatter(state, exception)}{detail}");
        }
    }

    private static (DataSynchronizationService Service, CapturingLogger<DataSynchronizationService> Log)
        NewService(
        ReportingDbContext db,
        ISalesDataService sales,
        IVehicleDataService vehicles,
        ICustomerDataService customers)
    {
        var log = new CapturingLogger<DataSynchronizationService>();
        return (new DataSynchronizationService(
            sales, vehicles, customers, db, log), log);
    }

    // --- Fakes: the fan-out is HTTP, so the boundary under test is the DB
    // write, not the HTTP call. Fakes keep the test hermetic while still
    // exercising every entity-mapping decision the real DTOs drive. ---

    private sealed class FakeSalesDataService : ISalesDataService
    {
        public List<OrderDataDto> Orders { get; } = new();
        public List<PaymentDataDto> Payments { get; } = new();
        public List<ContractDataDto> Contracts { get; } = new();

        public Task<List<QuoteDataDto>> GetQuotesAsync(DateTime? fromDate, DateTime? toDate, int? dealerId = null) =>
            Task.FromResult(new List<QuoteDataDto>());

        public Task<List<OrderDataDto>> GetOrdersAsync(DateTime? fromDate, DateTime? toDate, int? dealerId = null) =>
            Task.FromResult(Orders);

        public Task<List<PaymentDataDto>> GetPaymentsAsync(DateTime? fromDate, DateTime? toDate, int? orderId = null) =>
            Task.FromResult(Payments);

        public Task<List<ContractDataDto>> GetContractsAsync(DateTime? fromDate, DateTime? toDate, int? dealerId = null) =>
            Task.FromResult(Contracts);

        public Task<OrderDataDto?> GetOrderByIdAsync(int orderId) =>
            Task.FromResult(Orders.FirstOrDefault(o => o.OrderId == orderId));
    }

    private sealed class FakeVehicleDataService : IVehicleDataService
    {
        public List<VehicleInventoryDto> Vehicles { get; } = new();
        public List<DealerDto> Dealers { get; } = new();

        public Task<List<VehicleInventoryDto>> GetVehiclesAsync(int? dealerId = null) =>
            Task.FromResult(Vehicles);

        public Task<VehicleInventoryDto?> GetVehicleByIdAsync(int vehicleId) =>
            Task.FromResult(Vehicles.FirstOrDefault(v => v.Id == vehicleId));

        public Task<List<DealerDto>> GetDealersAsync() => Task.FromResult(Dealers);
    }

    private sealed class FakeCustomerDataService : ICustomerDataService
    {
        public List<CustomerDataDto> Customers { get; } = new();

        public Task<List<CustomerDataDto>> GetCustomersAsync(int? customerId = null) =>
            Task.FromResult(Customers);
    }

    [PostgresFact]
    public async Task SynchronizeSales_WritesSummaryRows_WithGroupingByDateAndDealer()
    {

        using var db = NewContext();
        db.SalesSummaries.RemoveRange(db.SalesSummaries);
        await db.SaveChangesAsync();

        var day = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);
        var sales = new FakeSalesDataService();
        // Two orders on the same day + dealer, one on a different dealer:
        // exercises the GROUP-BY branch at DataSynchronizationService.cs:56.
        sales.Orders.Add(new OrderDataDto
        {
            OrderId = 900001, OrderNumber = "P2-A", DealerId = 77, SalespersonId = 5,
            CustomerId = 11, VehicleId = 21, Quantity = 2,
            SubTotal = 1_000_000m, TotalDiscount = 0m, TotalPrice = 1_000_000m,
            PaymentMethod = "Trả thẳng", Status = "Confirmed", CreatedAt = day.AddHours(9)
        });
        sales.Orders.Add(new OrderDataDto
        {
            OrderId = 900002, OrderNumber = "P2-B", DealerId = 77, SalespersonId = 5,
            CustomerId = 11, VehicleId = 22, Quantity = 1,
            SubTotal = 500_000m, TotalDiscount = 0m, TotalPrice = 500_000m,
            PaymentMethod = "Trả thẳng", Status = "Confirmed", CreatedAt = day.AddHours(14)
        });
        sales.Orders.Add(new OrderDataDto
        {
            OrderId = 900003, OrderNumber = "P2-C", DealerId = 78, SalespersonId = 6,
            CustomerId = 12, VehicleId = 23, Quantity = 3,
            SubTotal = 300_000m, TotalDiscount = 0m, TotalPrice = 300_000m,
            PaymentMethod = "Trả thẳng", Status = "Confirmed", CreatedAt = day.AddHours(10)
        });

        var vehicles = new FakeVehicleDataService();
        vehicles.Dealers.Add(new DealerDto { Id = 77, Name = "Dealer 77", Region = "Miền Bắc" });
        vehicles.Dealers.Add(new DealerDto { Id = 78, Name = "Dealer 78", Region = "Miền Nam" });

        var (service, log) = NewService(db, sales, vehicles, new FakeCustomerDataService());
        await service.SynchronizeSalesDataAsync();

        // Not Assert.NotEmpty only: the aggregate must reflect the GROUP BY,
        // i.e. 2 rows (77 and 78), dealer 77's row carrying BOTH of its orders.
        var rows = await db.SalesSummaries.AsNoTracking()
            .Where(s => s.Date == day)
            .OrderBy(s => s.DealerId)
            .ToListAsync();

        Assert.True(rows.Count == 2,
            $"expected 2 summary rows (dealer 77 and 78), got {rows.Count}: {log.Transcript}");
        Assert.Equal(77, rows[0].DealerId);
        Assert.Equal("Dealer 77", rows[0].DealerName);
        Assert.Equal("Miền Bắc", rows[0].Region);
        Assert.Equal(3, rows[0].TotalOrders);
        Assert.Equal(1_500_000m, rows[0].TotalRevenue);
        Assert.Equal(78, rows[1].DealerId);
        // Dealer 78 has ONE order, but that order has Quantity = 3, and
        // TotalOrders sums quantities (DataSynchronizationService.cs:70),
        // not order counts. Asserting 1 here while the fixture says 3 was a
        // wrong expectation in this test, not a production defect.
        Assert.Equal(3, rows[1].TotalOrders);
        Assert.Equal(300_000m, rows[1].TotalRevenue);
    }

    [PostgresFact]
    public async Task SynchronizeInventory_WritesOneRowPerVehicle_AndReadsTheRegionFromTheDealer()
    {

        using var db = NewContext();
        db.InventorySummaries.RemoveRange(db.InventorySummaries);
        await db.SaveChangesAsync();

        var vehicles = new FakeVehicleDataService();
        vehicles.Dealers.Add(new DealerDto { Id = 77, Name = "Dealer 77", Region = "Miền Bắc" });
        vehicles.Vehicles.Add(new VehicleInventoryDto
        {
            Id = 4242, Model = "Porsche Taycan", DealerId = 77, DealerName = "Dealer 77",
            StockQuantity = 7, Price = 2_000_000m,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        var (service, log) = NewService(db, new FakeSalesDataService(), vehicles, new FakeCustomerDataService());
        await service.SynchronizeInventoryDataAsync();

        var inventory = await db.InventorySummaries.AsNoTracking().ToListAsync();
        Assert.True(inventory.Count == 1,
            $"expected 1 inventory row, got {inventory.Count}: {log.Transcript}");
        var row = inventory[0];
        Assert.Equal(4242, row.VehicleId);
        Assert.Equal("Porsche Taycan", row.VehicleName);
        Assert.Equal(77, row.DealerId);
        // Region is NOT on VehicleInventoryDto — it is joined from GetDealersAsync.
        // This asserts the join happened rather than defaulting to "Unknown".
        Assert.Equal("Miền Bắc", row.Region);
        Assert.Equal(7, row.StockCount);
    }

    [PostgresFact]
    public async Task SynchronizeDebt_CarriesDecimalPrecision_AndSkipsPaidContracts()
    {

        using var db = NewContext();
        db.DebtSummaries.RemoveRange(db.DebtSummaries);
        await db.SaveChangesAsync();

        var sales = new FakeSalesDataService();
        // An UNPAID contract becomes a DealerToManufacturer debt row.
        sales.Contracts.Add(new ContractDataDto
        {
            ContractId = 7001, OrderId = 900001, CustomerId = 11, DealerId = 77,
            SalespersonId = 5, ContractNumber = "P2-CT-1",
            SignedDate = new DateOnly(2026, 3, 10),
            TotalAmount = 1_234_567.89m,
            PaymentStatus = "Unpaid", Status = "Active",
            CreatedAt = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc)
        });
        // A PAID contract must NOT become debt — the guard at
        // DataSynchronizationService.cs:180. Without it every contract would
        // inflate the debt report forever.
        sales.Contracts.Add(new ContractDataDto
        {
            ContractId = 7002, OrderId = 900002, CustomerId = 12, DealerId = 77,
            SalespersonId = 5, ContractNumber = "P2-CT-2",
            SignedDate = new DateOnly(2026, 3, 11),
            TotalAmount = 500_000m,
            PaymentStatus = "Paid", Status = "Completed",
            CreatedAt = new DateTime(2026, 3, 11, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 3, 11, 0, 0, 0, DateTimeKind.Utc)
        });

        var vehicles = new FakeVehicleDataService();
        vehicles.Dealers.Add(new DealerDto { Id = 77, Name = "Dealer 77", Region = "Miền Bắc" });

        var (service, log) = NewService(db, sales, vehicles, new FakeCustomerDataService());
        await service.SynchronizeDebtDataAsync();

        var debts = await db.DebtSummaries.AsNoTracking().ToListAsync();
        Assert.True(debts.Count == 1,
            $"expected 1 debt row (the UNPAID contract only), got {debts.Count}: {log.Transcript}");
        var row = debts[0];
        Assert.Equal(7001, int.Parse(row.ReferenceId));
        Assert.Equal("Contract", row.ReferenceType);
        Assert.Equal("DealerToManufacturer", row.DebtType);
        Assert.Equal(77, row.DealerId);
        // The precision assertion: 1_234_567.89 has 2 decimals and 8 integer
        // digits. On a plain `numeric` column Postgres would still store it, but
        // the model declares decimal(18,2) and the migration now stamps that —
        // reading a value that lost its scale back is the regression this pins.
        Assert.Equal(1_234_567.89m, row.TotalAmount);
        Assert.Equal(1_234_567.89m, row.OutstandingAmount);
        // DueDate is derived from SignedDate + 1 month (line 193), which is
        // where a `date` column silently typed as `text` used to break.
        Assert.NotNull(row.DueDate);
    }

    [PostgresFact]
    public async Task SynchronizeAll_LeavesTheReportTablesQueryableByDateRange()
    {
        // The phase's headline symptom: a date filter over a `text` column
        // raised `operator does not exist: text > timestamp with time zone` on
        // every reporting call that took from/to (ReportEndpoints.cs:90,92).

        using var db = NewContext();
        db.SalesSummaries.RemoveRange(db.SalesSummaries);
        db.DebtSummaries.RemoveRange(db.DebtSummaries);
        await db.SaveChangesAsync();

        var day = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);
        var sales = new FakeSalesDataService();
        sales.Orders.Add(new OrderDataDto
        {
            OrderId = 900010, OrderNumber = "P2-RANGE", DealerId = 77, SalespersonId = 5,
            CustomerId = 11, VehicleId = 21, Quantity = 1,
            SubTotal = 42_000m, TotalDiscount = 0m, TotalPrice = 42_000m,
            PaymentMethod = "Trả thẳng", Status = "Confirmed", CreatedAt = day
        });
        sales.Contracts.Add(new ContractDataDto
        {
            ContractId = 7003, OrderId = 900010, CustomerId = 11, DealerId = 77,
            SalespersonId = 5, ContractNumber = "P2-RANGE-CT",
            SignedDate = new DateOnly(2026, 3, 15),
            TotalAmount = 42_000m, PaymentStatus = "Unpaid", Status = "Active",
            CreatedAt = day, UpdatedAt = day
        });

        var vehicles = new FakeVehicleDataService();
        vehicles.Dealers.Add(new DealerDto { Id = 77, Name = "Dealer 77", Region = "Miền Bắc" });

        var (service, log) = NewService(db, sales, vehicles, new FakeCustomerDataService());
        await service.SynchronizeAllDataAsync();

        // Exactly the LINQ ReportEndpoints.cs:90 issues. Before the
        // PostgresColumnTypes migration these threw 42883/operator-does-not-exist.
        var from = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc);
        var salesInRange = await db.SalesSummaries.AsNoTracking()
            .Where(s => s.Date >= from && s.Date <= to)
            .ToListAsync();
        var debtsInRange = await db.DebtSummaries.AsNoTracking()
            .Where(d => d.CreatedAt >= from && d.CreatedAt <= to)
            .ToListAsync();

        Assert.True(salesInRange.Count == 1,
            $"expected 1 sales row in March 2026, got {salesInRange.Count}: {log.Transcript}");
        Assert.True(debtsInRange.Count == 1,
            $"expected 1 debt row in March 2026, got {debtsInRange.Count}: {log.Transcript}");
    }
}
