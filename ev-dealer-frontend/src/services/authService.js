import api from './api'
import { registerDeviceTokenWithBackend, getDeviceToken } from '../firebase/notificationService'

const authService = {
  // Login
  login: async (username, password, rememberMe = false) => {
    const response = await api.post('/auth/login', { username, password })

    // Backend may return `Token` / `User` (PascalCase) or `token` / `user` (camelCase)
    const token = response?.token || response?.Token
    const user = response?.user || response?.User

    // Clear any old auth data to avoid stale tokens
    localStorage.removeItem('token')
    localStorage.removeItem('user')

    if (token) {
      localStorage.setItem('token', token)
    }

    if (user) {
      localStorage.setItem('user', JSON.stringify(user))
    }

    if (rememberMe) {
      localStorage.setItem('rememberMe', 'true')
    }

    // Issue #36: now that a JWT exists, bind this browser's FCM token to the
    // account's registry subjects (user:<id>, dealer:<n> when applicable).
    // Fire-and-forget — login must not fail over a notification channel, and
    // App.jsx already handles the permission-denied case (no token → no-op).
    registerDeviceTokenWithBackend(getDeviceToken()); // no await

    // Notify listeners (AuthProvider) to refresh current user
    window.dispatchEvent(new Event('authChanged'))

    return response
  },

  // Register
  register: async (userData) => {
    return await api.post('/auth/register', userData)
  },

  // Forgot Password
  forgotPassword: async (email) => {
    return await api.post('/auth/forgot-password', { email })
  },

  // Reset Password
  resetPassword: async (token, newPassword) => {
    return await api.post('/auth/reset-password', { token, newPassword })
  },

  // Logout
  logout: () => {
    localStorage.removeItem('token')
    localStorage.removeItem('user')
    localStorage.removeItem('rememberMe')
    // notify listeners and redirect
    window.dispatchEvent(new Event('authChanged'))
    window.location.href = '/login'
  },

  // Get current user
  getCurrentUser: () => {
    const userStr = localStorage.getItem('user')
    if (!userStr) return null
    try {
      return JSON.parse(userStr)
    } catch (error) {
      console.error('Error parsing user data:', error)
      localStorage.removeItem('user')
      return null
    }
  },

  // Check if user is authenticated
  isAuthenticated: () => {
    return !!localStorage.getItem('token')
  },
}

export default authService

