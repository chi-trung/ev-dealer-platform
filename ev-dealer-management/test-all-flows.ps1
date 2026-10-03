# ============================================
# COMPREHENSIVE TEST SCRIPT - All Notification Flows
# ============================================
# Assert thật ở mọi bước — exit code != 0 nếu flow fail:
#   buoc 1: prereq (RabbitMQ + 5 service + JWT) + chot baseline (RabbitMQ
#           deliver counter, noi dung log)
#   buoc 2-4: API call that bai -> exit 1 ngay
#   buoc 5: mgmt API chung minh message DA duoc deliver cho consumer
#           (deliver delta > 0 voi consumers >= 1) — khong phai "check tay"
#   buoc 6: log NotificationService co dong MOI (so voi baseline) cua ca 3
#           event + terminal outcome (push sent / no token)
#   buoc 7: in ✅/❌ theo ket qua that; exit 1 neu buoc 5/6 fail

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  EV DEALER - NOTIFICATION TEST SUITE  " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Test Configuration
$SalesServiceUrl = "http://localhost:5003"
$VehicleServiceUrl = "http://localhost:5068"
$NotificationServiceUrl = "http://localhost:5051"
$CustomerServiceUrl = "http://localhost:5039"
$RabbitMQUrl = "http://localhost:15672"
# docker-compose dat RABBITMQ_DEFAULT_USER/PASS = guest/guest
$RabbitMQAuth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("guest:guest"))
$QueuesToVerify = @("sales.completed", "vehicle.reserved", "testdrive.scheduled")

# JWT: ca 7 service deu dung chung Jwt:Key/Issuer/Audience trong appsettings
# (da verify). 3 POST trong script nay deu [Authorize] (#137) nen bat buoc co
# token. Pattern mint giong import-test-data.ps1 (PR #170 da verify live).
# -Role: claim "role" bi inbound-map thanh ClaimTypes.Role (verify tren
# CustomerService: sub-only -> 403, role=Admin -> 200 tren
# [Authorize(Roles="Admin,DealerManager,EVMStaff")]) — chi can cho buoc 4a.
function New-DevJwt {
    param([string]$Role = "")

    $cfgPath = Join-Path $PSScriptRoot "CustomerService\appsettings.json"
    $jwt = (Get-Content $cfgPath -Raw | ConvertFrom-Json).Jwt
    if (-not $jwt.Key) { throw "Jwt.Key thieu trong $cfgPath" }

    function ConvertTo-Base64Url([byte[]]$Bytes) {
        [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    }
    $h = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $payload = [ordered]@{ sub = "1"; iat = $now; exp = $now + 3600; iss = $jwt.Issuer; aud = $jwt.Audience }
    if ($Role) { $payload["role"] = $Role }
    $p = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes(($payload | ConvertTo-Json -Compress)))
    # New-Object HMACSHA256() rong + .Key = ... (truong constructor byte[] bi PS unroll)
    $hmac = New-Object System.Security.Cryptography.HMACSHA256
    $hmac.Key = [byte[]][Text.Encoding]::UTF8.GetBytes($jwt.Key)
    $sig = ConvertTo-Base64Url ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes("$h.$p")))
    return "$h.$p.$sig"
}

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
Write-Host "[1/7] Checking Prerequisites..." -ForegroundColor Yellow

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

# Check CustomerService (buoc 4 POST /api/testdrives)
Write-Host "  - Checking CustomerService..." -NoNewline
try {
    $customerResponse = Invoke-RestMethod -Uri "$CustomerServiceUrl/health" -TimeoutSec 3
    Write-Host " OK" -ForegroundColor Green
} catch {
    Write-Host " FAILED" -ForegroundColor Red
    Write-Host "    CustomerService is not running on port 5039" -ForegroundColor Red
    exit 1
}

