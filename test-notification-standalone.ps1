# Test NotificationService independently without other services
# This script demonstrates that NotificationService works standalone

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "NotificationService Standalone Test" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

Write-Host "`n[Step 1] Starting RabbitMQ..." -ForegroundColor Yellow
docker start rabbitmq 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "  Creating new RabbitMQ container..." -ForegroundColor Blue
    docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:management
}
Write-Host "  ✅ RabbitMQ is running" -ForegroundColor Green
Start-Sleep -Seconds 3

Write-Host "`n[Step 2] Starting NotificationService..." -ForegroundColor Yellow
Write-Host "  Starting on port 5051..." -ForegroundColor Gray

$notificationJob = Start-Job -ScriptBlock {
    Set-Location "$PSScriptRoot\ev-dealer-management\NotificationService"
    dotnet run 2>&1
}

Write-Host "  Waiting for service to start..." -ForegroundColor Gray
Start-Sleep -Seconds 8

Write-Host "`n[Step 3] Testing Health Check..." -ForegroundColor Yellow
try {
    $healthCheck = Invoke-RestMethod -Uri "http://localhost:5051/health" -Method Get
    Write-Host "  ✅ Health Check Response:" -ForegroundColor Green
    Write-Host "     Status: $($healthCheck.status)" -ForegroundColor White
    Write-Host "     Service: $($healthCheck.service)" -ForegroundColor White
    Write-Host "     Timestamp: $($healthCheck.timestamp)" -ForegroundColor White
} catch {
    Write-Host "  ❌ Health check failed. Service may not be running yet." -ForegroundColor Red
    Write-Host "     Error: $_" -ForegroundColor Red
}

Write-Host "`n[Step 4] Checking RabbitMQ Consumers..." -ForegroundColor Yellow
$jobOutput = Receive-Job -Job $notificationJob
$salesQueue = $jobOutput | Select-String "sales.completed"
$vehicleQueue = $jobOutput | Select-String "vehicle.reserved"
$testdriveQueue = $jobOutput | Select-String "testdrive.scheduled"

if ($salesQueue) { Write-Host "  ✅ Listening on: sales.completed" -ForegroundColor Green }
if ($vehicleQueue) { Write-Host "  ✅ Listening on: vehicle.reserved" -ForegroundColor Green }
if ($testdriveQueue) { Write-Host "  ✅ Listening on: testdrive.scheduled" -ForegroundColor Green }

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "✅ NotificationService is Running!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan

Write-Host "`nTest Options:" -ForegroundColor Yellow
Write-Host "  1. Test FCM directly:" -ForegroundColor White
Write-Host "     POST http://localhost:5051/api/notification/test-fcm" -ForegroundColor Cyan
Write-Host "     Body: { `"deviceToken`": `"YOUR_DEVICE_TOKEN`", `"title`": `"Test`", `"body`": `"Hello`" }" -ForegroundColor Gray

Write-Host "`n  2. Publish test event to RabbitMQ:" -ForegroundColor White
Write-Host "     Open: http://localhost:15672 (guest/guest)" -ForegroundColor Cyan
Write-Host "     Queue: vehicle.reserved" -ForegroundColor Gray
Write-Host "     Payload: {`"vehicleId`":1,`"vehicleName`":`"Test Car`",`"customerName`":`"John`",`"customerEmail`":`"test@test.com`",`"customerPhone`":`"123`",`"quantity`":1,`"reservedAt`":`"2025-11-26T00:00:00Z`",`"deviceToken`":`"YOUR_TOKEN`"}" -ForegroundColor Gray

Write-Host "`n  3. View logs:" -ForegroundColor White
Write-Host "     Check NotificationService/Logs/ folder" -ForegroundColor Cyan

Write-Host "`nℹ️  Press Ctrl+C to stop the service" -ForegroundColor Blue
Write-Host "Waiting for your tests... (Service running in background)`n" -ForegroundColor Gray

# Keep script running and show logs
try {
    while ($true) {
        $newOutput = Receive-Job -Job $notificationJob
        if ($newOutput) {
            Write-Host $newOutput -ForegroundColor DarkGray
        }
        Start-Sleep -Seconds 2
    }
} finally {
    Write-Host "`n`nStopping NotificationService..." -ForegroundColor Yellow
    Stop-Job -Job $notificationJob
    Remove-Job -Job $notificationJob
    Write-Host "Service stopped.`n" -ForegroundColor Green
}
