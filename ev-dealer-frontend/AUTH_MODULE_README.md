# 🔐 Authentication Module - Implementation Guide

**Người phụ trách:** Nguyen Chi Trung  
**Deadline:** 27/10/2025

---

## ✅ Completed Tasks

### 1. **Login Page** (`src/pages/Auth/Login.jsx`)
- ✅ Beautiful UI with Material-UI
- ✅ Email/password validation
- ✅ Remember me checkbox
- ✅ Show/hide password toggle
- ✅ Call login API
- ✅ Save token to localStorage
- ✅ Redirect to dashboard on success
- ✅ Error handling with alerts
- ✅ Loading state

### 2. **Register Page** (`src/pages/Auth/Register.jsx`)
- ✅ Complete registration form
- ✅ Form validation (name, email, phone, password)
- ✅ Password strength indicator (Weak/Medium/Strong)
- ✅ Confirm password matching
- ✅ Call register API
- ✅ Success message & redirect to login
- ✅ Error handling

### 3. **Forgot Password** (`src/pages/Auth/ForgotPassword.jsx`)
- ✅ Email input form
- ✅ Email validation
- ✅ Call forgot password API
- ✅ Success message
- ✅ Link back to login

### 4. **Auth Layout** (`src/layouts/AuthLayout.jsx`)
- ✅ Centered layout design
- ✅ Gradient background (purple theme)
- ✅ Logo/branding with Electric Car icon
- ✅ Responsive design
- ✅ Beautiful glassmorphism effect

### 5. **Auth Service** (`src/services/authService.js`)
- ✅ Login function
- ✅ Register function
- ✅ Forgot password function
- ✅ Reset password function
- ✅ Logout function
- ✅ Get current user
- ✅ Check authentication status

### 6. **API Configuration** (`src/services/api.js`)
- ✅ Axios instance with base URL
- ✅ Request interceptor (add token to headers)
- ✅ Response interceptor (handle errors)
- ✅ Auto redirect to login on 401

### 7. **Validators** (`src/utils/validators.js`)
- ✅ Email validation
- ✅ Password validation
- ✅ Password strength checker
- ✅ Phone validation (Vietnam format)
- ✅ Name validation
- ✅ Required field validation

### 8. **Protected Route** (`src/components/common/ProtectedRoute.jsx`)
- ✅ Check authentication
- ✅ Check user role
- ✅ Redirect to login if not authenticated

---

## 🚀 How to Run

### 1. Install Dependencies
```bash
cd ev-dealer-frontend
npm install
```

### 2. Create .env file
```bash
cp .env.example .env
```

Edit `.env` and set your API URL:
```env
VITE_API_BASE_URL=http://localhost:5036/api
```

### 3. Run Development Server
```bash
npm run dev
```

The app will run on `http://localhost:5173`

---

## 🧪 Testing the Module

### Test Login Page
1. Go to `http://localhost:5173/login`
2. Try invalid email → Should show error
3. Try empty fields → Should show validation errors
4. Enter valid credentials → Should call API and redirect

### Test Register Page
1. Go to `http://localhost:5173/register`
2. Fill the form
3. Watch password strength indicator change
4. Try mismatched passwords → Should show error
5. Submit valid form → Should show success and redirect

### Test Forgot Password
1. Go to `http://localhost:5173/forgot-password`
2. Enter email
3. Submit → Should show success message

---

## 📁 File Structure

```
src/
├── pages/
│   └── Auth/
│       ├── Login.jsx              ✅ Complete
│       ├── Register.jsx           ✅ Complete
│       └── ForgotPassword.jsx     ✅ Complete
├── layouts/
│   └── AuthLayout.jsx             ✅ Complete
├── services/
│   ├── api.js                     ✅ Complete
│   └── authService.js             ✅ Complete
├── utils/
│   └── validators.js              ✅ Complete
└── components/
    └── common/
        └── ProtectedRoute.jsx     ✅ Complete
```

---

## 🔌 API Integration

### Backend Endpoints (IMPLEMENTED)

