# ============================================
# HEALTH CHECK SCRIPT - All Services
# ============================================
# 7/7 services: gateway :5036 (aggregate /health) + 6 backend
# (Notification/Sales/Vehicle/User/Customer/Reporting), plus RabbitMQ
# (infra) and frontend (optional).

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  EV DEALER - HEALTH CHECK              " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$allHealthy = $true

# ============================================
# 1. CHECK RABBITMQ
# ============================================
Write-Host "[1/9] RabbitMQ..." -NoNewline
try {
    $response = Invoke-WebRequest -Uri "http://localhost:15672" -TimeoutSec 3 -UseBasicParsing -ErrorAction Stop
    Write-Host " ✅ Running" -ForegroundColor Green
    Write-Host "      UI: http://localhost:15672 (guest/guest)" -ForegroundColor Gray
} catch {
    Write-Host " ❌ Not responding" -ForegroundColor Red
    $allHealthy = $false
}

# ============================================
# 2. CHECK APIGATEWAY (aggregate /health)
# ============================================
# Gateway /health ping 6 upstream song song tra ve 200 (tat ca healthy)
# hoac 503 (co upstream down) — JSON gom { summary, services[] }.
Write-Host "[2/9] APIGateway (Port 5036, aggregate /health)..." -NoNewline
try {
    $response = Invoke-RestMethod -Uri "http://localhost:5036/health" -TimeoutSec 10 -ErrorAction Stop
    Write-Host " ✅ $($response.summary)" -ForegroundColor Green
} catch {
    $statusCode = 0
    try { $statusCode = [int]$_.Exception.Response.StatusCode } catch { }
    if ($statusCode -eq 503) {
        Write-Host " ❌ Unhealthy (HTTP 503) — 1+ upstream down" -ForegroundColor Red
        Write-Host "      Detail: xem cac service phia duoi, hoac GET http://localhost:5036/health" -ForegroundColor Gray
    } elseif ($statusCode -gt 0) {
        Write-Host " ❌ HTTP $statusCode" -ForegroundColor Red
    } else {
        Write-Host " ❌ Not responding" -ForegroundColor Red
        Write-Host "      Start: cd APIGatewayService; dotnet run" -ForegroundColor Yellow
    }
    $allHealthy = $false
}

# ============================================
# 3. CHECK NOTIFICATIONSERVICE
# ============================================
# Cac service co RabbitMQ health check (3/4/5/7): RabbitMQ down thi /health
# can ~4s de tra 503 (do that: 4049-4120ms). TimeoutSec 3 bi cancel truoc do,
# statusCode=0 -> in "Not responding" (sai, service van lang nghe) nen dung 8.
Write-Host "[3/9] NotificationService (Port 5051)..." -NoNewline
try {
    $response = Invoke-RestMethod -Uri "http://localhost:5051/health" -TimeoutSec 8 -ErrorAction Stop
    Write-Host " ✅ Healthy" -ForegroundColor Green
    Write-Host "      Status: $($response.status)" -ForegroundColor Gray
} catch {
    $statusCode = 0
    try { $statusCode = [int]$_.Exception.Response.StatusCode } catch { }
    if ($statusCode -eq 503) {
        Write-Host " ❌ Unhealthy (HTTP 503)" -ForegroundColor Red
        Write-Host "      Detail: GET http://localhost:5051/health — xem entries" -ForegroundColor Gray
    } elseif ($statusCode -gt 0) {
        Write-Host " ❌ HTTP $statusCode" -ForegroundColor Red
    } else {
        Write-Host " ❌ Not responding" -ForegroundColor Red
        Write-Host "      Start: cd NotificationService; dotnet run" -ForegroundColor Yellow
    }
    $allHealthy = $false
}

# ============================================
# 4. CHECK SALESSERVICE
# ============================================
Write-Host "[4/9] SalesService (Port 5003)..." -NoNewline
try {
    $response = Invoke-RestMethod -Uri "http://localhost:5003/api/orders/health" -TimeoutSec 8 -ErrorAction Stop
    Write-Host " ✅ Healthy" -ForegroundColor Green
    Write-Host "      Status: $($response.status)" -ForegroundColor Gray
} catch {
    $statusCode = 0
    try { $statusCode = [int]$_.Exception.Response.StatusCode } catch { }
    if ($statusCode -eq 503) {
        Write-Host " ❌ Unhealthy (HTTP 503)" -ForegroundColor Red
        Write-Host "      Detail: GET http://localhost:5003/api/orders/health" -ForegroundColor Gray
    } elseif ($statusCode -gt 0) {
        Write-Host " ❌ HTTP $statusCode" -ForegroundColor Red
    } else {
        Write-Host " ❌ Not responding" -ForegroundColor Red
        Write-Host "      Start: cd SalesService; dotnet run" -ForegroundColor Yellow
    }
    $allHealthy = $false
}

