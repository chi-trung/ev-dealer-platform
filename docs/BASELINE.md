# Baseline — ảnh chụp trạng thái trước refactor

Ghi lại trạng thái có số liệu thật **trước khi** refactor bắt đầu, để mỗi phase sau đó so sánh được.

**Điểm chụp:** `main` = `a5035e6` ("docs: remove 10 superseded guides and fix dangling links (#141)")
**Ngày:** 2026-09-25
**Toolchain:** .NET SDK 8.0.422 · Node 22.17.1 · npm 10.9.2 · Docker 29.8.0 · Compose v5.5.1 · Git LFS (glb thật)

---

## 1. Build

| Hạng mục | Kết quả |
|---|---|
| `dotnet build DealerSystem.sln` | ✅ Build succeeded, **0 error** |
| Warning | **19** (baseline để so sánh) |
| Thời gian | 16.8s |

Phân bố 19 warning:

| Service | Số | Loại |
|---|---|---|
| UserService | 4 | CS8601/CS8618/CS8604 ×2 (nullability) |
| SalesService | 9 | CS8618 ×9 (DTO/Model non-nullable property) |
| VehicleService | 5 | CS8600 ×2, CS8601 ×2, **CS7022 ×1** |
| Customer/Reporting/Notification/Gateway | 0 | — |

> ⚠️ `VehicleService/CreateTable/Program.cs(7,21): CS7022` — file này **đang được compile vào
> `VehicleService.csproj`** và gây cảnh báo "The entry point of the program is global code".
> Nó là công cụ dùng một lần, không phải entrypoint của service. P1 sẽ loại nó khỏi compile.

## 2. Test

| Hạng mục | Kết quả |
|---|---|
| `dotnet test DealerSystem.sln` | ✅ **239 passed, 0 failed, 0 skipped** |
| Thời gian | 33s |
| Project có test | **1 / 7 service** — `DealerSystem.Tests/NotificationService.Tests` |

> ⚠️ 239 test **toàn bộ nằm trong NotificationService**. 6 service còn lại — kể cả 36 endpoint
> mutation vừa được khoá `[Authorize]` ở #137 — **không có test nào**. Đây là khoảng trống
> lớn nhất của hệ thống test.

## 3. Frontend

| Hạng mục | Kết quả |
|---|---|
| `npm ci` | ✅ |
| `npm run build` | ✅ built in 32.57s, 13514 modules |
| Git LFS `public/porsche_taycan.glb` | ✅ file thật, 19.426.608 byte (không phải LFS pointer) |
| Test runner | ❌ **không có** — `package.json` không có `test` script |
| `npm run lint` | ❌ **fail: 66 problems (58 error, 8 warning)** — xem §3.1 |

### 3.1 Lint — 58 lỗi rải ở 22 file

`npm run lint` **đang fail** — đây là lý do nó không được đưa vào CI. Không phải lỗi mới.

| Lỗi | File | Ghi chú |
|---|---|---|
| 8 | `services/authService.js` | 4× `no-useless-catch`, 2× `no-empty`, 2× `no-unused-vars` |
| 8 | `public/firebase-messaging-sw.js` | service worker không có env của Vite → `no-undef` |
| 6 | `pages/Vehicles/VehicleFormModern.jsx` | **file vỡ thật**: `'VehicleFormModern' is not defined` ở dòng 67. Không route nào import nên chưa lộ. Sẽ xoá ở P4 |
| 5 | `pages/Vehicles/VehicleDetail.jsx` | +1 warning |
| 4 | `pages/Admin/UserManagement.jsx` | |
| 4 | `pages/Vehicles/VehicleForm.jsx` | +1 warning |
| 2 ×4 | `SalesList`, `Reports`, `DealerList`, `FeedbackList`, `DataTable` | |
| 1 ×11 | `LandingPage`, `OrderDetail`, `ComplaintDetailPage`, `TestDriveForm`, `DealerDetail`, `AuthContext`, `Dropdown`, `Form`, `Table`, `Tabs` | |
| 1 ×2 | `Toast`, `VehicleCompare` | +1 warning mỗi file |

**Bundle size** (Vite cảnh báo >500 kB):

