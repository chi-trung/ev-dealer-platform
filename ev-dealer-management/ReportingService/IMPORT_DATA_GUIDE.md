# Hướng dẫn Import dữ liệu qua Postman

Hướng dẫn này giúp bạn **import dữ liệu đa dạng** vào ReportingService qua Postman, để từ đó hiển thị lên web frontend.

---

## 1. Chuẩn bị

### Bước 1.1: Khởi động Service

```powershell
cd D:\gitclone\ev-dealer-management\ev-dealer-management\ReportingService
# SQLite là mặc định (DB_PROVIDER=sqlite) — không cần set gì thêm
dotnet run
```

Chờ: `Now listening on: http://localhost:5208`

> 🔁 **Lần đầu chạy?** Hãy đảm bảo schema mới nhất đã được áp dụng:  
> `dotnet ef database update --context ReportingDbContext`

### Bước 1.2: Mở Postman

- Tải: https://www.postman.com/downloads/
- Đăng nhập / Tạo workspace (nếu chưa có)

---

## 2. Import dữ liệu Sales-Summary

### Bộ dữ liệu mẫu đa vùng (gợi ý)

**Endpoint:** `POST http://localhost:5208/api/reports/sales-summary`

**Header:** `Content-Type: application/json`

Nhập tối thiểu 2–3 bản ghi cho **mỗi vùng** để biểu đồ hiển thị cân bằng. Bộ mẫu mới dưới đây bao phủ ba vùng với doanh số đa dạng tháng 01–03/2025:

| Dealer          | Vùng       | Ngày        | Đơn | Doanh thu (VNĐ) | Salesperson |
|-----------------|------------|-------------|-----|------------------|-------------|
| Dealer Hà Nội   | Miền Bắc   | 2025-01-05  | 6   | 1 800 000 000    | Nguyễn Văn A |
| Dealer Hà Nội   | Miền Bắc   | 2025-02-14  | 9   | 2 700 000 000    | Trần Thị B   |
| Dealer Hà Nội   | Miền Bắc   | 2025-03-02  | 8   | 2 560 000 000    | Lý Quốc C    |
| Dealer TP.HCM   | Miền Nam   | 2025-01-12  | 11  | 3 520 000 000    | Lê Văn C     |
| Dealer TP.HCM   | Miền Nam   | 2025-02-18  | 7   | 2 240 000 000    | Phạm Thị D   |
| Dealer TP.HCM   | Miền Nam   | 2025-03-08  | 9   | 2 970 000 000    | Đỗ Minh E    |
| Dealer Đà Nẵng  | Miền Trung | 2025-01-20  | 5   | 1 400 000 000    | Hoàng Văn E  |
| Dealer Đà Nẵng  | Miền Trung | 2025-02-10  | 6   | 1 740 000 000    | Võ Thu F     |
| Dealer Đà Nẵng  | Miền Trung | 2025-03-05  | 4   | 1 160 000 000    | Nguyễn Hà G  |

Ví dụ JSON (copy từng bản ghi):

```json
{
  "date": "2025-02-14T00:00:00Z",
  "dealerId": "a1b2c3d4-e5f6-4a5b-8c9d-1e2f3a4b5c6d",
  "dealerName": "Dealer Hà Nội",
  "region": "Miền Bắc",
  "salespersonId": "11111111-2222-3333-4444-555555555552",
  "salespersonName": "Trần Thị B",
  "totalOrders": 9,
  "totalRevenue": 2700000000
}
```

> ❗ **Bắt buộc:** trường `region` phải đúng một trong `Miền Bắc`, `Miền Trung`, `Miền Nam`.

### Phương pháp B: Collection (khuyên dùng)

---

### Phương pháp B: Import nhanh bằng Collection (Khuyến nghị)

**Bước 1: Tạo Collection trong Postman**

- Cửa sổ trái → **Collections** → **+ (New Collection)**
- Tên: `ReportingService - Data Import`
- Click **Create**

**Bước 2: Thêm các request**

1. Click collection vừa tạo
2. **Add request** (dấu +)
3. Đặt tên: `POST Sales-Summary #1`
4. Method: **POST**
5. URL: `http://localhost:5208/api/reports/sales-summary`
6. Tab **Body** → **raw** → **JSON** → paste JSON từ Request 1 ở trên
7. Click **Save**
8. Lặp lại với các Request 2-6

**Bước 3: Chạy toàn bộ Collection (Runner)**

