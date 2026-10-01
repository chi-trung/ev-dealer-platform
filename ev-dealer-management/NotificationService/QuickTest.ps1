# Quick smoke test for NotificationService — gọi thẳng các endpoint thật.
# Endpoint list verified from Controllers/NotificationController.cs (2026-10-01):
#   GET  /health
#   POST /api/notification/test-fcm
#   POST /api/notification/subscribe-topic
#   POST /api/notification/unsubscribe-topic
#   POST /api/notification/send-to-topic
#   POST /api/notification/send-multicast
#
# Kết quả từng endpoint:
#   PASS       — HTTP 2xx
#   REACHABLE  — HTTP 4xx (khác 404): endpoint tồn tại nhưng bị từ chối
#                (với device token placeholder thì FCM từ chối là bình thường)
#   FAIL       — HTTP 404 / 5xx / lỗi kết nối: endpoint sai hoặc service chết
# Exit code: 0 nếu không có FAIL, 1 nếu có FAIL.

param(
    [Parameter(Mandatory = $false)]
    [string]$BaseUrl = "http://localhost:5051",

    [Parameter(Mandatory = $false)]
    [string]$DeviceToken = "placeholder_device_token",

    [Parameter(Mandatory = $false)]
    [string]$Topic = "quicktest-topic"
)

$ErrorActionPreference = "Continue"

function Invoke-EndpointTest {
    param(
        [string]$Name,
        [string]$Method,
        [string]$Uri,
        [object]$Body = $null
    )

    Write-Host "`n--- Testing: $Name ---" -ForegroundColor Yellow
    Write-Host "URI: $Uri" -ForegroundColor Gray

    $params = @{ Uri = $Uri; Method = $Method }
    if ($Body) {
        $jsonBody = $Body | ConvertTo-Json -Depth 5
        Write-Host "Body: $jsonBody" -ForegroundColor DarkGray
        $params.Body = $jsonBody
        $params.ContentType = "application/json"
    }

    try {
        $response = Invoke-RestMethod @params
        Write-Host "PASS — HTTP success" -ForegroundColor Green
        $response | ConvertTo-Json -Depth 5 | Write-Host -ForegroundColor White
        return 'PASS'
    }
    catch {
        $statusCode = $null
        try { $statusCode = [int]$_.Exception.Response.StatusCode } catch { }

        if ($null -eq $statusCode) {
            Write-Host "FAIL — không kết nối được: $($_.Exception.Message)" -ForegroundColor Red
            return 'FAIL'
        }
        if ($statusCode -eq 404) {
            Write-Host "FAIL — HTTP 404: endpoint không tồn tại" -ForegroundColor Red
            return 'FAIL'
        }
        if ($statusCode -ge 400 -and $statusCode -lt 500) {
            Write-Host "REACHABLE — HTTP $statusCode (endpoint sống; token placeholder bị FCM từ chối là bình thường)" -ForegroundColor Yellow
            $errBody = $_.ErrorDetails.Message
            if ($errBody) { Write-Host "Response: $errBody" -ForegroundColor DarkGray }
            return 'REACHABLE'
        }
        Write-Host "FAIL — HTTP $statusCode" -ForegroundColor Red
        return 'FAIL'
    }
}

# Banner
Write-Host @"

╔═══════════════════════════════════════════════════╗
║     NotificationService - Quick Test Script       ║
╚═══════════════════════════════════════════════════╝

"@ -ForegroundColor Cyan

Write-Host "Base URL:    $BaseUrl" -ForegroundColor Gray
if ($DeviceToken -like "placeholder*") {
    Write-Host "DeviceToken: $DeviceToken (placeholder — FCM endpoints sẽ ra REACHABLE, không PASS; truyền -DeviceToken <token thật> để PASS)" -ForegroundColor Yellow
} else {
    Write-Host "DeviceToken: (đã truyền token thật)" -ForegroundColor Gray
}
Write-Host "Topic:       $Topic`n" -ForegroundColor Gray

