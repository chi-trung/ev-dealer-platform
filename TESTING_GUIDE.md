# 📋 EV DEALER MANAGEMENT - TESTING & OPERATIONS GUIDE

## 🎯 Quick Reference

### Start All Services
```powershell
.\ev-dealer-management\start-all-services.ps1
```

### Run All Tests
```powershell
.\ev-dealer-management\test-all-flows.ps1
```

### Check Service Health
```powershell
.\ev-dealer-management\check-health.ps1
```

> ✅ Fixed in #53: `start-all-services.ps1` now derives its base path from
> `$PSScriptRoot` (no more hardcoded dev-machine path), and
> `test-all-flows.ps1` / `check-health.ps1` probe the real endpoints
> (VehicleService **5068** `/health`, NotificationService **5051** `/health`
> — no more `/notifications/health`).

---

## 🧪 Testing Workflows

### ✅ Current Implemented Flows:

#### 1️⃣ Vehicle Reservation → FCM Push Notification
```
User Action: Reserve vehicle on website
    ↓
Frontend → VehicleService API (port 5068)
    ↓
VehicleService → RabbitMQ (topic exchange `vehicle_events`, routing key `vehicle.reserved`)
    ↓
NotificationService consumes `vehicle.reserved` → Firebase Cloud Messaging push
    ↓
Result: Customer's browser/device shows a push notification
```

**Test Manually:**
```powershell
# Start services
.\ev-dealer-management\start-all-services.ps1

# Navigate to frontend
http://localhost:5173/vehicles/1

# Click "🚗 Đặt xe ngay" button (opens ReservationDialog)
# Fill form and submit — the dialog reads the FCM token from localStorage
# ('fcm_device_token') and sends it in the request as deviceToken
# Check NotificationService terminal for:
[INF] Processing VehicleReservedEvent for Vehicle: 1, Customer: Test User
[INF] FCM notification sent successfully. MessageId: ..., Token: eyJ...
```

**Test via API:**
```powershell
# VehicleId comes from the URL route ({id}/reserve); body follows
# ReservationRequestDto: customerName/customerEmail/customerPhone required,
# quantity optional (default 1), deviceToken optional (push needs it)
$body = @{
    customerName = "Test User"
    customerEmail = "test@example.com"
    customerPhone = "+84912345678"
    quantity = 1
    notes = "Test reservation"
    deviceToken = "YOUR_FCM_DEVICE_TOKEN"
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:5068/api/vehicles/1/reserve" `
    -Method Post `
    -Body $body `
    -ContentType "application/json"

# Or through the Ocelot gateway (what the frontend uses by default):
#   http://localhost:5036/api/vehicles/1/reserve
```

---

#### 2️⃣ Order Completion → FCM Push Notification
```
User Action: Create order from a quote (page fires POST /api/Orders/complete)
    ↓
Frontend → SalesService API (port 5003)
    ↓
SalesService → RabbitMQ (default exchange, routing key `sales.completed`)
    ↓
NotificationService consumes `sales.completed` → FCM push
    (payload DeviceToken wins; otherwise registry lookup for
     `customer:<CustomerId>` → multicast to all its registered devices)
    ↓
Result: Push notification — or, when no token is registered, an
"logged only" entry (no email/SMS exists in this pipeline)
```

**Test Manually:**
```powershell
# Start services
.\ev-dealer-management\start-all-services.ps1

# Create an order from an existing quote — this is what fires sales.completed:
http://localhost:5173/sales/orders/create-from-quote/<quoteId>