```
dist/assets/index-BiB2I4Ai.js     1,492.33 kB │ gzip: 413.89 kB   ← bundle chính
dist/assets/Environment-CnWBTDA7.js  876.58 kB │ gzip: 241.81 kB
```

> ⚠️ Bundle chính 1.49 MB không được lazy-load. Đó là lý do `LandingPage` (68 kB) được
> code-split còn phần còn lại thì không. Sẽ xử lý ở P4.

## 4. Docker Compose

| Hạng mục | Kết quả |
|---|---|
| Số service | 8 (7 app + RabbitMQ) |
| Health check | ✅ **8/8 healthy** (sau khi sửa BUG-7) |
| Postgres | ❌ **không có** trong compose (xem P2) |

**Topology broker đo được** (RabbitMQ management API, không phải suy đoán từ code):

```
TOTAL QUEUES: 45   MAIN=15   RETRY=15   DLQ=15
```

Cả **15/15 main queue đều có đúng 1 consumer**, không queue nào có message tồn đọng
(`ready=0` toàn bộ). 15 main queue = 14 của NotificationService + `customer_vehicle_reserved`
của CustomerService.

> Plan ước lượng "14 queue × 3 = 42". Số thật là **15 × 3 = 45** — bỏ sót `customer_vehicle_reserved`,
> vì nó do CustomerService consume chứ không nằm trong 14 `RabbitMQ:Queues:*` của NotificationService.
> Test topology ở P3 phải pin 45, không phải 42.

### BUG-7 — `ReportingService` unhealthy vĩnh viễn trong compose

Lộ ra khi boot stack lần đầu, không lộ khi đọc code.

```
Health check database with status Unhealthy completed ... 'Database is unreachable'
```

**Nguyên nhân:** `ReportingService/appsettings.json:30` chứa connection string kiểu Postgres:

```json
"DefaultConnection": "Host=localhost;Port=5432;Database=ev_dealer_reporting;Username=postgres;Password=postgres"
```

nhưng compose chạy nó với `DB_PROVIDER=sqlite`. `Common/Data/DbProviderSelector.cs:76-77` chỉ dùng
`sqliteFallback` (tức `REPORTING_DB_PATH`) khi connection string **rỗng** — nó không rỗng, nên
chuỗi Postgres đó bị đưa thẳng vào `UseSqlite()`, và SQLite không parse được `Host=localhost;Port=5432`.

**Vì sao chỉ ReportingService:** 5 service còn lại có `Data Source=*.db` trong appsettings, và
compose override chúng bằng `ConnectionStrings__DefaultConnection=Data Source=/app/data/...`.
Riêng ReportingService **không có** override đó, nên nó là service duy nhất lộ lỗi.

Đã sửa trong P1 bằng cách thêm `ConnectionStrings__DefaultConnection=Data Source=/app/data/reporting_dev.db`
vào compose, khớp với 5 service kia.

**Bài học:** lỗi này bị che bởi file `reporting_dev.db` cũ tồn tại sẵn trên volume. Xoá file
→ lộ. Đây là loại bug chỉ tìm ra khi chạy thật, không phải khi đọc code.

### BUG-8 — `userservice` + `vehicleservice` crash vòng lặp với db cũ

```
SQLite Error 1: 'table "Users" already exists'.
   at ...Migrator.Migrate(String targetMigration)
   at Program.<Main>$(String[] args) in /src/UserService/Program.cs:line 154
```

Container restart liên tục (exit 134) vì `users.db` / `vehicles.db` trên volume có bảng từ
**13/09** — tạo trước khi hệ thống migration có mặt — nhưng `__EFMigrationsHistory` không có
bản ghi, nên `Migrate()` thử `CREATE TABLE` lại và crash.

Không phải lỗi code: `git ls-files | grep '\.db$'` = **0 file** (`.gitignore` đã loại `*.db`),
nên đây là dữ liệu dev local. Đã xoá 11 file `.db` rải rác trong 6 thư mục; stack boot sạch sau đó.

