# PowerShell script to import sample vehicle data into VehicleService
# Usage: .\import_vehicles.ps1

param(
    [string]$ApiUrl = "http://localhost:5068",
    [string]$DataFile = "sample_import_data.json"
)

Write-Host "Starting vehicle data import..." -ForegroundColor Green
Write-Host "API URL: $ApiUrl" -ForegroundColor Yellow
Write-Host "Data file: $DataFile" -ForegroundColor Yellow
Write-Host ""

# Check if data file exists
if (!(Test-Path $DataFile)) {
    Write-Host "Error: Data file '$DataFile' not found!" -ForegroundColor Red
    exit 1
}

# Read JSON data
try {
    $vehicles = Get-Content $DataFile | ConvertFrom-Json
    Write-Host "Found $($vehicles.Count) vehicles to import" -ForegroundColor Cyan
} catch {
    Write-Host "Error reading JSON file: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Test API connection
Write-Host "Testing API connection..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "$ApiUrl/health" -Method GET -TimeoutSec 10
    if ($response.StatusCode -eq 200 -and $response.Content -eq "Healthy") {
        Write-Host "✓ API is healthy" -ForegroundColor Green
    } else {
        Write-Host "✗ API health check failed" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "✗ Cannot connect to API: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

$successCount = 0
$errorCount = 0

# Import each vehicle
foreach ($vehicle in $vehicles) {
    Write-Host "Importing: $($vehicle.model)" -ForegroundColor Yellow

    try {
        $jsonBody = $vehicle | ConvertTo-Json -Depth 10
        $response = Invoke-WebRequest `
            -Uri "$ApiUrl/api/vehicles" `
            -Method POST `
            -ContentType "application/json" `
            -Body $jsonBody `
            -TimeoutSec 30

        if ($response.StatusCode -eq 201) {
            Write-Host "✓ Successfully imported: $($vehicle.model)" -ForegroundColor Green
            $successCount++
        } else {
            Write-Host "✗ Unexpected status code: $($response.StatusCode)" -ForegroundColor Red
            $errorCount++
        }
    } catch {
        Write-Host "✗ Failed to import $($vehicle.model): $($_.Exception.Message)" -ForegroundColor Red
        $errorCount++
    }
}

# Summary
Write-Host ""
Write-Host "Import Summary:" -ForegroundColor Cyan
Write-Host "✓ Successful imports: $successCount" -ForegroundColor Green
Write-Host "✗ Failed imports: $errorCount" -ForegroundColor Red
Write-Host "Total processed: $($vehicles.Count)" -ForegroundColor Cyan

if ($errorCount -eq 0) {
    Write-Host ""
    Write-Host "🎉 All vehicles imported successfully!" -ForegroundColor Green
    Write-Host "You can now test the API endpoints:" -ForegroundColor Cyan
    Write-Host "  GET  $ApiUrl/api/vehicles" -ForegroundColor White
    Write-Host "  GET  $ApiUrl/api/export/vehicles/json" -ForegroundColor White
} else {
    Write-Host ""
    Write-Host "⚠️  Some imports failed. Check the error messages above." -ForegroundColor Yellow
}
