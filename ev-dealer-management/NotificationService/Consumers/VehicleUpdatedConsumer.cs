using NotificationService.DTOs;
using Serilog;
using System.Text.Json;

namespace NotificationService.Consumers;

/// <summary>
/// vehicle.updated (vehicle_events topic exchange). Log-only today: the
/// producer payload carries no DeviceToken (see VehicleCreatedConsumer).
/// </summary>
public class VehicleUpdatedConsumer
{
    public Task HandleAsync(string message)
    {
        try
        {
            Log.Debug("📥 Received VehicleUpdated event: {Message}", message);

            var vehicleEvent = JsonSerializer.Deserialize<VehicleUpdatedEvent>(message);
            if (vehicleEvent == null)
            {
                Log.Warning("⚠️ Failed to deserialize VehicleUpdatedEvent from message: {Message}", message);
                return Task.CompletedTask;
            }

            var title = "🔄 Thông tin xe đã cập nhật!";
            var body = $"Xe {vehicleEvent.Model} ({vehicleEvent.Type}, Id #{vehicleEvent.VehicleId}) vừa được cập nhật — giá hiện tại {vehicleEvent.Price:N0} VND.";

            // Always log the notification
            Log.Information("📢 [THÔNG BÁO CẬP NHẬT XE] {Title} | {Body}", title, body);
            Log.Information("ℹ️ No device token in VehicleUpdatedEvent payload. Notification logged only (no push sent).");

            Log.Debug("✅ VehicleUpdated event processed successfully for Vehicle: {VehicleId}", vehicleEvent.VehicleId);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Error processing VehicleUpdatedEvent");
            // Rethrow: RabbitMQConsumerService decides retry-vs-DLQ from this
            // exception (EventRetryPolicy). Swallowing here used to ack the
            // delivery and lose the notification silently.
            throw;
        }
    }
}
