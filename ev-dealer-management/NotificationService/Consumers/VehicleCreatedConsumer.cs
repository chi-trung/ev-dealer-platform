using NotificationService.DTOs;
using NotificationService.Services;
using Serilog;
using System.Text.Json;

namespace NotificationService.Consumers;

/// <summary>
/// vehicle.created (vehicle_events topic exchange). Push-capable via the
/// device-token registry (Issue #38): the payload carries no DeviceToken, so
/// tokens resolve out-of-band for the subject dealer:&lt;DealerId&gt; — the
/// vehicle's owning dealer's staff devices — and fan out to ALL live tokens
/// via multicast. Zero registered tokens keeps the honest fallback: log-only,
/// with the exact subject string logged so a NotificationSubjects drift is
/// visible instead of failing silently. A push failure throws so the bus
/// retries and eventually DLQs the delivery (EventRetryPolicy).
/// </summary>
public class VehicleCreatedConsumer
{
    private readonly IFcmService _fcmService;
    private readonly IDeviceTokenRegistry _tokens;

    public VehicleCreatedConsumer(IFcmService fcmService, IDeviceTokenRegistry tokens)
    {
        _fcmService = fcmService;
        _tokens = tokens;
    }

    public async Task HandleAsync(string message)
    {
        try
        {
            Log.Debug("📥 Received VehicleCreated event: {Message}", message);

            var vehicleEvent = JsonSerializer.Deserialize<VehicleCreatedEvent>(message);
            if (vehicleEvent == null)
            {
                Log.Warning("⚠️ Failed to deserialize VehicleCreatedEvent from message: {Message}", message);
                return;
            }

            var title = "🚗 Mẫu xe mới đã về!";
            var body = $"Xe {vehicleEvent.Model} ({vehicleEvent.Type}, {vehicleEvent.Price:N0} VND) vừa được thêm vào kho (Id #{vehicleEvent.VehicleId}).";
            var data = new Dictionary<string, string>
            {
                { "type", "vehicleCreated" },
                { "vehicleId", vehicleEvent.VehicleId.ToString() },
                { "dealerId", vehicleEvent.DealerId.ToString() },
                { "model", vehicleEvent.Model },
                { "price", vehicleEvent.Price.ToString() }
            };

            // Always log the notification
            Log.Information("📢 [THÔNG BÁO XE MỚI] {Title} | {Body}", title, body);

            var subject = NotificationSubjects.Dealer(vehicleEvent.DealerId);
            var registered = await _tokens.GetTokensAsync(subject);
            if (registered.Count > 0)
            {
                var success = await _fcmService.SendMulticastAsync(
                    registered.ToList(), title, body, data);
                if (success)
                {
                    Log.Information("✅ Push notification sent successfully for Vehicle: {VehicleId}", vehicleEvent.VehicleId);
                }
                else
                {
                    // IFcmService swallows send errors and returns false; throwing
                    // here lets the bus retry and eventually DLQ the delivery.
                    throw new InvalidOperationException($"FCM push failed for Vehicle {vehicleEvent.VehicleId}");
                }
            }
            else
            {
                Log.Information("ℹ️ No device token registered for {Subject}. Notification logged only (no push sent).", subject);
            }

            Log.Debug("✅ VehicleCreated event processed successfully for Vehicle: {VehicleId}", vehicleEvent.VehicleId);
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