1. Click collection → **...** (Menu) → **Run collection**
2. Cửa sổ Collection Runner mở → **Run** button
3. Xem từng request được thực thi tự động
4. Kết quả: tất cả trả 201 Created = import thành công

---

## 3. Import dữ liệu Inventory-Summary

Để biểu đồ “Top vehicles” + “Inventory overview” sinh động hơn, mỗi vùng nên có ít nhất 2 mẫu xe.

| Dealer         | Vùng       | Xe              | Stock |
|----------------|-----------|-----------------|-------|
| Dealer Hà Nội  | Miền Bắc  | Tesla Model 3   | 18    |
| Dealer Hà Nội  | Miền Bắc  | VinFast VF9     | 12    |
| Dealer TP.HCM  | Miền Nam  | Audi e-tron     | 14    |
| Dealer TP.HCM  | Miền Nam  | Mercedes EQE    | 9     |
| Dealer Đà Nẵng | Miền Trung| Porsche Taycan  | 7     |
| Dealer Đà Nẵng | Miền Trung| Hyundai Ioniq 5 | 11    |

Ví dụ JSON:

```json
{
  "vehicleId": "v9991111-1111-1111-1111-111111111111",
  "vehicleName": "VinFast VF9",
  "dealerId": "a1b2c3d4-e5f6-4a5b-8c9d-1e2f3a4b5c6d",
  "dealerName": "Dealer Hà Nội",
  "region": "Miền Bắc",
  "stockCount": 12
}
```

Lặp lại cho các dòng khác (chú ý thay `vehicleId` mới để tránh trùng).***

---

## 4. Kiểm tra dữ liệu đã import

**GET toàn bộ Sales-Summary:**

```
GET http://localhost:5208/api/reports/sales-summary
```

Kết quả: danh sách 6 record đã import

**GET toàn bộ Inventory-Summary:**

```
GET http://localhost:5208/api/reports/inventory-summary
```

Kết quả: danh sách 5 record đã import

**Filter dữ liệu (ví dụ):**

```
GET http://localhost:5208/api/reports/sales-summary?dealerId=a1b2c3d4-e5f6-4a5b-8c9d-1e2f3a4b5c6d
```

Kết quả: 2 record cho Dealer Hà Nội

---

## 5. Tips & Tối ưu

### Sử dụng Environment Variables trong Postman

Nếu muốn thay đổi URL dễ dàng (localhost vs production):

1. **New Environment** → đặt tên `Local Dev`
2. Add biến:
   - `baseUrl`: `http://localhost:5208`
   - `dealerIdHN`: `a1b2c3d4-e5f6-4a5b-8c9d-1e2f3a4b5c6d`
   - `dealerIdHCM`: `b2c3d4e5-f6a7-4b5c-9d0e-2f3a4b5c6d7e`
3. Trong request, dùng: `{{baseUrl}}/api/reports/sales-summary`

### Sử dụng Pre-request Script để sinh ID tự động

Nếu muốn mỗi request có ID unique (tránh trùng):

```javascript
// Pre-request Script tab
pm.environment.set("dealerId", pm.utils.v4());
pm.environment.set("salespersonId", pm.utils.v4());
```

Sau đó body dùng:

```json
{
  "dealerId": "{{dealerId}}",
  "salespersonId": "{{salespersonId}}",
  ...
}
```

### Tạo Test để xác nhận import thành công

Trong tab **Tests** của request:

```javascript
pm.test("Status is 201 Created", function () {
  pm.response.to.have.status(201);
});

pm.test("Response contains ID", function () {
  var jsonData = pm.response.json();
  pm.expect(jsonData.success).to.be.true;
  pm.expect(jsonData.data.id).to.exist;
});
```

---

## 6. Export & Share Collection

Sau khi tạo Collection với tất cả request:

1. Click collection → **...** → **Export**
2. Chọn **Collection v2.1**
3. Lưu file `.json`
4. Chia sẻ với team hoặc version control

**Import Collection được lưu:**

- Postman → **Import** → chọn file `.json`

---

## 7. Tạo Postman Environment cho Multiple Dealers

**Tạo file JSON này và import vào Postman:**

