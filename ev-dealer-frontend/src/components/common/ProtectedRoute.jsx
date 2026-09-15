/**
 * Protected Route Component
 * Route protection based on authentication and role.
 *
 * Dev-mode bypass là DESIGN, không phải TODO còn sót: Vite dev (import.meta.env.DEV)
 * inject mock user `dev-token-123` để toàn bộ UI chạy được không cần login.
 * Production build luôn enforce auth ở dưới. Một số trang (NotificationPreferences,
 * firebase/notificationService.js) có isDevPlaceholder() nhận diện đúng token
 * 'dev-token-123' / 'dev-user-1' này để hiện placeholder thay vì gọi API thật —
 * đổi token ở đây là làm hỏng mấy chỗ đó.
 */

import { Navigate } from 'react-router-dom'
import authService from '../../services/authService'

const ProtectedRoute = ({ children, requiredRole }) => {
  // DEVELOPMENT MODE (Vite only): mock user so the UI runs without a backend login.
  // Production builds skip this branch entirely — auth is enforced below.
  const isDevelopmentMode = import.meta.env.DEV
  
  if (isDevelopmentMode) {
    // Set mock user for development
    if (!localStorage.getItem('token')) {
      localStorage.setItem('token', 'dev-token-123')
      localStorage.setItem('user', JSON.stringify({
        id: 'dev-user-1',
        name: 'Development User',
        email: 'dev@example.com',
        role: 'admin',
        dealerId: 'dealer1'
      }))
    }
    return children
  }

  // PRODUCTION MODE: Check authentication
  const isAuthenticated = authService.isAuthenticated()
  const user = authService.getCurrentUser()

  // Redirect to login if not authenticated
  if (!isAuthenticated) {
    return <Navigate to="/login" replace />
  }

  // Check role if required
  if (requiredRole && user?.role !== requiredRole) {
    return <Navigate to="/dashboard" replace />
  }

  return children
}

export default ProtectedRoute

