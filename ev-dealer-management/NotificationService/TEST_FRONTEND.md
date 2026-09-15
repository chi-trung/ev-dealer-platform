# 🎯 TEST FRONTEND - Đặt Xe Có Thông Báo

> **Cập nhật 2026-09 (docs sweep #52):** bản cũ mô tả nhận SMS Twilio sau khi
> đặt xe — **không có SMS trong hệ thống này**. Luồng thật: reservation →
> RabbitMQ → NotificationService → **FCM push** tới thiết bị đã đăng ký token.

## 📋 Chuẩn Bị

### 1️⃣ Start Backend Services

**Cách gọn nhất — compose** (từ `ev-dealer-management/`):

```powershell
docker compose up -d rabbitmq notificationservice vehicleservice
```

**Hoặc manual từng terminal** (port dev theo `launchSettings.json`):

```powershell
# Terminal 2 - NotificationService (http://localhost:5051)
cd ev-dealer-management/NotificationService
dotnet run

# Terminal 3 - VehicleService (http://localhost:5068)
cd ev-dealer-management/VehicleService
dotnet run
```

⚠️ NotificationService cần `firebase-credentials.json` cho các API push
(`FIREBASE_SETUP.md`).

### 2️⃣ Start Frontend (Terminal 4)

```powershell
cd ev-dealer-frontend
npm run dev
```

Mở browser: **http://localhost:5173**

---

## 🧪 TEST TRÊN FRONTEND

### Bước 1: Vào Trang Chi Tiết Xe
1. Vào trang **Danh Sách Xe**: http://localhost:5173/vehicles
2. Click vào 1 xe bất kỳ để vào trang chi tiết
3. URL sẽ là: http://localhost:5173/vehicles/1 (hoặc ID khác)

### Bước 2: Đặt Xe (Reservation)
1. Kéo xuống phần **"Đặt Xe"** hoặc click nút **"Đặt Xe Ngay"**
2. Điền form đặt xe:
   - **Tên khách hàng:** `Nguyen Van A`
   - **Email:** `test@example.com`
   - **Số điện thoại:** `+84987654321`
   - **Chọn màu xe:** Chọn 1 màu bất kỳ
   - **Số lượng:** `1`
   - **Ghi chú:** (tùy chọn) `Test reservation`

3. Click **"Xác Nhận Đặt Xe"**

### Bước 3: Kiểm Tra Kết Quả ✅

#### ✨ Trên Frontend
- **Dialog thành công** hiển thị (text thật trong `ReservationDialog.jsx`):
  ```
  ✅ Đặt xe thành công!
  Chúng tôi đã nhận được yêu cầu đặt xe của bạn cho [model]
  ✔ Thông báo đã được gửi đến thiết bị của bạn
  ```
- Không còn dòng "SMS xác nhận..." — không có kênh SMS nào

#### 📲 FCM Push
Riêng luồng reservation: frontend gắn `deviceToken` (từ
`src/firebase/notificationService.js`) vào chính request đặt xe →
VehicleService đưa vào event → consumer đẩy thẳng FCM tới token đó. Máy chạy
browser phải **cho phép notification + FCM đã init** thì mới có token (nếu
không, consumer log `No device token found ... Skipping push notification`).
13 consumer còn lại resolve token qua registry `user:<id>` (API DeviceTokens).

#### 🖥️ Backend Logs (NotificationService Terminal)
```
[INF] Started consuming from queue: vehicle.reserved
[INF] Processing VehicleReservedEvent for Vehicle: 1, Customer: Nguyen Van A
[INF] Reservation confirmation push notification sent for Vehicle: 1, ...
```
(Nếu thấy `No device token found ... Skipping push notification` — thiết bị
chưa đăng ký token, xem FIREBASE_SETUP.md.)

#### 🐰 RabbitMQ UI
1. Mở: http://localhost:15672
2. Login: guest / guest
3. Tab **Queues** → Chọn `vehicle.reserved`
4. Xem **Message Stats**: 1 message delivered and acknowledged

---

## 🎨 Thông Báo Trên UI

### Thành Công (Success) ✅
- **Màu xanh lá**
- Icon: ✅ CheckCircle
- Dialog xác nhận trong trang đặt xe

### Lỗi (Error) ❌
- **Màu đỏ**
- Icon: ❌ Error
- Hiện: "Đặt xe thất bại: [lý do]"

### Vị Trí
- **Top-Right** (góc phải trên)
- Không che mất nội dung quan trọng

---

## 🐛 TroubleShooting

### ❌ Không Hiện Notification
**Nguyên nhân:**
- Frontend chưa được refresh sau khi cập nhật code

**Giải pháp:**
```powershell
# Hard refresh trong browser
Ctrl + Shift + R (Windows)
Cmd + Shift + R (Mac)

# Hoặc restart Vite dev server
npm run dev
```

### ❌ Lỗi "Failed to create reservation"
**Nguyên nhân:**
- VehicleService chưa chạy
- Database chưa có dữ liệu

**Giải pháp:**
```powershell
# Check VehicleService running (port dev 5068)
curl http://localhost:5068/health

# Check có xe trong DB không
curl http://localhost:5068/api/vehicles
```

### ❌ FCM Push Không Tới Máy
**Nguyên nhân:**
- NotificationService chưa chạy / thiếu `firebase-credentials.json`
- RabbitMQ chưa chạy
- Thiết bị chưa đăng ký device token

**Giải pháp:**
```powershell
# Check NotificationService (port dev 5051)
curl http://localhost:5051/health

# Check RabbitMQ (container compose tên evm_rabbitmq)
docker ps | Select-String evm_rabbitmq

# Test đẩy thẳng 1 token để tách lỗi (script có sẵn)
.\test-fcm.ps1
```

---

## 🎯 Test Cases Khác

### Test 1: Đặt Xe Hết Hàng (Out of Stock)
1. Đặt xe với quantity > stock
2. **Kỳ vọng:** Notification lỗi màu đỏ: "Không đủ hàng trong kho"

### Test 2: Điền Sai Form
1. Bỏ trống tên/email/phone
2. **Kỳ vọng:** Form validation error (không call API)

### Test 3: Network Error
1. Tắt VehicleService
2. Đặt xe
3. **Kỳ vọng:** Notification lỗi: "Network error" hoặc timeout

---

## ✅ Checklist Hoàn Thành

- [ ] RabbitMQ đang chạy (port 5672)
- [ ] NotificationService đang chạy (port 5051)
- [ ] VehicleService đang chạy (port 5068)
- [ ] Frontend đang chạy (port 5173)
- [ ] Vào trang chi tiết xe thành công
- [ ] Điền form đặt xe đầy đủ
- [ ] Thông báo xuất hiện khi đặt xe
- [ ] FCM push nhận được (nếu thiết bị đã đăng ký token)
- [ ] Backend log hiện "push notification sent"

---

## 🎉 Thành Công Khi

✅ **Frontend:** Dialog "Đặt xe thành công!" hiện ra  
✅ **Backend:** NotificationService log "Reservation confirmation push notification sent"  
✅ **RabbitMQ:** Message delivered and acknowledged  
✅ **FCM:** Thiết bị nhận push (nếu device token hợp lệ)

---

## 📝 Notes Quan Trọng

1. **Reservation push theo token của chính máy đặt xe** (token đi cùng request)
   — muốn người khác cũng nhận push qua registry là chuyện của các event
   order/contract/payment...
2. **Không có mock SMS/email nữa** — thiếu credentials Firebase là fail loudly
   (500 + log), không im lặng giả vờ gửi.
3. Chi tiết kiến trúc + endpoint: `README.md`, `QUICK_START.md` cùng thư mục.
