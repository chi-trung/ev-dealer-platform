# Báo Cáo Reporting Service - Yêu Cầu và Implementation

## Tổng quan

ReportingService đã được mở rộng để hỗ trợ 5 loại báo cáo chính sử dụng dữ liệu thực từ các services khác trong hệ thống, được đồng bộ thông qua Apache NiFi.

## Các Báo Cáo Đã Implement

### 1. Báo cáo Doanh số (Dealer Portal) ✅

**Endpoint**: `GET /api/reports/sales-by-dealer`

**Mô tả**: Giúp Giám đốc đại lý xem doanh thu thực tế, số lượng xe bán ra theo ngày/tháng/năm của riêng đại lý đó.

**Query Parameters** (theo Program.cs):
- `dealerId` (optional): ID của đại lý (int). Không truyền → trả tổng hợp cho 10 đại lý đầu tiên
- `period` (optional): "day", "month", hoặc "year" (default: "month")
- `fromDate` (optional): Ngày bắt đầu (DateTime)
- `toDate` (optional): Ngày kết thúc (DateTime)

**Ví dụ**:
```bash
GET /api/reports/sales-by-dealer?dealerId=1&period=month&fromDate=2025-01-01&toDate=2025-01-31
```

**Response**:
```json
{
  "success": true,
  "data": {
    "dealerId": 1,
    "dealerName": "Dealer Hà Nội",
    "period": "month",
    "fromDate": "2025-01-01T00:00:00Z",
    "toDate": "2025-01-31T23:59:59Z",
    "totalVehiclesSold": 25,
    "totalRevenue": 37500000000,
    "salesByPeriod": [
      {
        "periodLabel": "2025-01",
        "periodDate": "2025-01-01T00:00:00Z",
        "vehiclesSold": 25,
        "revenue": 37500000000
      }
    ]
  }
}
```

**Nguồn dữ liệu**: SalesService (Orders)

---

### 2. Báo cáo Công nợ (Dealer Portal) ✅

**Endpoint**: `GET /api/reports/debt-report`

**Mô tả**: Theo dõi số tiền đại lý còn nợ hãng (công nợ mua xe) hoặc khách hàng còn nợ đại lý (trả góp).

**Query Parameters**:
- `dealerId` (optional): ID của đại lý (int). Không truyền → báo cáo tổng hợp tất cả đại lý

**Ví dụ**:
```bash
GET /api/reports/debt-report?dealerId=1
```

**Response**:
```json
{
  "success": true,
  "data": {
    "dealerId": 1,
    "dealerName": "Dealer Hà Nội",
    "reportDate": "2025-01-15T10:30:00Z",
    "debtToManufacturer": 5000000000,
    "debtToManufacturerDetails": [
      {
        "orderId": 1,
        "orderNumber": "ORD-20250115-12345",
        "orderDate": "2025-01-10T00:00:00Z",
        "orderAmount": 1500000000,
        "paidAmount": 1000000000,
        "remainingDebt": 5000000000,
        "status": "Pending"
      }
    ],
    "debtFromCustomers": 2000000000,
    "debtFromCustomerDetails": [
      {
        "orderId": 2,
        "orderNumber": "ORD-20250112-67890",
        "customerId": 1,
        "customerName": "Nguyễn Văn A",
        "orderDate": "2025-01-12T00:00:00Z",
        "totalAmount": 2000000000,
        "paidAmount": 500000000,
        "remainingDebt": 1500000000,
        "loanTermMonths": 24,
        "monthlyPayment": 62500000,
        "status": "Installment"
      }
    ],
    "totalDebt": 3000000000
  }
}
```

**Nguồn dữ liệu**: SalesService (Orders, Payments), CustomerService (tên khách hàng qua `customerNameMap`)

---

### 3. Dashboard Doanh số tổng (EVM Portal) ⚠️ (chưa có endpoint HTTP)

