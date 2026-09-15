# Reporting Service - Implementation Summary

## Tổng quan Implementation

Đã hoàn thành việc implement 5 loại báo cáo theo yêu cầu, sử dụng dữ liệu thực từ các services và tích hợp Apache NiFi để đồng bộ dữ liệu.

## Các thành phần đã tạo

### 1. DTOs và Models
- `ReportDtos.cs`: Chứa tất cả DTOs cho các báo cáo
  - `DealerSalesReportDto`: Báo cáo doanh số đại lý
  - `DealerDebtReportDto`: Báo cáo công nợ đại lý
  - `TotalSalesDashboardDto`: Dashboard doanh số tổng với heatmap
  - `InventoryAnalysisDto`: Phân tích tồn kho và turnover
  - Các DTOs hỗ trợ khác

### 2. Services
- `ISalesDataService` / `SalesDataService`: Fetch dữ liệu từ SalesService
- `IVehicleDataService` / `VehicleDataService`: Fetch dữ liệu từ VehicleService
- `IReportService` / `ReportService`: Business logic để tạo các báo cáo

### 3. Endpoints (đối chiếu Program.cs hiện tại)
- `GET /api/reports/sales-by-dealer`: Báo cáo doanh số đại lý (params: `dealerId`, `period`, `fromDate`, `toDate`)
- `GET /api/reports/debt-report`: Báo cáo công nợ đại lý (params: `dealerId`)
- `GET /api/reports/inventory-trends`: Phân tích tồn kho / inventory turnover
- `GET /api/reports/sales-by-region` + `GET /api/reports/sales-proportion` + `GET /api/reports/summary` + `GET /api/reports/top-vehicles`: các endpoint mà dashboard doanh số tổng thực dùng (method `GetTotalSalesDashboardAsync` có trong `ReportService` nhưng chưa được map thành route)
- `GET /api/reports/debt-summary`, `GET /api/reports/sales-by-staff`, `POST /api/reports/export`, `POST /api/reports/synchronize-data`, `GET/POST /api/reports/sales-summary`, `GET/POST /api/reports/inventory-summary`
- `GET /api/reports/demand-forecast`: AI dự báo nhu cầu

### 4. Cập nhật Services khác
- **SalesService**: `GET /api/Orders` và `GET /api/payments` — ReportingService gửi kèm query params `fromDate`/`toDate`/`dealerId`/`orderId` khi gọi, nhưng controller hiện chưa nhận filter (signature không có tham số, trả toàn bộ dữ liệu)
- **APIGatewayService**: Route rewrite `localhost:5208` → `reportingservice:80` (khai báo trong docker-compose.yml)

### 5. Apache NiFi Integration
- `nifi-flow.json`: Template flow cho NiFi
- `NIFI_INTEGRATION.md`: Hướng dẫn chi tiết về cấu hình và sử dụng

### 6. Documentation
- `REPORTING_REQUIREMENTS.md`: Chi tiết về các báo cáo và cách sử dụng
- `NIFI_INTEGRATION.md`: Hướng dẫn tích hợp Apache NiFi
- `README_IMPLEMENTATION.md`: File này - tóm tắt implementation

## Cấu trúc dữ liệu

### Nguồn dữ liệu
1. **SalesService** (Port 5003)
   - Orders: Thông tin đơn hàng
   - Payments: Thông tin thanh toán

2. **VehicleService** (Port 5068)
   - Vehicles: Thông tin xe và tồn kho
   - Dealers: Thông tin đại lý

3. **ReportingService Database**
   - SalesSummaries: Dữ liệu tổng hợp doanh số (sync từ SalesService)
   - InventorySummaries: Dữ liệu tổng hợp tồn kho (sync từ VehicleService)

### Luồng dữ liệu
```
SalesService/VehicleService
    ↓ (HTTP API calls)
ReportingService (Real-time queries)
    ↓
Report Generation
    ↓
Response to Client

Hoặc:

SalesService/VehicleService
    ↓ (Apache NiFi sync - scheduled)
ReportingService Database (SalesSummaries, InventorySummaries)
    ↓ (Query from database)
Report Generation
    ↓
Response to Client
```

## Cách sử dụng