# Fill the form and submit → alert "Đơn hàng đã được tạo thành công!"
# Check NotificationService terminal for:
[INF] Processing SaleCompletedEvent for Order: 5
[INF] ✅ Push notification sent successfully for Order: 5
# If the customer has no registered device token:
[INF] ℹ️ No device token registered for customer:5. Notification logged only (no push sent).
```

**Test via API:**
```powershell
# CreateOrderRequest payload — quoteId/customerId/etc. must reference real data;
# the backend recomputes the total from the quote and rejects totals <= 0
$body = @{
    quoteId = 1
    customerId = 1
    customerEmail = "test@example.com"
    customerName = "Test Customer"
    dealerId = 1
    salespersonId = 1
    paymentMethod = "Cash"
    paymentType = "Full"
    deliveryDate = (Get-Date).ToString("yyyy-MM-dd")
    estimatedDeliveryDate = (Get-Date).AddDays(7).ToString("yyyy-MM-dd")
    vehicleId = 1
    vehicleVariantId = 1
    colorId = 1
    quantity = 1
    unitPrice = 1500000000
    totalAmount = 1500000000
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:5003/api/orders/complete" `
    -Method Post `
    -Body $body `
    -ContentType "application/json"
```

---

## 🔍 Verification Checklist

### After Running Tests:

#### RabbitMQ Verification:
1. Open http://localhost:15672 (guest/guest)
2. Click "Queues" tab
3. NotificationService consumes **14 queues** (declared in its appsettings.json
   `RabbitMQ:Queues`, one consumer each). Every main queue also has a
   `<queue>.retry` and `<queue>.dlq` companion (retry→DLQ topology, docs/EVENTS.md):
   - ✅ `sales.completed`, `vehicle.reserved`, `testdrive.scheduled` - Ready: 0, Total: N+
   - ✅ `order.created`, `quote.created`, `contract.created`
   - ✅ `customer.created`, `customer.updated`, `customer.deleted`
   - ✅ `payment.received`, `order.status.changed`
   - ✅ `vehicle.created`, `vehicle.updated`, `vehicle.deleted`
4. `testdrive.scheduled` is **live** — CustomerService publishes it and
   `TestDriveScheduledConsumer` pushes (token via registry when payload has none).
5. Other services' queues: `customer_vehicle_reserved` (CustomerService also
   consumes `vehicle.reserved` → creates/updates customer + purchase).

#### Service Logs:
**NotificationService Terminal:**
```
[INF] Started consuming from queue: sales.completed
[INF] Started consuming from queue: vehicle.reserved
[INF] (…one line per queue; 14 total…)
[INF] Started consuming messages from all queues.
[INF] Processing SaleCompletedEvent for Order: 5
[INF] FCM notification sent successfully. MessageId: ..., Token: eyJ...
[INF] ✅ Push notification sent successfully for Order: 5
```

**SalesService Terminal:**
```
[INF] Order ORD-20260914... completed for customer test@example.com. Order ID: 5. Quote status updated to ConvertedToOrder.
[INF] Message published to queue: sales.completed
```

**VehicleService Terminal:**
```
[INF] Published message of type VehicleReservedEvent to exchange 'vehicle_events' with routing key 'vehicle.reserved'
```

---

## 📊 Expected Results Matrix

| Test | Frontend | API Response | Queue | NotificationService | Actual Delivery |
|------|----------|--------------|-------|---------------------|-----------------|
| Vehicle Reserve | ✅ ReservationDialog submits | 200 + reservation object | vehicle.reserved | FCM push-sent log | Browser/device push (needs deviceToken in payload) |
| Order Complete | ✅ "Đơn hàng đã được tạo thành công!" | 200 + order info | sales.completed | Multicast push or log-only | Push popup if `customer:<id>` token registered |
| Test Drive Scheduled | ✅ Test Drive form | 200 | testdrive.scheduled | FCM push-sent log | Push popup |

---

## 🚨 Common Issues & Solutions

### Issue 1: Service Not Responding
```powershell
# Symptoms
curl: (7) Failed to connect to localhost port 5003

# Solution
netstat -ano | findstr :5003  # Find PID
taskkill /F /PID <PID>         # Kill it
cd SalesService; dotnet run    # Restart
```

### Issue 2: RabbitMQ Connection Error
```
[ERR] RabbitMQ.Client.Exceptions.BrokerUnreachableException

# Solution — compose names the container `evm_rabbitmq`
docker ps | grep evm_rabbitmq   # Check running
docker start evm_rabbitmq       # Start if stopped
docker logs evm_rabbitmq        # Check logs
# Or via compose (from ev-dealer-management/): docker compose up -d rabbitmq
```

### Issue 3: CORS Error in Browser
```
Access-Control-Allow-Origin header is not present

# Solution - Already fixed in SalesService Program.cs
# If still occurs, check service has:
app.UseCors("AllowFrontend");
```

### Issue 4: Push Notification Not Delivered
There is no email/SMS path in NotificationService — delivery is Firebase Cloud
Messaging push only. **Check:**
1. `firebase-credentials.json` present next to NotificationService
   (`Firebase:CredentialPath` in its appsettings.json; project `ev-dealer-management-6c620`)
2. The device token is a live FCM web token — copy the current value from the
   browser: `localStorage.getItem('fcm_device_token')` (tokens expire)
3. For registry-driven events (`sales.completed`), a token is registered under the
   right subject: `GET http://localhost:5051/api/DeviceTokens/customer:<id>`
   (DeviceTokens endpoints require a UserService JWT — Issue #36)
4. Logs show `FCM notification sent successfully. MessageId: ...`; a
   `No device token found for Vehicle ...` or `ℹ️ No device token registered ...`
   line means the consumer ran but had no token to push to (log-only)

**Test FCM directly (bypasses the bus):**
```powershell
$body = @{
    deviceToken = "YOUR_FCM_DEVICE_TOKEN"
    title = "Test Notification"
    body = "Direct FCM test"
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:5051/api/Notification/test-fcm" `
    -Method Post `
    -Body $body `
    -ContentType "application/json"
```
Other controller endpoints: `subscribe-topic`, `unsubscribe-topic`,
`send-to-topic`, `send-multicast` on the same controller.

### Issue 5: Port Already in Use
```powershell
# Quick fix for all ports
$ports = @(7001, 5068, 5003, 5039, 5208, 5051, 5036)  # user/vehicle/sales/customer/reporting/notification/gateway
foreach ($port in $ports) {
    $proc = netstat -ano | Select-String ":$port " | Select-Object -First 1
    if ($proc) {
        $pid = ($proc -split '\s+')[-1]
        taskkill /F /PID $pid
        Write-Host "Killed process on port $port"
    }
}
```

---

## 🎓 Testing Best Practices

### 1. Clean State Testing
```powershell
# Before each test session:
# 1. Restart RabbitMQ to clear queues (compose names it evm_rabbitmq)
docker restart evm_rabbitmq

# 2. Stop all services
Get-Process | Where-Object {$_.ProcessName -eq "dotnet"} | Stop-Process -Force

# 3. Start fresh
.\start-all-services.ps1
```

### 2. Incremental Testing
```
✅ Step 1: Check services are running
✅ Step 2: Test individual service health endpoints
✅ Step 3: Test RabbitMQ connectivity
✅ Step 4: Test one flow at a time
✅ Step 5: Verify logs after each test
✅ Step 6: Run comprehensive test script
```

### 3. Log Analysis
**What to look for:**
```
✅ GOOD:
[INF] Published message of type VehicleReservedEvent to exchange 'vehicle_events' with routing key 'vehicle.reserved'
[INF] Message published to queue: sales.completed
[INF] Started consuming from queue: vehicle.reserved
[INF] FCM notification sent successfully. MessageId: ...

❌ BAD:
[ERR] Error processing SaleCompletedEvent   (handler rethrows → retry, then <queue>.dlq)
[WRN] Handler for sales.completed failed (attempt 1/3); retrying in 5000ms
[WRN] Malformed payload on sales.completed; parked in DLQ
[WRN] No device token found for Vehicle: 1, Customer: ... (reservation skips push)
```

---

## 📈 Performance Monitoring

### Message Processing Time
```
Typical flow timeline:
User click → API call: ~100-500ms
API → Queue publish: ~10-50ms
Queue → Consumer: ~1-10ms
Email/SMS send: ~500-2000ms
Total: ~1-3 seconds
```

### Queue Metrics (RabbitMQ UI)
- **Message rate**: Should be ~0-5/sec in dev
- **Ready messages**: Should quickly go to 0 (consumed)
- **Unacked messages**: Check if consumer is stuck
- **Consumer count**: NotificationService registers **14 consumers** (one per queue —
  Program.cs lines 69-82); each of its 14 main queues therefore shows 1 consumer
  while the service is up (plus `customer_vehicle_reserved` etc. on CustomerService)

---

## 🔄 Development Workflow

### Daily Development:
```powershell
# 1. Start services (once per day)
.\start-all-services.ps1

# 2. Make code changes
# Edit files in VS Code...

# 3. Restart affected service only
# In the service's terminal: Ctrl+C, then dotnet run

# 4. Test your changes
.\test-all-flows.ps1

# 5. Check logs for errors

# 6. Commit when all tests pass
git add .
git commit -m "feat: your changes"
```

### Before Pushing to Git:
```powershell
# 1. Run full test suite
.\test-all-flows.ps1

# 2. Check all services healthy
.\check-health.ps1

# 3. Verify no errors in logs

# 4. Update documentation if needed

# 5. Push
git push origin main
```

---

## 📦 Quick Commands Reference

```powershell
# START SERVICES
.\start-all-services.ps1

# TEST ALL FLOWS
.\test-all-flows.ps1

# CHECK HEALTH
netstat -ano | findstr "7001 5068 5003 5039 5208 5051 5036 5672"

# RABBITMQ UI
start http://localhost:15672

# KILL ALL DOTNET PROCESSES
Get-Process dotnet | Stop-Process -Force

# RESTART RABBITMQ
docker restart evm_rabbitmq

# VIEW SERVICE LOGS (in separate terminals)
cd NotificationService; dotnet run
cd SalesService; dotnet run
cd VehicleService; dotnet run

# FRONTEND
cd ev-dealer-frontend; npm run dev

# BUILD FRONTEND FOR PRODUCTION
cd ev-dealer-frontend; npm run build
```

---

## 📝 Test Results Template

Use this template to document your test results:

```
Date: YYYY-MM-DD
Tester: Your Name

Pre-conditions:
[✅] RabbitMQ running
[✅] All services started
[✅] Frontend accessible

Test 1: Vehicle Reservation → Push
- Frontend test: [PASS/FAIL]
- API test: [PASS/FAIL]
- Queue message: [PASS/FAIL]
- FCM push log + popup: [PASS/FAIL]
Notes: ...

Test 2: Order Completion → Push
- Frontend test: [PASS/FAIL]
- API test: [PASS/FAIL]
- Queue message: [PASS/FAIL]
- FCM push log + popup (or documented log-only): [PASS/FAIL]
Notes: ...

Issues Found:
1. ...
2. ...

Overall Result: [PASS/FAIL]
```

---

## 🎯 Next Testing Phases

### Phase 1 (Current): ✅ COMPLETE
- Vehicle Reservation → FCM push
- Order Completion → FCM push

### Phase 2: ✅ SHIPPED
- Test Drive Scheduling → push (`testdrive.scheduled` published by CustomerService,
  consumed by `TestDriveScheduledConsumer`)
- CustomerService integration (consumes `vehicle.reserved` via
  `customer_vehicle_reserved`; publishes `customer.*` + `testdrive.*`)
- Frontend integration for Test Drive (`/test-drives`, `/customers/test-drive/new`)
- Device-token registry (`/api/DeviceTokens/{key}`, JWT-auth) + notification
  preferences (`/api/Notification/preferences`) — Issues #33/#36/#51/#57

### Phase 3: 🟡 PARTIAL
- ✅ API Gateway routing through Ocelot is live (`http://localhost:5036/api/...`;
  the frontend's default base URL already goes through it)
- End-to-end tests via Gateway — not yet automated
- Load testing — not started

---

## 🆘 Getting Help

1. **Check logs first** - Most issues show in terminal logs
2. **Verify prerequisites** - RabbitMQ, all services running
3. **Run health checks** - Use automated script
4. **Check this guide** - Common issues section
5. **Review service README** - Each service has specific docs

---

**Created**: November 22, 2025  
**Last Updated**: September 14, 2026  
**Version**: 2.0.0 — rewritten for the FCM push pipeline (14 consumer queues), corrected dev ports, and the live Ocelot gateway
