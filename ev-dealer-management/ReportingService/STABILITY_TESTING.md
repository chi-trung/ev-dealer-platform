# Kiểm tra tính ổn định (Stability Testing)

Tệp này chứa các lệnh PowerShell để kiểm tra tính ổn định, hiệu suất và độ tin cậy của `ReportingService` trên máy local.
Các bài test bao gồm: load test, concurrent requests, edge cases, response time, error handling, v.v.

**Yêu cầu trước khi chạy:**

- Service đang chạy trên http://localhost:5208 (dùng `dotnet run` — SQLite là provider mặc định)
- Mở PowerShell và chạy lệnh từ folder `ReportingService`

---

## 1) Kiểm tra cơ bản — Service sẵn sàng

Đầu tiên, xác nhận service đang chạy bình thường:

```powershell
# Test endpoint không cần DB (mock data)
$response = Invoke-RestMethod -Uri "http://localhost:5208/api/reports/summary" -Method Get
$response | ConvertTo-Json
Write-Host "✓ Service sẵn sàng" -ForegroundColor Green
```

Kết quả mong đợi: 200 OK, JSON trả về `{ type, from, to, metrics }`.

---

## 2) Test Response Time — Đo thời gian phản hồi

```powershell
# Test 10 request lần lượt và đo thời gian
$url = "http://localhost:5208/api/reports/sales-by-region"
$times = @()

for ($i = 1; $i -le 10; $i++) {
    $start = Get-Date
    $response = Invoke-RestMethod -Uri $url -Method Get
    $end = Get-Date
    $elapsed = ($end - $start).TotalMilliseconds
    $times += $elapsed
    Write-Host "Request $i : $($elapsed) ms"
}

$avg = ($times | Measure-Object -Average).Average
$min = ($times | Measure-Object -Minimum).Minimum
$max = ($times | Measure-Object -Maximum).Maximum

Write-Host "---"
Write-Host "Trung bình: $avg ms"
Write-Host "Nhanh nhất: $min ms"
Write-Host "Chậm nhất: $max ms"
```

Kết quả mong đợi: Trung bình dưới 100ms (có thể cao hơn lần đầu tiên do warm-up).

---

## 3) Test Load — Gửi nhiều request nhanh (Sequential)

```powershell
# Gửi 50 request lần lượt
$url = "http://localhost:5208/api/reports/sales-summary"
$successCount = 0
$errorCount = 0
$times = @()

Write-Host "Gửi 50 request (lần lượt)..."

for ($i = 1; $i -le 50; $i++) {
    try {
        $start = Get-Date
        $response = Invoke-RestMethod -Uri $url -Method Get -ErrorAction Stop
        $end = Get-Date
        $elapsed = ($end - $start).TotalMilliseconds
        $times += $elapsed
        $successCount++

        if ($i % 10 -eq 0) {
            Write-Host "  ✓ Request $i thành công ($($elapsed)ms)"
        }
    } catch {
        $errorCount++
        Write-Host "  ✗ Request $i lỗi: $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host "---"
Write-Host "Thành công: $successCount / 50"
Write-Host "Lỗi: $errorCount"
if ($times.Count -gt 0) {
    $avg = ($times | Measure-Object -Average).Average
    Write-Host "Trung bình thời gian: $avg ms"
}
```

Kết quả mong đợi: 50/50 thành công, không có lỗi.

---

## 4) Test Concurrent Requests — Gửi song song

