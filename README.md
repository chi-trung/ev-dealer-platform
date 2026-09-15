# (WEB) EV DEALER MANAGEMENT SYSTEM

A comprehensive microservices-based dealer management system for electric vehicles, featuring real-time push notifications delivered through Firebase Cloud Messaging.

## Table of Contents
- [Overview](#overview)
- [Architecture](#architecture)
- [Features](#features)
- [Quick Start](#quick-start)
- [Documentation](#documentation)
- [Tech Stack](#tech-stack)

---

## Overview

EV Dealer Management System is a full-stack application designed to manage electric vehicle dealerships with automated customer notifications. The system uses event-driven architecture with RabbitMQ for reliable message delivery.

### Key Capabilities:
- **Vehicle Reservation with Push Notification**
- **Order Completion with Push Confirmation**
- **Real-time Event Processing**
- **Dealer Analytics & Reporting**
- **Customer Management**
- **Role-based Access Control**

---

## Architecture

### Microservices Architecture
```
┌─────────────────────────────────────────────────────────────┐
│                        Frontend (React)                     │
│                    http://localhost:5173                    │
└────────────────────────┬────────────────────────────────────┘
                         │
                         ↓
┌────────────────────────────────────────────────────────────┐
│              API Gateway (Ocelot)                          │
│              http://localhost:5036                         │
└───┬────────────┬────────────┬─────────────┬────────────────┘
    │            │            │             │
    ↓            ↓            ↓             ↓
┌─────────┐ ┌──────────┐ ┌─────────┐ ┌────────────────┐
│ User    │ │ Vehicle  │ │ Sales   │ │ Customer       │
│ Service │ │ Service  │ │ Service │ │ Service        │
│ :7001   │ │ :5068    │ │ :5003   │ │ :5039          │
└────┬────┘ └────┬─────┘ └────┬────┘ └───┬────────────┘
     │           │            │           │
     │           └────────────┴───────────┴─────┐
     │                                           │
     ↓                                           ↓
┌──────────┐                              ┌──────────────┐
│ SQLite   │                              │ RabbitMQ     │
│ per-svc  │                              │ :5672, 15672 │
└──────────┘                              └──────┬───────┘
                                                 │
                                                 ↓
                                        ┌──────────────────┐
                                        │ Notification     │
                                        │ Service :5051    │
                                        └────────┬─────────┘
                                                 ↓
                                        ┌──────────────────┐
                                        │ Firebase Cloud   │
                                        │ Messaging (Push) │
                                        └──────────────────┘
```

### Event-Driven Communication
```
Vehicle Reserved Event:
User → VehicleService → RabbitMQ → NotificationService → Push (Firebase FCM)

Order Completed Event:
User → SalesService → RabbitMQ → NotificationService → Push (Firebase FCM)
```

---

## Features

###  Implemented:

#### 1. Vehicle Reservation with Push Notification
- Reserve vehicles through web interface
- vehicle.reserved event published to RabbitMQ
- NotificationService consumes it and delivers an FCM push
- Customer information capture

#### 2. Order Completion with Confirmation
- Complete orders from Order Detail page
- sales.completed event triggers a push notification with order details
- Order number generation (ORD-YYYYMMDDHHMMSS-XXXX format)

#### 3. Notification Infrastructure
- Push delivery via Firebase Cloud Messaging (FirebaseAdmin)
- RabbitMQ message queuing (14 event queues)
- Consumer retry with DLQ (max 3 delivery attempts)
- Per-user device-token registry and notification preferences

#### 4. API Gateway
- Centralized routing with Ocelot
- Service discovery
- Load balancing ready

### In Progress:
- Dealer analytics dashboard

---

## Quick Start

### Prerequisites:
- .NET 8.0 SDK
- Node.js 18+
- Docker (for RabbitMQ)
- Git

### Option 1: Automated Setup (Recommended)

```powershell
# 1. Clone repository
git clone https://github.com/DangTDuy/ev-dealer-management.git
cd ev-dealer-management

# 2. Start all services
cd ev-dealer-management
.\start-all-services.ps1

# Wait 15-20 seconds for services to initialize

# 3. Run tests
.\test-all-flows.ps1

# 4. Open frontend
http://localhost:5173
```

### Option 2: Manual Setup

```powershell
# 1. Start RabbitMQ
docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:3-management

# 2. Start Backend Services (3 separate terminals)
cd ev-dealer-management\NotificationService
dotnet run

cd ev-dealer-management\SalesService
dotnet run

cd ev-dealer-management\VehicleService
dotnet run

# 3. Start Frontend
cd ev-dealer-frontend
npm install
npm run dev
```

### Verify Installation:
```powershell
# Script nằm trong ev-dealer-management/ — chạy từ đó.
# ⚠️ check-health.ps1 còn probe route/port cũ (NotificationService
# /notifications/health — route thật là /health; VehicleService :5002 —
# port dev thật 5068) nên vài dòng có thể báo ❌ giả —
# đối chiếu thủ công theo list bên dưới cho tới khi script được sửa (#53).
cd ev-dealer-management
.\check-health.ps1
```

Expected output (khi services chạy qua compose; phần RabbitMQ/Notification/
Sales đúng, dòng VehicleService chỉ đúng khi override port — xem caveat):
```
✅ RabbitMQ Running
✅ NotificationService Healthy
✅ SalesService Healthy
✅ VehicleService Healthy
✅ Frontend Running
```

---

##  Documentation

| Document | Description |
|----------|-------------|
| [DEPLOYMENT_GUIDE.md](./DEPLOYMENT_GUIDE.md) | Production deployment instructions |
| [TESTING_GUIDE.md](./TESTING_GUIDE.md) | Comprehensive testing guide |
| [SalesService/START_HERE.md](./ev-dealer-management/SalesService/START_HERE.md) | SalesService setup and API testing |
| [docs/EVENTS.md](./ev-dealer-management/docs/EVENTS.md) | Event topology across services |
| [NotificationService/README.md](./ev-dealer-management/NotificationService/README.md) | Notification service setup |

---

## Tech Stack

### Backend:
- **Framework**: .NET 8.0
- **API Gateway**: Ocelot
- **Message Broker**: RabbitMQ
- **Database**: SQLite (EF Core)
- **Push Notifications**: Firebase Cloud Messaging (FirebaseAdmin)
- **Email**: SMTP via MailKit (UserService)

### Frontend:
- **Framework**: React 18
- **UI Library**: Material-UI v5
- **Build Tool**: Vite
- **State Management**: React Hooks
- **HTTP Client**: Axios

### DevOps:
- **Containerization**: Docker
- **Orchestration**: Docker Compose
- **CI/CD**: GitHub Actions (.github/workflows/ci.yml — backend build + unit tests, frontend build + LFS check)

---

##  Services Overview

| Service | Port | Status | Description |
|---------|------|--------|-------------|
| **APIGatewayService** | 5036 |  Active | Central API gateway with Ocelot routing |
| **UserService** | 7001 |  Ready | Authentication, authorization, user management |
| **CustomerService** | 5039 |  Ready | Customer CRUD, test drives, complaints |
| **VehicleService** | 5068 |  Complete | Vehicle management, reservations, vehicle.reserved events |
| **SalesService** | 5003 |  Complete | Order management, RabbitMQ order/sales events |
| **NotificationService** | 5051 |  Complete | Push notifications via Firebase FCM |
| **DealerManagementService** | TBD |  Planned | Dealer management and analytics |
| **ReportingService** | 5208 |  Ready | Sales analytics and reporting |

*Ports are the development (launchSettings/ocelot.json) ports; Docker Compose maps different host ports (e.g. VehicleService 5224, UserService 5223) — see `ev-dealer-management/docker-compose.yml`.*

---

##  Testing

### Run All Tests:
```powershell
.\test-all-flows.ps1
```

### Test Individual Flows:

**Vehicle Reservation (Push):**
```powershell
$body = @{
    customerName = "Test User"
    customerEmail = "test@example.com"
    customerPhone = "+84912345678"
    quantity = 1
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:5068/api/vehicles/1/reserve" `
    -Method Post -Body $body -ContentType "application/json"
```

**Order Completion (Push):**
```powershell
$body = @{
    customerName = "Test Customer"
    customerEmail = "test@example.com"
    vehicleModel = "VinFast VF8"
    totalAmount = 1500000000
    paymentMethod = "Full Payment"
    quantity = 1
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:5003/api/orders/complete" `
    -Method Post -Body $body -ContentType "application/json"
```

---

##  Configuration

### Environment Variables:

**NotificationService:**
```env
ConnectionStrings__DefaultConnection=Data Source=notifications.db
Firebase__CredentialPath=firebase-credentials.json
Firebase__ProjectId=ev-dealer-management-6c620
```

**All Services:**
```env
RABBITMQ_HOST=localhost
RABBITMQ_PORT=5672
```

### Configuration Files:
- `appsettings.json` - Service-specific settings
- `ocelot.json` - API Gateway routing
- `.env` - Environment variables (production)

---

## 📈 Monitoring

### RabbitMQ Management UI:
```
URL: http://localhost:15672
Username: guest
Password: guest
```

### Health Endpoints:
- NotificationService: http://localhost:5051/health
- SalesService: http://localhost:5003/api/orders/health
- VehicleService: http://localhost:5068/api/health

### Check All Services:
```powershell
# từ ev-dealer-management/ (script chưa sửa port — xem caveat mục Quick Start)
cd ev-dealer-management
.\check-health.ps1
```

---

##  Contributing

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

---

##  License

This project is licensed under the MIT License - see the LICENSE file for details.

---

##  Team

- **Development Team**: DangTDuy
- **Project Type**: Educational / Portfolio Project
- **Institution**: University Project

---

##  Support

### Quick Help:
```powershell
# Check service health
cd ev-dealer-management; .\check-health.ps1   # ⚠️ vài probe còn port cũ (#53)

# View logs
# Check terminal windows for each service

# Restart services
.\start-all-services.ps1

# Run tests
.\test-all-flows.ps1
```

### Common Issues:
See [TESTING_GUIDE.md](./TESTING_GUIDE.md#common-issues--solutions)

---

##  Roadmap

### Phase 1:  Complete (Current)
- [x] Vehicle Reservation with Push Notification
- [x] Order Completion with Confirmation
- [x] RabbitMQ Integration
- [x] Frontend Integration
- [x] API Gateway Routing

### Phase 2: In Progress
- [x] Test Drive Scheduling
- [x] CustomerService Notifications
- [x] Docker Compose Deployment
- [ ] API Gateway Authentication

### Phase 3:  Planned
- [ ] Real-time Dashboard
- [ ] Admin Panel
- [ ] Analytics & Reporting
- [ ] Mobile App Support

---

**Version**: 1.0.0  
**Last Updated**: September 14, 2026  
**Status**:  Production Ready (Core Features)
