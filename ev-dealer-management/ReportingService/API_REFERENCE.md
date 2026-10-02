# ReportingService — API Reference

Tất cả route trừ `/weatherforecast` và `/health` đều yêu cầu JWT.
Nguồn sự thật: `Endpoints/ReportEndpoints.cs`, `DTOs/`, `Models/`.
Quy tắc chạy local + lấy token: [TESTING.md](TESTING.md).

## 1. Cơ chế chung

### Auth

```
GET /api/reports/summary
Authorization: Bearer <JWT>
```

- JWT HS256, `iss`/`aud` = `evm.local`, key = `Jwt:Key` trong `appsettings.json`.
- Không token / token sai → **401**. Hết hạn → 401.
- Token thật: `POST /api/auth/login` của UserService. Dev offline: mint thủ công
  (TESTING.md).

### Hai kiểu response

**(A) Envelope** — 12 endpoint:

```json
{ "success": true, "data": ... }            // báo cáo đơn lẻ / POST / GET {id}
{ "success": true, "count": 3, "data": [] } // list: sales-summary, inventory-summary, debt-summary
{ "success": false, "error": "...", "details": "..." }  // lỗi server (HTTP 500)
```

**(B) Raw** — 5 endpoint dashboard/forecast, trả thẳng object hoặc array
(không có `success`):

`/summary`, `/sales-by-region`, `/sales-proportion`, `/top-vehicles`,
`/demand-forecast`

Lỗi validate của POST (`400`) dùng `{ "message": "..." }`.

---

## 2. Dashboard (raw)

### GET `/api/reports/summary`

Tham số: `type` (mặc định `sales`, hiện chỉ là label), `from`, `to`
(ISO date; parse được thì filter `SalesSummaries.Date`).

```json
{
  "type": "sales",
  "from": "2025-01-01",
  "to": "2025-12-31",
  "metrics": {
    "totalSales": 21,
    "totalRevenue": 6450000000,
    "activeDealers": 2,
    "totalDealers": 3,
    "conversionRate": 0.4118
  }
}
```

- `totalSales` = Σ `TotalOrders`; `totalRevenue` = Σ `TotalRevenue` (double).
- `activeDealers` = max(unique dealer trong Sales, unique dealer trong Inventory);
  `totalDealers` = unique dealer của cả 2 bảng.
- `conversionRate` = `totalSales / (totalSales + Σ StockCount)`, round 4 chữ số.

### GET `/api/reports/sales-by-region`

Tham số: `from`, `to`. Nhóm `SalesSummaries` theo `Region` (bỏ record
Region rỗng), sắp xếp giảm dần theo `revenue`:

```json
[
  { "region": "Miền Bắc", "sales": 12, "revenue": 3200000000 },
  { "region": "Miền Nam", "sales": 9,  "revenue": 2450000000 }
]
```

### GET `/api/reports/sales-proportion`

Cùng query, thêm phần trăm (round 1 chữ số), sắp xếp giảm dần theo `sales`:

```json
[
  {
    "region": "Miền Bắc", "sales": 12, "revenue": 3200000000,
    "salesPercentage": 57.1, "revenuePercentage": 56.6
  }
]
```

### GET `/api/reports/top-vehicles`

Tham số: `limit` (mặc định 10, `limit<=0` cũng lấy 10). Nhóm
`InventorySummaries` theo `VehicleName`, sắp xếp giảm dần `stockCount`.
`revenue`/`estimatedRevenue` = `stockCount × avgRevenuePerOrder`
(ước tính từ tổng revenue / tổng order của SalesSummaries):

```json
[
  {
    "model": "VinFast VF 8",
    "stockCount": 40,
    "sales": 40,
    "revenue": 60000000000,
    "estimatedRevenue": 60000000000
  }
]
```

> `sales` = `stockCount` và `revenue` = `estimatedRevenue` — 2 field dư để
> frontend cũ không vỡ (giải thích trong code).

---

## 3. Báo cáo nghiệp vụ (envelope)

### GET `/api/reports/sales-by-dealer`

Tham số: `dealerId` (int), `period` (`day|month|year`, mặc định `month`),
`fromDate`, `toDate` (DateTime).

- Có `dealerId` → `{success, data}` với **một** `DealerSalesReportDto`.
- Không có → `{success, data: [...]}` tổng hợp **10 đại lý** (lỗi từng đại lý
  bị nuốt, bỏ qua dòng lỗi):