### 1. Khởi động services
```bash
# Khởi động tất cả services
cd ev-dealer-management
.\start-all-services.ps1
```

### 2. Test endpoints
```bash
# Báo cáo doanh số đại lý
curl "http://localhost:5208/api/reports/sales-by-dealer?dealerId=1&period=month"

# Báo cáo công nợ
curl "http://localhost:5208/api/reports/debt-report?dealerId=1"

# Doanh số theo vùng (dùng cho dashboard)
curl "http://localhost:5208/api/reports/sales-by-region"
curl "http://localhost:5208/api/reports/sales-proportion"

# Phân tích tồn kho
curl "http://localhost:5208/api/reports/inventory-trends"

# AI dự báo
curl "http://localhost:5208/api/reports/demand-forecast"
```

### 3. Cấu hình Apache NiFi (Optional)
Xem file `NIFI_INTEGRATION.md` để biết cách cấu hình NiFi flows để tự động sync dữ liệu.

## Lưu ý quan trọng

### 1. OrderId Type Mismatch — ĐÃ XỬ LÝ (Issue #37)
- `Order.OrderId` là `int`; `Payment.OrderId` giờ cũng là `int` FK thật (migration `PaymentOrderLinkToIntFK` trong SalesService — trước đây là `Guid` không ràng buộc gì, link thật nằm ở shadow column `OrderId1` chưa bao giờ được populate)
- `SalesDataService.GetPaymentsAsync` parse `orderId` bằng `GetInt32()` — payload int trên wire khớp trực tiếp, không còn JsonException bị swallow thành rỗng
- Không cần mapping table

### 2. Customer Name — ĐÃ XỬ LÝ
- `ReportService.GetDealerDebtReportAsync` fetch danh sách khách hàng qua `CustomerDataService` và dựng `customerNameMap` (`allCustomers.ToDictionary(c => c.Id, c => c.Name)`)
- `CustomerName = customerNameMap.GetValueOrDefault(o.CustomerId, $"Khách hàng {o.CustomerId}")` — tên thật từ CustomerService, chỉ fallback khi không tra được

### 3. Dealer Purchase Orders
- Logic tính "debt to manufacturer" hiện tại giả định tất cả orders là dealer purchases
- **Cần fix**: Tạo bảng riêng cho dealer purchases từ manufacturer

### 4. Region Mapping — ĐÃ XỬ LÝ
- Dealer model của VehicleService đã có field `Region` (`VehicleService/Models/Dealer.cs`), DealerDto cũng expose `Region`
- `DataSynchronizationService` dựng `dealerRegionMap` từ `dealer.Region` thật khi sync SalesSummaries/InventorySummaries; `ReportService.GetInventoryAnalysisAsync` dùng `dealer?.Region ?? "Unknown"`; `VehicleDataService` parse thuộc tính `region` từ JSON
- `EnsureRegionDataAsync` trong Program.cs chỉ còn là backfill fallback cho record cũ chưa có Region (map theo tên dealer hardcoded)

## Performance Considerations

1. **Caching**: Có thể cache kết quả báo cáo để giảm load
2. **Pagination**: Các báo cáo lớn nên có pagination
3. **Async Processing**: Các báo cáo phức tạp có thể xử lý async
4. **Database Indexing**: Đảm bảo indexes trên các fields thường query

## Testing Checklist

- [x] Tạo DTOs và Models
- [x] Implement data services
- [x] Implement report services
- [x] Tạo endpoints
- [x] Update API Gateway
- [x] Tạo NiFi configuration
- [x] Tạo documentation
- [ ] Unit tests
- [ ] Integration tests
- [ ] Load testing
- [ ] End-to-end testing với frontend

## Next Steps

1. Fix các issues đã nêu ở phần "Lưu ý quan trọng"
2. Thêm unit tests và integration tests
3. Implement caching cho các báo cáo
4. Thêm export functionality (PDF/Excel)
5. Tạo frontend components để hiển thị báo cáo
6. Setup monitoring và alerting

## Support

Nếu có vấn đề, xem:
- `REPORTING_REQUIREMENTS.md`: Chi tiết về các báo cáo
- `NIFI_INTEGRATION.md`: Hướng dẫn NiFi
- Logs trong `ReportingService` để debug


