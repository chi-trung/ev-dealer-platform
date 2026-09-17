# 📝 Tóm Tắt - Sử dụng Postman để Thêm Dữ liệu

## ⚡ Quick Start (2 phút)

### Bước 1: Khởi động API

```bash
cd ReportingService
dotnet run
```

> Nếu bạn không có PostgreSQL chạy cục bộ (hoặc migrations báo lỗi kết nối), bạn có thể chạy app trên SQLite để test nhanh — SQLite là provider mặc định, chỉ cần `dotnet run`:

```powershell
# SQLite là mặc định (DB_PROVIDER=sqlite) — không cần set gì thêm
dotnet run
```

### Bước 2: Mở Postman

- Tải từ: https://www.postman.com/downloads/
- Cài đặt và mở

### Bước 3: Tạo Request

```
Method: POST
URL: http://localhost:5208/api/reports/sales-summary
```

### Bước 4: Thêm Header

```
Key: Content-Type
Value: application/json
```

### Bước 5: Thêm Body (JSON)

```json
{
  "date": "2025-01-15T00:00:00Z",
  "dealerId": "550e8400-e29b-41d4-a716-446655440000",
  "dealerName": "Dealer Hà Nội",
  "region": "Miền Bắc",
  "salespersonId": "550e8400-e29b-41d4-a716-446655440002",
  "salespersonName": "Nguyễn Văn A",
  "totalOrders": 5,
  "totalRevenue": 1500000000
}
```

### Bước 6: Click "Send"

- Xem response trả về
- Status 201 = thành công!

---

## 📖 Chi tiết Hướng dẫn

Xem file `POSTMAN_GUIDE.md` để biết:

- Hướng dẫn đầy đủ từng bước
- Cách lấy dữ liệu (GET)
- Cách lọc dữ liệu
- Cách xử lý SSL errors
- Tips & Tricks
- Troubleshooting

---

## 🎯 Các URL Thường Dùng

### Thêm Doanh số

```
POST http://localhost:5208/api/reports/sales-summary
```

### Lấy Tất cả Doanh số

```
GET http://localhost:5208/api/reports/sales-summary
```

### Lấy Chi tiết Doanh số

```
GET http://localhost:5208/api/reports/sales-summary/{id}
```

### Thêm Tồn kho

```
POST http://localhost:5208/api/reports/inventory-summary
```

### Lấy Tất cả Tồn kho

```
GET http://localhost:5208/api/reports/inventory-summary
```

---

## 💾 Lưu Collection

1. Right-click collection
2. Click "Export"
3. Chọn "Collection v2.1"
4. Lưu file
5. Chia sẻ với team!

---

**🎉 Bạn đã sẵn sàng sử dụng Postman!**
