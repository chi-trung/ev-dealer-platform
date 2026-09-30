/**
 * useToast — toast queue manager.
 *
 * Split out of Toast.jsx so that file exports ONLY components (Toast,
 * ToastContainer): eslint-plugin-react-refresh's only-export-components rule
 * requires component files to be pure refresh boundaries, and a hook export
 * made every Fast Refresh of the toast a full reload. A file with no
 * component exports is not a refresh boundary, so this one is fine.
 */
import { useState } from 'react'

export const useToast = () => {
  const [toasts, setToasts] = useState([])

  const addToast = (toast) => {
    const id = Date.now() + Math.random()
    const newToast = {
      id,
      show: true,
      duration: 5000,
      position: 'top-right',
      type: 'info',
      ...toast
    }

    setToasts(prev => [...prev, newToast])
    return id
  }

  const removeToast = (id) => {
    setToasts(prev => prev.filter(toast => toast.id !== id))
  }

  const success = (message, options = {}) =>
    addToast({ message, type: 'success', ...options })

  const error = (message, options = {}) =>
    addToast({ message, type: 'error', ...options })

  const warning = (message, options = {}) =>
    addToast({ message, type: 'warning', ...options })

  const info = (message, options = {}) =>
    addToast({ message, type: 'info', ...options })

  return {
    toasts,
    addToast,
    removeToast,
    success,
    error,
    warning,
    info
  }
}
