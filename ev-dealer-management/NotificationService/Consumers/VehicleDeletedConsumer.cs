using NotificationService.DTOs;
using NotificationService.Services;
using Serilog;
using System.Text.Json;

namespace NotificationService.Consumers;

/// <summary>
/// vehicle.deleted (vehicle_events topic exchange). Push-capable via the
/// device-token registry (Issue #38): the payload gained a DealerId (it used
/// to carry only VehicleId/DeletedAt — no audience data at all), so tokens
/// resolve for dealer:&lt;DealerId&gt; exactly like the created/updated
/// consumers. A pre-#38 in-flight message without DealerId deserializes it to
/// 0 → subject dealer:0 exists by construction but nothing registers there →
/// honest log-only fallback rather than a retry→DLQ spiral.
/// </summary>
public class VehicleDeletedConsumer
{
    private readonly IFcmService _fcmService;
    private readonly IDeviceTokenRegistry _tokens;

    public VehicleDeletedConsumer(IFcmService fcmService, IDeviceTokenRegistry tokens)
    {
        _fcmService = fcmService;
        _tokens = tokens;
    }

    public async Task HandleAsync(string message)
    {
        try
        {
            Log.Debug("📥 Received VehicleDeleted event: {Message}", message);

            var vehicleEvent = JsonSerializer.Deserialize<VehicleDeletedEvent>(message);
            if (vehicleEvent == null)
            {
                Log.Warning("⚠️ Failed to deserialize VehicleDeletedEvent from message: {Message}", message);
                return;
            }

            var title = "🗑️ Mẫu xe đã ngừng kinh doanh!";
            var body = $"Xe Id #{vehicleEvent.VehicleId} vừa bị xóa khỏi hệ thống.";
            var data = new Dictionary<string, string>
            {
                { "type", "vehicleDeleted" },
                { "vehicleId", vehicleEvent.VehicleId.ToString() },
                { "dealerId", vehicleEvent.DealerId.ToString() }
            };

            // Always log the notification
            Log.Information("📢 [THÔNG BÁO XÓA XE] {Title} | {Body}", title, body);

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

            Log.Debug("✅ VehicleDeleted event processed successfully for Vehicle: {VehicleId}", vehicleEvent.VehicleId);
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
