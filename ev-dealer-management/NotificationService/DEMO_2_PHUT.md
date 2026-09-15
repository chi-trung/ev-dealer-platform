# 🎬 DEMO NHANH - Test Trong 2 Phút

> **Cập nhật 2026-09 (docs sweep #52):** bản cũ hướng dẫn luồng "SMS xác nhận" —
> NotificationService **không có SMS**; kênh duy nhất là **FCM push**. Các đường
> dẫn `D:\Nam_3\...` là máy dev cũ. Bản dưới khớp repo + code hiện tại.

## Cách Test Nhanh Nhất

### ⚡ Bước 1: Start Services

Chạy từ **gốc repo** (`ev-dealer-platform/ev-dealer-management`) — compose đã
khai báo sẵn toàn stack, container thật tên `evm_*`:

```powershell
docker compose up -d rabbitmq vehicleservice notificationservice
```

Hoặc dotnet từng service (port dev theo `launchSettings.json`:
VehicleService `5068`, NotificationService `5051`):

```powershell
# Terminal 1 - NotificationService
cd ev-dealer-management/NotificationService; dotnet run
# Terminal 2 - VehicleService
cd ev-dealer-management/VehicleService; dotnet run
# Terminal 3 - Frontend
cd ev-dealer-frontend; npm run dev
```

⚠️ NotificationService **cần** `firebase-credentials.json` (path cấu hình được
qua env `Firebase__CredentialPath` — xem `FIREBASE_SETUP.md`); thiếu file thì
service vẫn boot nhưng mọi API notification fail 500 ngay request đầu tiên.

Chờ **30 giây** để tất cả services khởi động.

---

### 🌐 Bước 2: Mở Browser

Truy cập: **http://localhost:5173/vehicles** (form đặt xe không yêu cầu login;
chỉ các API notification/preferences/DeviceTokens mới cần JWT).

---

### 🚗 Bước 3: Test Đặt Xe

1. **Click vào xe đầu tiên** trong danh sách
2. **Scroll xuống** hoặc click nút **"Đặt Xe Ngay"**
3. **Điền form:**
   ```
   Tên: Test User
   Email: test@example.com
   Phone: +84987654321
   Chọn màu: (chọn bất kỳ)
   Số lượng: 1
   ```
4. **Click "Xác Nhận"**

---

### ✅ Bước 4: Xem Kết Quả

#### 🎉 Thành Công Khi:

**1. Toast notification hiện lên góc phải trên** (thành công từ API — toast này
là frontend, không phải SMS):

**2. Check NotificationService log:**
```
[INF] Started consuming from queue: vehicle.reserved
[INF] Received VehicleReservedEvent ... (VehicleReservedConsumer)
```
→ consumer gửi **FCM push** tới device token đã đăng ký của subject.

**3. Check RabbitMQ:**
- Mở: http://localhost:15672 (guest/guest)
- Tab **Queues** → `vehicle.reserved`
- **Message rates** sẽ hiện 1 message delivered, Ready = 0

**4. (Đầu-cuối thật)** Browser đặt xe phải **cho phép notification** — frontend
gắn device token (FCM init qua `src/firebase/notificationService.js`) vào
chính request đặt xe; `VehicleReservedConsumer` đẩy FCM thẳng tới token đó.

---

## 🎯 Kết Quả Mong Đợi

| Component | Kết Quả |
|-------------|----------|
| Frontend | Notification xanh hiện 6 giây |
| Backend | Log "Received VehicleReservedEvent" |
| RabbitMQ | 1 message consumed (queue `vehicle.reserved`) |
| FCM push | Thiết bị nhận notification (nếu token còn hợp lệ) |

---

## 🐛 Nếu Lỗi

### ❌ Notification không hiện
```powershell
# Refresh browser
Ctrl + Shift + R
```

### ❌ "Network Error"
```powershell
# Check services running (đúng port dev hiện tại)
curl http://localhost:5068/health   # VehicleService
curl http://localhost:5051/health   # NotificationService
```

### ❌ NotificationService trả 500 trên mọi API
`FileNotFoundException: firebase-credentials.json` — đặt file credentials
(hoặc env `Firebase__CredentialPath`).

### ❌ "Đặt xe thất bại"
- Check xe còn hàng không (stockQuantity > 0)
- Check colorVariant có sẵn không
- VehicleService chạy chưa (`curl http://localhost:5068/health`)

---

## 🎊 Xong!

Nếu notification hiện → **THÀNH CÔNG!** 🎉

- ✅ Frontend đẹp với notification
- ✅ RabbitMQ event-driven architecture (14 queue đang chạy)
- ✅ Push FCM tới thiết bị (thay cho mock SMS/email ngày xưa)

---

## ⚡ Cách nhanh hơn nữa: test FCM trực tiếp

Không cần đặt xe — gọi thẳng endpoint test với 1 device token thật:

```powershell
cd ev-dealer-management/NotificationService
.\test-fcm.ps1
```

---

**Chúc test thành công! 🚀**