All auth endpoints below are live in UserService (`ev-dealer-management/UserService/Program.cs`,
minimal APIs mapped under `/api/auth/*`, port 7001) and routed through the API Gateway
(port 5036) via `ocelot.json` (`/api/auth/{everything}` → localhost:7001).
The frontend calls them for real — there is no mock layer in `src/services/authService.js`.

#### 1. Login
```
POST /api/auth/login
Body: { username, password }
Response (AuthResult): { success, message, token, userId, user: UserDto }
UserDto: { id, username, email, fullName, role, isActive, dealerId, createdAt, updatedAt }
```

#### 2. Register
```
POST /api/auth/register
Body: { username, email, fullName, password, role, dealerId }
Response: created AuthResult (201) or 400 with { success, message }
```

#### 3. Forgot Password
```
POST /api/auth/forgot-password
Body: { email }
Response: { success, message }   // sends a reset link email via EmailService (SMTP)
```

#### 4. Reset Password
```
POST /api/auth/reset-password
Body: { token, newPassword }
Response: { success, message }
```

#### 5. Change Password (Issue #50 — authenticated)
```
POST /api/auth/change-password
Headers: Authorization: Bearer <JWT>
Body: { currentPassword, newPassword }
Response: { success, message }  // used by the Settings page
```

---

## 🎨 UI Features

### Design Highlights
- **Gradient Background:** Purple theme (#667eea → #764ba2)
- **Material-UI Components:** Professional look
- **Icons:** Material Icons for better UX
- **Responsive:** Works on mobile, tablet, desktop
- **Animations:** Smooth transitions
- **Glassmorphism:** Modern frosted glass effect

### Form Validation
- Real-time validation
- Clear error messages
- Visual feedback (red borders, helper text)
- Disabled state during loading

### Password Features
- Show/hide toggle
- Strength indicator (Weak/Medium/Strong)
- Color-coded progress bar
- Minimum 8 characters requirement

---

## 🔒 Security Features

1. **Token Storage:** JWT token in localStorage
2. **Auto Logout:** On 401 response
3. **Protected Routes:** Redirect to login if not authenticated
4. **Password Validation:** Enforce strong passwords
5. **HTTPS Ready:** Works with secure connections

---

## 📝 Next Steps

### For Backend Team — ✅ DONE
1. ✅ The endpoints listed above are implemented in `UserService/Program.cs` (plus `/api/auth/change-password`, Issue #50)
2. ✅ Login returns a JWT — claims `id`, `name`, `role`, and a dealer claim when the user has a DealerId (Issue #36/#41)
3. ✅ Login response includes full user info via `UserDto` (id, username, email, fullName, role, isActive, dealerId)
4. ✅ `EmailService` (SMTP) is wired up and `/api/auth/forgot-password` emails a reset link (`{FrontendUrl}/reset-password?token=…`)

### For Frontend Team (Optional Enhancements)
1. Add "Remember Me" persistence (save email)
2. Add social login (Google, Facebook)
3. Add 2FA (Two-Factor Authentication)
4. ~~Add password reset page~~ ✅ Done — `src/pages/Auth/ResetPassword.jsx`, routed at `/reset-password` (`src/routes/index.jsx`)
5. Add email verification flow

---

## 🐛 Known Issues / TODO

- [x] ~~Backend API not implemented yet (using mock)~~ — the auth backend IS live: UserService (port 7001) implements the endpoints behind the API Gateway (port 5036); `src/services/authService.js` calls the real API directly, no mock layer exists
- [x] ~~Need to test with real backend~~ — login/register/forgot/reset/change-password are all wired to the real endpoints; UserService ships login + dealer-claim tests (e.g. DealerClaimLoginTests); remaining UX items below are still open
- [ ] Add loading skeleton for better UX
- [ ] Add animations (fade in/out)
- [ ] Add toast notifications instead of alerts

---

## 📞 Contact

**Developer:** Nguyen Chi Trung  
**Module:** Authentication  
**Status:** ✅ Complete (Frontend)  
**Last Updated:** 2025-10-20

---

## 🎉 Summary

All authentication pages are **100% complete** with:
- ✅ Beautiful UI/UX
- ✅ Full validation
- ✅ Error handling
- ✅ Loading states
- ✅ API integration ready
- ✅ Responsive design

**Ready for backend integration!** 🚀