```json
{
  "success": true,
  "data": {
    "dealerId": 1,
    "dealerName": "Dealer Hà Nội",
    "period": "month",
    "fromDate": "2025-01-01T00:00:00",
    "toDate": "2025-12-31T00:00:00",
    "totalVehiclesSold": 12,
    "totalRevenue": 3200000000,
    "salesByPeriod": [
      { "periodLabel": "2025-04", "periodDate": "2025-04-01T00:00:00",
        "vehiclesSold": 5, "revenue": 1500000000 }
    ]
  }
}
```

### GET `/api/reports/debt-report`

Tham số: `dealerId` (int) — **tham số duy nhất có hiệu lực**; `from`/`to`
được nhận nhưng handler không dùng (xem README known issues).

```json
{
  "success": true,
  "data": {
    "dealerId": 1,
    "dealerName": "Dealer Hà Nội",
    "reportDate": "2026-10-02T00:00:00",
    "debtToManufacturer": 4500000000,
    "debtToManufacturerDetails": [
      { "orderId": 101, "orderNumber": "ORD-101", "orderDate": "2025-06-01T00:00:00",
        "orderAmount": 1500000000, "paidAmount": 500000000,
        "remainingDebt": 1000000000, "status": "Partial" }
    ],
    "debtFromCustomers": 900000000,
    "debtFromCustomerDetails": [
      { "orderId": 202, "orderNumber": "ORD-202", "customerId": 5,
        "customerName": "Nguyễn Văn B", "orderDate": "2025-07-01T00:00:00",
        "totalAmount": 900000000, "paidAmount": 0, "remainingDebt": 900000000,
        "loanTermMonths": 36, "monthlyPayment": 25000000, "status": "Outstanding" }
    ],
    "totalDebt": 5400000000
  }
}
```

> `debtToManufacturer` đang giả định mọi order = đại lý mua từ hãng
> (README known issue 5).

### GET `/api/reports/debt-summary`

Tham số: `dealerId`, `customerId` (int), `debtType`
(`DealerToManufacturer|CustomerToDealer`), `status`
(`Outstanding|Paid|Overdue`), `from`, `to` (filter `CreatedAt`).

```json
{
  "success": true,
  "count": 1,
  "data": [
    {
      "id": "3f2b…-guid", "dealerId": 1, "dealerName": "Dealer Hà Nội",
      "customerId": null, "customerName": null,
      "debtType": "DealerToManufacturer", "referenceType": "Order",
      "referenceId": "101",
      "totalAmount": 1500000000, "outstandingAmount": 1000000000,
      "status": "Outstanding", "dueDate": null,
      "createdAt": "2025-06-01T00:00:00", "lastUpdatedAt": "2025-06-01T00:00:00"
    }
  ]
}
```

### GET `/api/reports/inventory-trends`

Không tham số. Tính từ `InventorySummaries`:

```json
{
  "success": true,
  "data": {
    "reportDate": "2026-10-02T00:00:00",
    "inventoryTurnover": [
      { "vehicleId": 1, "vehicleName": "VinFast VF 8", "dealerId": 1,
        "dealerName": "Dealer Hà Nội", "region": "Miền Bắc",
        "currentStock": 10, "averageMonthlySales": 3,
        "turnoverRate": 3.6, "daysInStock": 30, "status": "healthy" }
    ],
    "slowMovingInventory": [
      { "vehicleId": 2, "vehicleName": "VinFast VF 9", "dealerId": 2,
        "dealerName": "Dealer Huế", "region": "Miền Trung",
        "stockCount": 8, "daysInStock": 120, "firstStockDate": "2025-06-01T00:00:00",
        "alertLevel": "warning", "recommendation": "Khuyến mãi / giảm nhập" }
    ]
  }
}
```

### GET `/api/reports/sales-by-staff`

Tham số: `from`, `to`. Trả `{success, data: SalesByStaffDto[]}` —
`{salespersonId, salespersonName, role, totalQuotes, totalOrders,
totalContracts, totalDeals, totalVehiclesSold, totalRevenue, conversionRate}`.

### GET `/api/reports/demand-forecast` (raw)

Tham số: `from`, `to` (string, `DateTime.TryParse` — parse lỗi thì bỏ filter).
Linear regression trên lịch sử; lỗi → 500 `{success:false, error, details}`.

```json
{
  "title": "Dự báo nhu cầu",
  "description": "…",
  "generatedAt": "2026-10-02T03:00:00Z",
  "forecastData": [
    { "period": "2026-11", "forecastedValue": 42.5,
      "confidenceLowerBound": 31.2, "confidenceUpperBound": 53.8 }
  ],
  "summary": {
    "nextPeriodForecast": 42.5,
    "trendDirection": "Increasing",
    "trendStrength": 8.4
  }
}
```

---

## 4. CRUD SalesSummary / InventorySummary

