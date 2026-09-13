using NotificationService.DTOs;
using Serilog;
using System.Text.Json;

namespace NotificationService.Consumers;

/// <summary>
/// customer.updated (customer_events topic exchange). Log-only today: the
/// producer payload carries no DeviceToken (see CustomerCreatedConsumer).
/// </summary>
public class CustomerUpdatedConsumer
{
    public Task HandleAsync(string message)
    {
        try
        {
            Log.Debug("📥 Received CustomerUpdated event: {Message}", message);

            var customerEvent = JsonSerializer.Deserialize<CustomerUpdatedEvent>(message);
            if (customerEvent == null)
            {
                Log.Warning("⚠️ Failed to deserialize CustomerUpdatedEvent from message: {Message}", message);
                return Task.CompletedTask;
            }

            var title = "🔄 Thông tin khách hàng đã cập nhật!";
            var body = $"Khách hàng {customerEvent.Name} ({customerEvent.Email}, Id #{customerEvent.CustomerId}) vừa được cập nhật.";

            // Always log the notification
            Log.Information("📢 [THÔNG BÁO CẬP NHẬT KHÁCH HÀNG] {Title} | {Body}", title, body);
            Log.Information("ℹ️ No device token in CustomerUpdatedEvent payload. Notification logged only (no push sent).");

            Log.Debug("✅ CustomerUpdated event processed successfully for Customer: {CustomerId}", customerEvent.CustomerId);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Error processing CustomerUpdatedEvent");
            // Rethrow: RabbitMQConsumerService decides retry-vs-DLQ from this
            // exception (EventRetryPolicy). Swallowing here used to ack the
            // delivery and lose the notification silently.
            throw;
        }
    }
}
