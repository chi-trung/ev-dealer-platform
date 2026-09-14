using NotificationService.DTOs;
using NotificationService.Services;
using Serilog;
using System.Text.Json;

namespace NotificationService.Consumers;

/// <summary>
/// order.status.changed (default exchange, routing key = queue name).
/// Push-capable via the device-token registry (Issue #37): the payload carries
/// no DeviceToken (the producer never has one), so tokens resolve out-of-band
/// for the subject customer:&lt;CustomerId&gt; and fan out to ALL live devices
/// via multicast. Zero registered tokens keeps the honest fallback: log-only,
/// with the exact subject string logged so a NotificationSubjects drift is
/// visible instead of failing silently. A push failure throws so the bus
/// retries and eventually DLQs the delivery (EventRetryPolicy).
/// </summary>
public class OrderStatusChangedConsumer
{
    private readonly IFcmService _fcmService;
    private readonly IDeviceTokenRegistry _tokens;

    public OrderStatusChangedConsumer(IFcmService fcmService, IDeviceTokenRegistry tokens)
    {
        _fcmService = fcmService;
        _tokens = tokens;
    }

    public async Task HandleAsync(string message)
    {
        try
        {
            Log.Debug("📥 Received OrderStatusChanged event: {Message}", message);

            var statusEvent = JsonSerializer.Deserialize<OrderStatusChangedEvent>(message);
            if (statusEvent == null)
            {
                Log.Warning("⚠️ Failed to deserialize OrderStatusChangedEvent from message: {Message}", message);
                return;
            }

            var title = "🔄 Trạng thái đơn hàng đã thay đổi!";
            var body = $"Đơn hàng #{statusEvent.OrderNumber} đã chuyển từ '{statusEvent.OldStatus}' sang '{statusEvent.NewStatus}'.";
            var data = new Dictionary<string, string>
            {
                { "type", "orderStatus" },
                { "orderId", statusEvent.OrderId },
                { "orderNumber", statusEvent.OrderNumber },
                { "customerId", statusEvent.CustomerId.ToString() },
                { "oldStatus", statusEvent.OldStatus },
                { "newStatus", statusEvent.NewStatus }
            };

            // Always log the notification
            Log.Information("📢 [THÔNG BÁO TRẠNG THÁI ĐƠN HÀNG] {Title} | {Body}", title, body);

            var subject = NotificationSubjects.Customer(statusEvent.CustomerId);
            var registered = await _tokens.GetTokensAsync(subject);
            if (registered.Count > 0)
            {
                var result = await _fcmService.SendMulticastAsync(
                    registered.ToList(), title, body, data);
                // Evict rows FCM rejected permanently BEFORE deciding on the
                // throw (Issue #44); best-effort, never throws.
                await _tokens.RevokeDeadTokensAsync(subject, result.DeadTokens);
                if (result.Success)
                {
                    Log.Information("✅ Push notification sent successfully for Order: {OrderNumber}", statusEvent.OrderNumber);
                }
                else
                {
                    // The send layer swallows per-token errors and reports
                    // Success=false only when NO device was reached; throwing
                    // here lets the bus retry and eventually DLQ the delivery.
                    throw new InvalidOperationException($"FCM push failed for Order {statusEvent.OrderNumber}");
                }
            }
            else
            {
                Log.Information("ℹ️ No device token registered for {Subject}. Notification logged only (no push sent).", subject);
            }

            Log.Debug("✅ OrderStatusChanged event processed successfully for Order: {OrderNumber}", statusEvent.OrderNumber);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Error processing OrderStatusChangedEvent");
            // Rethrow: RabbitMQConsumerService decides retry-vs-DLQ from this
            // exception (EventRetryPolicy). Swallowing here used to ack the
            // delivery and lose the notification silently.
            throw;
        }
    }
}
