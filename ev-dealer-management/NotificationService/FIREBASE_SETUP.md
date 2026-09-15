# 🔥 FIREBASE SETUP GUIDE

Hướng dẫn chi tiết setup Firebase Cloud Messaging (FCM) cho NotificationService.

---

## 📋 BƯỚC 1: TẠO FIREBASE PROJECT

### 1.1. Truy cập Firebase Console
- Mở trình duyệt và truy cập: https://console.firebase.google.com
- Đăng nhập bằng Google Account

### 1.2. Tạo Project Mới
1. Click **"Add project"** hoặc **"Create a project"**
2. Nhập tên project: `ev-dealer-management`
3. Click **Continue**
4. (Optional) Enable Google Analytics → Click **Continue**
5. Chọn Analytics account hoặc tạo mới → Click **Create project**
6. Chờ 30-60 giây để Firebase tạo project
7. Click **Continue** khi hoàn tất

---

## 📋 BƯỚC 2: ENABLE FIREBASE CLOUD MESSAGING

### 2.1. Vào Cloud Messaging Settings
1. Trong Firebase Console, chọn project vừa tạo
2. Click vào **⚙️ Settings** (góc trên bên trái) → **Project settings**
3. Chọn tab **"Cloud Messaging"**

### 2.2. Enable Cloud Messaging API
1. Nếu thấy nút **"Enable Cloud Messaging API"**, click vào
2. Sẽ redirect sang Google Cloud Console
3. Click **"Enable"** để bật API
4. Quay lại Firebase Console

### 2.3. Lấy Server Key (Legacy)
**⚠️ Lưu ý:** Firebase khuyến nghị dùng Firebase Admin SDK thay vì Server Key, nhưng ta vẫn cần xem để tham khảo.

1. Trong tab **Cloud Messaging**
2. Scroll xuống phần **"Cloud Messaging API (Legacy)"**
3. Copy **Server key** (SẼ KHÔNG DÙNG - chỉ để tham khảo)

---

## 📋 BƯỚC 3: TẠO WEB APP

### 3.1. Add Web App to Firebase
1. Trong Firebase Console, chọn project
2. Click vào icon **Web** (</>) để add web app
3. Nhập App nickname: `ev-dealer-frontend`
4. ✅ Check vào **"Also set up Firebase Hosting"** (optional)
5. Click **"Register app"**

### 3.2. Lấy Firebase Config
Sau khi register, Firebase sẽ hiển thị config object:

```javascript
const firebaseConfig = {
  apiKey: "AIzaSyXXXXXXXXXXXXXXXXXXXXXXXXXXXX",
  authDomain: "ev-dealer-management.firebaseapp.com",
  projectId: "ev-dealer-management",
  storageBucket: "ev-dealer-management.appspot.com",
  messagingSenderId: "123456789012",
  appId: "1:123456789012:web:abcdef1234567890"
};
```

**📝 LƯU LẠI CONFIG NÀY** - Sẽ dùng cho frontend!

### 3.3. Lấy VAPID Key
1. Vẫn trong phần config setup, scroll xuống
2. Tìm phần **"Web Push certificates"**
3. Click **"Generate key pair"**
4. Copy **Key pair** (VAPID key) - dạng:
   ```
   BCdEfGhIjKlMnOpQrStUvWxYz1234567890AbCdEfGhIjKlMnOpQrStUvWxYz1234567890
   ```

**📝 LƯU LẠI VAPID KEY** - Sẽ dùng cho frontend!

---

## 📋 BƯỚC 4: TẠO SERVICE ACCOUNT (Backend)

### 4.1. Generate Private Key
1. Trong Firebase Console → **⚙️ Settings** → **Project settings**
2. Chọn tab **"Service accounts"**
3. Click **"Generate new private key"**
4. Popup xác nhận → Click **"Generate key"**
5. File JSON sẽ được download tự động

### 4.2. File JSON Credential Structure
File download sẽ có tên dạng: `ev-dealer-management-xxxxx.json`

Nội dung:
```json
{
  "type": "service_account",
  "project_id": "ev-dealer-management",
  "private_key_id": "abcdef1234567890",
  "private_key": "-----BEGIN PRIVATE KEY-----\nMIIEvQIBA...\n-----END PRIVATE KEY-----\n",
  "client_email": "firebase-adminsdk-xxxxx@ev-dealer-management.iam.gserviceaccount.com",
  "client_id": "123456789012345678901",
  "auth_uri": "https://accounts.google.com/o/oauth2/auth",
  "token_uri": "https://oauth2.googleapis.com/token",
  "auth_provider_x509_cert_url": "https://www.googleapis.com/oauth2/v1/certs",
  "client_x509_cert_url": "https://www.googleapis.com/robot/v1/metadata/x509/firebase-adminsdk-xxxxx%40ev-dealer-management.iam.gserviceaccount.com"
}
```

### 4.3. Lưu File Credential
1. Đổi tên file thành: `firebase-credentials.json`
2. Copy file vào:
   ```
   ev-dealer-management/ev-dealer-management/NotificationService/firebase-credentials.json
   ```

### 4.4. Add to .gitignore
**⚠️ QUAN TRỌNG:** Không commit credentials lên Git!

```bash
# .gitignore
firebase-credentials.json
**/firebase-credentials.json
```

---

## 📋 BƯỚC 5: CẤU HÌNH APPSETTINGS.JSON

### 5.1. Update appsettings.json
Mở file: `NotificationService/appsettings.json`

