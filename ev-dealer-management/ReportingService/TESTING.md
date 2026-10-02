# ReportingService — Testing Guide

Toàn bộ snippet trong file này đã chạy thật trên localhost:5208
(verify 2026-10-02). Response model: [API_REFERENCE.md](API_REFERENCE.md).

## 1. Chạy local

```powershell
cd ev-dealer-management\ReportingService
$env:ConnectionStrings__DefaultConnection = "Data Source=reporting_dev.db"
dotnet run --no-launch-profile --urls "http://localhost:5208"
```

```bash
# bash
cd ev-dealer-management/ReportingService
ConnectionStrings__DefaultConnection="Data Source=reporting_dev.db" \
  dotnet run --no-launch-profile --urls "http://localhost:5208"
```

**Bắt buộc có env override** — không có thì service boot được nhưng
`/health` = 503 `Database is unreachable` (xem README → "Chạy local").

Kiểm tra đã lên:

```powershell
Invoke-RestMethod http://localhost:5208/health    # → healthy, HTTP 200
```

Port khác: đổi `--urls "http://localhost:5300"` (các snippet dưới chỉnh theo).
Chiếm port: `netstat -ano | findstr :5208` rồi `taskkill /PID <pid> /F`.
Xem dữ liệu: `sqlite3 reporting_dev.db ".tables"` hoặc DB Browser for SQLite.

## 2. Lấy token

**Cách chính:** login thật qua UserService

```powershell
$login = Invoke-RestMethod -Uri 'http://localhost:7001/api/auth/login' -Method Post `
  -ContentType 'application/json' -Body '{"username":"<user>","password":"<pass>"}'