```json
{
  "name": "ReportingService Dealers",
  "values": [
    {
      "key": "baseUrl",
      "value": "http://localhost:5208",
      "enabled": true
    },
    {
      "key": "dealerHN",
      "value": "a1b2c3d4-e5f6-4a5b-8c9d-1e2f3a4b5c6d",
      "enabled": true
    },
    {
      "key": "dealerHCM",
      "value": "b2c3d4e5-f6a7-4b5c-9d0e-2f3a4b5c6d7e",
      "enabled": true
    },
    {
      "key": "dealerDN",
      "value": "c3d4e5f6-a7b8-4c5d-0e1f-3a4b5c6d7e8f",
      "enabled": true
    }
  ]
}
```

Postman → **Import** → chọn file trên.

---

## 8. Bulk Import Script (PowerShell)

Nếu muốn import dữ liệu hàng loạt tự động:

```powershell
$baseUrl = "http://localhost:5208/api/reports"

# Dataset mới (bao phủ 3 vùng, đa dạng)
$salesData = @(
    @{ date = "2025-01-05T00:00:00Z"; dealerId = "a1b2c3d4-e5f6-4a5b-8c9d-1e2f3a4b5c6d"; dealerName = "Dealer Hà Nội"; region = "Miền Bắc"; salespersonId = "11111111-2222-3333-4444-555555555551"; salespersonName = "Nguyễn Văn A"; totalOrders = 6; totalRevenue = 1800000000 },
    @{ date = "2025-02-14T00:00:00Z"; dealerId = "a1b2c3d4-e5f6-4a5b-8c9d-1e2f3a4b5c6d"; dealerName = "Dealer Hà Nội"; region = "Miền Bắc"; salespersonId = "11111111-2222-3333-4444-555555555552"; salespersonName = "Trần Thị B"; totalOrders = 9; totalRevenue = 2700000000 },
    @{ date = "2025-03-02T00:00:00Z"; dealerId = "a1b2c3d4-e5f6-4a5b-8c9d-1e2f3a4b5c6d"; dealerName = "Dealer Hà Nội"; region = "Miền Bắc"; salespersonId = "11111111-2222-3333-4444-555555555553"; salespersonName = "Lý Quốc C"; totalOrders = 8; totalRevenue = 2560000000 },
    @{ date = "2025-01-12T00:00:00Z"; dealerId = "b2c3d4e5-f6a7-4b5c-9d0e-2f3a4b5c6d7e"; dealerName = "Dealer TP.HCM"; region = "Miền Nam"; salespersonId = "22222222-3333-4444-5555-666666666661"; salespersonName = "Lê Văn C"; totalOrders = 11; totalRevenue = 3520000000 },
    @{ date = "2025-02-18T00:00:00Z"; dealerId = "b2c3d4e5-f6a7-4b5c-9d0e-2f3a4b5c6d7e"; dealerName = "Dealer TP.HCM"; region = "Miền Nam"; salespersonId = "22222222-3333-4444-5555-666666666662"; salespersonName = "Phạm Thị D"; totalOrders = 7; totalRevenue = 2240000000 },
    @{ date = "2025-03-08T00:00:00Z"; dealerId = "b2c3d4e5-f6a7-4b5c-9d0e-2f3a4b5c6d7e"; dealerName = "Dealer TP.HCM"; region = "Miền Nam"; salespersonId = "22222222-3333-4444-5555-666666666663"; salespersonName = "Đỗ Minh E"; totalOrders = 9; totalRevenue = 2970000000 },
    @{ date = "2025-01-20T00:00:00Z"; dealerId = "c3d4e5f6-a7b8-4c5d-0e1f-3a4b5c6d7e8f"; dealerName = "Dealer Đà Nẵng"; region = "Miền Trung"; salespersonId = "33333333-4444-5555-6666-777777777771"; salespersonName = "Hoàng Văn E"; totalOrders = 5; totalRevenue = 1400000000 },
    @{ date = "2025-02-10T00:00:00Z"; dealerId = "c3d4e5f6-a7b8-4c5d-0e1f-3a4b5c6d7e8f"; dealerName = "Dealer Đà Nẵng"; region = "Miền Trung"; salespersonId = "33333333-4444-5555-6666-777777777772"; salespersonName = "Võ Thu F"; totalOrders = 6; totalRevenue = 1740000000 },
    @{ date = "2025-03-05T00:00:00Z"; dealerId = "c3d4e5f6-a7b8-4c5d-0e1f-3a4b5c6d7e8f"; dealerName = "Dealer Đà Nẵng"; region = "Miền Trung"; salespersonId = "33333333-4444-5555-6666-777777777773"; salespersonName = "Nguyễn Hà G"; totalOrders = 4; totalRevenue = 1160000000 }
)

$inventoryData = @(
    @{ vehicleId = "v1111111-1111-1111-1111-111111111111"; vehicleName = "Tesla Model 3"; dealerId = "a1b2c3d4-e5f6-4a5b-8c9d-1e2f3a4b5c6d"; dealerName = "Dealer Hà Nội"; region = "Miền Bắc"; stockCount = 18 },
    @{ vehicleId = "v9991111-1111-1111-1111-111111111111"; vehicleName = "VinFast VF9"; dealerId = "a1b2c3d4-e5f6-4a5b-8c9d-1e2f3a4b5c6d"; dealerName = "Dealer Hà Nội"; region = "Miền Bắc"; stockCount = 12 },
    @{ vehicleId = "v3333333-3333-3333-3333-333333333333"; vehicleName = "Audi e-tron"; dealerId = "b2c3d4e5-f6a7-4b5c-9d0e-2f3a4b5c6d7e"; dealerName = "Dealer TP.HCM"; region = "Miền Nam"; stockCount = 14 },
    @{ vehicleId = "v4444444-4444-4444-4444-444444444444"; vehicleName = "Mercedes EQE"; dealerId = "b2c3d4e5-f6a7-4b5c-9d0e-2f3a4b5c6d7e"; dealerName = "Dealer TP.HCM"; region = "Miền Nam"; stockCount = 9 },
    @{ vehicleId = "v5555555-5555-5555-5555-555555555555"; vehicleName = "Porsche Taycan"; dealerId = "c3d4e5f6-a7b8-4c5d-0e1f-3a4b5c6d7e8f"; dealerName = "Dealer Đà Nẵng"; region = "Miền Trung"; stockCount = 7 },
    @{ vehicleId = "v5559999-5555-5555-5555-555555555555"; vehicleName = "Hyundai Ioniq 5"; dealerId = "c3d4e5f6-a7b8-4c5d-0e1f-3a4b5c6d7e8f"; dealerName = "Dealer Đà Nẵng"; region = "Miền Trung"; stockCount = 11 }
)

$imported = 0
$failed = 0

foreach ($item in $salesData) {
    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/sales-summary" -Method Post -Body ($item | ConvertTo-Json) -ContentType "application/json"
        if ($response.success) {
            $imported++
            Write-Host "✓ Imported sales: $($item.dealerName) - $($item.salespersonName)"
        }
    } catch {
        $failed++
        Write-Host "✗ Failed sales: $($_.Exception.Message)" -ForegroundColor Red
    }
}

$inventorySuccess = 0
$inventoryFailed = 0

foreach ($item in $inventoryData) {
    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/inventory-summary" -Method Post -Body ($item | ConvertTo-Json) -ContentType "application/json"
        if ($response.success) {
            $inventorySuccess++
            Write-Host "✓ Imported inventory: $($item.vehicleName) - $($item.dealerName)"
        }
    } catch {
        $inventoryFailed++
        Write-Host "✗ Failed inventory: $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host "---"
Write-Host "Sales:  $imported thành công, $failed lỗi"
Write-Host "Stock:  $inventorySuccess thành công, $inventoryFailed lỗi"
```

---

## 9. Troubleshooting

| Lỗi                       | Nguyên nhân              | Cách khắc phục                                                     |
| ------------------------- | ------------------------ | ------------------------------------------------------------------ |
| 400 Bad Request           | Body JSON không đúng     | Kiểm tra JSON format, các field bắt buộc (dealerName, vehicleName) |
| 500 Internal Server Error | Database không reachable | Chạy với SQLite: mặc định, chỉ cần `dotnet run`                        |
| Connection Refused        | Service không chạy       | Khởi động lại: `dotnet run`                                        |
| 404 Not Found             | Endpoint sai URL         | Kiểm tra lại URL (http vs https, port 5208)                        |

---

## 10. Tiếp theo: Hiển thị dữ liệu trên Web

Sau khi import dữ liệu, để hiển thị trên frontend:

1. Xem hướng dẫn tạo frontend page (file `FRONTEND_INTEGRATION.md` hoặc tương tự)
2. Frontend sẽ gọi API `GET /api/reports/sales-summary` để lấy dữ liệu
3. Hiển thị dưới dạng bảng hoặc biểu đồ

---

**Bạn đã sẵn sàng import dữ liệu! Chọn Phương pháp A (tuần tự) hoặc B (Collection Runner) và bắt đầu.**
