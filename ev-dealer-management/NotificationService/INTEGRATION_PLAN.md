# 🚀 Integration Plan - NotificationService với Frontend & Services

> **⚠️ TÀI LIỆU LỊCH SỬ (docs sweep #52, 2026-09):** đây là plan thiết kế từ
> trước khi integration được build — **đã được hiện thực hóa và thay thế**.
> Trạng thái thật hiện nay: NotificationService port **5051** (không phải
> 5005), VehicleService **5068** (không phải 5002), kênh duy nhất là **FCM
> push** (không SendGrid/Twilio/email/SMS), routes Ocelot Notification +
> DeviceTokens **đã có** trong `APIGatewayService/ocelot.json`, compose dùng
> container `evm_rabbitmq`/`evm_notificationservice` (không phải
> `ev-dealer-notification`). Đọc file này để hiểu *tại sao* thiết kế như vậy,
> nhưng làm theo `QUICK_START.md`/`TESTING_GUIDE.md` cùng thư mục.

## 📋 Tổng Quan

NotificationService đã hoàn thành và test backend thành công. Bây giờ cần tích hợp vào toàn bộ hệ thống để tạo luồng notification hoàn chỉnh từ frontend → backend services → NotificationService → Email/SMS.

---

## 🎯 Mục Tiêu

1. ✅ **Backend Integration**: VehicleService, SalesService publish events đến RabbitMQ
2. ✅ **Frontend Integration**: Hiển thị notification UI và gọi NotificationService
3. ✅ **Testing**: Test end-to-end flow từ frontend đến email/SMS

---

## 📦 Bước 1: VehicleService - Publish Reservation Events

### ✅ Hiện Trạng
VehicleService đã có:
- ✅ RabbitMQ Producer (`IMessageProducer`, `RabbitMQProducerService`)
- ✅ Method `CreateReservationAsync()` đã publish `VehicleReservedEvent`
- ✅ Event được gửi khi customer đặt xe

### 🔧 Cần Cập Nhật

**File: `VehicleService/Services/VehicleService.cs`**

Kiểm tra event structure match với NotificationService:

```csharp
// Current event (line ~639)
var vehicleReservedEvent = new 
{
    ReservationId = reservation.Id,
    VehicleId = vehicle.Id,
    VehicleName = vehicle.Model,
    CustomerName = reservation.CustomerName,
    CustomerEmail = reservation.CustomerEmail,
    CustomerPhone = reservation.CustomerPhone,
    Quantity = reservation.Quantity,
    TotalPrice = reservation.TotalPrice,
    ColorVariantId = reservation.ColorVariantId,
    ColorVariantName = colorVariant?.Name,
    DealerId = vehicle.DealerId,
    CreatedAt = reservation.CreatedAt
};
_messageProducer.PublishMessage(vehicleReservedEvent);
```

**Cần thêm:**
- ✅ `reservationId` → Đã có (`ReservationId`)
- ✅ `customerPhone` → Đã có
- ✅ `customerName` → Đã có
- ✅ `vehicleModel` → Cần map từ `VehicleName`
- ✅ `colorName` → Đã có (`ColorVariantName`)
- ✅ `reservedAt` → Đã có (`CreatedAt`)

### 📝 Action Items

**A1.1**: Update `VehicleService.cs` để match NotificationService DTO

```csharp
// Đổi property names để match NotificationService/DTOs/VehicleReservedEvent.cs
var vehicleReservedEvent = new 
{
    reservationId = reservation.Id.ToString(),  // string
    customerPhone = reservation.CustomerPhone,   // string
    customerName = reservation.CustomerName,     // string
    vehicleModel = vehicle.Model,                // đổi từ VehicleName
    colorName = colorVariant?.Name ?? "Standard", // string
    reservedAt = reservation.CreatedAt           // DateTime
};

// Publish to specific queue
_messageProducer.PublishMessage(vehicleReservedEvent, "vehicle.reserved");
```

**A1.2**: Update `RabbitMQProducerService.cs` để support routing key

```csharp
public void PublishMessage<T>(T message, string routingKey = "")
{
    var factory = new ConnectionFactory() 
    { 
        HostName = _hostName,
        Port = _port,
        UserName = _userName,
        Password = _password
    };

    using var connection = factory.CreateConnection();
    using var channel = connection.CreateModel();

    // Nếu không có routingKey, dùng default queue
    var queueName = string.IsNullOrEmpty(routingKey) 
        ? "vehicle.events" 
        : routingKey;

    channel.QueueDeclare(
        queue: queueName,
        durable: true,
        exclusive: false,
        autoDelete: false,
        arguments: null
    );

    var json = JsonSerializer.Serialize(message);
    var body = Encoding.UTF8.GetBytes(json);

    channel.BasicPublish(
        exchange: "",
        routingKey: queueName,
        basicProperties: null,
        body: body
    );

    _logger.LogInformation("Published message to queue {QueueName}: {Message}", 
        queueName, json);
}
```

**A1.3**: Test VehicleService

```powershell
# 1. Start RabbitMQ
docker start rabbitmq

# 2. Start NotificationService
cd NotificationService
dotnet run

# 3. Start VehicleService
cd VehicleService
dotnet run

# 4. Test create reservation via API
$reservationBody = @{
    vehicleId = 1
    colorVariantId = 1
    customerName = "Nguyen Van A"
    customerEmail = "test@gmail.com"
    customerPhone = "+84901234567"
    notes = "Test reservation"
    quantity = 1
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:5068/api/vehicles/1/reservations" `
    -Method Post `
    -Body $reservationBody `
    -ContentType "application/json"

# 5. Check NotificationService logs → Should see SMS sent
```

---

## 📦 Bước 2: SalesService - Publish Sale Events

### 🔧 Cần Tạo Mới

SalesService chưa có RabbitMQ integration.

**A2.1**: Thêm RabbitMQ packages vào SalesService

```powershell
cd SalesService
dotnet add package RabbitMQ.Client
```

**A2.2**: Tạo `IMessageProducer` interface

**File: `SalesService/Services/IMessageProducer.cs`**

```csharp
namespace SalesService.Services;

public interface IMessageProducer
{
    void PublishMessage<T>(T message, string routingKey = "");
}
```

**A2.3**: Tạo `RabbitMQProducerService`

**File: `SalesService/Services/RabbitMQProducerService.cs`**

```csharp
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace SalesService.Services;

public class RabbitMQProducerService : IMessageProducer
{
    private readonly string _hostName;
    private readonly int _port;
    private readonly string _userName;
    private readonly string _password;
    private readonly ILogger<RabbitMQProducerService> _logger;

    public RabbitMQProducerService(IConfiguration configuration, ILogger<RabbitMQProducerService> logger)
    {
        _hostName = configuration["RabbitMQ:HostName"] ?? "localhost";
        _port = int.Parse(configuration["RabbitMQ:Port"] ?? "5672");
        _userName = configuration["RabbitMQ:UserName"] ?? "guest";
        _password = configuration["RabbitMQ:Password"] ?? "guest";
        _logger = logger;
    }

    public void PublishMessage<T>(T message, string routingKey = "")
    {
        try
        {
            var factory = new ConnectionFactory() 
            { 
                HostName = _hostName,
                Port = _port,
                UserName = _userName,
                Password = _password
            };

            using var connection = factory.CreateConnection();
            using var channel = connection.CreateModel();

            var queueName = string.IsNullOrEmpty(routingKey) ? "sales.events" : routingKey;

            channel.QueueDeclare(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null
            );

            var json = JsonSerializer.Serialize(message);
            var body = Encoding.UTF8.GetBytes(json);

            channel.BasicPublish(
                exchange: "",
                routingKey: queueName,
                basicProperties: null,
                body: body
            );

            _logger.LogInformation("Published message to queue {QueueName}", queueName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing message to RabbitMQ");
        }
    }
}
```

**A2.4**: Cập nhật `appsettings.json`

**File: `SalesService/appsettings.json`**

```json
{
  "RabbitMQ": {
    "HostName": "localhost",
    "Port": 5672,
    "UserName": "guest",
    "Password": "guest"
  }
}
```

**A2.5**: Register service trong `Program.cs`

**File: `SalesService/Program.cs`**

```csharp
// Add RabbitMQ Producer
builder.Services.AddSingleton<IMessageProducer, RabbitMQProducerService>();
```

**A2.6**: Publish event khi complete sale

**File: `SalesService/Controllers/SalesController.cs` hoặc Service**

```csharp
[HttpPost("{id}/complete")]
public async Task<IActionResult> CompleteSale(int id)
{
    var sale = await _context.Sales
        .Include(s => s.Customer)
        .Include(s => s.Vehicle)
        .FirstOrDefaultAsync(s => s.Id == id);

    if (sale == null)
        return NotFound();

    sale.Status = "Completed";
    sale.CompletedAt = DateTime.UtcNow;
    await _context.SaveChangesAsync();

    // Publish event to NotificationService
    var saleCompletedEvent = new 
    {
        orderId = sale.Id.ToString(),
        customerEmail = sale.Customer.Email,
        customerName = sale.Customer.Name,
        vehicleModel = sale.Vehicle.Model,
        totalPrice = sale.TotalPrice,
        completedAt = sale.CompletedAt.Value
    };

    _messageProducer.PublishMessage(saleCompletedEvent, "sales.completed");

    return Ok(sale);
}
```

---

## 📦 Bước 3: Frontend Integration

### 🎨 A3.1: Notification UI Components

**File: `ev-dealer-frontend/src/components/Notification/NotificationToast.jsx`**

```jsx
import React, { useEffect } from 'react';
import { Alert, Snackbar } from '@mui/material';

const NotificationToast = ({ open, message, severity = 'success', onClose }) => {
  useEffect(() => {
    if (open) {
      const timer = setTimeout(() => {
        onClose();
      }, 5000);
      return () => clearTimeout(timer);
    }
  }, [open, onClose]);

  return (
    <Snackbar
      open={open}
      autoHideDuration={5000}
      onClose={onClose}
      anchorOrigin={{ vertical: 'top', horizontal: 'right' }}
    >
      <Alert onClose={onClose} severity={severity} sx={{ width: '100%' }}>
        {message}
      </Alert>
    </Snackbar>
  );
};

export default NotificationToast;
```

### 🎨 A3.2: Update notificationService.js

**File: `ev-dealer-frontend/src/services/notificationService.js`**

Thêm methods để call NotificationService API:

```javascript
import api from './api';

const notificationService = {
  // Test email
  sendTestEmail: async (to, subject, htmlContent) => {
    const response = await api.post('/notifications/test-email', {
      to,
      subject,
      htmlContent
    });
    return response;
  },

  // Send order confirmation (for testing)
  sendOrderConfirmation: async (orderData) => {
    const response = await api.post('/notifications/order-confirmation', orderData);
    return response;
  },

  // Send reservation confirmation (for testing)
  sendReservationConfirmation: async (reservationData) => {
    const response = await api.post('/notifications/reservation-confirmation', reservationData);
    return response;
  },

  // Send test drive confirmation
  sendTestDriveConfirmation: async (testDriveData) => {
    const response = await api.post('/notifications/test-drive-confirmation', testDriveData);
    return response;
  }
};

export default notificationService;
```

### 🎨 A3.3: Update VehicleDetail.jsx - Show notification khi đặt xe

**File: `ev-dealer-frontend/src/pages/Vehicles/VehicleDetail.jsx`**

```jsx
import NotificationToast from '../../components/Notification/NotificationToast';

// Inside component
const [notification, setNotification] = useState({
  open: false,
  message: '',
  severity: 'success'
});

const handleReservation = async (reservationData) => {
  try {
    const response = await vehicleService.createReservation(vehicleId, reservationData);
    
    // Show success notification
    setNotification({
      open: true,
      message: `🎉 Reservation successful! Confirmation SMS sent to ${reservationData.customerPhone}`,
      severity: 'success'
    });

    // Optionally: Also send email confirmation via NotificationService
    // (Backend đã tự động gửi SMS qua RabbitMQ)
  } catch (error) {
    setNotification({
      open: true,
      message: `❌ Reservation failed: ${error.message}`,
      severity: 'error'
    });
  }
};

// Add notification toast to render
return (
  <>
    {/* ... existing code ... */}
    
    <NotificationToast
      open={notification.open}
      message={notification.message}
      severity={notification.severity}
      onClose={() => setNotification({ ...notification, open: false })}
    />
  </>
);
```

### 🎨 A3.4: Test Drive Page - Notification integration

**File: `ev-dealer-frontend/src/pages/TestDrive/TestDriveForm.jsx`**

```jsx
const handleSubmit = async (formData) => {
  try {
    // Submit test drive request
    const response = await testDriveService.createTestDrive(formData);
    
    // Backend sẽ tự động gửi email confirmation qua RabbitMQ
    setNotification({
      open: true,
      message: `✅ Test drive scheduled! Confirmation email sent to ${formData.email}`,
      severity: 'success'
    });
  } catch (error) {
    setNotification({
      open: true,
      message: `❌ Failed to schedule test drive`,
      severity: 'error'
    });
  }
};
```

---

## 📦 Bước 4: API Gateway Configuration

### 🔧 A4.1: Update Ocelot configuration

**File: `APIGatewayService/ocelot.json`**

Thêm route cho NotificationService:

```json
{
  "Routes": [
    // ... existing routes ...
    
    {
      "DownstreamPathTemplate": "/api/notification/{everything}",
      "DownstreamScheme": "http",
      "DownstreamHostAndPorts": [
        {
          "Host": "localhost",
          "Port": 5005
        }
      ],
      "UpstreamPathTemplate": "/api/notifications/{everything}",
      "UpstreamHttpMethod": [ "GET", "POST", "PUT", "DELETE" ]
    },
    {
      "DownstreamPathTemplate": "/health",
      "DownstreamScheme": "http",
      "DownstreamHostAndPorts": [
        {
          "Host": "localhost",
          "Port": 5005
        }
      ],
      "UpstreamPathTemplate": "/notifications/health",
      "UpstreamHttpMethod": [ "GET" ]
    }
  ]
}
```

### 🔧 A4.2: Update frontend API base URL

**File: `ev-dealer-frontend/src/services/api.js`**

```javascript
// Notification endpoints go through API Gateway
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000';

// Create axios instance with gateway URL
const api = axios.create({
  baseURL: API_BASE_URL,
  timeout: 10000,
  headers: {
    'Content-Type': 'application/json'
  }
});
```

---

## 📦 Bước 5: Testing End-to-End

### 🧪 Test Flow 1: Vehicle Reservation

**Scenario**: Customer đặt xe → SMS confirmation tự động

```powershell
# 1. Start all services
# Terminal 1: RabbitMQ
docker start rabbitmq

# Terminal 2: NotificationService
cd NotificationService
dotnet run

# Terminal 3: VehicleService
cd VehicleService
dotnet run

# Terminal 4: API Gateway
cd APIGatewayService
dotnet run

# Terminal 5: Frontend
cd ev-dealer-frontend
npm run dev

# 2. Test via Frontend
# - Open browser: http://localhost:5174
# - Navigate to vehicle detail page
# - Click "Reserve Now"
# - Fill form with phone: +84901234567
# - Submit

# 3. Expected Results:
# ✅ Reservation created in VehicleService
# ✅ Event published to RabbitMQ queue "vehicle.reserved"
# ✅ NotificationService consumes event
# ✅ SMS sent via Twilio (mock for VN)
# ✅ Frontend shows success toast
# ✅ Check logs in NotificationService terminal
```

### 🧪 Test Flow 2: Test Drive Booking

**Scenario**: Customer đặt lịch test drive → Email confirmation

```powershell
# Frontend flow:
# 1. Navigate to Test Drive page
# 2. Select vehicle
# 3. Fill form with email: test@gmail.com
# 4. Submit

# Expected:
# ✅ Test drive created in CustomerService
# ✅ Event published to "testdrive.scheduled" queue
# ✅ NotificationService sends email
# ✅ Check email inbox (or SendGrid Activity)
```

### 🧪 Test Flow 3: Complete Sale Order

**Scenario**: Admin completes sale → Email confirmation to customer

```powershell
# Via API or Admin panel:
POST /api/sales/{id}/complete

# Expected:
# ✅ Sale marked as completed
# ✅ Event published to "sales.completed" queue
# ✅ Order confirmation email sent
```

---

## 📦 Bước 6: Docker Compose Integration

### 🐳 A6.1: Update docker-compose.yml

**File: `ev-dealer-management/docker-compose.yml`**

```yaml
version: '3.8'

services:
  rabbitmq:
    image: rabbitmq:3-management
    container_name: ev-dealer-rabbitmq
    ports:
      - "5672:5672"
      - "15672:15672"
    environment:
      RABBITMQ_DEFAULT_USER: guest
      RABBITMQ_DEFAULT_PASS: guest
    networks:
      - ev-dealer-network

  notification-service:
    build:
      context: ./NotificationService
      dockerfile: Dockerfile
    container_name: ev-dealer-notification
    ports:
      - "5005:5005"
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - RabbitMQ__HostName=rabbitmq
      - SendGrid__ApiKey=${SENDGRID_API_KEY}
      - Twilio__AccountSid=${TWILIO_ACCOUNT_SID}
      - Twilio__AuthToken=${TWILIO_AUTH_TOKEN}
    depends_on:
      - rabbitmq
    networks:
      - ev-dealer-network

  # ... other services ...

networks:
  ev-dealer-network:
    driver: bridge
```

**A6.2**: Tạo `.env` file

```env
SENDGRID_API_KEY=SG.your_api_key_here
TWILIO_ACCOUNT_SID=ACxxxxxxxxxxxxx
TWILIO_AUTH_TOKEN=xxxxxxxxxxxxxxx
TWILIO_PHONE_NUMBER=+1234567890
```

---

## ✅ Checklist Hoàn Chỉnh

### Backend Integration
- [ ] VehicleService publish `vehicle.reserved` events
- [ ] SalesService publish `sales.completed` events  
- [ ] CustomerService publish `testdrive.scheduled` events
- [ ] RabbitMQ queues được tạo tự động
- [ ] NotificationService consume events thành công

### Frontend Integration
- [ ] NotificationToast component tạo xong
- [ ] notificationService.js có đầy đủ methods
- [ ] VehicleDetail.jsx show notification sau khi reserve
- [ ] TestDrive form show notification sau khi book
- [ ] Sales page show notification (nếu có)

### API Gateway
- [ ] Ocelot routes cho NotificationService
- [ ] Health check endpoint accessible
- [ ] CORS configured correctly

### Testing
- [ ] Test vehicle reservation → SMS sent
- [ ] Test test drive booking → Email sent
- [ ] Test sale completion → Email sent
- [ ] Check RabbitMQ Management UI (queues, messages)
- [ ] Check SendGrid Activity dashboard
- [ ] Check Twilio Messaging logs (mock for VN)

### Deployment
- [ ] Docker Compose configuration
- [ ] Environment variables configured
- [ ] All services start successfully
- [ ] Can access via API Gateway

---

## 🎯 Next Steps (Ưu Tiên)

### **Phase 1: Quick Win (30 phút)**
1. ✅ Update VehicleService để publish đúng format event
2. ✅ Test vehicle reservation flow end-to-end
3. ✅ Verify SMS/Email được gửi

### **Phase 2: Complete Backend (1 giờ)**
1. Add RabbitMQ vào SalesService
2. Publish sale completed events
3. Test order confirmation emails

### **Phase 3: Frontend Polish (1 giờ)**
1. Tạo NotificationToast component
2. Update VehicleDetail.jsx
3. Update TestDrive pages
4. Test UI notifications

### **Phase 4: Production Ready (30 phút)**
1. Update API Gateway routes
2. Configure Docker Compose
3. Test full stack deployment

---

## 📞 Support & Debugging

### Kiểm tra RabbitMQ
```powershell
# Management UI
Start-Process http://localhost:15672

# Check queues via API
Invoke-RestMethod -Uri "http://localhost:15672/api/queues" `
    -Headers @{Authorization="Basic Z3Vlc3Q6Z3Vlc3Q="} | 
    Select-Object name, messages
```

### Kiểm tra Logs
```powershell
# NotificationService logs
Get-Content .\NotificationService\Logs\notification-service-*.log -Tail 50 -Wait

# VehicleService logs
dotnet run --project VehicleService > vehicle.log
```

### Test Quick Commands
```powershell
# Test NotificationService health
Invoke-RestMethod http://localhost:5005/health

# Test RabbitMQ connection
Test-NetConnection localhost -Port 5672

# Publish test message
.\NotificationService\TestProducer.ps1 -EventType reservation
```

---

**🚀 Bắt đầu từ Phase 1 - Update VehicleService!**
