# 🚀 Hướng Dẫn Khởi Động SalesService với RabbitMQ

> **Cập nhật 2026-09 (docs sweep #52):** container RabbitMQ trong
> `docker-compose.yml` tên **`evm_rabbitmq`** (compose service là `rabbitmq`);
> key cấu hình broker là **`RabbitMQ:HostName`** (không phải `Host`);
> VehicleService chạy port dev **5068** — value mặc định `http://localhost:5001`
> trong `appsettings.json` là port cũ, compose đã override bằng
> `Services__VehicleService=http://vehicleservice:8080`.

## 📋 Điều Kiện Cần Thiết

### 1. **RabbitMQ Server** (Bắt buộc)

SalesService cần RabbitMQ để publish events. Có 2 cách để chạy RabbitMQ:

#### **Option A: Sử dụng Docker (Khuyến nghị)**

```powershell
# Cách gọn nhất — dùng compose (từ ev-dealer-management/):
docker compose up -d rabbitmq     # container tạo ra tên evm_rabbitmq

# Kiểm tra:
docker ps --filter "name=evm_rabbitmq"

# Nếu muốn chạy container lẻ tự đặt tên (không khuyến nghị — lệch compose):
docker run -d --name rabbitmq `
  -p 5672:5672 `
  -p 15672:15672 `
  rabbitmq:3-management
```

**RabbitMQ Management UI**: http://localhost:15672
- Username: `guest`
- Password: `guest`

#### **Option B: Sử dụng Docker Compose**

```powershell
# Từ thư mục chứa docker-compose.yml (ev-dealer-management/)
docker compose up -d rabbitmq
```

### 2. **.NET 8.0 SDK** (Bắt buộc)

Kiểm tra version:
```powershell
dotnet --version
```

Nếu chưa có, tải từ: https://dotnet.microsoft.com/download/dotnet/8.0

### 3. **VehicleService** (Tùy chọn - Chỉ cần nếu muốn lấy vehicle model name)

Nếu VehicleService không chạy, SalesService vẫn hoạt động bình thường nhưng sẽ dùng giá trị mặc định `Vehicle-{VehicleId}` cho vehicle model trong events.

Để chạy VehicleService:
```powershell
cd ev-dealer-management\VehicleService
dotnet run
```

Port dev (launchSettings): `http://localhost:5068`. *(appsettings của
SalesService còn ghi `Services:VehicleService = http://localhost:5001` —
port cũ; khi chạy dev manual, override bằng
`$env:Services__VehicleService="http://localhost:5068"` trước `dotnet run`,
hoặc đơn giản là chạy cả stack qua compose.)*

### 4. **Database SQLite** (Tự động tạo)

Database `sales.db` sẽ được tạo tự động khi chạy lần đầu.

---

## 🚀 Cách Khởi Động SalesService

### **Option A: Sử dụng Docker Compose (Khuyến nghị cho Production)**

```powershell
# Từ thư mục chứa docker-compose.yml
cd ev-dealer-management

# Build và start tất cả services (bao gồm SalesService)
docker compose up -d

# Hoặc chỉ start SalesService và dependencies
docker compose up -d rabbitmq vehicleservice salesservice

# Xem logs
docker compose logs -f salesservice

# Stop services
docker compose down
```

**Lợi ích:**
- Tự động quản lý dependencies (RabbitMQ, VehicleService)
- Dễ dàng scale và deploy
- Database được persist qua volumes
- Health checks tự động

### **Option B: Chạy trực tiếp với dotnet (Development)**

#### Bước 1: Đảm bảo RabbitMQ đang chạy

```powershell
# Kiểm tra (container compose tên evm_rabbitmq)
docker ps --filter "name=evm_rabbitmq"

# Nếu chưa chạy, bật qua compose (từ ev-dealer-management/):
docker compose up -d rabbitmq
# hoặc start container có sẵn:
docker start evm_rabbitmq
```

#### Bước 2: Khởi động SalesService

```powershell
cd ev-dealer-management\SalesService
dotnet run
```

Service sẽ chạy tại: `http://localhost:5003` (hoặc port được cấu hình trong `launchSettings.json`)

### Bước 3: Kiểm tra Service đã sẵn sàng

```powershell
# Health check
curl http://localhost:5003/api/orders/health

# Hoặc mở browser
# http://localhost:5003/api/orders/health
```

### Bước 4: Kiểm tra RabbitMQ Connection

Mở RabbitMQ Management UI: http://localhost:15672

