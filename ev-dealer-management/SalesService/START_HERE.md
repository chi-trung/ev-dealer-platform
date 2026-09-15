# 🎯 QUICK START - Test Complete Order Feature

> **Cập nhật 2026-09 (docs sweep #52):** luồng cũ vẽ "NotificationService →
> SendGrid → email" — **không có email/SMS trong hệ thống**; NotificationService
> chỉ đẩy **FCM push**. Đường dẫn `D:\Nam_3\...` là máy dev cũ, RabbitMQ container
> thật tên `evm_rabbitmq`. Bài dưới khớp code hiện tại.

## ⚡ 30-Second Test

### Start Services:

**Cách gọn nhất — compose** (từ `ev-dealer-management/`):

```powershell
docker compose up -d rabbitmq salesservice notificationservice
```

**Hoặc manual:**

```powershell
# Terminal 2: NotificationService (port dev 5051)
cd ev-dealer-management/NotificationService; dotnet run

# Terminal 3: SalesService (port dev 5003)
cd ev-dealer-management/SalesService; dotnet run

# Terminal 4: Frontend
cd ev-dealer-frontend; npm run dev
```

### Test (3 clicks):
1. Open: http://localhost:5173
2. Click: **Sales** → **Xem chi tiết** (any order)
3. Click: Green button **"Hoàn tất đơn hàng"**

*(luồng tạo đơn + complete thật đi qua `OrderCreateFromQuote.jsx` →
`POST /api/Orders/complete` — endpoint này vừa tạo order vừa hoàn tất, xem
OrdersController.cs)*

### Verify (2 checks):
✅ Order được tạo/hoàn tất, status cập nhật  
✅ NotificationService log: `Processing SaleCompletedEvent ...` + push FCM gửi
tới subject registry

---

## 🎨 Visual Guide

### Where to Find the Button:
```
┌─────────────────────────────────────────────────────┐
│ ORDER DETAIL PAGE                                   │
├───────────────────┬─────────────────────────────────┤
│                   │  ← RIGHT SIDEBAR                │
│  Order Info       │  ┌─────────────────────────┐   │
│  Customer Info    │  │  Tóm tắt đơn hàng       │   │
│  Vehicle Info     │  │  ...                    │   │
│  Payment Info     │  └─────────────────────────┘   │
│  Contracts        │                                 │
│                   │  ┌─────────────────────────┐   │
│                   │  │  Thông tin thanh toán   │   │
│                   │  │  ...                    │   │
│                   │  └─────────────────────────┘   │
│                   │                                 │
│                   │  ┌─────────────────────────┐   │
│                   │  │  Thao tác nhanh         │   │
│                   │  │  [In đơn hàng]         │   │
│                   │  │  [Hoàn tất đơn hàng]   │ ← HERE!
│                   │  └─────────────────────────┘   │
└───────────────────┴─────────────────────────────────┘
```

### Button States:
```
BEFORE CLICK:
┌────────────────────────┐
│  ✅ Hoàn tất đơn hàng  │  ← Green, clickable
└────────────────────────┘

DURING API CALL:
┌────────────────────────┐
│  ⏳ Đang xử lý...      │  ← Gray, disabled
└────────────────────────┘

AFTER SUCCESS:
┌────────────────────────┐
│  ✅ Đã hoàn tất        │  ← Gray, disabled
└────────────────────────┘

SUCCESS TOAST (frontend):
┌──────────────────────────────────────────────────┐
│  ✅ Đơn hàng hoàn tất thành công!               │
│     Mã đơn: ORD-...                              │
└──────────────────────────────────────────────────┘
(không còn dòng "Email xác nhận đã được gửi" —
luồng này chưa từng gửi email)
```

---

## 🔍 What Happens Behind the Scenes:

```
1. Button Click
   ↓
2. Frontend → SalesService API
   POST http://localhost:5003/api/Orders/complete
   ↓
3. SalesService → RabbitMQ
   Publish SaleCompletedEvent → queue "sales.completed"
   (RabbitMQMessagePublisher; log "Published message")
   ↓
4. RabbitMQ → NotificationService
   SaleCompletedConsumer nhận message
   ↓
5. NotificationService → Firebase FCM
   Resolve device tokens qua registry subject `user:<customerId>`
   → push "🎉 Đơn hàng đã hoàn tất!" (title thật trong consumer)
   ↓
6. Thiết bị người dùng nhận push
   (nếu customer chưa có token đăng ký → log warning, bỏ qua)
   ↓
7. Frontend ← SalesService
   Return OrderId
   ↓
8. Show Success Toast
   ✅ Done!
```

---

## 🚨 Troubleshooting (1 Minute)

### Problem: Button does nothing
**Fix**: Check SalesService is running
```powershell
netstat -ano | findstr :5003
# If nothing, start SalesService:
cd ev-dealer-management/SalesService; dotnet run
```

### Problem: Error toast appears
**Fix**: Check browser console (F12)
```
Look for red error messages
Common: "Failed to fetch" = Service not running
```

### Problem: No push delivered
**Fix**: Check NotificationService logs
```
Should see: "Processing SaleCompletedEvent" (SaleCompletedConsumer)
Nếu "no device tokens" → customer chưa đăng ký token
(mở frontend, login, cho phép notification)
Nếu credentials lỗi → firebase-credentials.json (FIREBASE_SETUP.md)
```

---

## 📋 Quick Checklist

Before testing:
- [ ] Docker running (for RabbitMQ — `docker compose up -d rabbitmq`)
- [ ] NotificationService terminal open (port 5051)
- [ ] SalesService terminal open (port 5003)
- [ ] Frontend dev server running

During test:
- [ ] Can navigate to Order Detail page
- [ ] Can see green "Hoàn tất đơn hàng" button
- [ ] Button changes to "Đang xử lý..." when clicked

After test:
- [ ] Success toast appears with OrderId
- [ ] Status badge shows "Hoàn thành" (green)
- [ ] Button shows "Đã hoàn tất" (disabled)
- [ ] SalesService logs: "Published message"
- [ ] NotificationService logs: "Processing SaleCompletedEvent" + push sent

---

## 🎓 Key Files

| File | Role |
|------|------|
| `OrderCreateFromQuote.jsx` | Gọi `POST /Orders/complete` (tạo + hoàn tất đơn) |
| `SalesService/Controllers/OrdersController.cs` | Endpoint complete + publish `sales.completed` |
| `NotificationService/Consumers/SaleCompletedConsumer.cs` | Consume → FCM push qua token registry |
| `NotificationService/Services/FirebaseFcmService.cs` | Gửi FCM |

---

## 🎉 Success Criteria

✅ All services started  
✅ Button clicked  
✅ Toast shows success  
✅ Order status updated  
✅ FCM push tới thiết bị có token đăng ký  

**If all ✅ → YOU'RE DONE! 🎊**

---

## 📚 Full Documentation

- `RABBITMQ_SETUP.md` (cùng thư mục) - cấu hình broker
- `NotificationService/README.md` - overview + endpoint push

---

**Need help? Check the logs in all 3 terminals!**
