# Script to start all required services for Notification testing
# Usage: .\start-notification-test.ps1

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Starting Services for Notification Test" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Check if RabbitMQ is running
Write-Host "`n[1/5] Checking RabbitMQ..." -ForegroundColor Yellow
$rabbitMQ = docker ps --filter "name=rabbitmq" --format "{{.Names}}"
if ($rabbitMQ) {
    # docker-compose đặt tên container là evm_rabbitmq — filter "name=rabbitmq"
    # match cả hai, nên chỉ cần kiểm tra có output (trước đây -eq "rabbitmq"
    # luôn fail với evm_rabbitmq và kéo theo docker run container thứ hai).
    Write-Host "  ✅ RabbitMQ is already running" -ForegroundColor Green
} else {
    Write-Host "  ⚠️  RabbitMQ not running. Starting..." -ForegroundColor Yellow
    try {
        # Thử container của docker-compose trước (evm_rabbitmq, có thể đang stopped
        # nên không xuất hiện trong docker ps), sau đó mới tới tên standalone.
        docker start evm_rabbitmq 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            docker start rabbitmq 2>&1 | Out-Null
        }
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  ℹ️  Creating new RabbitMQ container..." -ForegroundColor Blue
            docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:management
        }
        Write-Host "  ✅ RabbitMQ started successfully" -ForegroundColor Green
    } catch {
        Write-Host "  ❌ Failed to start RabbitMQ: $_" -ForegroundColor Red
        exit 1
    }
}

# Wait for RabbitMQ to be ready
Write-Host "`n  Waiting for RabbitMQ to be ready..." -ForegroundColor Gray
Start-Sleep -Seconds 3

Write-Host "`n[2/5] Starting UserService (port 7001)..." -ForegroundColor Yellow
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$PSScriptRoot\ev-dealer-management\UserService'; Write-Host 'UserService Starting...' -ForegroundColor Cyan; dotnet run"

Write-Host "`n[3/5] Starting VehicleService (port 5068)..." -ForegroundColor Yellow
Start-Sleep -Seconds 2
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$PSScriptRoot\ev-dealer-management\VehicleService'; Write-Host 'VehicleService Starting...' -ForegroundColor Cyan; dotnet run"

Write-Host "`n[4/5] Starting NotificationService (port 5051)..." -ForegroundColor Yellow
Start-Sleep -Seconds 2
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$PSScriptRoot\ev-dealer-management\NotificationService'; Write-Host 'NotificationService Starting...' -ForegroundColor Cyan; dotnet run"

Write-Host "`n[5/5] Starting Frontend (port 5173)..." -ForegroundColor Yellow
Start-Sleep -Seconds 2
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$PSScriptRoot\ev-dealer-frontend'; Write-Host 'Frontend Starting...' -ForegroundColor Cyan; npm run dev"

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "✅ All services are starting!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan

Write-Host "`nService URLs:" -ForegroundColor White
Write-Host "  🌐 Frontend:            http://localhost:5173" -ForegroundColor Blue
Write-Host "  👤 UserService:         http://localhost:7001" -ForegroundColor Blue
Write-Host "  🚗 VehicleService:      http://localhost:5068" -ForegroundColor Blue
Write-Host "  🔔 NotificationService: http://localhost:5051" -ForegroundColor Blue
Write-Host "  🐰 RabbitMQ Management: http://localhost:15672 (guest/guest)" -ForegroundColor Blue

Write-Host "`nNext Steps:" -ForegroundColor Yellow
Write-Host "  1. Wait 10-15 seconds for all services to start" -ForegroundColor White
Write-Host "  2. Open http://localhost:5173/login in browser" -ForegroundColor White
Write-Host "  3. Login with valid credentials" -ForegroundColor White
Write-Host "  4. Allow notification permissions when prompted" -ForegroundColor White
Write-Host "  5. Reserve a vehicle to test notifications" -ForegroundColor White

Write-Host "`n⚠️  Note: Keep all terminal windows open while testing!" -ForegroundColor Red
Write-Host "Press Ctrl+C in each window to stop services when done.`n" -ForegroundColor Gray
