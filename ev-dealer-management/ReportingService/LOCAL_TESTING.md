# Hướng dẫn kiểm tra local (PowerShell)

Tệp này chứa các lệnh PowerShell tiện dụng để kiểm tra, khởi động và debug `ReportingService` trên máy local.
Sử dụng các lệnh dưới đây trong Windows PowerShell (bản PowerShell mặc định 5.1). Mỗi bước là độc lập — thực hiện theo thứ tự.

---

## 1) Chuẩn bị (mở hai cửa sổ PowerShell)

- Cửa sổ A: dùng để chạy `dotnet run` và xem log (giữ mở)
- Cửa sổ B: dùng để chạy các lệnh kiểm tra (Invoke-RestMethod, Get-NetTCPConnection, v.v.)

## 2) Build dự án

Mở cửa sổ PowerShell và chuyển về thư mục `ReportingService`:

```powershell
cd D:\gitclone\ev-dealer-management\ev-dealer-management\ReportingService
dotnet build
```

Nếu build bị lỗi do file exe đang bị chiếm, dừng tiến trình cũ trước (xem Mục 5).

## 3) Chạy server (mặc định)

Chạy trong Cửa sổ A:

```powershell
cd D:\gitclone\ev-dealer-management\ev-dealer-management\ReportingService
dotnet run
```

Chờ log xuất hiện: `Now listening on: http://localhost:5208` và `Application started. Press Ctrl+C to shut down.`

### Chạy với SQLite — nhanh để test nếu bạn không có PostgreSQL

SQLite là provider mặc định (không cần Postgres). Chỉ cần chạy:

```powershell
cd D:\gitclone\ev-dealer-management\ev-dealer-management\ReportingService
dotnet run
```

Provider được chọn bởi biến môi trường `DB_PROVIDER` (`sqlite` là mặc định, `postgres` để dùng
PostgreSQL — cùng switch cho tất cả 6 service, xem `Common/Data/DbProviderSelector.cs`).
Lưu ý: file SQLite `reporting_dev.db` sẽ được tạo trong thư mục chạy app (AppContext.BaseDirectory).

## 4) Kiểm tra port / tiến trình

Mở Cửa sổ B và kiểm tra xem có tiến trình lắng nghe cổng 5208:

```powershell
Get-NetTCPConnection -LocalPort 5208 | Select-Object LocalAddress,LocalPort,State,OwningProcess | Format-Table -AutoSize
$conn = Get-NetTCPConnection -LocalPort 5208 -ErrorAction SilentlyContinue
if ($conn) { Get-Process -Id $conn.OwningProcess | Select-Object Id,ProcessName,Path }
```

Nếu không có kết quả, server chưa chạy hoặc đã bị tắt.

## 5) Dừng tiến trình đang chiếm cổng (nếu cần)

Nếu thấy một tiến trình `ReportingService` đang chạy và bạn muốn khởi động lại, dừng nó:

```powershell
# Kiểm tra PID
Get-Process -Name ReportingService -ErrorAction SilentlyContinue | Select-Object Id,ProcessName,StartTime,Path

# Dừng (ví dụ PID=1234)
Stop-Process -Id 1234 -Force
```

Thay `1234` bằng PID thực tế từ lệnh trước.

## 6) Gọi các endpoint thử nghiệm (Cửa sổ B)

Các endpoint mẫu — một số không cần DB, một số sử dụng DB.

1. Endpoints không phụ thuộc DB (mock data)

```powershell
Invoke-RestMethod -Uri "http://localhost:5208/api/reports/summary" -Method Get | ConvertTo-Json
Invoke-RestMethod -Uri "http://localhost:5208/api/reports/sales-by-region" -Method Get | ConvertTo-Json
Invoke-RestMethod -Uri "http://localhost:5208/api/reports/top-vehicles" -Method Get | ConvertTo-Json
```

2. Endpoints dùng DB (GET/POST sales-summary, inventory-summary)

```powershell
# GET tất cả sales-summary (nếu DB down có thể trả lỗi 500)
Invoke-RestMethod -Uri "http://localhost:5208/api/reports/sales-summary" -Method Get | ConvertTo-Json

# POST mẫu - thêm một record (JSON trong -Body)
$body = @'
{
  "date": "2025-01-15T00:00:00Z",
  "dealerId": "550e8400-e29b-41d4-a716-446655440000",
  "dealerName": "Dealer Hà Nội",
  "salespersonId": "550e8400-e29b-41d4-a716-446655440002",
  "salespersonName": "Nguyễn Văn A",
  "totalOrders": 5,
  "totalRevenue": 1500000000
}
'@
Invoke-RestMethod -Uri "http://localhost:5208/api/reports/sales-summary" -Method Post -Body $body -ContentType "application/json" | ConvertTo-Json
```

Nếu bạn chạy server với SQLite (mặc định), POST sẽ lưu vào file SQLite.

## 7) Chạy PostgreSQL nhanh bằng Docker (nếu muốn dùng Postgres thật)

Tạo file `docker-compose.postgres.yml` (ở bất kỳ thư mục nào) với nội dung sau:

```yaml
version: "3.8"
services:
  postgres:
    image: postgres:15
    restart: unless-stopped
    environment:
      POSTGRES_DB: ev_dealer_reporting
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    ports:
      - "5432:5432"
    volumes:
      - pgdata:/var/lib/postgresql/data

volumes:
  pgdata:
```

Chạy:

```powershell
docker compose -f docker-compose.postgres.yml up -d
# Kiểm tra container
docker ps
```

Sau khi Postgres sẵn sàng, chạy lại app với `$env:DB_PROVIDER = "postgres"` (set ConnectionStrings:DefaultConnection trỏ tới Postgres) và migrations sẽ được áp dụng lên Postgres.

## 8) Thay đổi port tạm thời

Nếu port 5208 bị xung đột, bạn có thể chạy app trên port khác:

```powershell
cd D:\gitclone\ev-dealer-management\ev-dealer-management\ReportingService
dotnet run --urls "http://localhost:5210"
```

Hoặc đặt biến môi trường `ASPNETCORE_URLS`:

```powershell
$env:ASPNETCORE_URLS = "http://localhost:5210"
dotnet run
```

## 9) Xem file SQLite (nếu dùng SQLite)

File SQLite (reporting_dev.db) sẽ nằm trong thư mục chạy application (thường `bin/Debug/net8.0/` khi chạy từ Visual Studio) — bạn có thể mở bằng DB Browser for SQLite hoặc sqlite3.

Ví dụ dùng sqlite3 (nếu đã cài):

```powershell
sqlite3 .\reporting_dev.db
sqlite> .tables
sqlite> SELECT * FROM SalesSummaries LIMIT 10;
```

## 10) Ghi chú / Troubleshooting ngắn

- Nếu `dotnet run` báo lỗi binding port (address already in use): kiểm tra tiến trình đang chiếm (Mục 5) và dừng nó.
- Nếu POST trả lỗi 500: server cố gắng ghi vào DB nhưng DB không reachable; chạy app với SQLite hoặc bật Postgres.
- Nếu cần logs chi tiết: chạy `dotnet run` và đọc console output (là log chính). Nếu chạy trong IDE, xem cửa sổ Output/Console.

---

Nếu bạn muốn, tôi có thể thêm file `docker-compose.postgres.yml` trực tiếp vào repo và thêm một tập lệnh PowerShell (ví dụ `start-local.ps1`) để tự động hóa các bước trên. Bạn muốn tôi tạo thêm những file đó không?