**Trạng thái**: `GetTotalSalesDashboardAsync` đã có trong `ReportService` (trả `TotalSalesDashboardDto` với heatmap), nhưng **chưa được map thành route** trong Program.cs. Frontend dashboard hiện dựng từ các endpoint thật: `GET /api/reports/summary`, `GET /api/reports/sales-by-region`, `GET /api/reports/sales-proportion`, `GET /api/reports/top-vehicles`.

**Mô tả**: Business Intelligence (BI): Hiển thị bản đồ nhiệt (Heatmap) doanh số theo vùng miền để Hãng thấy khu vực nào bán tốt/kém.

**Ví dụ** (các route hiện có):
```bash
GET /api/reports/sales-by-region?from=2025-01-01&to=2025-01-31
GET /api/reports/sales-proportion?from=2025-01-01&to=2025-01-31
```

**Response**:
```json
{
  "success": true,
  "data": {
    "reportDate": "2025-01-15T10:30:00Z",
    "fromDate": "2025-01-01T00:00:00Z",
    "toDate": "2025-01-31T23:59:59Z",
    "totalVehiclesSold": 150,
    "totalRevenue": 225000000000,
    "salesByRegion": [
      {
        "region": "Miền Bắc",
        "vehiclesSold": 60,
        "revenue": 90000000000,
        "dealerCount": 3,
        "revenuePercentage": 40.0
      },
      {
        "region": "Miền Nam",
        "vehiclesSold": 50,
        "revenue": 75000000000,
        "dealerCount": 2,
        "revenuePercentage": 33.3
      },
      {
        "region": "Miền Trung",
        "vehiclesSold": 40,
        "revenue": 60000000000,
        "dealerCount": 2,
        "revenuePercentage": 26.7
      }
    ],
    "heatmapData": [
      {
        "region": "Miền Bắc",
        "dealerName": "Dealer Hà Nội",
        "dealerId": 1,
        "vehiclesSold": 30,
        "revenue": 45000000000,
        "heatLevel": "high"
      }
    ]
  }
}
```

**Nguồn dữ liệu**: ReportingService Database (SalesSummaries — được sync từ SalesService bởi `DataSynchronizationService`; NiFi chỉ là phương án tuỳ chọn)

---

### 4. Phân tích Tồn kho & Tốc độ tiêu thụ ✅

**Endpoint**: `GET /api/reports/inventory-trends`

**Mô tả**: Tính toán chỉ số Inventory Turnover. Báo cáo này cảnh báo những mẫu xe "nằm kho" quá lâu để hãng ra quyết định ngưng sản xuất hoặc giảm giá xả hàng.

**Query Parameters**: không có

**Ví dụ**:
```bash
GET /api/reports/inventory-trends
```

**Response**:
```json
{
  "success": true,
  "data": {
    "reportDate": "2025-01-15T10:30:00Z",
    "inventoryTurnover": [
      {
        "vehicleId": 1,
        "vehicleName": "Tesla Model 3",
        "dealerId": 1,
        "dealerName": "Dealer Hà Nội",
        "region": "Miền Bắc",
        "currentStock": 10,
        "averageMonthlySales": 5,
        "turnoverRate": 6.0,
        "daysInStock": 60,
        "status": "healthy"
      },
      {
        "vehicleId": 2,
        "vehicleName": "VinFast VF8",
        "dealerId": 1,
        "dealerName": "Dealer Hà Nội",
        "region": "Miền Bắc",
        "currentStock": 20,
        "averageMonthlySales": 2,
        "turnoverRate": 1.2,
        "daysInStock": 300,
        "status": "critical"
      }
    ],
    "slowMovingInventory": [
      {
        "vehicleId": 2,
        "vehicleName": "VinFast VF8",
        "dealerId": 1,
        "dealerName": "Dealer Hà Nội",
        "region": "Miền Bắc",
        "stockCount": 20,
        "daysInStock": 300,
        "firstStockDate": "2024-03-20T00:00:00Z",
        "alertLevel": "critical",
        "recommendation": "Ngưng sản xuất hoặc giảm giá xả hàng"
      }
    ]
  }
}
```

**Nguồn dữ liệu**: VehicleService (Vehicles/Inventory), SalesService (Orders để tính turnover)

