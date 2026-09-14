using NotificationService.DTOs;
using NotificationService.Services;
using Serilog;
using System.Text.Json;

namespace NotificationService.Consumers;

/// <summary>
/// sales.completed (default exchange, routing key = queue name). This is the
/// one Sales event whose payload may carry an in-band DeviceToken — a token in
/// the payload still wins (Issue #37 keeps that contract). When it is absent,
/// the consumer now falls back to the device-token registry keyed by
/// customer:&lt;CustomerId&gt; and fans out via multicast, like the other
/// push-capable consumers. Zero resolvable tokens keeps the honest fallback:
/// log-only, with the exact subject string logged.
/// </summary>
public class SaleCompletedConsumer
{
    private readonly IFcmService _fcmService;
    private readonly IDeviceTokenRegistry _tokens;

    public SaleCompletedConsumer(IFcmService fcmService, IDeviceTokenRegistry tokens)
    {
        _fcmService = fcmService;
        _tokens = tokens;
    }

    public async Task HandleAsync(string message)
    {
        try
        {
            var saleEvent = JsonSerializer.Deserialize<SaleCompletedEvent>(message);
            if (saleEvent == null)
            {
                Log.Warning("Failed to deserialize SaleCompletedEvent from message: {Message}", message);
                return;
            }

            Log.Information("Processing SaleCompletedEvent for Order: {OrderId}", saleEvent.OrderId);

            var title = "🎉 Đơn hàng đã hoàn tất!";
            var body = $"Đơn hàng #{saleEvent.OrderId} của bạn đã được xử lý. Xe {saleEvent.VehicleModel} - Tổng: {saleEvent.TotalPrice:C}. Cảm ơn bạn đã tin tưởng!";
            var data = new Dictionary<string, string>
            {
                { "type", "sale" },
                { "orderId", saleEvent.OrderId },
                { "customerId", saleEvent.CustomerId.ToString() },
                { "vehicleModel", saleEvent.VehicleModel },
                { "totalPrice", saleEvent.TotalPrice.ToString("F2") }
            };

            // Payload token wins (a producer that has one still bypasses the
            // lookup); otherwise resolve out-of-band for the order's customer
            // and fan out to all live devices (Issue #37).
            List<string> deviceTokens;
            string? registrySubject = null;
            if (!string.IsNullOrWhiteSpace(saleEvent.DeviceToken))
            {
                deviceTokens = new List<string> { saleEvent.DeviceToken };
            }
            else
            {
                registrySubject = NotificationSubjects.Customer(saleEvent.CustomerId);
                var registered = await _tokens.GetTokensAsync(registrySubject);
                deviceTokens = registered.Count > 0 ? registered.ToList() : new List<string>();
            }

            if (deviceTokens.Count > 0)
            {
                var result = await _fcmService.SendMulticastAsync(
                    deviceTokens,
                    title,
                    body,
                    data
                );
                // Issue #44: revoke rows FCM rejected permanently. Null key on
                // the payload-token path (no registry row to blame) makes the
                // call a no-op. Best-effort; never throws.
                await _tokens.RevokeDeadTokensAsync(registrySubject, result.DeadTokens);

                if (result.Success)
                {
                    Log.Information("✅ Push notification sent successfully for Order: {OrderId}", saleEvent.OrderId);
                }
                else
                {
                    // The send layer swallows per-token errors and reports
                    // Success=false only when NO device was reached; throwing
                    // here lets the bus retry the delivery and eventually park it
                    // in the DLQ (docs/EVENTS.md failure policy) instead of acking.
                    throw new InvalidOperationException($"FCM push failed for Order {saleEvent.OrderId}");
                }
            }
            else
            {
                // registrySubject is null only on the payload-token path, which
                // by definition had a token — so this branch means the registry
                // lookup ran and found nothing; log the exact subject.
                Log.Information("ℹ️ No device token registered for {Subject}. Notification logged only (no push sent).", registrySubject);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing SaleCompletedEvent");
            // Rethrow: RabbitMQConsumerService decides retry-vs-DLQ from this
            // exception (EventRetryPolicy). Swallowing here used to ack the
            // delivery and lose the notification silently.
            throw;
        }
    }
}