# JWT — 3 POST sau deu [Authorize] (#137); mint loi -> dung ngay, khong de 401
# noi suy o tung buoc
Write-Host "  - Minting JWT..." -NoNewline
try {
    $token = New-DevJwt
    $auth = @{ Authorization = "Bearer $token" }
    Write-Host " OK" -ForegroundColor Green
} catch {
    Write-Host " FAILED" -ForegroundColor Red
    Write-Host "    $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Baselines: buoc 5/6 can so MOI de chung minh message/log do CHINH run nay sinh ra
# (khong duoc tin counter/log tu cac lan truoc). Doc that bai -> baseline 0,
# buoc 5 se phan doan lai (401/404/conn deu FAIL o do).
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
Write-Host "[2/7] Testing Vehicle Reservation Flow (FCM push)..." -ForegroundColor Yellow

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
    $reservationResponse = Invoke-RestMethod -Uri "$VehicleServiceUrl/api/vehicles/1/reserve" -Method Post -Headers $auth -Body $reservationData -ContentType "application/json" -TimeoutSec 10
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
Write-Host "[3/7] Testing Order Completion Flow (FCM push)..." -ForegroundColor Yellow

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
    $orderResponse = Invoke-RestMethod -Uri "$SalesServiceUrl/api/orders/complete" -Method Post -Headers $auth -Body $orderData -ContentType "application/json" -TimeoutSec 10
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
# 4. TEST TEST-DRIVE SCHEDULING FLOW (FCM push)
# ============================================
Write-Host "[4/7] Testing Test-Drive Scheduling Flow (FCM push)..." -ForegroundColor Yellow

# 4a. Ensure customer — FK THAT SU: TestDrives co
# FK_TestDrives_Customers_CustomerId REFERENCES Customers(Id) (doc schema
# tu sqlite, va reproduce duoc: insert customerId khong ton tai -> 500
# "FOREIGN KEY constraint failed"). Customers trong -> phai tao truoc.
# POST /api/customers can role (Admin/DealerManager/EVMStaff): sub-only -> 403,
# role=Admin -> 200 (da verify). Email timestamp tranh 409 khi chay lai.
Write-Host "  - Ensuring customer exists (FK TestDrives.CustomerId)..." -NoNewline
try {
    $roleAuth = @{ Authorization = "Bearer $(New-DevJwt -Role Admin)" }
    $customers = Invoke-RestMethod -Uri "$CustomerServiceUrl/api/customers" -Headers $roleAuth -TimeoutSec 10
    $cust = @($customers) | Sort-Object id | Select-Object -First 1
    if ($cust) {
        $customerId = $cust.id
        Write-Host " OK (reuse id=$customerId)" -ForegroundColor Green
    } else {
        $custBody = @{
            name = "E2E Test Drive Customer"
            email = "e2e-$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())@test.local"
            dealerId = 1
            password = "E2ePassw0rd!"
            status = "Active"
        } | ConvertTo-Json
        $created = Invoke-RestMethod -Uri "$CustomerServiceUrl/api/customers" -Method Post -Headers $roleAuth -Body $custBody -ContentType "application/json" -TimeoutSec 20
        if ($null -eq $created.id) { throw "POST /api/customers khong tra ve id" }
        $customerId = $created.id
        Write-Host " OK (created id=$customerId)" -ForegroundColor Green
    }
} catch {
    Write-Host " FAILED" -ForegroundColor Red
    Write-Host "    Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# 4b. Body khop CreateTestDriveRequest (CustomerService) — POST can JWT (#137).
# status BAT BUOC: CreateMap<CreateTestDriveRequest,TestDrive>
# (MappingProfile.cs:14) map null -> ghi de default "Da len lich" -> SQLite
# NOT NULL failed (500). Gui nhu TestDriveForm.jsx:66.
$testDriveData = @{
    customerId = $customerId
    vehicleId = 1
    dealerId = 1
    appointmentDate = (Get-Date).AddDays(3).ToString("yyyy-MM-ddTHH:mm:ss")
    status = "Đã lên lịch"
    notes = "Test drive from automated test script"
} | ConvertTo-Json

Write-Host "  - Sending test-drive request..." -NoNewline
try {
    $testDriveResponse = Invoke-RestMethod -Uri "$CustomerServiceUrl/api/testdrives" -Method Post -Headers $auth -Body $testDriveData -ContentType "application/json" -TimeoutSec 10
    if ($null -eq $testDriveResponse.id) {
        throw "response khong co field 'id' — khong phai CreatedAtAction TestDriveDto"
    }
    Write-Host " OK" -ForegroundColor Green
    Write-Host "    Test Drive ID: $($testDriveResponse.id) (status: $($testDriveResponse.status))" -ForegroundColor Gray
    $testDriveId = $testDriveResponse.id
} catch {
    Write-Host " FAILED" -ForegroundColor Red
    Write-Host "    Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host "  - Waiting for TestDriveScheduledEvent -> FCM push (3 seconds)..." -NoNewline
Start-Sleep -Seconds 3
Write-Host " Done" -ForegroundColor Green

Write-Host ""

# ============================================
# 5. VERIFY RABBITMQ QUEUES (assert qua mgmt API)
# ============================================
Write-Host "[5/7] Verifying RabbitMQ Message Processing..." -ForegroundColor Yellow

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
# 6. VERIFY NOTIFICATIONSERVICE LOG (dong MOI so voi baseline)
# ============================================
Write-Host "[6/7] Verifying NotificationService Log Evidence..." -ForegroundColor Yellow

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
    @{ Name = "Processing TestDriveScheduledEvent"; Alt = @() },
    @{ Name = "vehicle push outcome"; Alt = @("Reservation confirmation push notification sent", "No device token found for Vehicle") },
    @{ Name = "order push outcome";   Alt = @("Push notification sent successfully for Order", "No device token registered for") },
    @{ Name = "testdrive push outcome"; Alt = @("Test drive confirmation push notification sent", "No device token for TestDrive event") }
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
# 7. TEST SUMMARY (✅/❌ theo ket qua THAT)
# ============================================
Write-Host "[7/7] Test Summary" -ForegroundColor Yellow
Write-Host ""

Write-Host "  ✅ Vehicle Reservation Flow (FCM push)" -ForegroundColor Green
Write-Host "     - API Call: SUCCESS, Vehicle ID: $vehicleReservationId" -ForegroundColor Gray
Write-Host "  ✅ Order Completion Flow (FCM push)" -ForegroundColor Green
Write-Host "     - API Call: SUCCESS, Order ID: $orderId" -ForegroundColor Gray
Write-Host "  ✅ Test-Drive Scheduling Flow (FCM push)" -ForegroundColor Green
Write-Host "     - API Call: SUCCESS, Test Drive ID: $testDriveId" -ForegroundColor Gray
Write-Host ""

if ($step4Pass) {
    Write-Host "  ✅ RabbitMQ delivery (step 5)" -ForegroundColor Green
} else {
    Write-Host "  ❌ RabbitMQ delivery (step 5)" -ForegroundColor Red
}
foreach ($queue in $QueuesToVerify) {
    Write-Host "     - ${queue}: $($step4Detail[$queue])" -ForegroundColor Gray
}

if ($step5Pass) {
    Write-Host "  ✅ Log evidence — dong MOI sau baseline (step 6)" -ForegroundColor Green
} else {
    Write-Host "  ❌ Log evidence (step 6)" -ForegroundColor Red
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
Write-Host "1. Chi can khi muon xem chi tiet (da assert o buoc 5):" -ForegroundColor Yellow
Write-Host "   RabbitMQ UI: $RabbitMQUrl -> Queues -> sales.completed / vehicle.reserved / testdrive.scheduled" -ForegroundColor Gray
Write-Host "2. Log day du (da assert o buoc 6): $logDir" -ForegroundColor Yellow
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