# ============================================
# 5. CHECK VEHICLESERVICE
# ============================================
Write-Host "[5/9] VehicleService (Port 5068)..." -NoNewline
try {
    $response = Invoke-RestMethod -Uri "http://localhost:5068/health" -TimeoutSec 8 -ErrorAction Stop
    Write-Host " ✅ Healthy" -ForegroundColor Green
} catch {
    $statusCode = 0
    try { $statusCode = [int]$_.Exception.Response.StatusCode } catch { }
    if ($statusCode -eq 503) {
        Write-Host " ❌ Unhealthy (HTTP 503)" -ForegroundColor Red
        Write-Host "      Detail: GET http://localhost:5068/health — xem entries" -ForegroundColor Gray
    } elseif ($statusCode -gt 0) {
        Write-Host " ❌ HTTP $statusCode" -ForegroundColor Red
    } else {
        Write-Host " ❌ Not responding" -ForegroundColor Red
        Write-Host "      Start: cd VehicleService; dotnet run" -ForegroundColor Yellow
    }
    $allHealthy = $false
}

# ============================================
# 6. CHECK USERSERVICE
# ============================================
Write-Host "[6/9] UserService (Port 7001)..." -NoNewline
try {
    $response = Invoke-RestMethod -Uri "http://localhost:7001/health" -TimeoutSec 3 -ErrorAction Stop
    Write-Host " ✅ Healthy" -ForegroundColor Green
    Write-Host "      Status: $($response.status)" -ForegroundColor Gray
} catch {
    $statusCode = 0
    try { $statusCode = [int]$_.Exception.Response.StatusCode } catch { }
    if ($statusCode -eq 503) {
        Write-Host " ❌ Unhealthy (HTTP 503)" -ForegroundColor Red
        Write-Host "      Detail: GET http://localhost:7001/health — xem entries" -ForegroundColor Gray
    } elseif ($statusCode -gt 0) {
        Write-Host " ❌ HTTP $statusCode" -ForegroundColor Red
    } else {
        Write-Host " ❌ Not responding" -ForegroundColor Red
        Write-Host "      Start: cd UserService; dotnet run" -ForegroundColor Yellow
    }
    $allHealthy = $false
}

# ============================================
# 7. CHECK CUSTOMERSERVICE
# ============================================
Write-Host "[7/9] CustomerService (Port 5039)..." -NoNewline
try {
    $response = Invoke-RestMethod -Uri "http://localhost:5039/health" -TimeoutSec 8 -ErrorAction Stop
    Write-Host " ✅ Healthy" -ForegroundColor Green
    Write-Host "      Status: $($response.status)" -ForegroundColor Gray
} catch {
    $statusCode = 0
    try { $statusCode = [int]$_.Exception.Response.StatusCode } catch { }
    if ($statusCode -eq 503) {
        Write-Host " ❌ Unhealthy (HTTP 503)" -ForegroundColor Red
        Write-Host "      Detail: GET http://localhost:5039/health — xem entries" -ForegroundColor Gray
    } elseif ($statusCode -gt 0) {
        Write-Host " ❌ HTTP $statusCode" -ForegroundColor Red
    } else {
        Write-Host " ❌ Not responding" -ForegroundColor Red
        Write-Host "      Start: cd CustomerService; dotnet run" -ForegroundColor Yellow
    }
    $allHealthy = $false
}

# ============================================
# 8. CHECK REPORTINGSERVICE
# ============================================
Write-Host "[8/9] ReportingService (Port 5208)..." -NoNewline
try {
    $response = Invoke-RestMethod -Uri "http://localhost:5208/health" -TimeoutSec 3 -ErrorAction Stop
    Write-Host " ✅ Healthy" -ForegroundColor Green
    Write-Host "      Status: $($response.status)" -ForegroundColor Gray
} catch {
    $statusCode = 0
    try { $statusCode = [int]$_.Exception.Response.StatusCode } catch { }
    if ($statusCode -eq 503) {
        # Khong co override nay /health tra 503 "Database is unreachable"
        # (appsettings khai DefaultConnection Postgres-format, provider lai sqlite)
        Write-Host " ❌ Unhealthy (HTTP 503)" -ForegroundColor Red
        Write-Host "      Detail: GET http://localhost:5208/health — thieu SQLite override thi DB down" -ForegroundColor Gray
    } elseif ($statusCode -gt 0) {
        Write-Host " ❌ HTTP $statusCode" -ForegroundColor Red
    } else {
        Write-Host " ❌ Not responding" -ForegroundColor Red
        Write-Host "      Start: cd ReportingService; `$env:ConnectionStrings__DefaultConnection='Data Source=reporting_dev.db'; dotnet run" -ForegroundColor Yellow
    }
    $allHealthy = $false
}

