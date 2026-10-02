# ============================================
# COMPREHENSIVE TEST SCRIPT - All Notification Flows
# ============================================
# Assert thật ở mọi bước — exit code != 0 nếu flow fail:
#   buoc 1: prereq + chot baseline (RabbitMQ deliver counter, noi dung log)
#   buoc 2-3: API call that bai -> exit 1 ngay
#   buoc 4: mgmt API chung minh message DA duoc deliver cho consumer
#           (deliver delta > 0 voi consumers >= 1) — khong phai "check tay"
#   buoc 5: log NotificationService co dong MOI (so voi baseline) cua ca 2
#           event + terminal outcome (push sent / no token)
#   buoc 6: in ✅/❌ theo ket qua that; exit 1 neu buoc 4/5 fail

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  EV DEALER - NOTIFICATION TEST SUITE  " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Test Configuration
$SalesServiceUrl = "http://localhost:5003"
$VehicleServiceUrl = "http://localhost:5068"
$NotificationServiceUrl = "http://localhost:5051"
$RabbitMQUrl = "http://localhost:15672"
# docker-compose dat RABBITMQ_DEFAULT_USER/PASS = guest/guest
$RabbitMQAuth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("guest:guest"))
$QueuesToVerify = @("sales.completed", "vehicle.reserved")

