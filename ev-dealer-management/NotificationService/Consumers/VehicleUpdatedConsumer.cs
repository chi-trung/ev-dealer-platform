using NotificationService.DTOs;
using NotificationService.Services;
using Serilog;
using System.Text.Json;

namespace NotificationService.Consumers;

/// <summary>
/// vehicle.updated (vehicle_events topic exchange). Push-capable via the
/// device-token registry (Issue #38): same dealer-subject fan-out as
/// VehicleCreatedConsumer — tokens resolve out-of-band for
/// dealer:&lt;DealerId&gt;, zero tokens degrades to log-only with the exact
/// subject logged, and a failed push throws for the retry/DLQ policy.
/// </summary>
public class VehicleUpdatedConsumer
{
    private readonly IFcmService _fcmService;
    private readonly IDeviceTokenRegistry _tokens;

    public VehicleUpdatedConsumer(IFcmService fcmService, IDeviceTokenRegistry tokens)
    {
        _fcmService = fcmService;
        _tokens = tokens;
    }

    public async Task HandleAsync(string message)
    {
        try
        {
            Log.Debug("📥 Received VehicleUpdated event: {Message}", message);

            var vehicleEvent = JsonSerializer.Deserialize<VehicleUpdatedEvent>(message);
            if (vehicleEvent == null)
            {
                Log.Warning("⚠️ Failed to deserialize VehicleUpdatedEvent from message: {Message}", message);
                return;
            }

            var title = "🔄 Thông tin xe đã cập nhật!";
            var body = $"Xe {vehicleEvent.Model} ({vehicleEvent.Type}, Id #{vehicleEvent.VehicleId}) vừa được cập nhật — giá hiện tại {vehicleEvent.Price:N0} VND.";
            var data = new Dictionary<string, string>
            {
                { "type", "vehicleUpdated" },
                { "vehicleId", vehicleEvent.VehicleId.ToString() },
                { "dealerId", vehicleEvent.DealerId.ToString() },
                { "model", vehicleEvent.Model },
                { "price", vehicleEvent.Price.ToString() }
            };

            // Always log the notification
            Log.Information("📢 [THÔNG BÁO CẬP NHẬT XE] {Title} | {Body}", title, body);

            var subject = NotificationSubjects.Dealer(vehicleEvent.DealerId);
            var registered = await _tokens.GetTokensAsync(subject);
            if (registered.Count > 0)
            {
                var result = await _fcmService.SendMulticastAsync(
                    registered.ToList(), title, body, data);
                // Evict rows FCM rejected permanently BEFORE deciding on the
                // throw (Issue #44); best-effort, never throws. This is the
                // dealer: shared-subject path the cap wedge bit hardest.
                await _tokens.RevokeDeadTokensAsync(subject, result.DeadTokens);
                if (result.Success)
                {
                    Log.Information("✅ Push notification sent successfully for Vehicle: {VehicleId}", vehicleEvent.VehicleId);
                }
                else
                {
                    // The send layer swallows per-token errors and reports
                    // Success=false only when NO device was reached; throwing
                    // here lets the bus retry and eventually DLQ the delivery.
                    throw new InvalidOperationException($"FCM push failed for Vehicle {vehicleEvent.VehicleId}");
                }
            }
            else
            {
                Log.Information("ℹ️ No device token registered for {Subject}. Notification logged only (no push sent).", subject);
            }

            Log.Debug("✅ VehicleUpdated event processed successfully for Vehicle: {VehicleId}", vehicleEvent.VehicleId);
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