Thêm section:
```json
{
  "Firebase": {
    "CredentialPath": "firebase-credentials.json",
    "ProjectId": "ev-dealer-management"
  }
}
```

### 5.2. Update appsettings.Development.json
```json
{
  "Firebase": {
    "CredentialPath": "firebase-credentials.json",
    "ProjectId": "ev-dealer-management"
  }
}
```

---

## 📋 BƯỚC 6: TẠO .ENV FILE CHO FRONTEND

### 6.1. Tạo file .env.local
Trong `ev-dealer-frontend/`, tạo file `.env.local`:

```env
# Firebase Configuration
VITE_FIREBASE_API_KEY=AIzaSyXXXXXXXXXXXXXXXXXXXXXXXXXXXX
VITE_FIREBASE_AUTH_DOMAIN=ev-dealer-management.firebaseapp.com
VITE_FIREBASE_PROJECT_ID=ev-dealer-management
VITE_FIREBASE_STORAGE_BUCKET=ev-dealer-management.appspot.com
VITE_FIREBASE_MESSAGING_SENDER_ID=123456789012
VITE_FIREBASE_APP_ID=1:123456789012:web:abcdef1234567890
VITE_FIREBASE_VAPID_KEY=BCdEfGhIjKlMnOpQrStUvWxYz1234567890...
```

**Thay thế giá trị bằng config thật từ Bước 3!**

### 6.2. Add to .gitignore
```bash
# .gitignore
.env.local
```

---

## ✅ CHECKLIST HOÀN TẤT

Đánh dấu khi hoàn thành:

- [ ] ✅ Đã tạo Firebase project: `ev-dealer-management`
- [ ] ✅ Đã enable Cloud Messaging API
- [ ] ✅ Đã tạo Web App và lấy Firebase config
- [ ] ✅ Đã generate VAPID key
- [ ] ✅ Đã tạo Service Account và download JSON
- [ ] ✅ File `firebase-credentials.json` đã lưu vào NotificationService/
- [ ] ✅ Đã update `appsettings.json`
- [ ] ✅ Đã tạo `.env.local` cho frontend
- [ ] ✅ Đã add credentials vào `.gitignore`

---

## 🔐 BẢO MẬT

### Credentials cần giữ bí mật:
- ❌ `firebase-credentials.json` - KHÔNG commit lên Git
- ❌ Private key trong JSON
- ❌ `.env.local` - KHÔNG commit lên Git

### Có thể public:
- ✅ Firebase config (apiKey, projectId, etc.) - Safe to expose
- ✅ VAPID key - Dùng cho client-side

---

## 🧪 TEST FIREBASE SETUP

### Test 1: Verify Credentials
```bash
cd ev-dealer-management/NotificationService
ls firebase-credentials.json
# Nếu thấy file → OK
```

### Test 2: Check JSON Valid
```bash
cat firebase-credentials.json | ConvertFrom-Json
# Nếu không lỗi → JSON hợp lệ
```

### Test 3: Verify Project ID
```bash
# Check projectId trong firebase-credentials.json
$json = Get-Content firebase-credentials.json | ConvertFrom-Json
$json.project_id
# Output: ev-dealer-management
```

---

## 🆘 TROUBLESHOOTING

### Lỗi: "Permission denied" khi download credentials
**Solution:** Bạn phải là Owner hoặc Editor của Firebase project

### Lỗi: "Cloud Messaging API not enabled"
**Solution:** 
1. Vào Google Cloud Console
2. Tìm "Cloud Messaging API"
3. Click "Enable"

### Lỗi: "Invalid VAPID key"
**Solution:** 
1. Re-generate key pair trong Firebase Console
2. Copy lại VAPID key mới
3. Update `.env.local`

### Lỗi: "Project ID mismatch"
**Solution:** Đảm bảo `projectId` trong:
- `firebase-credentials.json`
- `appsettings.json`
- `.env.local` (VITE_FIREBASE_PROJECT_ID)

Đều giống nhau: `ev-dealer-management`

---

## 📚 TÀI LIỆU THAM KHẢO

- Firebase Console: https://console.firebase.google.com
- FCM Documentation: https://firebase.google.com/docs/cloud-messaging
- Admin SDK Setup: https://firebase.google.com/docs/admin/setup
- Web Push Protocol: https://firebase.google.com/docs/cloud-messaging/js/client

---

## ✨ SAU KHI HOÀN TẤT

Toàn bộ code đã sẵn sàng — phần còn lại chỉ là credentials:
1. ✅ Firebase Admin SDK đã có trong `NotificationService.csproj` (`FirebaseAdmin` 3.0.1)
2. ✅ FCM Service đã implement (`Services/FirebaseFcmService.cs`, đăng ký singleton trong `Program.cs`)
3. ✅ Firebase SDK đã cài vào Frontend (`firebase` ^12.6.0 trong `ev-dealer-frontend/package.json`, code trong `src/firebase/`)
4. ⬜ Đặt `firebase-credentials.json` (Bước 4) vào `NotificationService/` — file này đã có trong `.gitignore` của repo, không commit lên Git
5. ⬜ Thêm các giá trị `VITE_FIREBASE_*` vào `.env.local` của frontend (repo chưa có file này)

**➡️ Tiếp theo: Sau khi có credentials, chạy `test-fcm.ps1` (gọi `POST http://localhost:5051/api/notification/test-fcm`) để test push notifications**

