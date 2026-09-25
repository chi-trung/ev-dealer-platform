# NotificationService

Microservice xử lý **push notification (Firebase Cloud Messaging)** cho hệ thống
EV Dealer Management — tiêu thụ event RabbitMQ từ các service khác và đẩy thông
báo tới thiết bị người dùng.

> **Cập nhật 2026-09 (docs sweep #52):** bản cũ mô tả SendGrid (email) +
> Twilio (SMS) và các endpoint `test-email`/`order-confirmation`/... — những
> thứ **không hề tồn tại trong code**. Email (SMTP qua MailKit) là việc của
> **UserService**, không phải service này. Nội dung dưới đối chiếu code hiện tại.

## Tính năng

- **Push Notifications** (`FirebaseFcmService`, package `FirebaseAdmin` 3.0.1):
  gửi theo device token, theo topic, và multicast.
- **RabbitMQ Consumers — 14 queue** (khai báo trong `appsettings.json` →
  `RabbitMQ:Queues`; mỗi queue một consumer class trong `Consumers/`):
  `SaleCompletedConsumer`, `VehicleReservedConsumer`, `TestDriveScheduledConsumer`,
  `OrderCreatedConsumer`, `QuoteCreatedConsumer`, `ContractCreatedConsumer`,
  `OrderStatusChangedConsumer`, `PaymentReceivedConsumer`, `CustomerCreated/Updated/Deleted...`,
  `VehicleCreated/Updated/Deleted...` — mỗi consumer dịch event sang push FCM.
- **Device-token registry** (`DeviceTokensController`, `api/DeviceTokens/{key}`):
  thiết bị đăng ký token theo subject (`user:<id>`), JWT-authorized, có LRU cap
  + thu hồi token chết (Issues #33/#36/#44).
- **Notification preferences** (`GET/PUT /api/Notification/preferences`):
  8 cờ lựa chọn của từng user, persist server-side (Issue #51) và ENFORCE
  tại fan-out (Issue #56) — xem mục "Preference enforcement" dưới.

## Cấu hình

### 1. RabbitMQ

`appsettings.json` — services đọc `RabbitMQ:HostName` (env override
`RabbitMQ__HostName`), kèm `Port`/`UserName`/`Password` và retry policy:

```json
"RabbitMQ": {
  "HostName": "localhost",
  "Port": 5672,
  "UserName": "guest",
  "Password": "guest",
  "MaxDeliveryAttempts": 3,
  "RetryTtlMilliseconds": 5000,
  "Queues": { "SaleCompleted": "sales.completed", "VehicleReserved": "vehicle.reserved", "...": "14 queue" }
}
```

### 2. Firebase (channel duy nhất của service này)

- Tạo service-account JSON trong Firebase Console (project
  `ev-dealer-management-6c620`), đặt file theo path cấu hình
  `Firebase:CredentialPath` (mặc định `firebase-credentials.json` — **không**
  commit vào repo; trong Docker dùng env `Firebase__CredentialPath` + mount/copy
  file). Hướng dẫn đầy đủ: `FIREBASE_SETUP.md`.
- `Firebase:ProjectId` trong appsettings.
- ⚠️ `FirebaseFcmService` (singleton) nạp credentials trong constructor và chỉ
  được resolve khi có request/controller đầu tiên chạm tới nó: thiếu file thì
  service vẫn boot và `/health` vẫn trả healthy, nhưng mọi API notification fail
  500 và consumer log lỗi ở message đầu tiên.

### 3. JWT

`Jwt:Key` phải **byte-identical** với UserService/KitchenService các service
phát token (cùng biến trong `docker-compose.yml`) — endpoint DeviceTokens và
preferences authorize bằng claim `id` của token đó.

## Chạy Service

### Prerequisites

- RabbitMQ đang chạy (compose: `docker compose up -d rabbitmq` → container
  `evm_rabbitmq`)
- .NET 8 SDK
- File credentials Firebase (mục 2 ở trên)

```bash
cd ev-dealer-management/NotificationService
dotnet restore && dotnet build && dotnet run
```

Service chạy tại `http://localhost:5051` (dev port trong `launchSettings.json`;
Docker map `5051:80`).

## API Endpoints (tồn tại thật — kiểm chứng trong Controllers/)

```http
GET /health          # { status: "healthy", service: "NotificationService", timestamp }
```

`api/Notification` (`NotificationController`):

```http
POST /api/Notification/test-fcm        # { deviceToken, title, body, data? }
POST /api/Notification/subscribe-topic # { deviceToken, topic }
POST /api/Notification/unsubscribe-topic
POST /api/Notification/send-to-topic   # { topic, title, body, data? }
POST /api/Notification/send-multicast  # { deviceTokens[], title, body, data? }
GET/PUT /api/Notification/preferences  # [Authorize] 8 cờ preference (#51)
```

`api/DeviceTokens` (`DeviceTokensController`, [Authorize]):

```http
PUT    /api/DeviceTokens/{key}   # { deviceToken } — đăng ký/refresh token cho subject
GET    /api/DeviceTokens/{key}   # danh sách token của subject
DELETE /api/DeviceTokens/{key}   # thu hồi
```

Swagger UI khi chạy Development: `http://localhost:5051/swagger`.

## Event-Driven Architecture

Publishers đã hoạt động: VehicleService (reservation/CRUD xe), SalesService
(`order.created`, `sales.completed`, quote/contract), CustomerService
(`testdrive.scheduled`, customer CRUD), Payments (`payment.received`). Mỗi
event → 1 queue → consumer phía đây dịch thành FCM push cho subject
tương ứng (`user:<id>` / `dealer:<id>` / topic). Chi tiết topology:
`docs/EVENTS.md` ở thư mục solution.

Ví dụ payload `SaleCompletedEvent` (schema thật trong `Events/`):

```json
{
  "orderId": 1,
  "customerName": "John Doe",
  "vehicleModel": "Tesla Model 3",
  "totalPrice": 45000.00,
  "completedAt": "2025-01-15T14:30:00Z",
  "deviceToken": "..."
}
```

## Preference enforcement (Issue #56)

`NotificationPreferencePolicy` (Services/) là CHỖ DUY NHẤT biến document
preference của #51 thành quyết định giao-hay-không. Ba quyết định thiết kế
(issue ủy quyền, chốt ở đây + test pin lại):

**1. Scope — chỉ subject `user:<n>`.** Preferences là bảng cá nhân đăng
nhập portal (khóa đúng cách `NotificationSubjects.User` sinh ra, cùng id-
space với claim `id` của JWT — chứng minh ở `QuoteCreatedConsumer` doc).
`customer:` / `dealer:` không có surface preference nên KHÔNG BAO GIỜ bị
lọc — hành vi giao hàng của họ y hệt trước #56, và một key va chạm
id-space (vd row lạ lọt vào khóa `customer:5`) không thể mute traffic thật.

**2. Kênh — FCM = in-app.** Service chỉ có một kênh giao là FCM push; với
user đã đăng nhập đó chính là "in-app" trong trang cài đặt. Nên push bị
gate bởi cờ `InAppNotifications`; hai cờ email/sms vẫn lưu (PUT 8 cờ không
đổi contract) nhưng chưa gate gì — frontend ẨN hai toggle đó cho tới khi
có sender thật, để UI và enforcement khớp nhau (acceptance #56).

Mapping `data["type"]` của consumer → cờ type (khớp bảng trong
`NotificationPreferencePolicy.TypeFlags`, test:
`NotificationPreferencePolicyTests.TagToFlag`):

| type tag                                        | cờ gate       | mặc định (#51) |
|-------------------------------------------------|---------------|----------------|
| `quote`, `order`, `orders`, `sale`, `contract`  | `Orders`      | BẬT |
| `orderStatus`                                   | `Deliveries`  | BẬT |
| `payment`                                       | `Payments`    | BẬT |
| `customer`, `testdrive`, `vehicle*`             | `System`      | TẮT |
| `promotion` / `promotions`                      | `Promotions`  | TẮT (chưa có producer nào gắn tag này — mapping sẵn, producer tương lai khỏi sửa code) |
| tag lạ (consumer mới chưa có trong bảng)        | —             | GIAO (fail open; unmapped ≠ muted) |

Điểm enforcement thật hôm nay: push cho **salesperson phụ trách**
(`user:<SalespersonId>`) trong `QuoteCreatedConsumer` và
`ContractCreatedConsumer`. Push chính (khách hàng) KHÔNG qua policy. Push
salesperson là best-effort: lỗi trong block đó log to, không throw —
rethrow sẽ làm bus retry cả event và đẩy trùng cho khách.

**3. Store hỏng → FAIL OPEN.** Preference-store outage không được mute
toàn bộ push dạng user: mass-suppression trông y hệt lớp lỗi silent-drop
`docs/EVENTS.md` sinh ra để chống, trong khi giao-hơi-nhiều thì thấy được
và tự lành. Policy không bao giờ throw ở bước đọc store; có Log.Warning
to. Test: `StoreOutage_Delivers_FailOpen`.

User chưa từng Save nhận đúng shared defaults của `GetAsync` (orders/
deliveries/payments BẬT, system/promotions TẮT) — tức traffic mới gắn
tag system với user: sẽ mặc định muted; đó là đọc đúng của "chưa chọn gì
thì dùng default". Test: `NeverSavedUser_GetsSharedDefaults`.

## Logging

Serilog: console có màu + file `Logs/notification-service-YYYYMMDD.log`
(config `Serilog` trong appsettings).

## Testing

- `.\test-fcm.ps1` — push trực tiếp tới một device token (endpoint `test-fcm`).
- `.\TestProducer.ps1` — publish event giả vào RabbitMQ để test consumer.
- `.\QuickTest.ps1` — script tổ hợp cũ (kiểm tra lại endpoint nó gọi trước khi
  dùng; một số phần chưa được sweep trong đợt docs này).
- Unit tests thật: `DealerSystem.Tests/NotificationService.Tests/`
  (device-token registry, consumers, preferences...).
- Hướng dẫn đầu-cuối: `TESTING_GUIDE.md` ở gốc repo.

## Troubleshooting

### Push không tới máy
- Credentials Firebase hợp lệ? (`Firebase__CredentialPath`, project đúng)
- Device token còn sống? Token chết được thu hồi tự động (#44) — đăng ký lại
  qua `PUT /api/DeviceTokens/{key}` từ chính thiết bị.
- Consumer đã consume? RabbitMQ UI → queue tương ứng, Ready = 0.

### 500 trên mọi endpoint
`FileNotFoundException: firebase-credentials.json` — xem mục 2 phần Cấu hình.

### RabbitMQ connection failed
Container tên `evm_rabbitmq` (không phải `rabbitmq`); `docker compose up -d
rabbitmq`; port 5672 không bị block.

## Dependencies (đúng csproj)

- `FirebaseAdmin` 3.0.1 — push
- `RabbitMQ.Client` 6.8.1 (+ `MassTransit`/`MassTransit.RabbitMQ` 8.2.3 — hiện không dùng bus, chỉ tham khảo)
- `Microsoft.EntityFrameworkCore.Sqlite` 8.0.0 — device-token registry + preferences DB
- `Microsoft.AspNetCore.Authentication.JwtBearer` 8.0.0
- `Serilog.AspNetCore` 9.0.0 (+ Console/File sinks), `Swashbuckle.AspNetCore` 6.6.2

## Next Steps (còn thiếu thật)

- [ ] Gửi **email xác nhận** đơn/test-drive từ NotificationService (hiện chỉ có
      SMTP trong UserService cho luồng password-reset)
- [ ] Notification history page (lưu + hiển thị lịch sử push)
- [ ] Metrics/monitoring