```powershell
# Gửi 20 request cùng lúc bằng background job
$url = "http://localhost:5208/api/reports/top-vehicles"
$jobs = @()

Write-Host "Khởi động 20 concurrent request..."

for ($i = 1; $i -le 20; $i++) {
    $job = Start-Job -ScriptBlock {
        param($url, $id)
        try {
            $start = Get-Date
            $response = Invoke-RestMethod -Uri $url -Method Get
            $end = Get-Date
            $elapsed = ($end - $start).TotalMilliseconds
            @{
                Id = $id
                Success = $true
                Time = $elapsed
                Count = @($response).Count
            }
        } catch {
            @{
                Id = $id
                Success = $false
                Error = $_.Exception.Message
                Time = 0
            }
        }
    } -ArgumentList $url, $i
    $jobs += $job
}

# Chờ tất cả job hoàn tất
$results = $jobs | Wait-Job | Receive-Job

# Phân tích kết quả
$successful = @($results | Where-Object { $_.Success }).Count
$failed = @($results | Where-Object { -not $_.Success }).Count
$avgTime = ($results | Measure-Object -Property Time -Average).Average

Write-Host "---"
Write-Host "Thành công: $successful / 20"
Write-Host "Lỗi: $failed"
Write-Host "Trung bình thời gian: $avgTime ms"

if ($failed -gt 0) {
    Write-Host "Chi tiết lỗi:"
    $results | Where-Object { -not $_.Success } | ForEach-Object {
        Write-Host "  Request $($_.Id): $($_.Error)" -ForegroundColor Red
    }
}

# Dọn dẹp job
$jobs | Remove-Job
```

Kết quả mong đợi: 20/20 thành công, trung bình thời gian dưới 200ms.

---

## 5) Test POST — Gửi dữ liệu và kiểm tra lưu trữ

```powershell
# Gửi 10 POST request với dữ liệu khác nhau
$url = "http://localhost:5208/api/reports/sales-summary"
$createdIds = @()
$successCount = 0

Write-Host "Gửi 10 POST request..."

for ($i = 1; $i -le 10; $i++) {
    $dealerId = [guid]::NewGuid().ToString()
    $salespersonId = [guid]::NewGuid().ToString()

    $body = @{
        date = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
        dealerId = $dealerId
        dealerName = "Dealer $i - $(Get-Random)"
        salespersonId = $salespersonId
        salespersonName = "Salesperson $i"
        totalOrders = Get-Random -Minimum 1 -Maximum 100
        totalRevenue = Get-Random -Minimum 1000000 -Maximum 10000000000
    } | ConvertTo-Json

    try {
        $response = Invoke-RestMethod -Uri $url -Method Post -Body $body -ContentType "application/json"
        if ($response.success) {
            $createdIds += $response.data.id
            $successCount++
            Write-Host "  ✓ POST $i thành công (ID: $($response.data.id))"
        } else {
            Write-Host "  ✗ POST $i không thành công: $($response.error)" -ForegroundColor Red
        }
    } catch {
        Write-Host "  ✗ POST $i lỗi: $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host "---"
Write-Host "Tổng thành công: $successCount / 10"
Write-Host "IDs tạo được: $($createdIds.Count)"
```

Kết quả mong đợi: 10/10 thành công, mỗi POST trả 201 Created.

---

## 6) Test Retrieve — Lấy dữ liệu vừa tạo

```powershell
# Sau khi POST, lấy lại dữ liệu để xác nhận lưu trữ
$url = "http://localhost:5208/api/reports/sales-summary"

Write-Host "Lấy toàn bộ dữ liệu sales-summary..."

try {
    $response = Invoke-RestMethod -Uri $url -Method Get
    $count = $response.count
    Write-Host "✓ Tổng số record: $count"

    if ($count -gt 0) {
        Write-Host "Mẫu record đầu tiên:"
        $response.data[0] | ConvertTo-Json -Depth 2
    }
} catch {
    Write-Host "✗ Lỗi khi lấy dữ liệu: $($_.Exception.Message)" -ForegroundColor Red
}
```

Kết quả mong đợi: count > 0, dữ liệu vừa POST có mặt.

---

## 7) Test Filter — Kiểm tra lọc dữ liệu

```powershell
# Test filter bằng fromDate, toDate, dealerId
$baseUrl = "http://localhost:5208/api/reports/sales-summary"

Write-Host "Test 1: Filter bằng fromDate"
$start = (Get-Date).AddDays(-7).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$url = "$baseUrl?fromDate=$start"
try {
    $response = Invoke-RestMethod -Uri $url -Method Get
    Write-Host "  ✓ Kết quả: $($response.count) record"
} catch {
    Write-Host "  ✗ Lỗi: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host "Test 2: Filter bằng toDate"
$end = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$url = "$baseUrl?toDate=$end"
try {
    $response = Invoke-RestMethod -Uri $url -Method Get
    Write-Host "  ✓ Kết quả: $($response.count) record"
} catch {
    Write-Host "  ✗ Lỗi: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host "Test 3: Filter bằng dealerId"
$dealerId = [guid]::NewGuid().ToString()
$url = "$baseUrl?dealerId=$dealerId"
try {
    $response = Invoke-RestMethod -Uri $url -Method Get
    Write-Host "  ✓ Kết quả: $($response.count) record"
} catch {
    Write-Host "  ✗ Lỗi: $($_.Exception.Message)" -ForegroundColor Red
}
```

