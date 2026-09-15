# NotificationService - Hướng Dẫn Test Chi Tiết

> **Cập nhật 2026-09 (docs sweep #52):** bản cũ hướng dẫn test SendGrid (email)
> + Twilio (SMS) với 5 endpoint như `/api/notification/test-email` — **service
> này chưa từng có những thứ đó**. Kênh duy nhất là **Firebase FCM push**. Bài
> dưới viết lại theo code thật (`Controllers/`, `Consumers/`, appsettings).

## 📋 Mục Lục

1. [Chuẩn Bị](#chuẩn-bị)
2. [Test FCM Push Trực Tiếp](#test-fcm-push-trực-tiếp)
3. [Test RabbitMQ Integration](#test-rabbitmq-integration)
4. [Test API Endpoints](#test-api-endpoints)
5. [Troubleshooting](#troubleshooting)

---

## 🔧 Chuẩn Bị

### Bước 1: RabbitMQ

**Khuyến nghị — qua compose** (từ `ev-dealer-management/`):

```powershell
docker compose up -d rabbitmq
```

Container thật tên `evm_rabbitmq` (định nghĩa trong `docker-compose.yml` — không
phải `docker start rabbitmq`). Management UI: http://localhost:15672 (guest/guest).

### Bước 2: Firebase credentials

1. Project Firebase `ev-dealer-management-6c620` → Project settings → Service
   accounts → **Generate new private key**
2. Đặt file tại `NotificationService/firebase-credentials.json` (hoặc trỏ env
   `Firebase__CredentialPath` tới file khác)
3. Lấy **registration token** của một thiết bị: dễ nhất qua frontend — login,
   bật notification trên trình duyệt/mobile; component FCM
   (`ev-dealer-frontend/src/firebase/notificationService.js`) tự đăng ký token
   vào `PUT /api/DeviceTokens/user:<id>`

Hướng dẫn chi tiết từng bước: `FIREBASE_SETUP.md`.

⚠️ `FirebaseFcmService` (singleton) đọc credentials trong constructor và chỉ
được resolve khi có request đầu tiên chạm tới nó: thiếu file thì service vẫn
boot, `/health` vẫn healthy, nhưng mọi API notification fail 500 và consumer log
lỗi ở message đầu tiên.

### Bước 3: Cấu hình

`appsettings.json` đã có sẵn giá trị dev đúng — thường **không cần sửa gì**:

```json
{
  "RabbitMQ": {
    "HostName": "localhost",
    "Port": 5672,
    "UserName": "guest",
    "Password": "guest",
    "Queues": { "...": "14 queue: sales.completed, vehicle.reserved, testdrive.scheduled, order.created, quote.created, contract.created, payment.received, customer.*, vehicle.*, order.status.changed, ..." }
  },
  "Firebase": { "ProjectId": "...", "CredentialPath": "firebase-credentials.json" },
  "Jwt": { "Key": "<phải giống UserService>" }
}
```

### Bước 4: Khởi chạy service

```powershell
cd ev-dealer-management/NotificationService
dotnet build
dotnet run
```

Service chạy tại: **http://localhost:5051** (dev port trong `launchSettings.json`)
Swagger UI: **http://localhost:5051/swagger** (Development)

---

## 📲 Test FCM Push Trực Tiếp

Cách nhanh nhất — dùng script có sẵn:

```powershell
cd ev-dealer-management/NotificationService
.\test-fcm.ps1
```

Script hỏi device token rồi gọi `POST /api/Notification/test-fcm`:

```powershell
Invoke-RestMethod -Uri "http://localhost:5051/api/Notification/test-fcm" `
  -Method Post -ContentType "application/json" `
  -Body (@{ deviceToken = "<token-that>"; title = "Test"; body = "Hello from dev" } | ConvertTo-Json)
```

**Kết quả mong đợi:** thiết bị bật popup push "Test". Log:
`[INF] FCM notification sent` (FirebaseFcmService).

Các biến thể khác:

| Endpoint | Body | Kết quả |
|---|---|---|
| `POST /api/Notification/subscribe-topic` | `{deviceToken, topic}` | device nhận broadcast topic |
| `POST /api/Notification/send-to-topic` | `{topic, title, body, data?}` | push cả topic |
| `POST /api/Notification/send-multicast` | `{deviceTokens[], title, body}` | push nhiều máy 1 lượt |

---

## 🐰 Test RabbitMQ Integration

### Cách 1: TestProducer.ps1 (có sẵn)

`TestProducer.ps1` publish message giả qua RabbitMQ HTTP API (guest/guest):

```powershell
.\TestProducer.ps1                      # gửi cả 3 loại chính
.\TestProducer.ps1 -EventType sales     # chỉ sales.completed
```

Queue nó publish vào: `sales.completed`, `vehicle.reserved`, `testdrive.scheduled`
(3 trong số **14** queue service đang consume).

### Cách 2: Publish thủ công qua Management UI

Tab Queues → chọn queue → Publish message. **Lưu ý format:** consumers deserialize
bằng `System.Text.Json` với `PropertyNameCaseInsensitive`, nên payload flat
lowerCamel **có** parse được khi thiếu `MessageId`/headers — nhưng chuẩn nhất là
publish bằng publisher .NET (cách 1 hoặc qua service thật).

Payload mẫu khớp DTO `Events/SaleCompletedEvent.cs`:

```json
{
  "orderId": 1,
  "customerName": "Nguyen Test",
  "vehicleModel": "Tesla Model 3",
  "totalPrice": 42000.00,
  "completedAt": "2026-09-15T14:30:00Z",
  "deviceToken": "<token-that>"
}
```

**Kiểm tra sau khi publish:**
- Log NotificationService:
  ```
  [INF] Started consuming from queue: sales.completed
  [INF] Processing SaleCompletedEvent ...        (SaleCompletedConsumer)
  ```
- Thiết bị nhận push. Nếu event không có `deviceToken` và subject
  (`user:<id>`...) chưa đăng ký token nào → consumer log
  `No device token found ... Skipping push notification` (vẫn ACK, không requeue).

### Cách 3: Đầu-cuối qua service thật

```powershell
docker compose up -d   # hoặc chạy VehicleService + SalesService riêng
# VehicleService (port dev 5068) tạo reservation →
#   [INF] Published message of type VehicleReservedEvent to exchange 'vehicle_events' ...
```

---

## 🔌 Test API Endpoints

### Danh sách endpoint THẬT (kiểm chứng trong Controllers/)

```
GET  /health                                  # {status, service, timestamp}
POST /api/Notification/test-fcm               # {deviceToken, title, body, data?}
POST /api/Notification/subscribe-topic        # {deviceToken, topic}
POST /api/Notification/unsubscribe-topic      # {deviceToken, topic}
POST /api/Notification/send-to-topic          # {topic, title, body, data?}
POST /api/Notification/send-multicast         # {deviceTokens[], title, body, data?}
GET/PUT /api/Notification/preferences         # [Authorize] — 8 cờ (#51)
PUT/GET/DELETE /api/DeviceTokens/{key}        # [Authorize] — device-token registry
```

### Test Health Check

```powershell
Invoke-RestMethod -Uri "http://localhost:5051/health"
```

```json
{ "status": "healthy", "service": "NotificationService", "timestamp": "..." }
```

### Test preferences (#51)

Cần JWT access token từ UserService (login thật hoặc lấy token qua API):

```powershell
$h = @{ Authorization = "Bearer <JWT>" }
Invoke-RestMethod "http://localhost:5051/api/Notification/preferences" -Headers $h
Invoke-RestMethod "http://localhost:5051/api/Notification/preferences" -Method Put `
  -Headers $h -ContentType "application/json" `
  -Body '{"orderUpdates":true,"vehicleUpdates":false,"promotions":true,"salesNotifications":false,"testDriveNotifications":true,"newVehicleAlerts":false,"orderStatusChanges":true,"notifications":true}'
```

PUT trả về đúng 8 cờ đã lưu; GET lại phải thấy y hệt.

### Postman

Import collection tự viết theo danh sách endpoint trên (base URL
`http://localhost:5051`), 2 collection cũ trỏ `.../test-email` trong bài bản cũ
không dùng được nữa — **không có endpoint email/SMS nào cả**.

---

## 🐛 Troubleshooting

### Lỗi 1: 500 trên mọi endpoint — `firebase-credentials.json`

**Triệu chứng:** `FileNotFoundException` / `InvalidOperationException` liên
quan service account, ngay request đầu tiên.

**Giải quyết:** đặt file credentials đúng path (hoặc `Firebase__CredentialPath`),
đảm bảo JSON là **service-account** key (có `private_key`, `client_email`
`...@firebaseclient...`), không phải API key.

### Lỗi 2: Push không tới thiết bị

- Token còn sống? Token chết bị tự-thu hồi (#44) → đăng ký lại
  `PUT /api/DeviceTokens/{key}` hoặc mở frontend + bật notification.
- FCM trả lỗi gì? Look `Log.Warning` trong consumer log (INVALID_REGISTRATION,
  SENDER_ID_MISMATCH → sai project).
- Device cùng project Firebase với credentials?

### Lỗi 3: RabbitMQ connection failed

```
Could not connect to RabbitMQ for consuming. Check connection settings.
```

```powershell
docker ps | Select-String evm_rabbitmq   # container compose
Test-NetConnection localhost -Port 5672
docker restart evm_rabbitmq
```

### Lỗi 4: Messages không được consume

1. Service đang chạy? `http://localhost:5051/health`
2. Log có `Started consuming from queue: <14 queue>`?
3. Queue name khớp `appsettings.json → RabbitMQ:Queues`?
4. RabbitMQ UI: Ready > 0 + Consumers = 0 → consumer chưa bind đúng.

### Lỗi 5: 401 trên preferences/DeviceTokens

Thiếu/hết hạn JWT, hoặc `Jwt:Key` khác UserService → token không verify được.
Key phải cùng giá trị trong `docker-compose.yml` cho mọi service.

---

## ✅ Checklist Test Hoàn Chỉnh

### Pre-Test Setup
- [ ] RabbitMQ đang chạy (`docker compose up -d rabbitmq` → `evm_rabbitmq`)
- [ ] `firebase-credentials.json` hợp lệ, đúng project
- [ ] Có device token thật (từ frontend hoặc FCM console)
- [ ] NotificationService chạy tại :5051

### FCM Tests
- [ ] `.\test-fcm.ps1` → thiết bị nhận push
- [ ] subscribe-topic → send-to-topic → device nhận broadcast
- [ ] send-multicast với ≥2 token

### RabbitMQ Tests
- [ ] `.\TestProducer.ps1` publish vào 3 queue chính
- [ ] Consumer log in "Processing ...Event" cho mỗi loại
- [ ] Message được ACK (Ready quay về 0)
- [ ] Payload không có deviceToken → log "No device token found", không crash

### API Tests
- [ ] `/health` trả healthy
- [ ] Swagger UI mở được
- [ ] preferences GET/PUT round-trip đúng 8 cờ ([Authorize])
- [ ] 401 khi thiếu JWT

### Integration Tests
- [ ] VehicleService: tạo reservation → push tới user
- [ ] SalesService: order hoàn tất → `sales.completed` → push
- [ ] CustomerService: đặt lịch test drive → `testdrive.scheduled` → push

### Logging Tests
- [ ] Console logs Serilog
- [ ] File logs `Logs/notification-service-*.log`

---

## 📊 Expected Results Summary

| Test Case | Expected Result | Verification |
|-----------|----------------|--------------|
| test-fcm | 200 OK | Device popup |
| send-to-topic | 200 OK | All topic subscribers nhận push |
| TestProducer sale event | Consumer log "Processing SaleCompletedEvent" | Log + device |
| Reservation end-to-end | `vehicle.reserved` consumed → push | RabbitMQ UI + device |
| Preferences GET/PUT | 200, 8 flags round-trip | JSON response |
| Health Check | 200 `{status: healthy}` | Response body |

---

## 📞 Support

1. Logs trong `Logs/notification-service-*.log`
2. Console output của service
3. RabbitMQ Management UI (queues + consumers)
4. `QUICK_START.md` (cùng thư mục) — Common Issues
5. Firebase Console → Cloud Messaging → gửi test từ phía Firebase để tách lỗi "FCM được chưa" vs "service gửi chưa"

**Happy Testing! 🚀**
