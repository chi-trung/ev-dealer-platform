# 🚀 EV DEALER MANAGEMENT - DEPLOYMENT GUIDE

## 📋 Table of Contents
1. [System Requirements](#system-requirements)
2. [Services Overview](#services-overview)
3. [Quick Start (Development)](#quick-start-development)
4. [Production Deployment](#production-deployment)
5. [Service Configuration](#service-configuration)
6. [Troubleshooting](#troubleshooting)
7. [Health Checks](#health-checks)

---

## 🖥️ System Requirements

### Development Environment:
- **OS**: Windows 10/11, Linux, macOS
- **.NET SDK**: 8.0 or later
- **Node.js**: 18.x or later
- **Docker**: Latest stable version
- **RAM**: Minimum 8GB (16GB recommended)
- **Disk Space**: 10GB free space

### Production Environment:
- **Container Runtime**: Docker with Docker Compose
- **RAM**: 16GB minimum
- **CPU**: 4 cores minimum
- **Storage**: 50GB+ SSD

---

## 🏗️ Services Overview

### Backend Services:

| Service | Port (dev) | Port (Docker host) | Description | Dependencies |
|---------|------------|--------------------|-------------|--------------|
| **APIGatewayService** | 5036 | 5036 | Ocelot API Gateway | All services |
| **UserService** | 7001 | 5223 | Authentication & Users; sends email via MailKit SMTP | SQLite |
| **CustomerService** | 5039 | 5039 | Customer Management | SQLite, RabbitMQ |
| **VehicleService** | 5068 | 5224 | Vehicle Management | SQLite, RabbitMQ (producer) |
| **SalesService** | 5003 | 5003 | Sales & Orders | SQLite, RabbitMQ (producer) |
| **NotificationService** | 5051 | 5051 | Push Notifications via Firebase Cloud Messaging | SQLite, RabbitMQ, Firebase |
| **ReportingService** | 5208 | 5208 | Reports & Analytics (HTTP fan-out, no broker) | SQLite |

> **DealerManagementService** is listed in the README as *planned* only — there is
> no such project in `DealerSystem.sln` and no source directory; it is not deployed.

### Infrastructure:

| Component | Port | Credentials |
|-----------|------|-------------|
| **RabbitMQ** | 5672 (AMQP), 15672 (UI) | guest/guest |
| **SQLite** | – | per-service `*.db` files, volume-mounted under each service's `data/` dir |
| **Frontend** | 5173 (Vite dev) | - |

---

## ⚡ Quick Start (Development)

### Option 1: Automated Script (Recommended)

```powershell
# Navigate to the backend solution directory (contains the scripts below)
cd ev-dealer-management

# Start all services
.\start-all-services.ps1

# Wait for services to start (15-20 seconds)

# Run comprehensive tests
.\test-all-flows.ps1
```

### Option 2: Manual Start

#### 1. Start RabbitMQ
```bash
docker run -d --name rabbitmq \
  -p 5672:5672 \
  -p 15672:15672 \
  rabbitmq:3-management
```

#### 2. Start Backend Services (Open 3 separate terminals)

**Terminal 1 - NotificationService:**
```powershell
cd ev-dealer-management\NotificationService
dotnet run
```

**Terminal 2 - SalesService:**
```powershell
cd ev-dealer-management\SalesService
dotnet run
```

**Terminal 3 - VehicleService:**
```powershell
cd ev-dealer-management\VehicleService
dotnet run
```

#### 3. Start Frontend
```powershell
cd ev-dealer-frontend
npm install
npm run dev
```

#### 4. Verify Services
- RabbitMQ UI: http://localhost:15672 (guest/guest)
- NotificationService: http://localhost:5051/health
- SalesService: http://localhost:5003/api/orders/health
- VehicleService: http://localhost:5068/health
- Frontend: http://localhost:5173

---

## 🐳 Production Deployment

### Using Docker Compose

#### 1. Create Production docker-compose.yml

A working compose file already exists at `ev-dealer-management/docker-compose.yml`
(RabbitMQ + all six backend services + gateway). The production copy below follows
its service names and env-var spellings — note `RabbitMQ__HostName`, not `RabbitMQ__Host`.

```yaml
version: '3.8'

services:
  rabbitmq:
    image: rabbitmq:3-management
    container_name: ev-dealer-rabbitmq
    ports:
      - "5672:5672"
      - "15672:15672"
    environment:
      RABBITMQ_DEFAULT_USER: admin
      RABBITMQ_DEFAULT_PASS: ${RABBITMQ_PASSWORD}
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq
    networks:
      - ev-dealer-network
    restart: unless-stopped

  notification-service:
    build:
      context: ./NotificationService
      dockerfile: Dockerfile
    container_name: ev-dealer-notification
    ports:
      - "5051:80"
    volumes:
      - ./NotificationService/Logs:/app/Logs
      - ./NotificationService/data:/app/data
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:80
      - ConnectionStrings__DefaultConnection=Data Source=/app/data/notifications.db
      - Jwt__Key=${JWT_KEY}
      - Jwt__Issuer=evm.local
      - Jwt__Audience=evm.local
      - RabbitMQ__HostName=rabbitmq
      - RabbitMQ__Port=5672
      - RabbitMQ__UserName=${RABBITMQ_USER}
      - RabbitMQ__Password=${RABBITMQ_PASSWORD}
      - Firebase__CredentialPath=/app/firebase-credentials.json
    depends_on:
      - rabbitmq
    networks:
      - ev-dealer-network
    restart: unless-stopped

  sales-service:
    build:
      context: ./SalesService
      dockerfile: Dockerfile
    container_name: ev-dealer-sales
    ports:
      - "5003:80"
    volumes:
      - ./SalesService/data:/app/data
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:80
      - ConnectionStrings__DefaultConnection=Data Source=/app/data/sales.db
      - RabbitMQ__HostName=rabbitmq
      - RabbitMQ__Port=5672
      - RabbitMQ__UserName=${RABBITMQ_USER}
      - RabbitMQ__Password=${RABBITMQ_PASSWORD}
    depends_on:
      - rabbitmq
    networks:
      - ev-dealer-network
    restart: unless-stopped

  vehicle-service:
    build:
      context: ./VehicleService
      dockerfile: Dockerfile
    container_name: ev-dealer-vehicle
    ports:
      - "5224:8080"
    volumes:
      - ./VehicleService/data:/app/data
      - ./VehicleService/wwwroot/images:/app/wwwroot/images
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:8080
      - ConnectionStrings__DefaultConnection=Data Source=/app/data/vehicles.db
      - RabbitMQ__HostName=rabbitmq
      - RabbitMQ__Port=5672
      - RabbitMQ__UserName=${RABBITMQ_USER}
      - RabbitMQ__Password=${RABBITMQ_PASSWORD}
    depends_on:
      - rabbitmq
    networks:
      - ev-dealer-network
    restart: unless-stopped

  api-gateway:
    build:
      context: ./APIGatewayService
      dockerfile: Dockerfile
    container_name: ev-dealer-gateway
    ports:
      - "5036:80"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
    depends_on:
      - notification-service
      - sales-service
      - vehicle-service
    networks:
      - ev-dealer-network
    restart: unless-stopped

  # No frontend service: ev-dealer-frontend ships no Dockerfile (a `build:` here
  # would fail). Run `npm install && npm run dev` for Vite on http://localhost:5173,
  # or `npm run build` and serve dist/ with any static host; point the browser app at
  # the gateway via VITE_API_BASE_URL=http://localhost:5036/api

volumes:
  rabbitmq_data:

networks:
  ev-dealer-network:
    driver: bridge
```

#### 2. Create .env file

```env
# RabbitMQ (must match RABBITMQ_DEFAULT_USER / RABBITMQ_DEFAULT_PASS in the rabbitmq service)
RABBITMQ_USER=your_user
RABBITMQ_PASSWORD=your_secure_password_here

# JWT signing key (must be byte-identical across UserService, CustomerService and
# NotificationService, or every token they validate is rejected)
JWT_KEY=replace-with-a-real-secret

# Databases: SQLite files under each service's ./data volume (see the compose
# volumes above) — no DB connection-string secrets to put here.
# Firebase: the service-account JSON is provisioned out-of-band and is NOT in the
# repo; the compose deliberately does not bind-mount it (see docker-compose.yml
# notificationservice comment) — use docker cp + restart, or add the mount yourself.
```


#### 3. Deploy

```bash
# Build and start all services
docker-compose up -d

# View logs
docker-compose logs -f

# Stop all services
docker-compose down

# Stop and remove volumes
docker-compose down -v
```

---

## ⚙️ Service Configuration

### NotificationService

**appsettings.json** (abridged — the real file also lists all 14 consumed `RabbitMQ:Queues`):
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=notifications.db"
  },
  "Jwt": {
    "Key": "ReplaceThisWithASecretKeyForDevelopment",
    "Issuer": "evm.local",
    "Audience": "evm.local"
  },
  "RabbitMQ": {
    "HostName": "localhost",
    "Port": 5672,
    "UserName": "guest",
    "Password": "guest",
    "MaxDeliveryAttempts": 3,
    "RetryTtlMilliseconds": 5000
  },
  "Firebase": {
    "CredentialPath": "firebase-credentials.json",
    "ProjectId": "ev-dealer-management-6c620"
  }
}
```

**Environment Variables:**
- `RabbitMQ__HostName` / `RabbitMQ__Port` / `RabbitMQ__UserName` / `RabbitMQ__Password`: broker connection (the key is `HostName`, not `Host` — `RabbitMQ__Host` is silently ignored)
- `Firebase__CredentialPath`: service-account JSON for Firebase Cloud Messaging (the actual delivery transport)
- `Jwt__Key` / `Jwt__Issuer` / `Jwt__Audience`: HS256 validation of UserService tokens on the DeviceTokens API (Issue #36)
- `ConnectionStrings__DefaultConnection`: SQLite file backing the device-token registry (Issue #33)

### SalesService

**appsettings.json:**
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=sales.db"
  },
  "RabbitMQ": {
    "HostName": "localhost",
    "Port": 5672,
    "UserName": "guest",
    "Password": "guest"
  }
}
```

### VehicleService

**appsettings.json:**
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=vehicles.db"
  },
  "RabbitMQ": {
    "HostName": "localhost",
    "Port": 5672,
    "UserName": "guest",
    "Password": "guest"
  }
}
```

---

## 🔧 Troubleshooting

### Issue: RabbitMQ Connection Failed

**Symptoms:**
```
Failed to connect to RabbitMQ at localhost:5672
```

**Solutions:**
1. Check RabbitMQ is running:
   ```bash
   docker ps | grep rabbitmq
   ```

2. Restart RabbitMQ (tên container trong compose là `evm_rabbitmq`):
   ```bash
   docker restart evm_rabbitmq
   ```

3. Check port not blocked:
   ```powershell
   netstat -ano | findstr :5672
   ```

### Issue: Service Port Already in Use

**Symptoms:**
```
Failed to bind to address http://127.0.0.1:5003: address already in use
```

**Solution:**
```powershell
# Find process using port
netstat -ano | findstr :5003

# Kill process (replace PID)
taskkill /F /PID <PID>
```

### Issue: CORS Error in Frontend

**Symptoms:**
```
Access to fetch at 'http://localhost:5003/api/orders/complete' has been blocked by CORS policy
```

**Solution:**
Ensure service has CORS configured:
```csharp
// In Program.cs
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

app.UseCors("AllowFrontend");
```

### Issue: Email Not Sending

Email is sent by **UserService** through MailKit SMTP — there is no SendGrid integration.

**Check:**
1. `EmailSettings__SmtpHost` / `EmailSettings__SmtpPort` / `EmailSettings__SmtpUser` / `EmailSettings__SmtpPassword` are set (the dev compose points at `smtp.gmail.com:587`)
2. The SMTP account accepts the login — for Gmail this requires an app password, not the account password
3. Check UserService logs for SMTP exceptions

### Issue: Push Notification Not Delivered

Push is sent by **NotificationService** via Firebase Cloud Messaging (`FirebaseAdmin`) — there is no Twilio/SMS transport.

**Check:**
1. The Firebase service-account JSON exists at `Firebase__CredentialPath` (in Docker: `docker cp firebase-credentials.json evm_notificationservice:/app/firebase-credentials.json`, then `docker compose restart notificationservice`)
2. The recipient registered a device token via the DeviceTokens API (requires a valid UserService JWT)
3. Without the credential file the service still boots; the first push fails lazily and consumed events cycle retry → DLQ

### Issue: SMS Preference Flag

`smsNotifications` is stored per user as a preference flag only; no SMS is actually sent anywhere in the current codebase. Real-time delivery is push via FCM (section above).

---

## ✅ Health Checks

### Manual Health Checks

```powershell
# NotificationService
curl http://localhost:5051/health

# SalesService
curl http://localhost:5003/api/orders/health

# VehicleService
curl http://localhost:5068/health

# UserService / CustomerService / ReportingService
curl http://localhost:7001/health
curl http://localhost:5039/health
curl http://localhost:5208/health

# RabbitMQ
curl http://localhost:15672 -u guest:guest

# API Gateway (aggregate /health — pings every service's /health in parallel)
curl http://localhost:5036/health
```

### Automated Health Check Script

```powershell
# Save as check-health.ps1
$services = @(
    @{ Name="RabbitMQ UI"; Url="http://localhost:15672" },
    @{ Name="NotificationService"; Url="http://localhost:5051/health" },
    @{ Name="SalesService"; Url="http://localhost:5003/api/orders/health" },
    @{ Name="VehicleService"; Url="http://localhost:5068/health" },
    @{ Name="UserService"; Url="http://localhost:7001/health" },
    @{ Name="CustomerService"; Url="http://localhost:5039/health" },
    @{ Name="ReportingService"; Url="http://localhost:5208/health" },
    @{ Name="APIGatewayService"; Url="http://localhost:5036/health" }
)

foreach ($service in $services) {
    Write-Host "$($service.Name): " -NoNewline
    try {
        $response = Invoke-RestMethod -Uri $service.Url -TimeoutSec 3
        Write-Host "✅ OK" -ForegroundColor Green
    } catch {
        Write-Host "❌ DOWN" -ForegroundColor Red
    }
}
```

---

## 📊 Monitoring

### RabbitMQ Queue Monitoring

1. Open RabbitMQ Management UI: http://localhost:15672
2. Login: `guest` / `guest`
3. Check queues:
   - `sales.completed` - Order completion emails
   - `vehicle.reserved` - Vehicle reservation SMS
   - `testdrive.scheduled` - Test drive confirmations

### Service Logs

**View logs in terminal:**
- Each service outputs logs to console in development
- Look for:
  - `[INF]` - Information
  - `[WRN]` - Warnings
  - `[ERR]` - Errors

**Production logging:**
- Configure Serilog to write to files
- Use log aggregation (e.g., ELK Stack, Seq)

---

## 🔐 Security Considerations

### Production Checklist:

- [ ] Change RabbitMQ default credentials
- [ ] Use HTTPS for all services
- [ ] Store secrets in environment variables or Azure Key Vault
- [ ] Enable authentication on API Gateway
- [ ] Configure firewall rules
- [ ] Set up SSL certificates
- [ ] Disable Swagger UI in production
- [ ] Enable rate limiting
- [ ] Configure CORS for production domains only
- [ ] Regular security updates

---

## 📚 Additional Resources

- **RabbitMQ Documentation**: https://www.rabbitmq.com/documentation.html
- **Firebase Cloud Messaging Docs**: https://firebase.google.com/docs/cloud-messaging
- **Ocelot Gateway**: https://ocelot.readthedocs.io/
- **.NET 8 Docs**: https://learn.microsoft.com/en-us/dotnet/

---

## 🆘 Support

For issues or questions:
1. Check logs in service terminals
2. Verify all services are running
3. Check RabbitMQ queue status
4. Review this troubleshooting guide
5. Check service-specific README files

---

**Last Updated**: November 22, 2025  
**Version**: 1.0.0