1. Đăng nhập với `guest/guest`
2. Vào tab **Queues**
3. Kiểm tra các queues đã được tạo:
   - `sales.completed`
   - `order.created`
   - `payment.received`
   - `order.status.changed`

**Lưu ý**: Queues chỉ được tạo khi có message đầu tiên được publish.

---

## ⚙️ Cấu Hình

### File: `appsettings.json`

```json
{
  "RabbitMQ": {
    "HostName": "localhost",
    "Port": 5672,
    "UserName": "guest",
    "Password": "guest",
    "Queues": {
      "SaleCompleted": "sales.completed",
      "OrderCreated": "order.created",
      "PaymentReceived": "payment.received",
      "OrderStatusChanged": "order.status.changed"
    }
  },
  "Services": {
    "VehicleService": "http://localhost:5001"
  }
}
```

*(key broker là `HostName` — code đọc `RabbitMQ:HostName`; block Queues trên
rút gọn, file thật còn khai báo thêm QuoteCreated/ContractCreated...)*

### Thay đổi RabbitMQ Connection

Nếu RabbitMQ chạy ở host/port khác, cập nhật trong `appsettings.json` (hoặc env
`RabbitMQ__HostName` / `RabbitMQ__Port`):

```json
{
  "RabbitMQ": {
    "HostName": "your-rabbitmq-host",
    "Port": 5672,
    "UserName": "your-username",
    "Password": "your-password"
  }
}
```

---

## 🧪 Test RabbitMQ Integration

### 1. Tạo Order mới

```powershell
POST http://localhost:5003/api/orders/complete
Content-Type: application/json

{
  "quoteId": 1,
  "customerId": 1,
  "customerEmail": "test@example.com",
  "customerName": "Test Customer",
  "dealerId": 1,
  "salespersonId": 1,
  "vehicleId": 1,
  "vehicleVariantId": 1,
  "colorId": 1,
  "quantity": 1,
  "unitPrice": 1000000,
  "paymentMethod": "Cash",
  "paymentType": "Full",
  "deliveryDate": "2024-12-31T00:00:00",
  "estimatedDeliveryDate": "2024-12-31T00:00:00"
}
```

### 2. Kiểm tra Events trong RabbitMQ

1. Mở RabbitMQ Management UI: http://localhost:15672
2. Vào tab **Queues**
3. Click vào queue `sales.completed` hoặc `order.created`
4. Xem messages đã được publish

### 3. Kiểm tra Logs

Trong console của SalesService, bạn sẽ thấy:
```
Published OrderCreated event for Order ORD-20241201...
Published SaleCompleted event for Order ORD-20241201...
```

---

## ❌ Xử Lý Lỗi

### Lỗi: "Failed to initialize RabbitMQ connection"

**Nguyên nhân**: RabbitMQ chưa chạy hoặc connection string sai.

**Giải pháp**:
1. Kiểm tra RabbitMQ đang chạy: `docker ps --filter "name=evm_rabbitmq"`
2. Kiểm tra port 5672 không bị block
3. Kiểm tra cấu hình `RabbitMQ:HostName` trong `appsettings.json`

### Lỗi: "Error publishing events to RabbitMQ"

**Nguyên nhân**: RabbitMQ connection bị mất sau khi khởi động.

**Giải pháp**:
- Service sẽ tự động retry khi publish message tiếp theo
- Kiểm tra RabbitMQ vẫn đang chạy
- Xem logs để biết chi tiết lỗi

### Lỗi: "Failed to fetch vehicle model"

**Nguyên nhân**: VehicleService không chạy hoặc không accessible.

**Giải pháp**:
- Service vẫn hoạt động bình thường
- Vehicle model sẽ dùng giá trị mặc định: `Vehicle-{VehicleId}`
- Để có vehicle model chính xác, start VehicleService

---

## 📝 Lưu Ý Quan Trọng

1. **RabbitMQ phải chạy trước SalesService** - Nếu không, service vẫn khởi động nhưng sẽ không publish được events.

2. **Events được publish bất đồng bộ** - Nếu publish event thất bại, request vẫn thành công (chỉ log error).

3. **Queues tự động tạo** - Queues sẽ được tạo tự động khi có message đầu tiên.

4. **Connection tự động reconnect** - Nếu RabbitMQ connection bị mất, service sẽ tự động reconnect khi publish message tiếp theo.

---

## 🔗 Liên Kết Hữu Ích

- RabbitMQ Management UI: http://localhost:15672
- SalesService Swagger UI: http://localhost:5003/swagger
- Health Check: http://localhost:5003/api/orders/health

