/**
 * Notification type filter options (label + icon for the filter chips).
 *
 * Moved out of data/mockNotifications.js: that file held the invented
 * notification rows this app no longer serves, and these labels are real
 * config, not sample data — they had to survive the file's deletion.
 *
 * The `value`s must match the type tags the backend stores on a
 * notification (see getNotificationStats' byType buckets).
 */
export const notificationTypes = [
  { value: "all", label: "Tất cả", icon: "🔔" },
  { value: "orders", label: "Đơn hàng", icon: "📦" },
  { value: "deliveries", label: "Giao hàng", icon: "🚚" },
  { value: "payments", label: "Thanh toán", icon: "💰" },
  { value: "system", label: "Hệ thống", icon: "⚙️" }
];
