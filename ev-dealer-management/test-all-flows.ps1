# ============================================
# COMPREHENSIVE TEST SCRIPT - All Notification Flows
# ============================================

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  EV DEALER - NOTIFICATION TEST SUITE  " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Test Configuration
$SalesServiceUrl = "http://localhost:5003"
$VehicleServiceUrl = "http://localhost:5068"
$NotificationServiceUrl = "http://localhost:5051"
$RabbitMQUrl = "http://localhost:15672"

# ============================================
# 1. CHECK PREREQUISITES
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
    Write-Host "    Start it: docker start rabbitmq" -ForegroundColor Yellow
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
# tinh o backend mới > 0, khong vay 400. quoteId=0 -> bo qua buoc convert quote.
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
# 4. VERIFY RABBITMQ QUEUES
# ============================================
Write-Host "[4/6] Verifying RabbitMQ Message Processing..." -ForegroundColor Yellow

Write-Host "  - Checking sales.completed queue..." -NoNewline
Write-Host " (Check RabbitMQ UI manually)" -ForegroundColor Gray

Write-Host "  - Checking vehicle.reserved queue..." -NoNewline
Write-Host " (Check RabbitMQ UI manually)" -ForegroundColor Gray

Write-Host ""

# ============================================
# 5. CHECK NOTIFICATION SERVICE LOGS
# ============================================
Write-Host "[5/6] Expected Results in Logs..." -ForegroundColor Yellow

Write-Host "  NotificationService should show:" -ForegroundColor Cyan
Write-Host "    - Processing VehicleReservedEvent" -ForegroundColor Gray
Write-Host "    - Reservation confirmation push notification sent for Vehicle" -ForegroundColor Gray
Write-Host "    - Processing SaleCompletedEvent" -ForegroundColor Gray
Write-Host "    - SaleCompletedEvent processed (push via token registry, no token -> warning)" -ForegroundColor Gray

Write-Host ""

# ============================================
# 6. TEST SUMMARY
# ============================================
Write-Host "[6/6] Test Summary" -ForegroundColor Yellow
Write-Host ""
Write-Host "  ✅ Vehicle Reservation Flow (FCM push)" -ForegroundColor Green
Write-Host "     - API Call: SUCCESS" -ForegroundColor Gray
Write-Host "     - Vehicle ID: $vehicleReservationId" -ForegroundColor Gray
Write-Host "     - Push: check NotificationService log / device" -ForegroundColor Gray
Write-Host ""
Write-Host "  ✅ Order Completion Flow (FCM push)" -ForegroundColor Green
Write-Host "     - API Call: SUCCESS" -ForegroundColor Gray
Write-Host "     - Order ID: $orderId" -ForegroundColor Gray
Write-Host "     - Push: via device-token registry user:<customerId>" -ForegroundColor Gray
Write-Host ""

# ============================================
# NEXT STEPS
# ============================================
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  NEXT STEPS                            " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. Open RabbitMQ UI: $RabbitMQUrl" -ForegroundColor Yellow
Write-Host "   - Check queue: sales.completed (messages consumed)" -ForegroundColor Gray
Write-Host "   - Check queue: vehicle.reserved (messages consumed)" -ForegroundColor Gray
Write-Host ""
Write-Host "2. Check NotificationService terminal logs" -ForegroundColor Yellow
Write-Host "   - Should show 'Processing SaleCompletedEvent'" -ForegroundColor Gray
Write-Host "   - Should show 'Processing VehicleReservedEvent'" -ForegroundColor Gray
Write-Host ""
Write-Host "3. Check NotificationService logs (khong co email/SMS inbox)" -ForegroundColor Yellow
Write-Host "   (khong co email/SMS trong he thong nay - chi FCM push)" -ForegroundColor Gray
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  TEST COMPLETED SUCCESSFULLY! 🎉       " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
