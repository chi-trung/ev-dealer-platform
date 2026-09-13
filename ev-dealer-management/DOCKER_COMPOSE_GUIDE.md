# 🐳 Docker Compose Guide - RabbitMQ & Services

## 📋 Tổng Quan

File `docker-compose.yml` đã được cấu hình với **toàn bộ stack 7 services + RabbitMQ** (W5):
- ✅ **RabbitMQ** với persistent volumes và health checks
- ✅ **APIGatewayService** (Ocelot) — cổng host **5036**, route tới 6 services qua `Gateway__Rewrites` (xem `docs/GATEWAY.md`)
- ✅ **UserService** (5223), **VehicleService** (5224), **SalesService** (5003)
- ✅ **CustomerService** (5039), **ReportingService** (5208, `USE_SQLITE=true`), **NotificationService** (5051)
- ✅ Network isolation (`ev-dealer-network`) và service dependencies; `userservice` đã được nối lại vào network (thiếu trước W5)
- ⚠️ NotificationService chạy **không có** `firebase-credentials.json` (secret không ở trong repo): API boot bình thường, push trả 500 và event tiêu thụ rơi vào `*.dlq`. Provision key out-of-band rồi `docker cp` vào container (xem comment trong docker-compose.yml).

---

## 🚀 Cách Sử Dụng

### 1. Khởi Động Tất Cả Services

```powershell
# Từ thư mục ev-dealer-management
cd ev-dealer-management

# Build và start tất cả services
docker-compose up -d

# Xem logs của tất cả services
docker-compose logs -f

# Xem logs của một service cụ thể
docker-compose logs -f rabbitmq
docker-compose logs -f salesservice
```

### 2. Chỉ Khởi Động RabbitMQ

```powershell
# Start chỉ RabbitMQ
docker-compose up -d rabbitmq

# Kiểm tra status
docker-compose ps rabbitmq

# Xem logs
docker-compose logs -f rabbitmq
```

### 3. Khởi Động RabbitMQ + SalesService

```powershell
# Start RabbitMQ và SalesService (tự động start dependencies)
docker-compose up -d rabbitmq salesservice

# Hoặc start theo thứ tự
docker-compose up -d rabbitmq
docker-compose up -d salesservice
```

### 4. Dừng Services

```powershell
# Stop tất cả services (giữ data)
docker-compose stop

# Stop và remove containers (giữ data)
docker-compose down

# Stop và remove containers + volumes (XÓA TẤT CẢ DATA!)
docker-compose down -v
```

### 5. Rebuild Services

```powershell
# Rebuild và restart một service
docker-compose up -d --build salesservice

# Rebuild tất cả services
docker-compose up -d --build
```

---

## 🔍 Kiểm Tra Services

### RabbitMQ Management UI

**URL**: http://localhost:15672
- **Username**: `guest`
- **Password**: `guest`

**Tính năng**:
- Xem queues, exchanges, bindings
- Monitor messages
- Xem connections và channels
- Quản lý users và permissions

### Health Checks

```powershell
# RabbitMQ health
docker-compose exec rabbitmq rabbitmq-diagnostics ping

# SalesService health
curl http://localhost:5003/api/orders/health

# VehicleService health
curl http://localhost:5224/health
```

### Kiểm Tra Status

```powershell
# Xem status tất cả services
docker-compose ps

# Xem resource usage
docker stats

# Xem logs real-time
docker-compose logs -f
```

---

## 📦 Volumes & Data Persistence

### RabbitMQ Data

RabbitMQ data được lưu trong Docker volumes:
- `rabbitmq_data`: Lưu messages, queues, exchanges
- `rabbitmq_logs`: Lưu logs

**Lưu ý**: Data sẽ được giữ lại khi restart container, nhưng sẽ bị xóa nếu dùng `docker-compose down -v`

### Backup RabbitMQ Data