Kết quả mong đợi: Các filter hoạt động, trả về 200 OK với count tương ứng.

---

## 8) Test Edge Cases — Trường hợp biên

```powershell
# Test các edge case: body trống, field bắt buộc thiếu, v.v.

Write-Host "Test 1: POST với body trống"
try {
    $response = Invoke-RestMethod -Uri "http://localhost:5208/api/reports/sales-summary" -Method Post -Body "{}" -ContentType "application/json"
    Write-Host "  Kết quả: $($response.success) - $($response.error)"
} catch {
    Write-Host "  ✗ Exception: $($_.Exception.Message)"
}

Write-Host "Test 2: POST thiếu dealerName (required)"
$body = @{
    date = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    dealerId = [guid]::NewGuid().ToString()
    salespersonId = [guid]::NewGuid().ToString()
    salespersonName = "Test"
    totalOrders = 5
    totalRevenue = 1000000
} | ConvertTo-Json
try {
    $response = Invoke-RestMethod -Uri "http://localhost:5208/api/reports/sales-summary" -Method Post -Body $body -ContentType "application/json"
    Write-Host "  Kết quả: $($response.success) - $($response.error)"
} catch {
    Write-Host "  ✓ Lỗi expected: $($_.Exception.Message)"
}

Write-Host "Test 3: GET với ID không tồn tại"
$fakeId = [guid]::NewGuid().ToString()
try {
    $response = Invoke-RestMethod -Uri "http://localhost:5208/api/reports/sales-summary/$fakeId" -Method Get
    Write-Host "  Kết quả: $($response.message)"
} catch {
    Write-Host "  Mã lỗi HTTP: $($_.Exception.Response.StatusCode)"
}
```

Kết quả mong đợi:

- POST body trống → 400 Bad Request
- POST thiếu required field → 400 Bad Request
- GET ID không tồn tại → 404 Not Found

---

## 9) Test Memory/Resource — Kiểm tra tài nguyên

```powershell
# Kiểm tra memory usage của process
$process = Get-Process -Name "ReportingService" -ErrorAction SilentlyContinue

if ($process) {
    Write-Host "Thông tin process ReportingService:"
    Write-Host "  PID: $($process.Id)"
    Write-Host "  Memory: $([Math]::Round($process.WorkingSet / 1MB, 2)) MB"
    Write-Host "  CPU Time: $($process.TotalProcessorTime.TotalSeconds) giây"
    Write-Host "  Threads: $($process.Threads.Count)"
    Write-Host "  Handles: $($process.Handles)"
} else {
    Write-Host "✗ Process ReportingService không tìm thấy" -ForegroundColor Red
}
```

Kết quả mong đợi: Memory dưới 500 MB (tuỳ loại requests), threads/handles hợp lý.

---

## 10) Test Endpoints Inventory — Tương tự sales-summary

```powershell
# Kiểm tra endpoints inventory-summary

Write-Host "Test Inventory Endpoints..."

Write-Host "1. GET /api/reports/inventory-summary"
try {
    $response = Invoke-RestMethod -Uri "http://localhost:5208/api/reports/inventory-summary" -Method Get
    Write-Host "  ✓ Count: $($response.count)"
} catch {
    Write-Host "  ✗ Lỗi: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host "2. POST /api/reports/inventory-summary"
$body = @{
    vehicleId = [guid]::NewGuid().ToString()
    vehicleName = "Tesla Model 3"
    dealerId = [guid]::NewGuid().ToString()
    dealerName = "Dealer Test"
    stockCount = 10
} | ConvertTo-Json

try {
    $response = Invoke-RestMethod -Uri "http://localhost:5208/api/reports/inventory-summary" -Method Post -Body $body -ContentType "application/json"
    Write-Host "  ✓ Success: $($response.success) (ID: $($response.data.id))"
} catch {
    Write-Host "  ✗ Lỗi: $($_.Exception.Message)" -ForegroundColor Red
}
```

