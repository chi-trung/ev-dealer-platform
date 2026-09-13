using NotificationService.DTOs;
using Serilog;
using System.Text.Json;

namespace NotificationService.Consumers;

/// <summary>
/// vehicle.deleted (vehicle_events topic exchange). Log-only today: the
/// producer payload carries no DeviceToken (see VehicleCreatedConsumer).
/// </summary>
public class VehicleDeletedConsumer
{
    public Task HandleAsync(string message)
    {
        try
        {
            Log.Debug("📥 Received VehicleDeleted event: {Message}", message);

            var vehicleEvent = JsonSerializer.Deserialize<VehicleDeletedEvent>(message);
            if (vehicleEvent == null)
            {
                Log.Warning("⚠️ Failed to deserialize VehicleDeletedEvent from message: {Message}", message);
                return Task.CompletedTask;
            }

            var title = "🗑️ Mẫu xe đã ngừng kinh doanh!";
            var body = $"Xe Id #{vehicleEvent.VehicleId} vừa bị xóa khỏi hệ thống.";

            // Always log the notification
            Log.Information("📢 [THÔNG BÁO XÓA XE] {Title} | {Body}", title, body);

            Log.Debug("✅ VehicleDeleted event processed successfully for Vehicle: {VehicleId}", vehicleEvent.VehicleId);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Error processing VehicleDeletedEvent");
            // Rethrow: RabbitMQConsumerService decides retry-vs-DLQ from this
            // exception (EventRetryPolicy). Swallowing here used to ack the
            // delivery and lose the notification silently.
            throw;
        }
    }
}
