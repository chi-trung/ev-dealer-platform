import { requestNotificationPermission, onMessageListener, getCurrentToken } from './messaging';
import api from '../services/api';

/**
 * Notification Service
 * Handles all Firebase Cloud Messaging operations
 */

const DEVICE_TOKEN_KEY = 'fcm_device_token';

/**
 * Issue #36: register the minted FCM token with the NotificationService
 * device-token registry under the LOGGED-IN staff user's subjects, so push
 * consumers can find this device. The registry (PUT /api/DeviceTokens/{key})
 * is JWT-gated and accepts only the caller's own subjects:
 *   user:<id>     always (the account's own mailbox)
 *   dealer:<n>    only when the account has a DealerId (the JWT carries the
 *                 matching "dealer" claim minted at login)
 * Fire-and-forget: a failed registration must never break the app boot or
 * login flow — pushes are a bonus channel, not a product dependency.
 * Returns the subjects it tried (for tests/debugging), or [] when nobody is
 * logged in or there is no token.
 */
export const registerDeviceTokenWithBackend = async (token) => {
  if (!token) return [];
  const jwt = localStorage.getItem('token');
  let user = null;
  try {
    user = JSON.parse(localStorage.getItem('user') || 'null');
  } catch {
    user = null;
  }
  if (!jwt || !user) return []; // not logged in — no subject to register under

  // authService stores the /auth/login UserDto (camelCase), but tolerate the
  // PascalCase shape the same code already tolerates for the login response.
  const userId = user.id ?? user.Id;
  // Mirror the SERVER contract (AuthorizeSubject does int.TryParse on the id
  // claim): a non-numeric id can never own a subject, so don't fire a request
  // the controller provably rejects — notably ProtectedRoute's DEV-mode mock
  // (id 'dev-user-1'), whose fake bearer would 401 and trip api.js's
  // session-wipe redirect on every boot of the login-free dev flow.
  // (/^\d+$/ also rejects null/undefined via String(), and rejects 0-padded
  // or negative spellings int.TryParse would accept — harmless either way,
  // the server owns the final decision; this only prunes doomed traffic.)
  if (!/^\d+$/.test(String(userId))) return [];

  const subjects = [`user:${userId}`];
  const dealerId = user.dealerId ?? user.DealerId;
  if (dealerId && /^\d+$/.test(String(dealerId))) subjects.push(`dealer:${dealerId}`);

  for (const subject of subjects) {
    try {
      await api.put(`/DeviceTokens/${subject}`, { token });
      console.log(`🔑 Device token registered for ${subject}`);
    } catch (error) {
      // 401 here also trips api.js's session-expiry handling; swallow
      // everything else (offline gateway, 403 after a role change, 409 cap).
      console.warn(`⚠️ Device token registration failed for ${subject}:`, error.message);
    }
  }
  return subjects;
};

/**
 * Initialize notifications (request permission and save token)
 * Call this when app loads
 */
export const initializeNotifications = async () => {
  try {
    console.log('🔔 Initializing notifications...');
    
    // Check if already have permission
    if (Notification.permission === 'granted') {
      const token = await getCurrentToken();
      if (token) {
        localStorage.setItem(DEVICE_TOKEN_KEY, token);
        console.log('✅ Device token restored from Firebase');
        // App-load path (Issue #36): a returning logged-in session re-registers
        // its device — the registry upsert is idempotent, so refreshing on
        // every load keeps UpdatedAt honest across the 8h JWT lifetime.
        // No-ops when nobody is logged in.
        registerDeviceTokenWithBackend(token); // fire-and-forget
        return token;
      }
    }

    // Request permission and get new token
    const token = await requestNotificationPermission();

    if (token) {
      // Save token to localStorage
      localStorage.setItem(DEVICE_TOKEN_KEY, token);
      console.log('✅ Device token saved to localStorage');

      // Issue #36: send the token to the device-token registry for the
      // logged-in account's subjects (no-op pre-login; login() re-registers).
      registerDeviceTokenWithBackend(token); // fire-and-forget

      return token;
    } else {
      console.warn('⚠️ Failed to get device token');
      return null;
    }
  } catch (error) {
    console.error('❌ Error initializing notifications:', error);
    return null;
  }
};

/**
 * Get device token from localStorage
 */
export const getDeviceToken = () => {
  return localStorage.getItem(DEVICE_TOKEN_KEY);
};

/**
 * Clear device token from localStorage
 */
export const clearDeviceToken = () => {
  localStorage.removeItem(DEVICE_TOKEN_KEY);
};

/**
 * Save notification to localStorage
 */
const saveNotificationToStorage = (notification) => {
  try {
    const STORAGE_KEY = 'firebase_notifications';
    const stored = localStorage.getItem(STORAGE_KEY);
    const notifications = stored ? JSON.parse(stored) : [];
    
    // Add new notification with unique ID
    const newNotification = {
      id: `fcm_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`,
      title: notification.title,
      message: notification.body,
      type: notification.data?.type || 'system',
      isRead: false,
      createdAt: notification.timestamp.toISOString(),
      data: notification.data
    };
    
    // Add to beginning of array (newest first)
    notifications.unshift(newNotification);
    
    // Keep only last 100 notifications
    const trimmed = notifications.slice(0, 100);
    
    localStorage.setItem(STORAGE_KEY, JSON.stringify(trimmed));
    console.log('💾 Notification saved to localStorage:', newNotification);
    
    return newNotification;
  } catch (error) {
    console.error('❌ Error saving notification to localStorage:', error);
    return null;
  }
};

/**
 * Setup foreground message listener
 * @param {Function} onNotification - Callback when notification received
 */
export const setupNotificationListener = (onNotification) => {
  return onMessageListener((payload) => {
    console.log('📬 New notification:', payload);
    
    // Extract notification data
    const notification = {
      title: payload.notification?.title || 'New Notification',
      body: payload.notification?.body || '',
      data: payload.data || {},
      timestamp: new Date()
    };

    // Save to localStorage
    const savedNotification = saveNotificationToStorage(notification);

    // Call callback
    if (onNotification && typeof onNotification === 'function') {
      onNotification(notification);
    }

    // Show browser notification if supported
    if ('Notification' in window && Notification.permission === 'granted') {
      new Notification(notification.title, {
        body: notification.body,
        icon: '/logo.png',
        badge: '/badge.png',
        tag: notification.data.type || 'default',
        requireInteraction: false,
        vibrate: [200, 100, 200]
      });
    }
    
    // Dispatch custom event for real-time UI updates
    if (savedNotification) {
      window.dispatchEvent(new CustomEvent('firebase-notification-received', { 
        detail: savedNotification 
      }));
    }
  });
};

/**
 * Check if notifications are supported
 */
export const isNotificationSupported = () => {
  return 'Notification' in window && 'serviceWorker' in navigator;
};

/**
 * Check if permission is granted
 */
export const isPermissionGranted = () => {
  return Notification.permission === 'granted';
};

export default {
  initializeNotifications,
  getDeviceToken,
  clearDeviceToken,
  setupNotificationListener,
  isNotificationSupported,
  isPermissionGranted,
  registerDeviceTokenWithBackend
};