---

## 11) Kịch bản tổng hợp — Mixed Workload

```powershell
# Giả lập real-world usage: POST + GET + Filter lẫn lộn

$baseUrl = "http://localhost:5208/api/reports"
$testDuration = 30  # 30 giây
$startTime = Get-Date

Write-Host "Chạy mixed workload trong $testDuration giây..."
Write-Host "Gồm: 30% POST, 50% GET, 20% Filter"
Write-Host "---"

$stats = @{
    PostSuccess = 0
    PostFail = 0
    GetSuccess = 0
    GetFail = 0
    FilterSuccess = 0
    FilterFail = 0
}

while ((Get-Date) -lt $startTime.AddSeconds($testDuration)) {
    $action = Get-Random -Minimum 1 -Maximum 101

    if ($action -le 30) {
        # POST
        $body = @{
            date = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
            dealerId = [guid]::NewGuid().ToString()
            dealerName = "Dealer $(Get-Random)"
            salespersonId = [guid]::NewGuid().ToString()
            salespersonName = "SP $(Get-Random)"
            totalOrders = Get-Random -Minimum 1 -Maximum 50
            totalRevenue = Get-Random -Minimum 100000 -Maximum 5000000000
        } | ConvertTo-Json

        try {
            Invoke-RestMethod -Uri "$baseUrl/sales-summary" -Method Post -Body $body -ContentType "application/json" | Out-Null
            $stats.PostSuccess++
        } catch {
            $stats.PostFail++
        }
    } elseif ($action -le 80) {
        # GET
        try {
            Invoke-RestMethod -Uri "$baseUrl/sales-summary" -Method Get | Out-Null
            $stats.GetSuccess++
        } catch {
            $stats.GetFail++
        }
    } else {
        # Filter
        $start = (Get-Date).AddDays(-1).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
        try {
            Invoke-RestMethod -Uri "$baseUrl/sales-summary?fromDate=$start" -Method Get | Out-Null
            $stats.FilterSuccess++
        } catch {
            $stats.FilterFail++
        }
    }
}

Write-Host "Kết quả sau $testDuration giây:"
Write-Host "POST: $($stats.PostSuccess) thành công, $($stats.PostFail) lỗi"
Write-Host "GET: $($stats.GetSuccess) thành công, $($stats.GetFail) lỗi"
Write-Host "FILTER: $($stats.FilterSuccess) thành công, $($stats.FilterFail) lỗi"
Write-Host "---"
$total = $stats.PostSuccess + $stats.GetSuccess + $stats.FilterSuccess + $stats.PostFail + $stats.GetFail + $stats.FilterFail
$successRate = [Math]::Round(($stats.PostSuccess + $stats.GetSuccess + $stats.FilterSuccess) / $total * 100, 2)
Write-Host "Tỉ lệ thành công: $successRate %"
```

---

## 12) Tóm tắt kiểm tra ổn định

Sau khi chạy các bài test trên, bạn có thể:

1. Ghi lại kết quả (đặc biệt response time, success rate, error rate)
2. So sánh với mục tiêu hiệu suất
3. Tìm các bottleneck hoặc vấn đề
4. Báo cáo với team về độ tin cậy của service

**Mục tiêu tối ưu (khuyến nghị):**

- Response time trung bình: < 100ms
- Success rate: ≥ 99% (với SQLite) hoặc ≥ 99.5% (với Postgres)
- Peak concurrent: xử lý ≥ 20 request đồng thời
- Memory: < 500 MB (SQLite), < 800 MB (Postgres)
- Error handling: đúng HTTP status code (400, 404, 500)

---

Để chạy tất cả các bài test này một lúc, bạn có thể copy từng block lệnh vào PowerShell và chạy. Hoặc tôi có thể tạo một script `run-stability-tests.ps1` để bạn chỉ chạy 1 lệnh duy nhất là chạy hết tất cả. Muốn tôi tạo script đó không?
