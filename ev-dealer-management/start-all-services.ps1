$services = @(
    "UserService",
    "SalesService",
    "VehicleService",
    "CustomerService",
    "ReportingService",
    "APIGatewayService",
    "NotificationService"
)

$basePath = $PSScriptRoot   # repo-relative: script song ngay trong ev-dealer-management/

# Khởi tạo chuỗi lệnh cho Windows Terminal
$wtArgs = ""

foreach ($service in $services) {
    $servicePath = Join-Path $basePath $service

    # Cấu trúc lệnh: new-tab + thư mục làm việc (-d) + tiêu đề (--title) + lệnh chạy
    # Lưu ý: Dấu chấm phẩy (;) được dùng để ngăn cách các tab trong Windows Terminal
    $wtArgs += "new-tab --title `"$service`" -d `"$servicePath`" powershell -NoExit -Command `"dotnet run`" ; "
}

# Xóa dấu chấm phẩy thừa ở cuối chuỗi
if ($wtArgs.EndsWith(" ; ")) {
    $wtArgs = $wtArgs.Substring(0, $wtArgs.Length - 3)
}

Write-Host "Opening services in Windows Terminal tabs..."

# Gọi wt.exe (Windows Terminal) với danh sách tham số đã tạo
Start-Process wt -ArgumentList $wtArgs