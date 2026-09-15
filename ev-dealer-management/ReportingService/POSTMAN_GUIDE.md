# Sử dụng Postman với ReportingService API

## 📥 Cài đặt Postman

1. **Tải Postman** từ: https://www.postman.com/downloads/
2. **Cài đặt** và **mở ứng dụng**
3. **Đăng ký tài khoản** (hoặc bỏ qua)

---

## 🎯 Sơ đồ Giao diện Postman

```
┌─────────────────────────────────────────────────────────────┐
│                        POSTMAN                              │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  Sidebar (trái)              │  Main Area (giữa)            │
│  ├─ Collections              │  ┌───────────────────────┐   │
│  │  └─ ReportingService      │  │ POST / GET dropdown   │   │
│  │     ├─ Create Sales       │  ├───────────────────────┤   │
│  │     ├─ Get Sales          │  │ URL: https://...      │   │
│  │     └─ Create Inventory   │  ├───────────────────────┤   │
│  ├─ History                  │  │ Tabs: Params Headers  │   │
│  │                           │  │       Body Auth       │   │
│  └─ Environments             │  ├───────────────────────┤   │
│                              │  │  [Send]  [Save] ...   │   │
│                              │  └───────────────────────┘   │
│                              │                              │
│                              │  Response Area (dưới)        │
│                              │  ┌───────────────────────┐   │
│                              │  │ Status: 201 Created   │   │
│                              │  │   "success": true,    │   │
│                              │  │   "data": { ... }     │   │
│                              │  │ }                     │   │
│                              │  └───────────────────────┘   │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

---

## 🚀 Tạo Request để Thêm Dữ liệu

### Bước 1: Tạo Collection Mới (Tùy chọn)

- Click **"Collections"** ở sidebar trái
- Click **"+"** → **"Create collection"**
- Đặt tên: `ReportingService`
- Click **"Create"**

### Bước 2: Tạo Request POST

#### **Cách A: Tạo request trong collection**

1. Hover vào collection `ReportingService`
2. Click **"..."** → **"Add request"**
3. Đặt tên: `Create Sales Summary`
4. Press Enter

#### **Cách B: Tạo request từ menu**

1. Click **"+"** ở tab để tạo request mới
2. Chọn **"POST"** từ dropdown (mặc định là GET)

---

## 📝 Cấu hình Request - Thêm Doanh số

### 1. URL

```
POST http://localhost:5208/api/reports/sales-summary
```

**Lưu ý:**

- Protocol: `http` (không phải `https`)
- Port: `5208` (không phải 5214)
- Method: `POST`

### 2. Headers (Tab "Headers")

Click vào tab **"Headers"** và thêm:

| Key            | Value              |
| -------------- | ------------------ |
| `Content-Type` | `application/json` |

_(Postman thường tự thêm nếu bạn dùng Body)_

### 3. Body (Tab "Body")

1. Click tab **"Body"**
2. Chọn **"raw"**
3. Chọn **"JSON"** từ dropdown bên phải
4. Paste dữ liệu này:

```json
{
  "date": "2025-01-15T00:00:00Z",
  "dealerId": 1,
  "dealerName": "Dealer Hà Nội",
  "region": "Miền Bắc",
  "salespersonId": 2,
  "salespersonName": "Nguyễn Văn A",
  "totalOrders": 5,
  "totalRevenue": 1500000000
}
```

*(các field `required` trong model `SalesSummary.cs`: dealerName, region,
salespersonName — thiếu là 400)*

### 4. Gửi Request

Click nút **"Send"** (màu xanh) góc trên phải

### 5. Kiểm tra Response

Response sẽ hiển thị ở dưới:

```json
{
  "success": true,
  "data": {
    "id": "550e8400-e29b-41d4-a716-446655440001",
    "date": "2025-01-15T00:00:00Z",
    "dealerId": 1,
    "dealerName": "Dealer Hà Nội",
    "region": "Miền Bắc",
    "salespersonId": 2,
    "salespersonName": "Nguyễn Văn A",
    "totalOrders": 5,
    "totalRevenue": 1500000000,
    "lastUpdatedAt": "2025-01-15T10:30:00Z"
  }
}
```

---

## 📦 Thêm Dữ liệu Tồn kho

Tạo request mới:

**URL:**

```
POST http://localhost:5208/api/reports/inventory-summary
```

**Body:**

```json
{
  "vehicleId": 3,
  "vehicleName": "Tesla Model 3",
  "dealerId": 1,
  "dealerName": "Dealer Hà Nội",
  "region": "Miền Bắc",
  "stockCount": 15
}
```

---

## 🔍 Lấy Dữ liệu (GET Request)

### Lấy tất cả doanh số

**Method:** GET
**URL:**

```
http://localhost:5208/api/reports/sales-summary
```

Click **"Send"** → Xem kết quả

### Lấy doanh số với filter

**URL:**

```
http://localhost:5208/api/reports/sales-summary?fromDate=2025-01-01&toDate=2025-01-31&dealerId=1
```

### Lấy chi tiết một doanh số

**URL:**

```
http://localhost:5208/api/reports/sales-summary/550e8400-e29b-41d4-a716-446655440001
```

---

## 🛠️ SSL Certificate Error - Giải Quyết

Nếu gặp lỗi SSL, làm theo:

1. Click **"Settings"** (mũi tên bên trái "Send")
2. Tìm mục **"SSL certificate verification"**
3. **Tắt** switch (Tạm thời chỉ dùng local dev!)

---

## 💾 Lưu Request

Postman tự động lưu request. Để sử dụng lại:

1. Click vào request trong **"Collections"** bên trái
2. Sửa đổi nếu cần
3. Click **"Send"** lại

---

## 📤 Export Collection (Chia sẻ với team)

1. Right-click collection **"ReportingService"**
2. Click **"Export"**
3. Chọn format **"Collection v2.1"**
4. Lưu file
5. Chia sẻ với team, họ có thể import bằng **"Import"**

---

## 📊 Ví dụ Đầy đủ - Workflow

### Bước 1: Tạo 3 doanh số

```json
{
  "date": "2025-01-15T00:00:00Z",
  "dealerId": 1,
  "dealerName": "Dealer Hà Nội",
  "region": "Miền Bắc",
  "salespersonId": 2,
  "salespersonName": "Nguyễn Văn A",
  "totalOrders": 5,
  "totalRevenue": 1500000000
}
```

```json
{
  "date": "2025-01-16T00:00:00Z",
  "dealerId": 1,
  "dealerName": "Dealer Hà Nội",
  "region": "Miền Bắc",
  "salespersonId": 2,
  "salespersonName": "Nguyễn Văn A",
  "totalOrders": 3,
  "totalRevenue": 900000000
}
```

```json
{
  "date": "2025-01-17T00:00:00Z",
  "dealerId": 1,
  "dealerName": "Dealer Hà Nội",
  "region": "Miền Bắc",
  "salespersonId": 2,
  "salespersonName": "Nguyễn Văn A",
  "totalOrders": 7,
  "totalRevenue": 2100000000
}
```

### Bước 2: Lấy lại tất cả

**URL:** `http://localhost:5208/api/reports/sales-summary`

