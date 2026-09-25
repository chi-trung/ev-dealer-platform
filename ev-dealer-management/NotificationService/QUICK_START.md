# ✅ NotificationService - COMPLETE SETUP SUMMARY

> **Cập nhật 2026-09 (docs sweep #52):** bản cũ mô tả SendGrid email + Twilio SMS
> mock và các endpoint `/api/notification/test-email|order-confirmation|...` —
> **chưa từng tồn tại trong code**. Service thật chỉ có một kênh: **Firebase
> Cloud Messaging (FCM) push**. Bài viết dưới đã viết lại theo code hiện tại.
> Chi tiết test đầu-cuối: xem `TESTING_GUIDE.md` ở thư mục gốc repo.

## 🎉 Tình Trạng Hiện Tại

### ✅ Đã Hoàn Thành (đối chiếu code)

1. **NotificationService Backend**
   - `FirebaseFcmService` (`FirebaseAdmin` 3.0.1) — push + topic + multicast
   - **14** RabbitMQ consumers (`Program.cs`, mỗi queue một consumer — khai báo
     trong `appsettings.json` → `RabbitMQ:Queues`)
   - `NotificationController` (`/api/Notification/*`) + `DeviceTokensController`
     (`/api/DeviceTokens/{key}`, JWT-authorized, #33/#36/#44) +
     `GET/PUT /api/Notification/preferences` (#51)
   - Serilog logging, health check `GET /health`

2. **VehicleService Integration**
   - `RabbitMQProducerService` publish `vehicle.reserved` (và các event xe khác)
   - Event format khớp DTO consumer phía NotificationService

3. **Frontend Components**
   - `src/firebase/notificationService.js` — đăng ký device token theo subject
     `user:<id>` (JWT), nhận push trực tiếp từ Firebase
   - Trang Notifications + NotificationPreferences (persist server-side từ #51)

---

## 🚀 Quick Start - Test End-to-End

### Bước 1: Start All Services

**Cách nhanh nhất — chạy cả stack bằng compose (từ thư mục `ev-dealer-management`):**
```powershell
docker compose up -d rabbitmq notificationservice
```
Container thật tên `evm_rabbitmq` / `evm_notificationservice` (`container_name`
trong compose); có thể start lẻ bằng `docker start evm_rabbitmq`.

**Hoặc chạy từng service bằng dotnet (port dev theo `launchSettings.json`):**
```powershell
# Terminal 1 - NotificationService (http://localhost:5051)
cd ev-dealer-management/NotificationService
dotnet run
```
→ Health: http://localhost:5051/health

⚠️ Cần file credentials Firebase (`firebase-credentials.json`, path cấu hình
được bằng env `Firebase__CredentialPath` — xem FIREBASE_SETUP.md). Thiếu file,
service vẫn boot được nhưng mọi API notification fail 500 ngay request đầu
tiên (`FirebaseFcmService` đọc credentials khi được resolve; xem Common
Issues - Issue 2).

```powershell
# Terminal 2 - VehicleService (http://localhost:5068)
cd ev-dealer-management/VehicleService
dotnet run

# Terminal 3 - Frontend (optional, http://localhost:5173)
cd ev-dealer-frontend
npm run dev
```

---

## 🧪 Test Case 1: Vehicle Reservation → FCM Push (đầu-cuối)

```powershell
# Tạo reservation (API VehicleService, port dev 5068)
$reservationBody = @{
    colorVariantId = 1
    customerName = "Nguyen Van Test"
    customerEmail = "your-email@gmail.com"
    customerPhone = "+84901234567"
    notes = "Test reservation from PowerShell"
    quantity = 1
} | ConvertTo-Json

Invoke-RestMethod `
    -Uri "http://localhost:5068/api/vehicles/1/reserve" `
    -Method Post `
    -Body $reservationBody -ContentType "application/json"
```

### Expected Flow:

1. **VehicleService** tạo reservation → publish `vehicle.reserved` (log:
   `[INF] Published message of type VehicleReservedEvent to exchange 'vehicle_events' ...`)
2. **RabbitMQ** queue `vehicle.reserved` nhận message
3. **NotificationService** (`VehicleReservedConsumer`) consume → gửi **FCM push**
   thẳng tới `deviceToken` đi kèm trong event (frontend gắn token của máy đặt
   xe vào request — 13 consumer khác mới resolve token qua registry
   `user:<id>`)

### Check Results:

- **RabbitMQ UI** http://localhost:15672 (guest/guest) → tab Queues →
  `vehicle.reserved`: message published, consumed (Ready = 0), 1 consumer.
- **NotificationService logs:**
  `[INF] Started consuming from queue: vehicle.reserved` và
  processing/success log của `VehicleReservedConsumer`.
- **Máy đặt xe (browser đã cho phép notification):** popup push thật từ
  Firebase (token đi theo event; không còn mock SMS).

---

## 🧪 Test Case 2: Direct API Test (Nhanh nhất)

### Test FCM push trực tiếp tới một device token:

```powershell
curl -X POST http://localhost:5051/api/Notification/test-fcm `
  -H "Content-Type: application/json" `
  -d '{"deviceToken":"<token-máy-thật>","title":"Test","body":"Hello from dev"}'
```

Có sẵn script: `.\test-fcm.ps1` (gọi đúng endpoint `api/Notification/test-fcm`).

### Các endpoint FCM khác (`/api/Notification/`):

| Endpoint | Tác dụng |
|---|---|
| `POST subscribe-topic` / `unsubscribe-topic` | Bớt device token vào/ra topic FCM |
| `POST send-to-topic` | Broadcast push theo topic |
| `POST send-multicast` | Push nhiều token một lượt |
| `PUT/GET/DELETE /api/DeviceTokens/{key}` | Registry device token theo subject (`user:<id>`), JWT required |
| `GET/PUT /api/Notification/preferences` | Preference flags 8 cổng (#51), JWT required |

Swagger UI (khi Development): http://localhost:5051/swagger

---

## 📊 Verification Checklist

```powershell
# NotificationService health
Invoke-RestMethod http://localhost:5051/health

# RabbitMQ listening
Test-NetConnection localhost -Port 5672

# 14 queues (không phải 3!) — mở http://localhost:15672 → tab Queues:
# sales.completed, order.created, vehicle.reserved, vehicle.created/updated/deleted,
# testdrive.scheduled, contract.*, customer.*, payment.*, quote.*, sale.* ...
# (danh sách chuẩn: NotificationService/appsettings.json → RabbitMQ:Queues)
```

---

## 🐛 Common Issues & Solutions

### Issue 1: "Could not connect to RabbitMQ"
```powershell
docker ps | Select-String evm_rabbitmq
docker start evm_rabbitmq    # tên container thật
# hoặc: docker compose up -d rabbitmq
```

### Issue 2: 500 khi gọi bất kỳ API notification nào
`FileNotFoundException: firebase-credentials.json` — `FirebaseFcmService` nạp
credentials khi được resolve (request đầu tiên). Service vẫn boot + `/health`
vẫn OK. Fix: đặt file credentials (hoặc `Firebase__CredentialPath` trỏ tới file
hợp lệ). Xem FIREBASE_SETUP.md.

### Issue 3: "Message not consumed"
1. NotificationService đang chạy? Health `http://localhost:5051/health`
2. Queue có message? → RabbitMQ Management UI (Ready > 0 = consumer chưa nhận)
3. Logs: `.\Logs\notification-service-*.log`

### Issue 4: "VehicleService publish failed"
```powershell
# VehicleService logs should show:
[INF] Published message of type VehicleReservedEvent to exchange 'vehicle_events' with routing key 'vehicle.reserved'
# Config thật (appsettings.json): RabbitMQ.HostName/Port/UserName/Password
```

---

## 🎊 Success Criteria

✅ `http://localhost:5051/health` trả healthy
✅ RabbitMQ có **14** queue được consume (Ready = 0 khi service bật)
✅ Create reservation → push FCM tới thiết bị đã đăng ký device token
✅ `test-fcm.ps1` → thiết bị nhận popup "Test"
✅ (Tùy chọn) Đổi preference flags → GET lại thấy persist (#51)

---

**Bắt đầu từ Test Case 2 — `test-fcm.ps1` — rồi mới tới đầu-cuối! 🎉**

Questions? Check:
- `TESTING_GUIDE.md` (gốc repo) — test chi tiết
- `docs/EVENTS.md` — topology 14 queue/event
- `README.md` của service — overview + kiến trúc
