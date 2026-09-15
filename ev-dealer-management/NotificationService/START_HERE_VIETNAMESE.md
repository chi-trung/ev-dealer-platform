# 🎯 BẮT ĐẦU TEST - Đọc File Này Trước!

> **Cập nhật 2026-09 (docs sweep #52):** bản cũ hứa "SMS tự động gửi" —
> service này **không có SMS**, kênh duy nhất là **FCM push**. Đường dẫn
> `D:\Nam_3\...` là máy dev cũ, không còn đúng. Bản dưới khớp code hiện tại.

## ✅ Đã Làm Xong Gì?

- **NotificationToast** trên trang đặt xe (frontend)
- **NotificationService**: 14 RabbitMQ consumers → **FCM push**
  (`FirebaseFcmService`), device-token registry + preferences (#33–#51)
- VehicleService/SalesService/CustomerService đều đã publish event thật

## 🚀 Test Ngay Trong 3 Bước

### Bước 1: Start Services

**Cách khuyến nghị — Docker Compose** (chạy từ `ev-dealer-management/`):

```powershell
docker compose up -d rabbitmq vehicleservice notificationservice
```

Container thật tên `evm_rabbitmq` / `evm_vehicleservice` /
`evm_notificationservice`.

⚠️ NotificationService cần `firebase-credentials.json` cho mọi API notification
(xem `FIREBASE_SETUP.md`).

*Còn* `.\start-all.ps1` trong thư mục này vẫn hardcode đường dẫn `D:\Nam_3\`
máy dev cũ + tạo container `rabbitmq` riêng — dùng compose ở trên thay vì
script này (script nằm trong đợt cleanup tiếp theo, Issue #53).

Chờ **30 giây** để services khởi động.

---

### Bước 2: Mở Browser

Truy cập: **http://localhost:5173/vehicles** (form đặt xe không cần login).

---

### Bước 3: Đặt Xe

1. Click vào 1 chiếc xe bất kỳ
2. Kéo xuống phần **"Đặt Xe"**
3. Điền thông tin:
   - Tên: `Nguyen Van A`
   - Email: `test@example.com`
   - Phone: `+84987654321`
   - Chọn màu xe
   - Số lượng: `1`
4. Click **"Xác Nhận Đặt Xe"**

---

## 🎉 Kết Quả Mong Đợi

### Trên Frontend
Notification màu xanh hiện lên:
```
✅ Đặt xe thành công!
   Mã đặt chỗ: 123
```
(toast là của frontend — không có SMS nào được gửi)

### Trên Backend (NotificationService)
```
[INF] Started consuming from queue: vehicle.reserved
[INF] Received VehicleReservedEvent ...   (VehicleReservedConsumer)
```
→ consumer đẩy **FCM push** tới device token của máy đặt (frontend gắn token
   vào request — browser phải cho phép notification).

### Trên RabbitMQ
- Mở: http://localhost:15672 (guest/guest)
- Tab **Queues** → `vehicle.reserved`
- Thấy message đã được consumed (Ready = 0)

---

## 🐛 Nếu Có Lỗi

### ❌ Notification không hiện
```powershell
# Refresh trình duyệt
Ctrl + Shift + R
```

### ❌ Services không start
```powershell
docker ps | Select-String evm_     # RabbitMQ + services chạy chưa
curl http://localhost:5068/health  # VehicleService (port dev 5068)
curl http://localhost:5051/health  # NotificationService (port dev 5051)
```

### ❌ API NotificationService trả 500
Thiếu `firebase-credentials.json` — xem `FIREBASE_SETUP.md`.

---

## 📚 Đọc Thêm

| File | Khi Nào Đọc |
|------|-------------|
| **DEMO_2_PHUT.md** | Test nhanh không cần đọc nhiều |
| **QUICK_START.md** | Setup + endpoint thật + common issues |
| **README.md** | Overview service + kiến trúc |
| **FIREBASE_SETUP.md** | Credentials + cách lấy device token |

---

## ✨ Demo

**Sau khi click "Xác Nhận":**
- ⏳ Loading 1-2 giây
- ✅ Notification xanh hiện lên
- 📱 Thiết bị nhận FCM push (nếu browser đã cho phép notification)
- 🎊 Dialog đóng lại
- ✅ Hoàn tất!

---

## 🎯 Checklist Nhanh

- [ ] `docker compose up -d rabbitmq vehicleservice notificationservice`
- [ ] Chờ 30 giây
- [ ] Mở http://localhost:5173/vehicles
- [ ] Click xe → Đặt xe
- [ ] Điền form → Submit
- [ ] ✅ Notification hiện lên
- [ ] ✅ THÀNH CÔNG!

---

**Good luck! 🚀 Nếu có lỗi, đọc QUICK_START.md phần Common Issues**