### Model SalesSummary (`Models/SalesSummary.cs`)

| Field | Type | Ghi chú |
|---|---|---|
| `id` | Guid | server tạo (`Guid.NewGuid()`) khi POST |
| `date` | DateTime | |
| `dealerId` | **int** | không nhận GUID |
| `dealerName` | string (required) | không được whitespace |
| `region` | string (required) | xem quy tắc bên dưới |
| `salespersonId` | **int** | |
| `salespersonName` | string (required) | không được whitespace |
| `totalOrders` | int | |
| `totalRevenue` | decimal | |
| `lastUpdatedAt` | DateTime | server set `UtcNow` khi POST |

### Model InventorySummary (`Models/InventorySummary.cs`)

`id` Guid · `vehicleId` **int** (required) · `vehicleName` string required ·
`dealerId` **int** (required) · `dealerName` string required ·
`region` string required · `stockCount` int · `lastUpdatedAt` DateTime.

### GET `/api/reports/sales-summary`

Tham số: `fromDate`, `toDate` (DateTime), `dealerId` (**int**).
Sắp xếp giảm dần theo `Date` → `{success, count, data[]}`.

### GET `/api/reports/sales-summary/{id}`

`id` là **Guid** (khác `dealerId` int). Không có → **404**
`{"message": "Sales summary not found"}`; có → `{success, data}`.

### POST `/api/reports/sales-summary`

```json
{
  "date": "2025-04-01T00:00:00Z",
  "dealerId": 1,
  "dealerName": "Dealer Hà Nội",
  "region": "Miền Bắc",
  "salespersonId": 2,
  "salespersonName": "Nguyễn Văn A",
  "totalOrders": 5,
  "totalRevenue": 1500000000
}
```

→ **201** `Location: /api/reports/sales-summary/{guid}` +

```json
{ "success": true, "data": { "id": "e8bc687a-…", "dealerId": 1, "…": "…" } }
```

**Validate (đã verify live 2026-10-02):**

| Điều kiện | Kết quả |
|---|---|
| Không JWT | **401** |
| `dealerId` là GUID string | **400** (model binding fail, body rỗng) |
| Thiếu property `required` | **400** |
| `dealerName`/`salespersonName` whitespace | **400** `{"message":"DealerName and SalespersonName are required"}` |
| `region` rỗng/thiếu | **400** `{"message":"Region is required (Miền Bắc, Miền Trung, or Miền Nam)"}` |
| `region` = giá trị lạ, ví dụ `"X"` | **201** — message gợi ý 3 vùng nhưng code **không validate enum** (README known issue 2) |
| Body đúng (int ID + UTF-8) | **201** |

### GET `/api/reports/inventory-summary`

Tham số: `dealerId`, `vehicleId` (**cả int**). Giảm dần `LastUpdatedAt` →
`{success, count, data[]}`. `GET /{id}` (Guid) → 404
`{"message": "Inventory summary not found"}` khi không thấy.

### POST `/api/reports/inventory-summary`

```json
{
  "vehicleId": 1,
  "vehicleName": "VinFast VF 8",
  "dealerId": 1,
  "dealerName": "Dealer Hà Nội",
  "region": "Miền Bắc",
  "stockCount": 10
}
```

→ **201** `{success, data}`. Validate như sales-summary, khác:
`VehicleName and SalespersonName` → đúng message là
`{"message":"VehicleName and DealerName are required"}` cho 2 fieldname đó.

---

## 5. Export & đồng bộ

### POST `/api/reports/export`

Body:

```json
{ "type": "sales", "from": "2025-01-01", "to": "2025-12-31" }
```

- `type`: `sales` → CSV doanh số; `inventory` → CSV tồn kho; giá trị khác →
  gộp cả hai. `format` trong body **không ảnh hưởng** — luôn CSV.
- Response: **file download** `text/csv` (UTF-8 **có BOM**, mở đúng tiếng Việt
  trong Excel), tên file `report-*.csv`. Request được lưu vào
  `ReportRequests` + `ReportExports` để lịch sử.
- Lỗi → 500 `{success:false, error, details}`.

### POST `/api/reports/synchronize-data`

Không body. Kéo dữ liệu từ Sales/Vehicle/Customer/User Service qua HTTP
(cấu hình `Services:*` trong `appsettings.json`) →
`{"success": true, "message": "Data synchronization initiated successfully."}`
; lỗi → 500 `{success:false, error, details}`.

---

## 6. Route không auth

| Route | Ghi chú |
|---|---|
| `GET /weatherforecast` | template mẫu minimal API |
| `GET /health` | health check — **503 khi DB không kết nối được** (xem README) |
