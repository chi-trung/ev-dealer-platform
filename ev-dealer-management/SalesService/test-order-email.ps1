# Test SalesService - Order Confirmation Email
# Script test flow: SalesService -> RabbitMQ -> NotificationService -> SendGrid Email

Write-Host "Testing SalesService - Order Confirmation Flow" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Check prerequisites
Write-Host "Step 1: Checking Prerequisites..." -ForegroundColor Yellow

# Check RabbitMQ
$rabbitmq = docker ps --filter "name=rabbitmq" --format "{{.Status}}"
if ($rabbitmq) {
    Write-Host "   RabbitMQ is running" -ForegroundColor Green
} else {
    Write-Host "   RabbitMQ is NOT running!" -ForegroundColor Red
    Write-Host "   Run: docker start rabbitmq" -ForegroundColor Yellow
    exit 1
}

# Check NotificationService
try {
    $notif = Invoke-RestMethod -Uri "http://localhost:5051/health" -TimeoutSec 3 -ErrorAction Stop
    Write-Host "   NotificationService is running (Port 5051)" -ForegroundColor Green
} catch {
    Write-Host "   NotificationService is NOT running!" -ForegroundColor Red
    Write-Host "   Run: cd NotificationService; dotnet run" -ForegroundColor Yellow
    exit 1
}

# Check SalesService
try {
    $sales = Invoke-RestMethod -Uri "http://localhost:5003/api/orders/health" -TimeoutSec 3 -ErrorAction Stop
    Write-Host "   SalesService is running (Port 5003)" -ForegroundColor Green
} catch {
    Write-Host "   SalesService is NOT running!" -ForegroundColor Yellow
    Write-Host "   Starting SalesService..." -ForegroundColor Yellow
    
    $salesPath = $PSScriptRoot
    Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$salesPath'; Write-Host 'SalesService' -ForegroundColor Cyan; dotnet run"
    
    Write-Host "   Waiting 10 seconds for SalesService to start..." -ForegroundColor Yellow
    Start-Sleep -Seconds 10
}

Write-Host ""

# Step 2: Prepare test data
Write-Host "Step 2: Preparing Test Data..." -ForegroundColor Yellow

# Change email below to your real email to receive test email
$testOrder = @{
    customerName = "Duy Test Order"
    customerEmail = "duytest@example.com"
    vehicleModel = "VinFast VF 8 Plus"
    totalAmount = 950000000
    paymentMethod = "Bank Transfer"
    quantity = 1
}

Write-Host "   Customer: $($testOrder.customerName)" -ForegroundColor White
Write-Host "   Email: $($testOrder.customerEmail)" -ForegroundColor White
Write-Host "   Vehicle: $($testOrder.vehicleModel)" -ForegroundColor White
Write-Host "   Amount: $($testOrder.totalAmount) VND" -ForegroundColor White
Write-Host ""

# Step 3: Call API
Write-Host "Step 3: Creating Order via API..." -ForegroundColor Yellow

$body = $testOrder | ConvertTo-Json

try {
    $response = Invoke-RestMethod -Uri "http://localhost:5003/api/orders/complete" `
        -Method POST `
        -ContentType "application/json" `
        -Body $body `
        -ErrorAction Stop
    
    Write-Host "   API Call Successful!" -ForegroundColor Green
    Write-Host "   Order ID: $($response.orderId)" -ForegroundColor Cyan
    Write-Host "   Message: $($response.message)" -ForegroundColor White
    Write-Host ""
    
} catch {
    Write-Host "   API Call Failed!" -ForegroundColor Red
    Write-Host "   Error: $_" -ForegroundColor Red
    exit 1
}

# Step 4: Verify
Write-Host "Step 4: Verifying Results..." -ForegroundColor Yellow

Write-Host ""
Write-Host "Expected Results:" -ForegroundColor Green
Write-Host "   1. SalesService Log:" -ForegroundColor White
Write-Host "      - Published message to queue sales.completed" -ForegroundColor Gray
Write-Host "      - Order $($response.orderId) completed" -ForegroundColor Gray
Write-Host ""
Write-Host "   2. NotificationService Log:" -ForegroundColor White
Write-Host "      - Received SaleCompletedEvent" -ForegroundColor Gray
Write-Host "      - Sending order confirmation email to $($testOrder.customerEmail)" -ForegroundColor Gray
Write-Host "      - Email sent successfully" -ForegroundColor Gray
Write-Host ""
Write-Host "   3. RabbitMQ UI (http://localhost:15672):" -ForegroundColor White
Write-Host "      - Queue sales.completed has 1 message delivered" -ForegroundColor Gray
Write-Host ""
Write-Host "   4. Email Inbox ($($testOrder.customerEmail)):" -ForegroundColor White
Write-Host "      - Subject: Order Confirmation - $($response.orderId)" -ForegroundColor Gray
Write-Host "      - Body: Order details with vehicle, amount, payment method" -ForegroundColor Gray
Write-Host ""

# Open RabbitMQ UI
Write-Host "Opening RabbitMQ Management UI..." -ForegroundColor Yellow
Start-Process "http://localhost:15672"
Write-Host "   Login: guest / guest" -ForegroundColor Gray
Write-Host "   Navigate to: Queues -> sales.completed" -ForegroundColor Gray
Write-Host ""

Write-Host "================================================" -ForegroundColor Cyan
Write-Host "Test Complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Yellow
Write-Host "   1. Check NotificationService terminal for Email sent successfully" -ForegroundColor White
Write-Host "   2. Check your email inbox: $($testOrder.customerEmail)" -ForegroundColor White
Write-Host "   3. Verify RabbitMQ message consumed (UI opened above)" -ForegroundColor White
Write-Host ""
Write-Host "Press any key to exit..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')