**Kết quả:** Thấy 3 bản ghi vừa thêm

### Bước 3: Lọc theo ngày

**URL:** `http://localhost:5208/api/reports/sales-summary?fromDate=2025-01-15&toDate=2025-01-16`

**Kết quả:** Thấy 2 bản ghi (15 và 16)

---

## 🎯 Tips & Tricks

| Mẹo                       | Hành động                                  |
| ------------------------- | ------------------------------------------ |
| **Tái sử dụng dữ liệu**   | Sao chép request → Sửa một vài giá trị     |
| **Kiểm tra response**     | Click tab **"Body"** để xem formatted JSON |
| **Lưu workspace**         | Postman tự lưu tất cả collections          |
| **Pre-request Scripts**   | Có thể tạo test automation (nâng cao)      |
| **Environment Variables** | Cấu hình URL/auth một lần dùng nhiều lần   |

---

## 🆘 Troubleshooting

| Vấn đề               | Giải pháp                                  |
| -------------------- | ------------------------------------------ |
| **"Cannot GET"**     | Kiểm tra URL, ensure ReportingService chạy |
| **SSL error**        | Tắt SSL verification trong Settings        |
| **400 Bad Request**  | Kiểm tra JSON format, có lỗi syntax không? |
| **401 Unauthorized** | API không cần authentication lúc này       |
| **500 Server Error** | Xem console ReportingService để debug      |

---

## ✅ Checklist

- [ ] Cài đặt Postman
- [ ] Tạo Collection "ReportingService"
- [ ] Tạo POST request để thêm doanh số
- [ ] Thêm dữ liệu thành công (Status 201)
- [ ] Tạo GET request để lấy dữ liệu
- [ ] Kiểm tra dữ liệu vừa thêm
- [ ] Tạo POST request để thêm tồn kho
- [ ] Export collection để chia sẻ

---

**Bạn đã sẵn sàng sử dụng Postman!** 🚀

**Khuyên dùng:** Bắt đầu với 1 POST request → Test → Tạo thêm.
