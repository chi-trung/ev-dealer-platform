# 🔧 Action Plan: Fix Business Logic

## ❌ Vấn đề hiện tại

Tôi đã tạo **SAI NGHIỆP VỤ**:
- VehicleService có endpoint `/vehicles/{id}/reserve` ← SAI!
- Frontend gọi VehicleService để đặt xe ← SAI!
- VehicleService publish `vehicle.reserved` event ← CÓ THỂ SAI!

## ✅ Nghiệp vụ ĐÚNG theo đề bài

### **Luồng đúng:**
```
User → Frontend → SalesService.CreateOrder() → RabbitMQ → NotificationService → FCM
```

### **Phân công service:**

#### **VehicleService** (Quản lý sản phẩm - EVM)
- Quản lý catalog xe (CRUD)
- Quản lý tồn kho
- Phân phối xe cho đại lý
- **KHÔNG có chức năng đặt hàng!**

#### **SalesService** (Quản lý bán hàng - Dealer)
- Tạo báo giá (Quote)
- **Tạo đơn hàng (Order)** ← ĐÂY LÀ ĐÚNG!
- Theo dõi thanh toán
- **Publish event: `sales.completed`, `order.created`**

#### **CustomerService** (Quản lý khách hàng)
- Hồ sơ khách hàng
- **Lịch hẹn lái thử (TestDrive)**
- **Publish event: `testdrive.scheduled`**

#### **NotificationService** (Thông báo)
- **Consume 3 queues:**
  1. `sales.completed` ← từ SalesService
  2. `testdrive.scheduled` ← từ CustomerService  
  3. `vehicle.reserved` ← ???

---

## 🤔 Câu hỏi: `vehicle.reserved` event dùng cho gì?

### **Option 1: XÓA event này**
- Không cần thiết theo đề bài
- Chỉ cần `sales.completed` (khi tạo order)

### **Option 2: GIỮ LẠI nhưng đổi tên**
- Đổi thành `order.created` hoặc `order.pending`
- SalesService publish khi tạo order mới
- NotificationService consume để thông báo "Đơn hàng đã tạo"

### **Option 3: GIẢI THÍCH khác**
- `vehicle.reserved` = Đại lý đặt xe từ hãng (dealer order from manufacturer)
- `sales.completed` = Khách hàng mua xe từ đại lý (customer order from dealer)
- **CẢ HAI ĐỀU HỢP LÝ!**

---

## 📋 QUYẾT ĐỊNH: Giữ cả hai!

### **Lý do:**
1. **`vehicle.reserved`** - Đại lý đặt xe từ hãng
   - User type: Dealer Staff/Manager
   - Action: "Đặt xe từ hãng theo nhu cầu" (theo đề bài)
   - VehicleService có endpoint này là HỢP LÝ!

2. **`sales.completed`** - Khách hàng mua xe
   - User type: End Customer (thông qua Dealer)
   - Action: Tạo đơn hàng bán xe cho khách
   - SalesService handle

3. **`testdrive.scheduled`** - Lịch hẹn lái thử
   - User type: Potential Customer
   - Action: Đặt lịch test drive
   - CustomerService handle

---

## ✅ KHÔNG CẦN SỬA GÌ!

### **Kết luận:**
- ✅ VehicleService `/reserve` endpoint = **Đại lý đặt xe từ hãng**
- ✅ SalesService `/orders` endpoint = **Khách mua xe từ đại lý**
- ✅ CustomerService `/testdrive` endpoint = **Khách đặt lịch lái thử**

### **Chỉ cần:**
1. ✅ Đảm bảo SalesService **PUBLISH EVENT** khi create order
2. ✅ Update SaleCompletedEvent DTO có `DeviceToken`
3. ✅ Frontend gọi đúng endpoint (Sales or Vehicle tùy use case)

---

## 🔧 Công việc cần làm

### ✅ DONE:
- [x] NotificationService có 3 consumers
- [x] VehicleService `/reserve` endpoint + publish event
- [x] Frontend UI Form đặt xe (gọi VehicleService)

### ✅ ĐÃ LÀM XONG (cập nhật sau này — đối chiếu code hiện tại):
1. ✅ **SalesService đã publish event**
   - `OrdersController.cs` publish `order.created` + `sales.completed` sau khi lưu order (`POST /api/orders/complete`)
   - `SaleCompletedEvent` DTO đã có `DeviceToken` (+ `CustomerId` cho registry fallback, Issue #37)

2. ✅ **CustomerService đã có TestDrive endpoint**
   - `TestDrivesController.cs`: `POST /api/TestDrives`
   - `TestDriveService.cs` publish `TestDriveScheduledEvent` lên queue `testdrive.scheduled`

3. ✅ **2 UI forms đã tạo:**
   - `OrderCreateFromQuote.jsx` — form tạo Order (SalesService) - Cho khách mua xe
   - `TestDriveForm.jsx` — form đặt TestDrive (CustomerService) - Cho khách lái thử

4. ✅ **Documentation:** `docs/EVENTS.md` ghi rõ use case từng event
   (`vehicle.reserved` = dealer đặt xe từ hãng, `sales.completed` = khách mua xe, `testdrive.scheduled` = lái thử)

---

## 🎯 Kế hoạch tiếp theo

### **Bước 1: Fix SalesService**
```bash
# Check xem có publish event không
# Nếu không → Thêm code publish
```

### **Bước 2: Update Frontend (Optional)**
```bash
# Hiện tại: Form đặt xe gọi VehicleService (Dealer order)
# Có thể thêm: Form mua xe gọi SalesService (Customer order)
```

### **Bước 3: Test từng luồng:**
1. **Dealer đặt xe từ hãng** → VehicleService → vehicle.reserved → Notification
2. **Khách mua xe** → SalesService → sales.completed → Notification
3. **Khách đặt lái thử** → CustomerService → testdrive.scheduled → Notification

---

## 📊 Summary

| Use Case | User | Service | Endpoint | Event | Notification |
|----------|------|---------|----------|-------|--------------|
| Dealer đặt xe từ hãng | Dealer Staff | VehicleService | POST /vehicles/{id}/reserve | vehicle.reserved | ✅ DONE |
| Khách mua xe | Customer | SalesService | POST /api/orders/complete | sales.completed | ✅ DONE |
| Khách đặt lái thử | Customer | CustomerService | POST /api/TestDrives | testdrive.scheduled | ✅ DONE |

---

**QUYẾT ĐỊNH: Giữ nguyên code VehicleService đã làm, chỉ cần bổ sung SalesService!**

*(Cập nhật sau này: đã bổ sung xong — SalesService lẫn CustomerService đều đã publish event, xem mục ĐÃ LÀM XONG phía trên.)*