> ⚠️ Nhưng điều này phơi bày một vấn đề thật: **`Migrate()` crash cứng nếu DB có bảng mà không có
> migration history.** Trên Render, nếu database từng được tạo bằng `EnsureCreated()` rồi đổi
> sang `Migrate()`, deploy sẽ loop restart y hệt. Cần xử lý ở P2.

## 5. Bug đã biết — TÁI HIỆN ĐƯỢC (không phải phỏng đoán)

Mỗi bug dưới đây đã được xác nhận bằng đọc code, **không phải suy đoán**:

### BUG-1 — `/admin/users` không có role gate thật (bảo mật)

```
src/routes/index.jsx:170    <ProtectedRoute roles={['Admin']}>
src/components/common/ProtectedRoute.jsx:18   const ProtectedRoute = ({ children, requiredRole }) => {
src/components/common/ProtectedRoute.jsx:48   if (requiredRole && user?.role !== requiredRole) {
```

Truyền `roles` (mảng) nhưng component chỉ đọc `requiredRole` → luôn `undefined` → **vòng kiểm tra
role bị bỏ qua hoàn toàn**. Bất kỳ user nào đã đăng nhập đều vào được `/admin/users`.

Backend vẫn chặn đúng (`[Authorize(Roles = "Admin")]`), nên đây là lỗ hổng **UI hiển thị dữ liệu
admin**, không phải lỗ hổng API. Vẫn cần sửa.

Hai hệ thống role không đồng nhất:
- Backend: role PascalCase, danh sách hợp lệ `{DealerStaff, DealerManager, EVMStaff, Admin}` (`UserService/Program.cs:639`)
- Frontend: truyền `roles={['Admin']}` — **đúng case**, sai tên prop
- Backend dùng `!=` so sánh thẳng (`UserService/Program.cs:266`) — phân biệt hoa/thường

### BUG-2 — DEV bypass tự phong admin

`ProtectedRoute.jsx:21-36`: khi `import.meta.env.DEV`, tự set token `dev-token-123` và user
`role: 'admin'` nếu localStorage trống, rồi `return children` — **bỏ qua cả auth lẫn role**.

Không ảnh hưởng production build (`import.meta.env.DEV` = false sau `vite build`), nhưng dễ
tạo cảm giác an toàn giả khi dev. Còn 2 "defender phụ" phụ thuộc giá trị giả này:
`NotificationPreferences.jsx` (`isDevPlaceholder()` so `token === 'dev-token-123'`) và
`firebase/notificationService.js` (user id `dev-user-1` không match `/^\d+$/` nên bỏ qua
API DeviceTokens).

### BUG-3 — `test-all-flows.ps1` cho kết quả không kiểm chứng

- Bước `[4/6]` (`:148-152`): in `" (Check RabbitMQ UI manually)"` — không đọc queue thật
- Bước `[5/6]` (`:159-165`): in log **dự kiến** ("should show") — không đọc log thật
- Bước `[6/6]` (`:174-182`): in `✅` **vô điều kiện**, không phụ thuộc kết quả bước 4-5

Đây là script mà `README.md` và `TESTING_GUIDE.md` cùng chỉ tới, nên nó tạo cảm giác
"đã verify" trong khi không verify gì.

**Phát hiện thêm trong lúc P0:** script POST thẳng vào SalesService
(`$SalesServiceUrl/api/orders/complete`) **không kèm header `Authorization`**. Sau #137,
`OrdersController` có `[Authorize]` ở cấp class (`SalesService/Controllers/OrdersController.cs:21`)
→ request này phải trả **401**. Bước 3 có `exit 1` khi lỗi, nên script **fail sớm** chứ không
in ✅ giả. Tức là: script hỏng, nhưng hỏng theo cách *thành thật* — đã hỏng từ #137, chưa ai chạy.

### BUG-4 — `QuickTest.ps1` chết hoàn toàn

`NotificationService/QuickTest.ps1` gọi port `5005` (thật là `5051`) và 5 endpoint **không tồn tại**:

