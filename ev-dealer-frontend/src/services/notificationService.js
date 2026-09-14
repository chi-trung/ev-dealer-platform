import api from './api'
import { mockNotifications, mockNotificationStats } from '../data/mockNotifications'

class NotificationService {
  // ============ NotificationService Backend API Methods ============
  
  // Test send email
  async sendTestEmail(to, subject, htmlContent) {
    try {
      const response = await api.post('/notifications/test-email', {
        to,
        subject,
        htmlContent
      })
      return response.data
    } catch (error) {
      console.error('Error sending test email:', error)
      throw error
    }
  }

  // Send order confirmation email
  async sendOrderConfirmation(orderData) {
    try {
      const response = await api.post('/notifications/order-confirmation', orderData)
      return response.data
    } catch (error) {
      console.error('Error sending order confirmation:', error)
      throw error
    }
  }

  // Send reservation confirmation SMS
  async sendReservationConfirmation(reservationData) {
    try {
      const response = await api.post('/notifications/reservation-confirmation', reservationData)
      return response.data
    } catch (error) {
      console.error('Error sending reservation confirmation:', error)
      throw error
    }
  }

  // Send test drive confirmation email
  async sendTestDriveConfirmation(testDriveData) {
    try {
      const response = await api.post('/notifications/test-drive-confirmation', testDriveData)
      return response.data
    } catch (error) {
      console.error('Error sending test drive confirmation:', error)
      throw error
    }
  }

  // Test send SMS
  async sendTestSms(phoneNumber, message) {
    try {
      const response = await api.post('/notifications/test-sms', {
        phoneNumber,
        message
      })
      return response.data
    } catch (error) {
      console.error('Error sending test SMS:', error)
      throw error
    }
  }

  // Check NotificationService health
  async checkHealth() {
    try {
      const response = await api.get('/notifications/health')
      return response.data
    } catch (error) {
      console.error('Error checking notification service health:', error)
      throw error
    }
  }

  // ============ Legacy Notification UI Methods ============
  
  // Get all notifications
  async getNotifications() {
    try {
      // Read from localStorage (Firebase notifications)
      const STORAGE_KEY = 'firebase_notifications';
      const stored = localStorage.getItem(STORAGE_KEY);
      const notifications = stored ? JSON.parse(stored) : [];
      
      // If no Firebase notifications, return mockdata
      const data = notifications.length > 0 ? notifications : mockNotifications;
      
      return new Promise((resolve) => {
        setTimeout(() => {
          resolve({
            data: data,
            success: true
          })
        }, 300)
      })
    } catch (error) {
      console.error('Error fetching notifications:', error)
      throw error
    }
  }

  // Get notification stats
  async getNotificationStats() {
    try {
      // Calculate stats from localStorage
      const STORAGE_KEY = 'firebase_notifications';
      const stored = localStorage.getItem(STORAGE_KEY);
      const notifications = stored ? JSON.parse(stored) : [];
      
      if (notifications.length === 0) {
        // Return mockdata if no Firebase notifications
        return new Promise((resolve) => {
          setTimeout(() => {
            resolve({
              data: mockNotificationStats,
              success: true
            })
          }, 300)
        })
      }
      
      // Calculate real stats
      const stats = {
        total: notifications.length,
        unread: notifications.filter(n => !n.isRead).length,
        byType: {
          orders: notifications.filter(n => n.type === 'orders').length,
          deliveries: notifications.filter(n => n.type === 'deliveries').length,
          payments: notifications.filter(n => n.type === 'payments').length,
          system: notifications.filter(n => n.type === 'system').length
        }
      };
      
      return new Promise((resolve) => {
        setTimeout(() => {
          resolve({
            data: stats,
            success: true
          })
        }, 300)
      })
    } catch (error) {
      console.error('Error fetching notification stats:', error)
      throw error
    }
  }

  // Mark notification as read/unread
  async markAsRead(notificationId, isRead = true) {
    try {
      // Update in localStorage
      const STORAGE_KEY = 'firebase_notifications';
      const stored = localStorage.getItem(STORAGE_KEY);
      const notifications = stored ? JSON.parse(stored) : [];
      
      const updated = notifications.map(n => 
        n.id === notificationId ? { ...n, isRead } : n
      );
      
      localStorage.setItem(STORAGE_KEY, JSON.stringify(updated));
      
      return new Promise((resolve) => {
        setTimeout(() => {
          resolve({
            data: { id: notificationId, isRead },
            success: true,
            message: `Notification marked as ${isRead ? 'read' : 'unread'}`
          })
        }, 200)
      })
    } catch (error) {
      console.error('Error updating notification:', error)
      throw error
    }
  }

  // Mark all notifications as read
  async markAllAsRead() {
    try {
      // Update all in localStorage
      const STORAGE_KEY = 'firebase_notifications';
      const stored = localStorage.getItem(STORAGE_KEY);
      const notifications = stored ? JSON.parse(stored) : [];
      
      const updated = notifications.map(n => ({ ...n, isRead: true }));
      
      localStorage.setItem(STORAGE_KEY, JSON.stringify(updated));
      
      return new Promise((resolve) => {
        setTimeout(() => {
          resolve({
            success: true,
            message: 'All notifications marked as read'
          })
        }, 300)
      })
    } catch (error) {
      console.error('Error marking all notifications as read:', error)
      throw error
    }
  }

  // Delete notification
  async deleteNotification(notificationId) {
    try {
      // Remove from localStorage
      const STORAGE_KEY = 'firebase_notifications';
      const stored = localStorage.getItem(STORAGE_KEY);
      const notifications = stored ? JSON.parse(stored) : [];
      
      const filtered = notifications.filter(n => n.id !== notificationId);
      
      localStorage.setItem(STORAGE_KEY, JSON.stringify(filtered));
      
      return new Promise((resolve) => {
        setTimeout(() => {
          resolve({
            success: true,
            message: 'Notification deleted successfully'
          })
        }, 200)
      })
    } catch (error) {
      console.error('Error deleting notification:', error)
      throw error
    }
  }

  // Get notification preferences (Issue #51: real API — the old setTimeout
  // mock resolved the page's "saved" state was a lie; GET returns what PUT
  // stored server-side, or shared defaults before the first save. The axios
  // response interceptor already unwraps one layer, so this resolves
  // { data, success } exactly like the mock did.)
  async getNotificationPreferences() {
    try {
      return await api.get('/notifications/preferences')
    } catch (error) {
      console.error('Error fetching notification preferences:', error)
      throw error
    }
  }

  // Update notification preferences (Issue #51: real API; the server
  // rejects a document missing ANY of the eight flags with 400 — off means
  // muted, so a silent default would mute settings the user never touched)
  async updateNotificationPreferences(preferences) {
    try {
      return await api.put('/notifications/preferences', preferences)
    } catch (error) {
      console.error('Error updating notification preferences:', error)
      throw error
    }
  }
}

export default new NotificationService()
