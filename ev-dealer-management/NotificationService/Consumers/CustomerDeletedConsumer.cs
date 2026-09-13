using NotificationService.DTOs;
using Serilog;
using System.Text.Json;

namespace NotificationService.Consumers;

/// <summary>
/// customer.deleted (customer_events topic exchange). Log-only today: the
/// producer payload carries no DeviceToken (see CustomerCreatedConsumer).
/// </summary>
public class CustomerDeletedConsumer
{
    public Task HandleAsync(string message)
    {
        try
        {
            Log.Debug("📥 Received CustomerDeleted event: {Message}", message);

            var customerEvent = JsonSerializer.Deserialize<CustomerDeletedEvent>(message);
            if (customerEvent == null)
            {
                Log.Warning("⚠️ Failed to deserialize CustomerDeletedEvent from message: {Message}", message);
                return Task.CompletedTask;
            }

            var title = "🗑️ Khách hàng đã bị xóa!";
            var body = $"Khách hàng Id #{customerEvent.CustomerId} vừa bị xóa khỏi hệ thống.";

            // Always log the notification
            Log.Information("📢 [THÔNG BÁO XÓA KHÁCH HÀNG] {Title} | {Body}", title, body);

            Log.Debug("✅ CustomerDeleted event processed successfully for Customer: {CustomerId}", customerEvent.CustomerId);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Error processing CustomerDeletedEvent");
            // Rethrow: RabbitMQConsumerService decides retry-vs-DLQ from this
            // exception (EventRetryPolicy). Swallowing here used to ack the
            // delivery and lose the notification silently.
            throw;
        }
    }
}
