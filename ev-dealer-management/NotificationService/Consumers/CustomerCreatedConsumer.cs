using NotificationService.DTOs;
using Serilog;
using System.Text.Json;

namespace NotificationService.Consumers;

/// <summary>
/// customer.created (customer_events topic exchange). The producer payload
/// carries no DeviceToken, so this is log-only today — same degraded pattern
/// as OrderCreated/QuoteCreated/ContractCreated. When the booking API starts
/// collecting device tokens, add the FCM push block (throw on
/// success == false so the bus retries; see TestDriveScheduledConsumer).
/// </summary>
public class CustomerCreatedConsumer
{
    public Task HandleAsync(string message)
    {
        try
        {
            Log.Debug("📥 Received CustomerCreated event: {Message}", message);

            var customerEvent = JsonSerializer.Deserialize<CustomerCreatedEvent>(message);
            if (customerEvent == null)
            {
                Log.Warning("⚠️ Failed to deserialize CustomerCreatedEvent from message: {Message}", message);
                return Task.CompletedTask;
            }

            var title = "👋 Chào mừng khách hàng mới!";
            var body = $"Khách hàng {customerEvent.Name} ({customerEvent.Email}) vừa được tạo (Id #{customerEvent.CustomerId}).";

            // Always log the notification
            Log.Information("📢 [THÔNG BÁO KHÁCH HÀNG MỚI] {Title} | {Body}", title, body);
            Log.Information("ℹ️ No device token in CustomerCreatedEvent payload. Notification logged only (no push sent).");

            Log.Debug("✅ CustomerCreated event processed successfully for Customer: {CustomerId}", customerEvent.CustomerId);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Error processing CustomerCreatedEvent");
            // Rethrow: RabbitMQConsumerService decides retry-vs-DLQ from this
            // exception (EventRetryPolicy). Swallowing here used to ack the
            // delivery and lose the notification silently.
            throw;
        }
    }
}
