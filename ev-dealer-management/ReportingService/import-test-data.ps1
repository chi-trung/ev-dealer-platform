# Script Import Test Data cho ReportingService
# Sử dụng: .\import-test-data.ps1 [-BaseUrl <url>] [-Token <jwt>]
# - Tất cả endpoint đều yêu cầu JWT: tự mint từ appsettings.json Jwt:Key,
#   hoặc truyền -Token nếu service đang chạy với key khác (env Jwt__Key).
# - dealerId/salespersonId/vehicleId là int (model binding từ GUID → 400).

param(
    [string]$BaseUrl = "http://localhost:5208/api/reports",
    [string]$Token = ""
)

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  IMPORT TEST DATA - ReportingService" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# ===== LẤY TOKEN =====
if (-not $Token) {
    try {
        $cfgPath = Join-Path $PSScriptRoot "appsettings.json"
        $jwtKey = (Get-Content $cfgPath -Raw | ConvertFrom-Json).Jwt.Key
        if (-not $jwtKey) { throw "Jwt.Key không có trong appsettings.json" }

        function ConvertTo-Base64Url([byte[]]$Bytes) {
            [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
        }
        $h = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
        $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
        $payload = [ordered]@{ sub = "1"; iat = $now; exp = $now + 3600; iss = "evm.local"; aud = "evm.local" }
        $p = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes(($payload | ConvertTo-Json -Compress)))
        $hmac = New-Object System.Security.Cryptography.HMACSHA256
        $hmac.Key = [byte[]][Text.Encoding]::UTF8.GetBytes($jwtKey)
        $sig = ConvertTo-Base64Url ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes("$h.$p")))
        $Token = "$h.$p.$sig"
        Write-Host "✓ Đã mint JWT tự động (hết hạn sau 1 giờ, key từ appsettings.json)" -ForegroundColor Green
    } catch {
        Write-Host "✗ Không mint được token: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "   Truyền token thủ công: .\import-test-data.ps1 -Token <jwt> (xem TESTING.md mục 2)" -ForegroundColor Yellow
        exit 1
    }
}
$auth = @{ Authorization = "Bearer $Token" }

# Kiểm tra service có đang chạy không
try {
    Invoke-WebRequest -Uri "$BaseUrl/sales-summary" -Method Get -Headers $auth -TimeoutSec 2 -ErrorAction Stop | Out-Null
    Write-Host "✓ Service đang chạy tại $BaseUrl (JWT hợp lệ)" -ForegroundColor Green
} catch {
    $code = 0
    try { $code = [int]$_.Exception.Response.StatusCode } catch { }
    if ($code -eq 401 -or $code -eq 403) {
        Write-Host "✗ Lỗi: HTTP $code — JWT không hợp lệ/hết hạn." -ForegroundColor Red
        Write-Host "   Nếu service chạy với env Jwt__Key khác appsettings.json, truyền -Token <jwt>." -ForegroundColor Yellow
    } else {
        Write-Host "✗ Lỗi: Service không chạy hoặc không thể kết nối!" -ForegroundColor Red
        Write-Host "   Hãy chạy: `$env:ConnectionStrings__DefaultConnection='Data Source=reporting_dev.db'; dotnet run --no-launch-profile --urls http://localhost:5208" -ForegroundColor Yellow
    }
    exit 1
}

# ===== IMPORT SALES SUMMARY =====
Write-Host "`n=== Importing Sales Summary Data ===" -ForegroundColor Green

$salesData = @(
    @{ date = "2025-01-05T00:00:00Z"; dealerId = 1; dealerName = "Dealer Hà Nội"; region = "Miền Bắc"; salespersonId = 11; salespersonName = "Nguyễn Văn A"; totalOrders = 6; totalRevenue = 1800000000 },
    @{ date = "2025-02-14T00:00:00Z"; dealerId = 1; dealerName = "Dealer Hà Nội"; region = "Miền Bắc"; salespersonId = 11; salespersonName = "Nguyễn Văn A"; totalOrders = 9; totalRevenue = 2700000000 },
    @{ date = "2025-03-02T00:00:00Z"; dealerId = 1; dealerName = "Dealer Hà Nội"; region = "Miền Bắc"; salespersonId = 12; salespersonName = "Trần Thị B"; totalOrders = 8; totalRevenue = 2560000000 },
    @{ date = "2025-01-12T00:00:00Z"; dealerId = 3; dealerName = "Dealer TP.HCM"; region = "Miền Nam"; salespersonId = 31; salespersonName = "Lê Văn C"; totalOrders = 11; totalRevenue = 3520000000 },
    @{ date = "2025-02-18T00:00:00Z"; dealerId = 3; dealerName = "Dealer TP.HCM"; region = "Miền Nam"; salespersonId = 31; salespersonName = "Lê Văn C"; totalOrders = 7; totalRevenue = 2240000000 },
    @{ date = "2025-03-08T00:00:00Z"; dealerId = 3; dealerName = "Dealer TP.HCM"; region = "Miền Nam"; salespersonId = 32; salespersonName = "Phạm Thị D"; totalOrders = 9; totalRevenue = 2970000000 },
    @{ date = "2025-01-20T00:00:00Z"; dealerId = 2; dealerName = "Dealer Đà Nẵng"; region = "Miền Trung"; salespersonId = 21; salespersonName = "Hoàng Văn E"; totalOrders = 5; totalRevenue = 1400000000 },
    @{ date = "2025-02-10T00:00:00Z"; dealerId = 2; dealerName = "Dealer Đà Nẵng"; region = "Miền Trung"; salespersonId = 21; salespersonName = "Hoàng Văn E"; totalOrders = 6; totalRevenue = 1740000000 },
    @{ date = "2025-03-05T00:00:00Z"; dealerId = 2; dealerName = "Dealer Đà Nẵng"; region = "Miền Trung"; salespersonId = 22; salespersonName = "Võ Thu F"; totalOrders = 4; totalRevenue = 1160000000 }
)