---

### 5. AI Dự báo nhu cầu ✅

**Endpoint**: `GET /api/reports/demand-forecast`

**Mô tả**: Predictive Report (Báo cáo dự báo). Service này sử dụng thuật toán để dự đoán tháng sau cần sản xuất bao nhiêu xe.

**Query Parameters**:
- `from` (optional): Ngày bắt đầu dữ liệu lịch sử
- `to` (optional): Ngày kết thúc dữ liệu lịch sử

**Ví dụ**:
```bash
GET /api/reports/demand-forecast?from=2024-01-01&to=2025-01-31
```

**Response**:
```json
{
  "title": "Dự báo nhu cầu bán hàng",
  "description": "Dự báo cho 3 tháng tới dựa trên dữ liệu bán hàng lịch sử.",
  "generatedAt": "2025-01-15T10:30:00Z",
  "forecastData": [
    {
      "period": "2025-02",
      "forecastedValue": 55,
      "confidenceLowerBound": 47,
      "confidenceUpperBound": 63
    },
    {
      "period": "2025-03",
      "forecastedValue": 58,
      "confidenceLowerBound": 49,
      "confidenceUpperBound": 67
    },
    {
      "period": "2025-04",
      "forecastedValue": 61,
      "confidenceLowerBound": 52,
      "confidenceUpperBound": 70
    }
  ],
  "summary": {
    "nextPeriodForecast": 55,
    "trendDirection": "Tăng trưởng",
    "trendStrength": 2.5
  }
}
```

**Nguồn dữ liệu**: ReportingService Database (SalesSummaries — được sync từ SalesService bởi `DataSynchronizationService`; NiFi chỉ là phương án tuỳ chọn)

**Thuật toán**: Linear Regression dựa trên dữ liệu bán hàng theo tháng

---

## Apache NiFi Integration

Dữ liệu được đồng bộ tự động từ các services vào ReportingService thông qua Apache NiFi flows:

1. **Sales Data Sync**: Đồng bộ Orders từ SalesService → SalesSummaries trong ReportingService
2. **Inventory Data Sync**: Đồng bộ Vehicles từ VehicleService → InventorySummaries trong ReportingService

Xem chi tiết trong file `NIFI_INTEGRATION.md`

---

## Testing

### Test với curl:

```bash
# 1. Báo cáo Doanh số
curl "http://localhost:5208/api/reports/sales-by-dealer?dealerId=1&period=month"

# 2. Báo cáo Công nợ
curl "http://localhost:5208/api/reports/debt-report?dealerId=1"

# 3. Doanh số theo vùng (dashboard)
curl "http://localhost:5208/api/reports/sales-by-region?from=2025-01-01&to=2025-01-31"

# 4. Phân tích Tồn kho
curl "http://localhost:5208/api/reports/inventory-trends"

# 5. AI Dự báo
curl "http://localhost:5208/api/reports/demand-forecast"
```

### Test qua API Gateway:

```bash
# Tất cả requests qua API Gateway (port 5036)
curl "http://localhost:5036/api/reports/sales-by-dealer?dealerId=1&period=month"
```

---

## Lưu ý

1. **Dữ liệu thực**: Tất cả báo cáo sử dụng dữ liệu thực từ các services, không phải mock data
2. **Đồng bộ dữ liệu**: Dữ liệu được sync qua Apache NiFi, có thể có độ trễ nhỏ
3. **Performance**: Các báo cáo phức tạp có thể mất vài giây để tính toán
4. **Error Handling**: Nếu service nguồn không khả dụng, báo cáo sẽ trả về dữ liệu rỗng hoặc lỗi

---

## Cải tiến tương lai

- [ ] Cache kết quả báo cáo để tăng performance
- [ ] Thêm pagination cho các báo cáo lớn
- [ ] Export báo cáo ra PDF/Excel
- [ ] Real-time updates qua WebSocket
- [ ] Advanced forecasting algorithms (ARIMA, Prophet)
- [ ] Dashboard visualization với charts


