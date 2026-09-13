using NotificationService.DTOs;
using Serilog;
using System.Text.Json;

namespace NotificationService.Consumers;

/// <summary>
/// order.status.changed (default exchange, routing key = queue name). Log-only
/// today: the producer payload carries no DeviceToken (see
/// PaymentReceivedConsumer).
/// </summary>
public class OrderStatusChangedConsumer
{
    public Task HandleAsync(string message)
    {
        try
        {
            Log.Debug("📥 Received OrderStatusChanged event: {Message}", message);

            var statusEvent = JsonSerializer.Deserialize<OrderStatusChangedEvent>(message);
            if (statusEvent == null)
            {
                Log.Warning("⚠️ Failed to deserialize OrderStatusChangedEvent from message: {Message}", message);
                return Task.CompletedTask;
            }

            var title = "🔄 Trạng thái đơn hàng đã thay đổi!";
            var body = $"Đơn hàng #{statusEvent.OrderNumber} đã chuyển từ '{statusEvent.OldStatus}' sang '{statusEvent.NewStatus}'.";

            // Always log the notification
            Log.Information("📢 [THÔNG BÁO TRẠNG THÁI ĐƠN HÀNG] {Title} | {Body}", title, body);
            Log.Information("ℹ️ No device token in OrderStatusChangedEvent payload. Notification logged only (no push sent).");

            Log.Debug("✅ OrderStatusChanged event processed successfully for Order: {OrderNumber}", statusEvent.OrderNumber);
            return Task.CompletedTask;
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
