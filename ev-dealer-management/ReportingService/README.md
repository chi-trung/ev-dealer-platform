# ReportingService

Service báo cáo của EV Dealer Platform — .NET 8 minimal API, gom dữ liệu từ
Sales/Vehicle/Customer, lưu summary cục bộ, phục vụ dashboard frontend và
export CSV.

- Port dev: `http://localhost:5208` (`launchSettings.json` profile `http`;
  profile `https` thêm `https://localhost:7245`)
- Docker: compose map `5208:80`; gateway (Ocelot) rewrite `localhost:5208`
- Swagger UI (Development): `http://localhost:5208/swagger`
- Docs liên quan: [API_REFERENCE.md](API_REFERENCE.md) ·
  [TESTING.md](TESTING.md)

## Chạy local

```bash
cd ev-dealer-management/ReportingService
ConnectionStrings__DefaultConnection="Data Source=reporting_dev.db" \
  dotnet run --no-launch-profile --urls "http://localhost:5208"
```

(PowerShell: `$env:ConnectionStrings__DefaultConnection = "Data Source=reporting_dev.db"`
trước khi `dotnet run --no-launch-profile --urls "http://localhost:5208"`)

**Tại sao bắt buộc có override:** `appsettings.json` khai
`DefaultConnection` dạng chuỗi **Postgres** (`Host=...;Port=5432;...`) trong
khi provider mặc định là **SQLite** (`DB_PROVIDER` không set → mặc định
`sqlite`, xem `Common/Data/DbProviderSelector.cs`). Kết quả chạy **không**
có override (đã verify 2026-10-02):

- service vẫn boot được, nhưng log warning
  `could not apply database migrations or synchronize data: Connection string keyword 'host' is not supported`
- `GET /health` → **503** `{"status":"Unhealthy",... "Database is unreachable"}`
- mọi endpoint tra dữ liệu sẽ lỗi ở tầng DB

Có override → `GET /health` trả **200** (verified). File SQLite tạo tại
`reporting_dev.db` — xem nhanh bằng `sqlite3 reporting_dev.db ".tables"`
hoặc DB Browser for SQLite.

## Xác thực

Tất cả **18 route `/api/reports/*` đều `.RequireAuthorization()`** — JWT
HS256, `iss`/`aud` = `evm.local`, key trong `appsettings.json` → `Jwt:Key`.
Không token → **401** (verified). Hai route ngoại lệ:

- `GET /weatherforecast` — template mẫu của minimal API, không auth
- `GET /health` — health check, anonymous

Token hợp lệ do **UserService** cấp qua `POST /api/auth/login`
(`Program.cs` ghi rõ token chỉ được mint từ đó). Dev không chạy đủ stack
thì mint token offline — xem [TESTING.md](TESTING.md#2-lấy-token).

## Routes

18 route báo cáo (prefix `/api/reports`, **tất cả cần JWT**), khai báo trong
`Endpoints/ReportEndpoints.cs`:

| Method | Route | Tham số chính |
|---|---|---|
| GET | `/summary` | `type` (mặc định `sales`), `from`, `to` |
| GET | `/sales-by-region` | `from`, `to` |
| GET | `/sales-proportion` | `from`, `to` |
| GET | `/top-vehicles` | `limit` (mặc định 10) |
| GET | `/sales-by-dealer` | `dealerId`, `period`, `fromDate`, `toDate` |
| GET | `/sales-by-staff` | `from`, `to` |
| GET | `/inventory-trends` | — |
| GET | `/debt-summary` | `dealerId`, `customerId`, `debtType`, `status`, `from`, `to` |
| GET | `/debt-report` | `dealerId` (`from`/`to` nhận nhưng **chưa dùng**) |
| GET | `/demand-forecast` | `from`, `to` |
| GET | `/sales-summary` | `fromDate`, `toDate`, `dealerId` (int) |
| GET | `/sales-summary/{id}` | `id` (Guid) |
| GET | `/inventory-summary` | `dealerId`, `vehicleId` (int) |
| GET | `/inventory-summary/{id}` | `id` (Guid) |
| POST | `/sales-summary` | body SalesSummary |
| POST | `/inventory-summary` | body InventorySummary |
| POST | `/export` | body `{type, from, to}` → file CSV |
| POST | `/synchronize-data` | — (kéo data từ các service) |

Chi tiết params, request/response mẫu và quy tắc validate: xem
[API_REFERENCE.md](API_REFERENCE.md).

## Kiến trúc dữ liệu

- **Đồng bộ:** `DataSynchronizationService` kéo HTTP từ
  SalesService (`:5003`), VehicleService (`:5068`), CustomerService (`:5039`),
  UserService (`:7001` — cấu hình trong `appsettings.json` → `Services`) qua
  `POST /api/reports/synchronize-data`. Không có pipeline ETL ngoài nào khác.
- **Tóm tắt:** summary ghi vào DB cục bộ (`SalesSummaries`,
  `InventorySummaries`, `DebtSummaries`, `ReportExports`...); các báo cáo
  region/proportion/top-vehicles tính trực tiếp từ các bảng này.
- **Frontend:** `ev-dealer-frontend/src/services/reportService.js` import
  shared `api` (baseURL `VITE_API_BASE_URL` mặc định
  `http://localhost:5036/api` → gateway → `:5208`), tự gắn
  `Authorization: Bearer <token>` từ localStorage. Người dùng:
  `pages/Reports/Reports.jsx`, `components/DemandForecastChart.jsx`.
- **Khác biệt quan trọng về response:** 12 endpoint trả envelope
  `{success, data}` / `{success, count, data}`; 4 endpoint dashboard
  (`summary`, `sales-by-region`, `sales-proportion`, `top-vehicles`) và
  `demand-forecast` trả **raw object/array** — chi tiết trong API_REFERENCE.

## Known issues (chưa sửa — ghi lại để không quên)

1. **`GetTotalSalesDashboardAsync` không gắn route nào** — method có trong
   `Services/ReportService.cs` nhưng `ReportEndpoints.cs` không map; frontend
   dùng `summary`/`sales-by-region`/`sales-proportion`/`top-vehicles` thay.
2. **Region bắt buộc nhưng không validate giá trị** — message 400 ghi
   "Miền Bắc, Miền Trung, or Miền Nam" nhưng code chỉ check
   không rỗng; gửi `region: "X"` vẫn **201** (verified).
3. **`debt-report` nhận `from`/`to` nhưng handler bỏ qua** — chỉ `dealerId` có hiệu lực.
4. **Bug provider SQLite/Postgres ở trên** — `DefaultConnection` Postgres-format
   trong khi provider mặc định sqlite; local bắt buộc override env.
5. **Mua nợ của đại lý chưa tách bạch** — `GetDealerDebtReportAsync` giả định
   mọi order là đại lý mua từ hãng (comment trong code).
6. **`import-test-data.ps1` chưa khớp API hiện tại** — dữ liệu mẫu vẫn dùng
   `dealerId` dạng GUID và script không gửi JWT → với code hiện tại mọi dòng
   sẽ 400/401. Cách import đúng: xem [TESTING.md](TESTING.md).
