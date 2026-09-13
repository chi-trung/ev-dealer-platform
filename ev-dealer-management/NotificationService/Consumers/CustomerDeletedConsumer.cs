using NotificationService.DTOs;
using NotificationService.Services;
using Serilog;
using System.Text.Json;

namespace NotificationService.Consumers;

/// <summary>
/// customer.deleted (customer_events topic exchange). Push-capable via the
/// device-token registry (Issue #35) — see CustomerCreatedConsumer for the
/// resolution contract. The payload carries only CustomerId (no name/email),
/// which is exactly what the subject customer:&lt;CustomerId&gt; needs. Note the
/// registry row is NOT revoked by deletion — the device keeps receiving its
/// own account's lifecycle notices; a later id reuse would also receive them
/// (pre-existing int-key property of the registry, unchanged here).
/// </summary>
public class CustomerDeletedConsumer
{
    private readonly IFcmService _fcmService;
    private readonly IDeviceTokenRegistry _tokens;

    public CustomerDeletedConsumer(IFcmService fcmService, IDeviceTokenRegistry tokens)
    {
        _fcmService = fcmService;
        _tokens = tokens;
    }

    public async Task HandleAsync(string message)
    {
        try
        {
            Log.Debug("📥 Received CustomerDeleted event: {Message}", message);

            var customerEvent = JsonSerializer.Deserialize<CustomerDeletedEvent>(message);
            if (customerEvent == null)
            {
                Log.Warning("⚠️ Failed to deserialize CustomerDeletedEvent from message: {Message}", message);
                return;
            }

            var title = "🗑️ Xóa khách hàng";
            var body = $"Khách hàng Id #{customerEvent.CustomerId} vừa bị xóa khỏi hệ thống.";
            var data = new Dictionary<string, string>
            {
                { "type", "customer" },
                { "customerId", customerEvent.CustomerId.ToString() },
            };

            // Always log the notification
            Log.Information("📢 [THÔNG BÁO XÓA KHÁCH HÀNG] {Title} | {Body}", title, body);

            var subject = NotificationSubjects.Customer(customerEvent.CustomerId);
            var registered = await _tokens.GetTokensAsync(subject);
            if (registered.Count > 0)
            {
                var success = await _fcmService.SendMulticastAsync(
                    registered.ToList(), title, body, data);
                if (success)
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

            Log.Debug("✅ CustomerDeleted event processed successfully for Customer: {CustomerId}", customerEvent.CustomerId);
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