$token = $login.token   # shape: { token: "..." } — verify theo response thật
```

**Cách offline** (không cần UserService — đã verify: token này gọi
`GET /summary` → 200). Key/iss/aud lấy từ `appsettings.json` → `Jwt`:

```python
import hmac, hashlib, base64, json, time
KEY = b"ReplaceThisWithASecretKeyForDevelopment"   # Jwt:Key trong appsettings.json
def b64url(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode()
header = b64url(json.dumps({"alg": "HS256", "typ": "JWT"}).encode())
payload = b64url(json.dumps({
    "sub": "1", "iat": int(time.time()), "exp": int(time.time()) + 3600,
    "iss": "evm.local", "aud": "evm.local",
}).encode())
sig = b64url(hmac.new(KEY, f"{header}.{payload}".encode(), hashlib.sha256).digest())
print(f"{header}.{payload}.{sig}")
```

Token hết hạn sau 1 giờ → chạy lại script. Đặt vào header mọi request:

```powershell
$H = @{ Authorization = "Bearer $token" }
```

## 3. Import dữ liệu mẫu

**Bulk import PowerShell** (verified end-to-end: 201 +
`summary.totalSales` tăng đúng) — int ID, region 1 trong 3 tên miền,
kèm JWT:

```powershell
$H = @{ Authorization = "Bearer $token" }
$rows = @(
  @{ date='2025-04-01T00:00:00Z'; dealerId=1; dealerName='Dealer Hà Nội'
     region='Miền Bắc'; salespersonId=2; salespersonName='Nguyễn Văn A'
     totalOrders=5; totalRevenue=1500000000 },
  @{ date='2025-05-01T00:00:00Z'; dealerId=2; dealerName='Dealer Đà Nẵng'
     region='Miền Trung'; salespersonId=3; salespersonName='Trần Văn B'
     totalOrders=3; totalRevenue=900000000 },
  @{ date='2025-06-01T00:00:00Z'; dealerId=3; dealerName='Dealer Hồ Chí Minh'
     region='Miền Nam'; salespersonId=4; salespersonName='Lê Thị C'
     totalOrders=8; totalRevenue=2400000000 }
)
foreach ($r in $rows) {
  Invoke-RestMethod -Uri 'http://localhost:5208/api/reports/sales-summary' -Method Post `
    -Headers $H -Body ($r | ConvertTo-Json) -ContentType 'application/json'
}
# Tồn kho:
$inv = @{ vehicleId=1; vehicleName='VinFast VF 8'; dealerId=1
          dealerName='Dealer Hà Nội'; region='Miền Bắc'; stockCount=10 }
Invoke-RestMethod -Uri 'http://localhost:5208/api/reports/inventory-summary' -Method Post `
  -Headers $H -Body ($inv | ConvertTo-Json) -ContentType 'application/json'
```

Cách khác: **Swagger** (`/swagger` → Authorize → Execute) hoặc **Postman**
(xem mục 6).

> ⚠️ **`import-test-data.ps1` hiện chưa chạy được với code này**: dữ liệu
> mẫu vẫn dùng `dealerId` GUID (→ 400) và script không gửi Authorization
> (→ 401). Chưa sửa (ngoài scope docs) — dùng snippet trên thay.

## 4. Test xác minh nhanh

Chạy sau khi import dữ liệu ở mục 3 (PowerShell, cùng `$H`):

```powershell
# 1. Dashboard — 200 + metrics
(Invoke-RestMethod -Uri 'http://localhost:5208/api/reports/summary' -Headers $H).metrics

# 2. Theo vùng — mảng rỗng [] nếu chưa có dữ liệu Region
Invoke-RestMethod -Uri 'http://localhost:5208/api/reports/sales-by-region' -Headers $H

# 3. Top xe — field model/stockCount
Invoke-RestMethod -Uri 'http://localhost:5208/api/reports/top-vehicles?limit=5' -Headers $H

# 4. List + filter int
Invoke-RestMethod -Uri 'http://localhost:5208/api/reports/sales-summary?dealerId=1' -Headers $H

# 5. Export CSV (lưu file)
Invoke-WebRequest -Uri 'http://localhost:5208/api/reports/export' -Method Post -Headers $H `
  -Body '{"type":"sales","from":"2025-01-01","to":"2025-12-31"}' `
  -ContentType 'application/json' -OutFile report.csv

# 6. Đồng bộ data từ các service (cần service khác đang chạy; thiếu thì 500)
Invoke-RestMethod -Uri 'http://localhost:5208/api/reports/synchronize-data' -Method Post -Headers $H
```

**Edge cases — khẳng định gate bảo vệ (đều verified):**

```powershell
# Không token → 401
try { Invoke-RestMethod 'http://localhost:5208/api/reports/summary' } catch {
  [int]$_.Exception.Response.StatusCode }        # 401

# dealerId là GUID → 400
$body = @{ date='2025-04-01'; dealerId='3f2b1c00-0000-0000-0000-000000000000'
  dealerName='X'; region='Miền Bắc'; salespersonId=1; salespersonName='Y'
  totalOrders=1; totalRevenue=1 } | ConvertTo-Json
try { Invoke-RestMethod 'http://localhost:5208/api/reports/sales-summary' -Method Post `
  -Headers $H -Body $body -ContentType 'application/json' } catch {
  [int]$_.Exception.Response.StatusCode }        # 400

# id không tồn tại → 404
try { Invoke-RestMethod 'http://localhost:5208/api/reports/sales-summary/00000000-0000-0000-0000-000000000000' -Headers $H } catch {
  [int]$_.Exception.Response.StatusCode }        # 404
```

Kỳ vọng: `metrics.totalSales` = tổng `totalOrders` đã import, region trả
đúng 3 miền, export ra file CSV có BOM mở Excel không lỗi chữ.

## 5. Postman (rút gọn)

1. Import file `ReportingService.http` (repo có sẵn) hoặc tạo request tay:
   `GET {{base}}/api/reports/summary`, `{{base}}` = `http://localhost:5208`.
2. Tab **Authorization** → Type `Bearer Token` → dán token (mục 2).
   **không** dùng pre-request script tự gắn — chỉ cần paste 1 lần/collection.
3. POST: Body → raw → JSON, `dealerId` **số nguyên** (không GUID), giữ
   tiếng Việt bình thường (Postman encode UTF-8 đúng).
4. Chạy hàng loạt: Collection Runner → thêm request → Run; environment
   biến `{{base}}` đổi 1 chỗ cho mọi request.
5. SSL: local http không cần; nếu thử `https://localhost:7245` thì bật
   Settings → SSL certificate verification **tắt** (self-signed dev cert).

Sửa lỗi thường gặp với Postman:

| Thấy | Nguyên nhân | Sửa |
|---|---|---|
| 401 | thiếu/sai/hết hạn Bearer token | paste token mới (mục 2) |
| 400 body rỗng | `dealerId` là GUID, thiếu field required, hoặc region rỗng | xem bảng validate API_REFERENCE §4 |
| 404 | `GET /{id}` với Guid không tồn tại | lấy id từ response POST |

## 6. Stability / load test (PowerShell)

Chạy sau mục 1–3. **Mọi request phải kèm `$H`** — thiếu token là 401, số
được sẽ sai (bài cũ đo khi API chưa auth nên kết quả không tái dùng được).

```powershell
$base = 'http://localhost:5208'; $H = @{ Authorization = "Bearer $token" }
$sw = [System.Diagnostics.Stopwatch]::StartNew()

# A. Response time — 10 request GET /summary
$times = 1..10 | ForEach-Object {
  $s = [System.Diagnostics.Stopwatch]::StartNew()
  Invoke-RestMethod "$base/api/reports/summary" -Headers $H | Out-Null
  $s.Elapsed.TotalMilliseconds
}
"AVG={0:N1}ms MAX={1:N1}ms" -f ($times | Measure-Object -Average).Average `
  ($times | Measure-Object -Maximum).Maximum

# B. Sequential load — 50 GET sales-summary
$ok = 0; 1..50 | ForEach-Object {
  try { Invoke-RestMethod "$base/api/reports/sales-summary" -Headers $H | Out-Null; $ok++ } catch {}
}; "OK=$ok/50"

# C. Concurrent — 20 job song song
$jobs = 1..20 | ForEach-Object {
  Start-ThreadJob { param($b,$hh)
    try { Invoke-RestMethod "$b/api/reports/summary" -Headers $hh | Out-Null; 1 } catch { 0 }
  } -ArgumentList $base, $H }
$okC = ($jobs | Wait-Job | Receive-Job | Measure-Object -Sum).Sum
Remove-Job $jobs; "OK=$okC/20"

# D. Mixed workload 30 giây (GET summary / region / top-vehicles / list xen kẽ)
$paths = '/api/reports/summary','/api/reports/sales-by-region',
         '/api/reports/top-vehicles','/api/reports/sales-summary'
$okM=0; $n=0; $sw = [System.Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt 30) {
  $n++
  try { Invoke-RestMethod ($base + $paths[$n % 4]) -Headers $H | Out-Null; $okM++ } catch {}
}
"Mixed: OK=$okM/$n"

# E. POST round-trip — 10 dòng sales-summary rồi đếm lại
1..10 | ForEach-Object {
  $b = @{ date=(Get-Date).ToString('yyyy-MM-dd'); dealerId=1; dealerName='Load Test'
    region='Miền Bắc'; salespersonId=1; salespersonName='LT'
    totalOrders=1; totalRevenue=1000000 } | ConvertTo-Json
  try { Invoke-RestMethod "$base/api/reports/sales-summary" -Method Post -Headers $H `
    -Body $b -ContentType 'application/json' | Out-Null } catch {}
}
```

**Target (từ đề bài stability cũ, giữ nguyên):**

| Chỉ số | Target |
|---|---|
| Avg response time GET | < 100 ms |
| Success rate | ≥ 99% |
| Concurrent tối thiểu | ≥ 20 request đồng thời không lỗi |
| RSS bộ nhớ service | < 500 MB (xem Task Manager / `dotnet` process) |

## 7. Troubleshooting

| Triệu chứng | Nguyên nhân đã xác minh | Sửa |
|---|---|---|
| `401` mọi `/api/reports/*` | thiếu/sai token | mốc 2 — mint token mới |
| `/health` = **503** `Database is unreachable`; log warning `Connection string keyword 'host' is not supported` | `DefaultConnection` là chuỗi Postgres trong khi provider mặc định SQLite, không override | set `ConnectionStrings__DefaultConnection="Data Source=reporting_dev.db"` rồi chạy lại |
| `Connection refused` :5208 | service chưa start / port sai | kiểm tra cửa sổ console; `--urls` đúng port |
| **POST body tiếng Việt qua Git Bash `curl -d '...'` → 400, body rỗng** | curl trên Windows/MSYS hỏng multi-byte UTF-8 trong argv (client, **không phải service lỗi**) | dùng `--data-binary @file.json`, hoặc PowerShell `Invoke-RestMethod`, hoặc Postman — đều verified 201 |
| 400 body rỗng POST | `dealerId` GUID / thiếu required field / region rỗng | API_REFERENCE §4 |
| 404 `sales-summary/{id}` | Guid sai hoặc record chưa tồn tại | lấy `data.id` từ response POST |
| Excel mở CSV bị lỗi tiếng Việt | file thiếu BOM | export qua `POST /export` — service đã gắn UTF-8 BOM sẵn |
| 500 `synchronize-data` | service nguồn (5003/5068/5039/7001) chưa chạy | start service cần thiết hoặc bỏ qua bước này |