```powershell
# Backup volume
docker run --rm -v ev-dealer-management_rabbitmq_data:/data -v ${PWD}:/backup alpine tar czf /backup/rabbitmq-backup.tar.gz /data

# Restore volume
docker run --rm -v ev-dealer-management_rabbitmq_data:/data -v ${PWD}:/backup alpine tar xzf /backup/rabbitmq-backup.tar.gz -C /
```

---

## 🔧 Cấu Hình

### Thay Đổi RabbitMQ Credentials

Sửa trong `docker-compose.yml`:

```yaml
rabbitmq:
  environment:
    RABBITMQ_DEFAULT_USER: your_username
    RABBITMQ_DEFAULT_PASS: your_password
```

Sau đó cập nhật credentials trong các services khác.

### Thay Đổi Ports

```yaml
rabbitmq:
  ports:
    - "5673:5672"    # Thay đổi host port
    - "15673:15672"  # Thay đổi management UI port
```

### Thêm Environment Variables

Có thể tạo file `.env` để quản lý biến môi trường:

```env
RABBITMQ_USER=guest
RABBITMQ_PASS=guest
RABBITMQ_PORT=5672
```

Sau đó sử dụng trong `docker-compose.yml`:

```yaml
environment:
  RABBITMQ_DEFAULT_USER: ${RABBITMQ_USER}
  RABBITMQ_DEFAULT_PASS: ${RABBITMQ_PASS}
```

---

## 🐛 Troubleshooting

### RabbitMQ không start

```powershell
# Kiểm tra logs
docker-compose logs rabbitmq

# Kiểm tra port đã bị chiếm chưa
netstat -ano | findstr :5672
netstat -ano | findstr :15672

# Xóa container và tạo lại
docker-compose down
docker-compose up -d rabbitmq
```

### Services không kết nối được RabbitMQ

```powershell
# Kiểm tra network
docker network ls
docker network inspect ev-dealer-management_ev-dealer-network

# Kiểm tra RabbitMQ đang chạy
docker-compose ps rabbitmq

# Test connection từ service
docker-compose exec salesservice ping rabbitmq
```

### Xóa và Tạo Lại Tất Cả

```powershell
# Dừng và xóa tất cả
docker-compose down -v

# Xóa images
docker-compose down --rmi all

# Build lại từ đầu
docker-compose build --no-cache
docker-compose up -d
```

---

## 📊 Monitoring

### Xem Queues trong RabbitMQ

1. Mở http://localhost:15672
2. Đăng nhập với `guest/guest`
3. Vào tab **Queues**
4. Xem các queues:
   - `sales.completed`
   - `order.created`
   - `payment.received`
   - `order.status.changed`

### Xem Messages

1. Vào tab **Queues**
2. Click vào queue name
3. Xem messages trong queue
4. Có thể publish/consume messages thủ công

### Xem Connections

1. Vào tab **Connections**
2. Xem các services đang kết nối
3. Xem thông tin chi tiết về connections

---

## 🎯 Best Practices

1. **Luôn dùng health checks**: Services sẽ đợi RabbitMQ healthy trước khi start
2. **Backup volumes định kỳ**: Đặc biệt trong production
3. **Monitor logs**: Sử dụng `docker-compose logs -f` để theo dõi
4. **Không xóa volumes nhầm**: `docker-compose down -v` sẽ xóa tất cả data!
5. **Sử dụng networks**: Đảm bảo services giao tiếp qua network riêng

---

## 📝 Quick Reference

```powershell
# Start
docker-compose up -d

# Stop
docker-compose stop

# Restart
docker-compose restart

# Logs
docker-compose logs -f [service_name]

# Status
docker-compose ps

# Rebuild
docker-compose up -d --build [service_name]

# Clean up
docker-compose down -v
```

---

## 🔗 Liên Kết Hữu Ích

- **RabbitMQ Management UI**: http://localhost:15672
- **SalesService Swagger**: http://localhost:5003/swagger
- **SalesService Health**: http://localhost:5003/api/orders/health
- **VehicleService**: http://localhost:5224

---

**Lưu ý**: Đảm bảo Docker đang chạy trước khi sử dụng docker-compose!