# Doc 1 queue qua mgmt API — tra ve state + so message DA deliver (counter tich luy)
function Get-QueueStat {
    param([string]$Queue)
    try {
        $q = Invoke-RestMethod -Uri "$RabbitMQUrl/api/queues/%2F/$Queue" `
            -Headers @{ Authorization = "Basic $RabbitMQAuth" } -TimeoutSec 5
        $deliver = 0
        if ($q.message_stats) {
            if ($null -ne $q.message_stats.deliver_get) { $deliver = $q.message_stats.deliver_get }
            elseif ($null -ne $q.message_stats.deliver) { $deliver = $q.message_stats.deliver }
        }
        return [pscustomobject]@{
            Ok        = $true
            Consumers = [int]$q.consumers
            Ready     = [int]$q.messages_ready
            Unacked   = [int]$q.messages_unacknowledged
            Deliver   = [int]$deliver
            Error     = $null
        }
    } catch {
        $code = "conn"
        try { $code = [int]$_.Exception.Response.StatusCode } catch { }
        return [pscustomobject]@{
            Ok = $false; Consumers = -1; Ready = -1; Unacked = -1; Deliver = -1
            Error = "HTTP $code ($($_.Exception.Message))"
        }
    }
}

# ============================================
# 1. CHECK PREREQUISITES + BASELINE
# ============================================
Write-Host "[1/6] Checking Prerequisites..." -ForegroundColor Yellow

# Check RabbitMQ
Write-Host "  - Checking RabbitMQ..." -NoNewline
try {
    $rabbitResponse = Invoke-WebRequest -Uri $RabbitMQUrl -TimeoutSec 3 -UseBasicParsing -ErrorAction Stop
    Write-Host " OK" -ForegroundColor Green
} catch {
    Write-Host " FAILED" -ForegroundColor Red
    Write-Host "    RabbitMQ is not running on port 15672" -ForegroundColor Red
    exit 1
}

# Check NotificationService
Write-Host "  - Checking NotificationService..." -NoNewline
try {
    $notifResponse = Invoke-RestMethod -Uri "$NotificationServiceUrl/health" -TimeoutSec 3
    Write-Host " OK" -ForegroundColor Green
} catch {
    Write-Host " FAILED" -ForegroundColor Red
    Write-Host "    NotificationService is not running on port 5051" -ForegroundColor Red
    exit 1
}

# Check SalesService
Write-Host "  - Checking SalesService..." -NoNewline
try {
    $salesResponse = Invoke-RestMethod -Uri "$SalesServiceUrl/api/orders/health" -TimeoutSec 3
    Write-Host " OK" -ForegroundColor Green
} catch {
    Write-Host " FAILED" -ForegroundColor Red
    Write-Host "    SalesService is not running on port 5003" -ForegroundColor Red
    exit 1
}

# Check VehicleService
Write-Host "  - Checking VehicleService..." -NoNewline
try {
    $vehicleResponse = Invoke-RestMethod -Uri "$VehicleServiceUrl/health" -TimeoutSec 3
    Write-Host " OK" -ForegroundColor Green
} catch {
    Write-Host " FAILED" -ForegroundColor Red
    Write-Host "    VehicleService is not running on port 5068" -ForegroundColor Red
    exit 1
}

# Baselines: buoc 4/5 can so MOI de chung minh message/log do CHINH run nay sinh ra
# (khong duoc tin counter/log tu cac lan truoc). Doc that bai -> baseline 0,
# buoc 4 se phan doan lai (401/404/conn deu FAIL o do).
Write-Host "  - Capturing baselines (RabbitMQ deliver counters + log)..." -NoNewline
$queueBaseline = @{}
foreach ($queue in $QueuesToVerify) {
    $stat = Get-QueueStat $queue
    if ($stat.Ok) { $queueBaseline[$queue] = $stat.Deliver } else { $queueBaseline[$queue] = 0 }
}
# Log: chot baseline TUNG file notification-service-*.log (khong hardcode ngay —
# container Docker tz UTC co the dang ghi vao file ngay khac ngay host).
$logDir = Join-Path $PSScriptRoot "NotificationService\Logs"
$logGlob = Join-Path $logDir "notification-service-*.log"
$baselineMap = @{}
foreach ($f in Get-ChildItem -Path $logGlob -ErrorAction SilentlyContinue) {
    $content = Get-Content -Path $f.FullName -Raw -ErrorAction SilentlyContinue
    if ($null -eq $content) { $content = "" }
    $baselineMap[$f.FullName] = $content
}
Write-Host " OK" -ForegroundColor Green

Write-Host ""

# ============================================
# 2. TEST VEHICLE RESERVATION FLOW (FCM push)
# ============================================
Write-Host "[2/6] Testing Vehicle Reservation Flow (FCM push)..." -ForegroundColor Yellow

# Body khop ReservationRequestDto (VehicleService) - vehicleId nam trong URL
$reservationData = @{
    customerName = "Test User - Vehicle"
    customerEmail = "vehicle-test@example.com"
    customerPhone = "+84912345678"
    quantity = 1
    notes = "Test reservation from automated test script"
} | ConvertTo-Json

Write-Host "  - Sending reservation request..." -NoNewline
try {
    $reservationResponse = Invoke-RestMethod -Uri "$VehicleServiceUrl/api/vehicles/1/reserve" -Method Post -Body $reservationData -ContentType "application/json" -TimeoutSec 10
    Write-Host " OK" -ForegroundColor Green
    Write-Host "    Reserved vehicle: $($reservationResponse.reservation.vehicleId) ($($reservationResponse.reservation.vehicleName))" -ForegroundColor Gray
    $vehicleReservationId = $reservationResponse.reservation.vehicleId
} catch {
    Write-Host " FAILED" -ForegroundColor Red
    Write-Host "    Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host "  - Waiting for VehicleReservedEvent -> FCM push (3 seconds)..." -NoNewline
Start-Sleep -Seconds 3
Write-Host " Done" -ForegroundColor Green

Write-Host ""

# ============================================
# 3. TEST ORDER COMPLETION FLOW (FCM push)
# ============================================
Write-Host "[3/6] Testing Order Completion Flow (FCM push)..." -ForegroundColor Yellow

# Body khop DTO CreateOrderRequest (SalesService) - unitPrice > 0 thi TotalPrice
# tinh o backend moi > 0, khong vay 400. quoteId=0 -> bo qua buoc convert quote.
$orderData = @{
    quoteId = 0
    customerId = 1
    customerName = "Test User - Order"
    customerEmail = "order-test@example.com"
    dealerId = 1
    salespersonId = 1
    vehicleId = 1
    vehicleVariantId = 1
    colorId = 1
    quantity = 1
    unitPrice = 1500000000
    paymentMethod = "Cash"
    paymentType = "Full"
    deliveryDate = (Get-Date).AddDays(30).ToString("yyyy-MM-ddTHH:mm:ss")
    estimatedDeliveryDate = (Get-Date).AddDays(30).ToString("yyyy-MM-ddTHH:mm:ss")
} | ConvertTo-Json

Write-Host "  - Sending order completion request..." -NoNewline
try {
    $orderResponse = Invoke-RestMethod -Uri "$SalesServiceUrl/api/orders/complete" -Method Post -Body $orderData -ContentType "application/json" -TimeoutSec 10
    Write-Host " OK" -ForegroundColor Green
    Write-Host "    Order ID: $($orderResponse.orderId) ($($orderResponse.orderNumber))" -ForegroundColor Gray
    $orderId = $orderResponse.orderId
} catch {
    Write-Host " FAILED" -ForegroundColor Red
    Write-Host "    Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host "  - Waiting for SaleCompletedEvent -> FCM push (3 seconds)..." -NoNewline
Start-Sleep -Seconds 3
Write-Host " Done" -ForegroundColor Green

Write-Host ""

# ============================================
# 4. VERIFY RABBITMQ QUEUES (assert qua mgmt API)
# ============================================
Write-Host "[4/6] Verifying RabbitMQ Message Processing..." -ForegroundColor Yellow

$step4Pass = $true
$step4Detail = @{}

foreach ($queue in $QueuesToVerify) {
    Write-Host "  - ${queue}: message delivered to consumer..." -NoNewline
    # Poll den khi deliver delta >= 1 (consumer nhan message) — max 15s
    $deadline = (Get-Date).AddSeconds(15)
    do {
        $stat = Get-QueueStat $queue
        $delta = -1
        if ($stat.Ok) { $delta = $stat.Deliver - [int]$queueBaseline[$queue] }
        if ($stat.Ok -and $stat.Consumers -ge 1 -and $delta -ge 1) { break }
        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)

    if (-not $stat.Ok) {
        Write-Host " FAILED" -ForegroundColor Red
        Write-Host "    mgmt API khong doc duoc queue '$queue': $($stat.Error)" -ForegroundColor Red
        $step4Pass = $false
        $step4Detail[$queue] = "mgmt API loi: $($stat.Error)"
    } elseif ($stat.Consumers -lt 1) {
        Write-Host " FAILED" -ForegroundColor Red
        Write-Host "    consumers = 0 — consumer khong attach (chet/am tham hoac chua start)" -ForegroundColor Red
        $step4Pass = $false
        $step4Detail[$queue] = "consumers=0, ready=$($stat.Ready)"
    } elseif ($delta -lt 1) {
        Write-Host " FAILED" -ForegroundColor Red
        Write-Host "    deliver delta = 0 — khac giua baseline va sau run khong doi" -ForegroundColor Red
        Write-Host "    (publish loi, routing khong match, hoac consumer khong consume)" -ForegroundColor Red
        Write-Host "    state: ready=$($stat.Ready) unacked=$($stat.Unacked) totalDeliver=$($stat.Deliver) baseline=$($queueBaseline[$queue])" -ForegroundColor Gray
        $step4Pass = $false
        $step4Detail[$queue] = "deliver delta=0 (total=$($stat.Deliver), baseline=$($queueBaseline[$queue]))"
    } else {
        Write-Host " OK" -ForegroundColor Green
        Write-Host "    delivered +$delta, consumers=$($stat.Consumers), ready=$($stat.Ready), unacked=$($stat.Unacked)" -ForegroundColor Gray
        $step4Detail[$queue] = "+$delta delivered, consumers=$($stat.Consumers)"
    }
}

Write-Host ""

# ============================================
# 5. VERIFY NOTIFICATIONSERVICE LOG (dong MOI so voi baseline)
# ============================================
Write-Host "[5/6] Verifying NotificationService Log Evidence..." -ForegroundColor Yellow

# Doc TUNG file log, cat bo phan da co tu luc baseline -> chi tin DONG MOI cua
# run nay (lan test truoc khong the gia pass). File xuat hien sau baseline
# (roll sang ngay moi) -> toan bo noi dung la cua run nay.
function Get-NewLogPart {
    $files = @(Get-ChildItem -Path $logGlob -ErrorAction SilentlyContinue)
    if ($files.Count -eq 0) { return $null }
    $parts = @()
    foreach ($f in $files) {
        $now = Get-Content -Path $f.FullName -Raw -ErrorAction SilentlyContinue
        if ($null -eq $now) { $now = "" }
        $b = ""
        if ($baselineMap.ContainsKey($f.FullName)) { $b = $baselineMap[$f.FullName] }
        if ($b.Length -eq 0) {
            $parts += $now   # file moi (hoac baseline rong) -> ca file la MOI
        } elseif ($now.Length -ge $b.Length -and $now.StartsWith($b)) {
            $parts += $now.Substring($b.Length)
        } else {
            $parts += $now   # append khong con la prefix (bat thuong) -> fallback
        }
    }
    return ($parts -join "`n")
}

# Needle ASCII chi tim trong .cs consumer (khong chua emoji -> doc dung ANSI cung khop)
$checks = @(
    @{ Name = "Processing VehicleReservedEvent"; Alt = @() },
    @{ Name = "Processing SaleCompletedEvent";   Alt = @() },
    @{ Name = "vehicle push outcome"; Alt = @("Reservation confirmation push notification sent", "No device token found for Vehicle") },
    @{ Name = "order push outcome";   Alt = @("Push notification sent successfully for Order", "No device token registered for") }
)

function Test-Check {
    param([string]$Text, [hashtable]$Check)
    if ($Check.Alt.Count -gt 0) {
        foreach ($needle in $Check.Alt) { if ($Text.Contains($needle)) { return $true } }
        return $false
    }
    return $Text.Contains($Check.Name)
}

$step5Pass = $true
$missing = @()
$deadline = (Get-Date).AddSeconds(15)
do {
    $part = Get-NewLogPart
    $missing = @()
    if ($null -eq $part) {
        $missing = @("khong co file log nao trong: $logDir")
        break
    }
    foreach ($check in $checks) {
        if (-not (Test-Check -Text $part -Check $check)) { $missing += $check.Name }
    }
    if ($missing.Count -eq 0) { break }
    Start-Sleep -Seconds 1
} while ((Get-Date) -lt $deadline)

if ($missing -contains "khong co file log nao trong: $logDir") {
    # Khong co log -> moi check deu khong the ket luan, khong duoc in OK
    foreach ($check in $checks) {
        Write-Host "  - $($check.Name)... MISSING (no log file)" -ForegroundColor Red
    }
    Write-Host "  - log dir: $logDir" -ForegroundColor Red
    Write-Host "    Khong co file notification-service-*.log — NotificationService chay tu thu muc khac hoac File sink bi loi" -ForegroundColor Red
    $step5Pass = $false
} else {
    foreach ($check in $checks) {
        Write-Host "  - $($check.Name)..." -NoNewline
        if ($missing -contains $check.Name) {
            Write-Host " MISSING" -ForegroundColor Red
            $step5Pass = $false
        } else {
            Write-Host " OK" -ForegroundColor Green
        }
    }
}
Write-Host "  - Source: $logGlob (chi tinh dong MOI sau baseline)" -ForegroundColor Gray

Write-Host ""

# ============================================
# 6. TEST SUMMARY (✅/❌ theo ket qua THAT)
# ============================================
Write-Host "[6/6] Test Summary" -ForegroundColor Yellow
Write-Host ""

Write-Host "  ✅ Vehicle Reservation Flow (FCM push)" -ForegroundColor Green
Write-Host "     - API Call: SUCCESS, Vehicle ID: $vehicleReservationId" -ForegroundColor Gray
Write-Host "  ✅ Order Completion Flow (FCM push)" -ForegroundColor Green
Write-Host "     - API Call: SUCCESS, Order ID: $orderId" -ForegroundColor Gray
Write-Host ""

if ($step4Pass) {
    Write-Host "  ✅ RabbitMQ delivery (step 4)" -ForegroundColor Green
} else {
    Write-Host "  ❌ RabbitMQ delivery (step 4)" -ForegroundColor Red
}
foreach ($queue in $QueuesToVerify) {
    Write-Host "     - ${queue}: $($step4Detail[$queue])" -ForegroundColor Gray
}

if ($step5Pass) {
    Write-Host "  ✅ Log evidence — dong MOI sau baseline (step 5)" -ForegroundColor Green
} else {
    Write-Host "  ❌ Log evidence (step 5)" -ForegroundColor Red
    foreach ($m in $missing) { Write-Host "     - MISSING: $m" -ForegroundColor Red }
}
Write-Host ""

# ============================================
# NEXT STEPS (chi con buoc tay neu can drill-down)
# ============================================
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  NEXT STEPS                            " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. Chi can khi muon xem chi tiet (da assert o buoc 4):" -ForegroundColor Yellow
Write-Host "   RabbitMQ UI: $RabbitMQUrl -> Queues -> sales.completed / vehicle.reserved" -ForegroundColor Gray
Write-Host "2. Log day du (da assert o buoc 5): $logDir" -ForegroundColor Yellow
Write-Host ""
Write-Host "3. Khong co email/SMS inbox trong he thong nay — chi FCM push" -ForegroundColor Yellow
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
if ($step4Pass -and $step5Pass) {
    Write-Host "  TEST COMPLETED SUCCESSFULLY! 🎉       " -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    exit 0
} else {
    Write-Host "  TEST FAILED — xem ❌ o tren            " -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Cyan
    exit 1
}