| Script gọi | Endpoint thật trong `NotificationController` |
|---|---|
| `/api/notification/test-email` | `/api/Notification/test-fcm` |
| `/api/notification/order-confirmation` | `/api/Notification/send-to-topic` |
| `/api/notification/test-drive-confirmation` | `/api/Notification/send-multicast` |
| `/api/notification/test-sms` | `/api/Notification/subscribe-topic` |
| `/api/notification/reservation-confirmation` | `/api/Notification/unsubscribe-topic` |

Không endpoint nào trùng. 0/5 khớp.

### BUG-5 — `check-health.ps1` chỉ kiểm tra 4/7 service

Script tự ghi `[1/5]`…`[5/5]` nhưng thiếu UserService (7001), CustomerService (5039),
ReportingService (5208) và gateway (5036). Lại kiểm tra cả frontend `5173` (optional) và
RabbitMQ UI — tức là 5 mục nhưng chỉ 3/7 service backend.

### BUG-6 — Docs mô tả thứ không tồn tại

| Nội dung | Thực tế |
|---|---|
| ReportingService: 13 file .md nhắc Apache NiFi | grep `nifi` trong `.cs`/`docker-compose.yml`/`render.yaml` = **0 kết quả**. Sync thật là `ReportingService/Services/DataSynchronizationService.cs` (HTTP fan-out) |
| `DEPLOYMENT_GUIDE.md:515-517` bảng queue ghi "emails"/"SMS" | Trái ngược §441 ngay trên cùng file: chỉ có FCM push |
| `README.md:382` roadmap `[ ] API Gateway Authentication` | Đã xong ở #137 |
| `README.md:334` "Status: Production Ready" | Mâu thuẫn với mục "In Progress" ngay dưới |

## 6. Rủi ro hạ tầng chưa xử lý

| Vấn đề | Bằng chứng |
|---|---|
| **RabbitMQ không có TLS client-side** | `RabbitMQ.Client 6.8.1` ở cả 5 service; grep `SslOptions`/`SslProtocol`/`AuthMechanism` = **0 kết quả**. Broker ngoài bắt buộc phải có **plain AMQP 5672** |
| **Email không gửi được trên Render free** | `UserService/Program.cs:1013` dùng MailKit `SecureSocketOptions.StartTls`; Render free chặn outbound 25/465/587 |
| **Consumer chết âm thầm khi broker down lúc boot** | `customerservice` trong compose có comment thừa nhận điều này (`:190-193`); Render không có `depends_on: service_healthy` tương đương |
| **`evm-rabbitmq` là `plan: starter` trả phí** | `render.yaml`; workspace chưa có payment info nên `render blueprints validate` fail |
| **2 file `Program.cs` 1000+ dòng** | `UserService/Program.cs` 1088 dòng, `ReportingService/Program.cs` 1073 dòng — toàn bộ logic inline, không controller/service layer |
| **`EventRetryPolicy.cs` copy-paste** | Bản giống hệt ở `NotificationService/Events/` và `CustomerService/Events/`, không share qua `Common` |

## 7. Kết quả lệnh verify (dán nguyên output)

<details>
<summary><code>dotnet build DealerSystem.sln</code></summary>

```
Build succeeded.
    19 Warning(s)
    0 Error(s)
Time Elapsed 00:00:16.80
```
</details>

<details>
<summary><code>dotnet test DealerSystem.sln</code></summary>

```
A total of 1 test files matched the specified pattern.
Passed!  - Failed: 0, Passed: 239, Skipped: 0, Total: 239, Duration: 33 s
  - NotificationService.Tests.dll (net8.0)
```
</details>

<details>
<summary><code>git lfs ls-files</code> + kích thước file</summary>

```
f06c53d5d2 * ev-dealer-frontend/public/porsche_taycan.glb

Name                   Length
----                   ------
porsche_taycan.glb   19426608
```
</details>

## 8. Baseline dùng để so sánh

Sau mỗi phase, chạy lại và đối chiếu:

| Metric | Baseline | Mục tiêu |
|---|---|---|
| Build error | 0 | giữ 0 |
| Build warning | 19 | giảm dần, **giữ 0 error** |
| Test pass | 239 | ≥239, **không được giảm** |
| Service có test | 1/7 | tăng dần |
| FE test runner | không có | có |
| Số bug mở | 6 (BUG-1..6) | → 0 ở P5 |