# ============================================
# 9. CHECK FRONTEND
# ============================================
Write-Host "[9/9] Frontend (Port 5173)..." -NoNewline
try {
    $response = Invoke-WebRequest -Uri "http://localhost:5173" -TimeoutSec 3 -UseBasicParsing -ErrorAction Stop
    Write-Host " ✅ Running" -ForegroundColor Green
    Write-Host "      URL: http://localhost:5173" -ForegroundColor Gray
} catch {
    Write-Host " ⚠️  Not running (Optional)" -ForegroundColor Yellow
    Write-Host "      Start: cd ev-dealer-frontend; npm run dev" -ForegroundColor Gray
}

Write-Host ""

# ============================================
# RABBITMQ QUEUE STATUS
# ============================================
Write-Host "RabbitMQ Queue Status:" -ForegroundColor Cyan
try {
    $cred = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("guest:guest"))
    $headers = @{ Authorization = "Basic $cred" }

    $queues = Invoke-RestMethod -Uri "http://localhost:15672/api/queues" -Headers $headers -TimeoutSec 3

    $relevantQueues = $queues | Where-Object { $_.name -in @("sales.completed", "vehicle.reserved", "testdrive.scheduled") }

    if ($relevantQueues.Count -gt 0) {
        foreach ($queue in $relevantQueues) {
            $ready = if ($queue.messages_ready) { $queue.messages_ready } else { 0 }
            $total = if ($queue.messages) { $queue.messages } else { 0 }
            $consumers = if ($queue.consumers) { $queue.consumers } else { 0 }

            Write-Host "  - $($queue.name):" -NoNewline
            if ($consumers -gt 0) {
                Write-Host " Ready=$ready, Total=$total, Consumers=$consumers ✅" -ForegroundColor Green
            } else {
                Write-Host " Ready=$ready, Total=$total, Consumers=$consumers ⚠️" -ForegroundColor Yellow
            }
        }
    } else {
        Write-Host "  ⚠️  No queues found (services may need to start first)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "  ❌ Unable to fetch queue status" -ForegroundColor Red
    Write-Host "     Check RabbitMQ UI manually: http://localhost:15672" -ForegroundColor Gray
}

Write-Host ""

# ============================================
# PORT STATUS
# ============================================
Write-Host "Port Status:" -ForegroundColor Cyan
$ports = @(
    @{ Port = 5036; Service = "APIGateway" },
    @{ Port = 5068; Service = "VehicleService" },
    @{ Port = 5003; Service = "SalesService" },
    @{ Port = 5051; Service = "NotificationService" },
    @{ Port = 7001; Service = "UserService" },
    @{ Port = 5039; Service = "CustomerService" },
    @{ Port = 5208; Service = "ReportingService" },
    @{ Port = 5672; Service = "RabbitMQ AMQP" },
    @{ Port = 15672; Service = "RabbitMQ UI" },
    @{ Port = 5173; Service = "Frontend (Optional)" }
)

foreach ($portInfo in $ports) {
    $port = $portInfo.Port
    $service = $portInfo.Service

    $listening = netstat -ano | Select-String ":$port " | Select-Object -First 1

    Write-Host "  - $port ($service):" -NoNewline
    if ($listening) {
        Write-Host " ✅ Listening" -ForegroundColor Green
    } else {
        if ($port -eq 5173) {
            Write-Host " ⚠️  Not listening (Optional)" -ForegroundColor Yellow
        } else {
            Write-Host " ❌ Not listening" -ForegroundColor Red
            $allHealthy = $false
        }
    }
}

Write-Host ""

# ============================================
# OVERALL RESULT
# ============================================
if ($allHealthy) {
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "  ALL CRITICAL SERVICES HEALTHY! ✅     " -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Ready to run tests:" -ForegroundColor Cyan
    Write-Host "  .\test-all-flows.ps1" -ForegroundColor Yellow
} else {
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "  SOME SERVICES ARE DOWN! ❌            " -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "To start all services:" -ForegroundColor Cyan
    Write-Host "  .\start-all-services.ps1" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Or start individually:" -ForegroundColor Cyan
    Write-Host "  cd NotificationService; dotnet run" -ForegroundColor Gray
    Write-Host "  cd SalesService; dotnet run" -ForegroundColor Gray
    Write-Host "  cd VehicleService; dotnet run" -ForegroundColor Gray
    Write-Host "  cd UserService; dotnet run" -ForegroundColor Gray
    Write-Host "  cd CustomerService; dotnet run" -ForegroundColor Gray
    Write-Host "  cd APIGatewayService; dotnet run" -ForegroundColor Gray
    Write-Host "  cd ReportingService  (xem README: can SQLite override)" -ForegroundColor Gray
}
