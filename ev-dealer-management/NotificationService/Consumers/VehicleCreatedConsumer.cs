using NotificationService.DTOs;
using Serilog;
using System.Text.Json;

namespace NotificationService.Consumers;

/// <summary>
/// vehicle.created (vehicle_events topic exchange). The producer payload
/// carries no DeviceToken, so this is log-only today — same degraded pattern
/// as OrderCreated/QuoteCreated/ContractCreated. When the API starts
/// collecting device tokens, add the FCM push block (throw on
/// success == false so the bus retries; see TestDriveScheduledConsumer).
/// </summary>
public class VehicleCreatedConsumer
{
    public Task HandleAsync(string message)
    {
        try
        {
            Log.Debug("📥 Received VehicleCreated event: {Message}", message);

            var vehicleEvent = JsonSerializer.Deserialize<VehicleCreatedEvent>(message);
            if (vehicleEvent == null)
            {
                Log.Warning("⚠️ Failed to deserialize VehicleCreatedEvent from message: {Message}", message);
                return Task.CompletedTask;
            }

            var title = "🚗 Mẫu xe mới đã về!";
            var body = $"Xe {vehicleEvent.Model} ({vehicleEvent.Type}, {vehicleEvent.Price:N0} VND) vừa được thêm vào kho (Id #{vehicleEvent.VehicleId}).";

            // Always log the notification
            Log.Information("📢 [THÔNG BÁO XE MỚI] {Title} | {Body}", title, body);
            Log.Information("ℹ️ No device token in VehicleCreatedEvent payload. Notification logged only (no push sent).");

            Log.Debug("✅ VehicleCreated event processed successfully for Vehicle: {VehicleId}", vehicleEvent.VehicleId);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Error processing VehicleCreatedEvent");
            // Rethrow: RabbitMQConsumerService decides retry-vs-DLQ from this
            // exception (EventRetryPolicy). Swallowing here used to ack the
            // delivery and lose the notification silently.
            throw;
        }
    }
}
