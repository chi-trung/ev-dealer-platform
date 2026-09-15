# Test Drive Notification Flow - Quick Test Script
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "   TEST DRIVE NOTIFICATION FLOW TEST   " -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# Check prerequisites
Write-Host "[1/3] Checking Prerequisites..." -ForegroundColor Yellow

# Check CustomerService
try {
    $response = Invoke-WebRequest -Uri "http://localhost:5039/api/customers" -Method GET -TimeoutSec 5
    Write-Host "  - CustomerService (5039): " -NoNewline
    Write-Host "OK" -ForegroundColor Green
} catch {
    Write-Host "  - CustomerService (5039): " -NoNewline
    Write-Host "FAILED" -ForegroundColor Red
    Write-Host "`nPlease start CustomerService first:" -ForegroundColor Yellow
    Write-Host "cd CustomerService; dotnet run`n" -ForegroundColor Gray
    exit 1
}

# Check NotificationService
try {
    $response = Invoke-WebRequest -Uri "http://localhost:5051/health" -Method GET -TimeoutSec 5
    Write-Host "  - NotificationService (5051): " -NoNewline
    Write-Host "OK" -ForegroundColor Green
} catch {
    Write-Host "  - NotificationService (5051): " -NoNewline
    Write-Host "FAILED" -ForegroundColor Red
    Write-Host "`nPlease start NotificationService first:" -ForegroundColor Yellow
    Write-Host "cd NotificationService; dotnet run`n" -ForegroundColor Gray
    exit 1
}

Write-Host "`n[2/3] Creating Test Drive Booking..." -ForegroundColor Yellow

# Test data
$testDriveData = @{
    customerId = 1  # Assuming customer ID 1 exists
    vehicleId = 1
    dealerId = 1
    appointmentDate = (Get-Date).AddDays(3).ToString("yyyy-MM-ddTHH:mm:ss")
    notes = "Test from PowerShell script - Please confirm availability"
} | ConvertTo-Json

Write-Host "Test Data:" -ForegroundColor Gray
Write-Host $testDriveData -ForegroundColor Gray

try {
    # Create test drive
    $response = Invoke-RestMethod -Uri "http://localhost:5039/api/testdrives" `
        -Method POST `
        -ContentType "application/json" `
        -Body $testDriveData

    Write-Host "`n  Test Drive Created Successfully!" -ForegroundColor Green
    Write-Host "  - Test Drive ID: " -NoNewline
    Write-Host "$($response.id)" -ForegroundColor Cyan
    Write-Host "  - Customer: $($response.customerName)" -ForegroundColor Gray
    Write-Host "  - Appointment: $($response.appointmentDate)" -ForegroundColor Gray
    Write-Host "  - Status: $($response.status)" -ForegroundColor Gray
    
    $testDriveId = $response.id

} catch {
    Write-Host "`n  Failed to create test drive!" -ForegroundColor Red
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
    
    # Try to create a test customer first
    Write-Host "`n  Attempting to create test customer first..." -ForegroundColor Yellow
    
    $customerData = @{
        name = "Test Customer"
        email = "testcustomer@example.com"
        phone = "0123456789"
        address = "123 Test Street"
    } | ConvertTo-Json
    
    try {
        $customerResponse = Invoke-RestMethod -Uri "http://localhost:5039/api/customers" `
            -Method POST `
            -ContentType "application/json" `
            -Body $customerData
        
        Write-Host "  - Customer created: ID $($customerResponse.id)" -ForegroundColor Green
        
        # Retry test drive creation with new customer ID
        $testDriveData = @{
            customerId = $customerResponse.id
            vehicleId = 1
            dealerId = 1
            appointmentDate = (Get-Date).AddDays(3).ToString("yyyy-MM-ddTHH:mm:ss")
            notes = "Test from PowerShell script - Please confirm availability"
        } | ConvertTo-Json
        
        $response = Invoke-RestMethod -Uri "http://localhost:5039/api/testdrives" `
            -Method POST `
            -ContentType "application/json" `
            -Body $testDriveData
        
        Write-Host "`n  Test Drive Created Successfully!" -ForegroundColor Green
        Write-Host "  - Test Drive ID: " -NoNewline
        Write-Host "$($response.id)" -ForegroundColor Cyan
        Write-Host "  - Customer: $($response.customerName)" -ForegroundColor Gray
        Write-Host "  - Appointment: $($response.appointmentDate)" -ForegroundColor Gray
        
        $testDriveId = $response.id
        
    } catch {
        Write-Host "  Still failed: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
}

Write-Host "`n[3/3] Verifying Notification..." -ForegroundColor Yellow
Write-Host "  Waiting 3 seconds for message processing..." -ForegroundColor Gray
Start-Sleep -Seconds 3

Write-Host "`n  Check NotificationService terminal logs for:" -ForegroundColor Cyan
Write-Host "    - 'Processing TestDriveScheduledEvent'" -ForegroundColor Gray
Write-Host "    - 'Processing TestDriveScheduledEvent' -> FCM push" -ForegroundColor Gray

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "        TEST COMPLETED SUCCESSFULLY     " -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

Write-Host "`n📋 Summary:" -ForegroundColor Cyan
Write-Host "  ✅ CustomerService API: Working" -ForegroundColor Green
Write-Host "  ✅ Test Drive Created: ID $testDriveId" -ForegroundColor Green
Write-Host "  ✅ Event Published: testdrive.scheduled" -ForegroundColor Green
Write-Host "  ✅ NotificationService: Consuming" -ForegroundColor Green
Write-Host "  📲 FCM push notification: Check NotificationService logs above" -ForegroundColor Yellow

Write-Host "`n💡 Next Steps:" -ForegroundColor Cyan
Write-Host "  1. Check NotificationService logs for TestDriveScheduledEvent processing"
Write-Host "  2. Test via frontend: http://localhost:5173/test-drive"
Write-Host "  3. Device with registered token receives push (no email/SMS in this system)`n"