$results = @()

$results += [pscustomobject]@{
    Name   = "GET /health"
    Result = Invoke-EndpointTest -Name "Health Check" -Method "GET" -Uri "$BaseUrl/health"
}

$results += [pscustomobject]@{
    Name   = "POST test-fcm"
    Result = Invoke-EndpointTest -Name "POST test-fcm" -Method "POST" -Uri "$BaseUrl/api/notification/test-fcm" -Body @{
        deviceToken = $DeviceToken
        title       = "Quick Test"
        body        = "Hello from QuickTest.ps1"
    }
}

$results += [pscustomobject]@{
    Name   = "POST subscribe-topic"
    Result = Invoke-EndpointTest -Name "POST subscribe-topic" -Method "POST" -Uri "$BaseUrl/api/notification/subscribe-topic" -Body @{
        deviceToken = $DeviceToken
        topic       = $Topic
    }
}

$results += [pscustomobject]@{
    Name   = "POST unsubscribe-topic"
    Result = Invoke-EndpointTest -Name "POST unsubscribe-topic" -Method "POST" -Uri "$BaseUrl/api/notification/unsubscribe-topic" -Body @{
        deviceToken = $DeviceToken
        topic       = $Topic
    }
}

$results += [pscustomobject]@{
    Name   = "POST send-to-topic"
    Result = Invoke-EndpointTest -Name "POST send-to-topic" -Method "POST" -Uri "$BaseUrl/api/notification/send-to-topic" -Body @{
        topic = $Topic
        title = "Quick Test"
        body  = "Hello from QuickTest.ps1"
    }
}

$results += [pscustomobject]@{
    Name   = "POST send-multicast"
    Result = Invoke-EndpointTest -Name "POST send-multicast" -Method "POST" -Uri "$BaseUrl/api/notification/send-multicast" -Body @{
        deviceTokens = @($DeviceToken)
        title        = "Quick Test"
        body         = "Hello from QuickTest.ps1"
    }
}

# Summary
$passCount = @($results | Where-Object { $_.Result -eq 'PASS' }).Count
$reachableCount = @($results | Where-Object { $_.Result -eq 'REACHABLE' }).Count
$failCount = @($results | Where-Object { $_.Result -eq 'FAIL' }).Count

Write-Host "`n`n╔═══════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║                  TEST SUMMARY                     ║" -ForegroundColor Cyan
Write-Host "╚═══════════════════════════════════════════════════╝" -ForegroundColor Cyan

$results | Format-Table -Property Name, Result -AutoSize | Out-String | Write-Host

Write-Host "PASS: $passCount  |  REACHABLE: $reachableCount  |  FAIL: $failCount" -ForegroundColor $(if ($failCount -eq 0) { "Green" } else { "Red" })

if ($failCount -gt 0) {
    Write-Host "`nCÓ ENDPOINT BỊ FAIL (404/5xx/lỗi kết nối) — kiểm tra service có chạy không." -ForegroundColor Red
    exit 1
}
Write-Host "`nKhông có FAIL — mọi endpoint đều phản hồi." -ForegroundColor Green
if ($reachableCount -gt 0) {
    Write-Host "(REACHABLE = endpoint sống nhưng bị 4xx — với token placeholder là bình thường.)" -ForegroundColor Yellow
}
exit 0

# Usage
<#
.EXAMPLE
.\QuickTest.ps1
Smoke test health + 5 FCM endpoint với token placeholder (FCM endpoints sẽ REACHABLE).

.EXAMPLE
.\QuickTest.ps1 -DeviceToken "real_device_token"
Chạy với device token thật để FCM endpoints PASS.

.EXAMPLE
.\QuickTest.ps1 -BaseUrl "http://localhost:5051" -Topic "my-topic"
Chỉ định service URL và topic riêng.
#>