$salesSuccess = 0
$salesFailed = 0

foreach ($item in $salesData) {
    try {
        $response = Invoke-RestMethod -Uri "$BaseUrl/sales-summary" -Method Post -Headers $auth `
            -Body ($item | ConvertTo-Json) -ContentType "application/json" -ErrorAction Stop
        if ($response.success) {
            $salesSuccess++
            Write-Host "  ✓ Sales: $($item.dealerName) - $($item.salespersonName) ($($item.totalOrders) orders)" -ForegroundColor Green
        } else {
            $salesFailed++
            Write-Host "  ✗ Failed: $($item.dealerName)" -ForegroundColor Red
        }
    } catch {
        $salesFailed++
        Write-Host "  ✗ Error: $($item.dealerName) - $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host "`nSales Summary: $salesSuccess thành công, $salesFailed lỗi" -ForegroundColor Cyan

# ===== IMPORT INVENTORY SUMMARY =====
Write-Host "`n=== Importing Inventory Summary Data ===" -ForegroundColor Green

$inventoryData = @(
    @{ vehicleId = 1; vehicleName = "Tesla Model 3"; dealerId = 1; dealerName = "Dealer Hà Nội"; region = "Miền Bắc"; stockCount = 18 },
    @{ vehicleId = 2; vehicleName = "VinFast VF9"; dealerId = 1; dealerName = "Dealer Hà Nội"; region = "Miền Bắc"; stockCount = 12 },
    @{ vehicleId = 3; vehicleName = "Audi e-tron"; dealerId = 3; dealerName = "Dealer TP.HCM"; region = "Miền Nam"; stockCount = 14 },
    @{ vehicleId = 4; vehicleName = "Mercedes EQE"; dealerId = 3; dealerName = "Dealer TP.HCM"; region = "Miền Nam"; stockCount = 9 },
    @{ vehicleId = 5; vehicleName = "Porsche Taycan"; dealerId = 2; dealerName = "Dealer Đà Nẵng"; region = "Miền Trung"; stockCount = 7 },
    @{ vehicleId = 6; vehicleName = "Hyundai Ioniq 5"; dealerId = 2; dealerName = "Dealer Đà Nẵng"; region = "Miền Trung"; stockCount = 11 }
)

$inventorySuccess = 0
$inventoryFailed = 0

foreach ($item in $inventoryData) {
    try {
        $response = Invoke-RestMethod -Uri "$BaseUrl/inventory-summary" -Method Post -Headers $auth `
            -Body ($item | ConvertTo-Json) -ContentType "application/json" -ErrorAction Stop
        if ($response.success) {
            $inventorySuccess++
            Write-Host "  ✓ Inventory: $($item.vehicleName) - $($item.dealerName) (Stock: $($item.stockCount))" -ForegroundColor Green
        } else {
            $inventoryFailed++
            Write-Host "  ✗ Failed: $($item.vehicleName)" -ForegroundColor Red
        }
    } catch {
        $inventoryFailed++
        Write-Host "  ✗ Error: $($item.vehicleName) - $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host "`nInventory Summary: $inventorySuccess thành công, $inventoryFailed lỗi" -ForegroundColor Cyan

# ===== TỔNG KẾT =====
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  IMPORT HOÀN TẤT!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Tổng cộng: $($salesSuccess + $inventorySuccess) records đã được import thành công" -ForegroundColor Green
Write-Host "Lỗi: $($salesFailed + $inventoryFailed) records" -ForegroundColor $(if ($salesFailed + $inventoryFailed -gt 0) { "Red" } else { "Green" })

# ===== KIỂM TRA DỮ LIỆU =====
Write-Host "`n=== Kiểm tra dữ liệu đã import ===" -ForegroundColor Yellow

try {
    $salesCheck = Invoke-RestMethod -Uri "$BaseUrl/sales-summary" -Method Get -Headers $auth
    Write-Host "✓ Sales Summary: $($salesCheck.count) records" -ForegroundColor Green

    $inventoryCheck = Invoke-RestMethod -Uri "$BaseUrl/inventory-summary" -Method Get -Headers $auth
    Write-Host "✓ Inventory Summary: $($inventoryCheck.count) records" -ForegroundColor Green

    $summaryCheck = Invoke-RestMethod -Uri "$BaseUrl/summary" -Method Get -Headers $auth
    Write-Host "✓ Summary Metrics:" -ForegroundColor Green
    Write-Host "  - Total Sales: $($summaryCheck.metrics.totalSales)" -ForegroundColor Cyan
    Write-Host "  - Total Revenue: $($summaryCheck.metrics.totalRevenue)" -ForegroundColor Cyan
    Write-Host "  - Active Dealers: $($summaryCheck.metrics.activeDealers)/$($summaryCheck.metrics.totalDealers)" -ForegroundColor Cyan

} catch {
    Write-Host "✗ Không thể kiểm tra dữ liệu: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host "`nBạn có thể test các endpoints khác tại: http://localhost:5208/swagger" -ForegroundColor Yellow
Write-Host "Xem hướng dẫn chi tiết trong file: TESTING.md`n" -ForegroundColor Yellow
