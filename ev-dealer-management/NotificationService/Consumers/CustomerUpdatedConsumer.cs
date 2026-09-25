using NotificationService.DTOs;

using Common.Events;
using NotificationService.Services;
using Serilog;
using System.Text.Json;

namespace NotificationService.Consumers;

/// <summary>
/// customer.updated (customer_events topic exchange). Push-capable via the
/// device-token registry (Issue #35) — see CustomerCreatedConsumer for the
/// resolution contract (registry subject customer:&lt;CustomerId&gt;, multicast,
/// log-only + logged subject when nothing is registered).
/// </summary>
public class CustomerUpdatedConsumer
{
    private readonly IFcmService _fcmService;
    private readonly IDeviceTokenRegistry _tokens;

    public CustomerUpdatedConsumer(IFcmService fcmService, IDeviceTokenRegistry tokens)
    {
        _fcmService = fcmService;
        _tokens = tokens;
    }

    public async Task HandleAsync(string message)
    {
        try
        {
            Log.Debug("📥 Received CustomerUpdated event: {Message}", message);

            var customerEvent = JsonSerializer.Deserialize<CustomerUpdatedEvent>(message);
            if (customerEvent == null)
            {
                Log.Warning("⚠️ Failed to deserialize CustomerUpdatedEvent from message: {Message}", message);
                return;
            }

            var title = "🔄 Thông tin khách hàng đã cập nhật!";
            var body = $"Khách hàng {customerEvent.Name} ({customerEvent.Email}, Id #{customerEvent.CustomerId}) vừa được cập nhật.";
            var data = new Dictionary<string, string>
            {
                { "type", "customer" },
                { "customerId", customerEvent.CustomerId.ToString() },
            };

            // Always log the notification
            Log.Information("📢 [THÔNG BÁO CẬP NHẬT KHÁCH HÀNG] {Title} | {Body}", title, body);

            var subject = NotificationSubjects.Customer(customerEvent.CustomerId);
            var registered = await _tokens.GetTokensAsync(subject);
            if (registered.Count > 0)
            {
                var result = await _fcmService.SendMulticastAsync(
                    registered.ToList(), title, body, data);
                // Evict rows FCM rejected permanently BEFORE deciding on the
                // throw (Issue #44); best-effort, never throws.
                await _tokens.RevokeDeadTokensAsync(subject, result.DeadTokens);
                if (result.Success)
                {
                    Log.Information("✅ Push notification sent successfully for Customer: {CustomerId}", customerEvent.CustomerId);
                }
                else
                {
                    throw new InvalidOperationException($"FCM push failed for Customer {customerEvent.CustomerId}");
                }
            }
            else
            {
                Log.Information("ℹ️ No device token registered for {Subject}. Notification logged only (no push sent).", subject);
            }

            Log.Debug("✅ CustomerUpdated event processed successfully for Customer: {CustomerId}", customerEvent.CustomerId);
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
