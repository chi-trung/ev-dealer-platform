using NotificationService.DTOs;

using Common.Events;
using NotificationService.DTOs;
using NotificationService.Services;
using Serilog;
using System.Text.Json;

namespace NotificationService.Consumers;

/// <summary>
/// customer.created (customer_events topic exchange). Push-capable via the
/// device-token registry (Issue #35): the payload carries no DeviceToken (the
/// producer never has one), so tokens resolve out-of-band for the subject
/// customer:&lt;CustomerId&gt; and fan out to ALL live devices via multicast.
/// Zero registered tokens keeps the honest fallback: log-only. The exact
/// subject string is logged so a NotificationSubjects drift is visible
/// instead of failing silently.
/// </summary>
public class CustomerCreatedConsumer
{
    private readonly IFcmService _fcmService;
    private readonly IDeviceTokenRegistry _tokens;

    public CustomerCreatedConsumer(IFcmService fcmService, IDeviceTokenRegistry tokens)
    {
        _fcmService = fcmService;
        _tokens = tokens;
    }

    public async Task HandleAsync(string message)
    {
        try
        {
            Log.Debug("📥 Received CustomerCreated event: {Message}", message);

            var customerEvent = JsonSerializer.Deserialize<CustomerCreatedEvent>(message);
            if (customerEvent == null)
            {
                Log.Warning("⚠️ Failed to deserialize CustomerCreatedEvent from message: {Message}", message);
                return;
            }

            var title = "👋 Chào mừng khách hàng mới!";
            var body = $"Khách hàng {customerEvent.Name} ({customerEvent.Email}) vừa được tạo (Id #{customerEvent.CustomerId}).";
            var data = new Dictionary<string, string>
            {
                { "type", "customer" },
                { "customerId", customerEvent.CustomerId.ToString() },
            };

            // Always log the notification
            Log.Information("📢 [THÔNG BÁO KHÁCH HÀNG MỚI] {Title} | {Body}", title, body);

            var subject = NotificationSubjects.Customer(customerEvent.CustomerId);
            var registered = await _tokens.GetTokensAsync(subject);
            if (registered.Count > 0)
            {
                var result = await _fcmService.SendMulticastAsync(
                    registered.ToList(), title, body, data);
                // Evict rows FCM rejected permanently BEFORE deciding on the
                // throw: a dead token must leave the mailbox even when this
                // delivery is requeued for other reasons (Issue #44). The call
                // is best-effort and never throws.
                await _tokens.RevokeDeadTokensAsync(subject, result.DeadTokens);
                if (result.Success)
                {
                    Log.Information("✅ Push notification sent successfully for Customer: {CustomerId}", customerEvent.CustomerId);
                }
                else
                {
                    // The send layer swallows per-token errors and reports
                    // Success=false only when NO device was reached; throwing
                    // here lets the bus retry and eventually DLQ the delivery.
                    throw new InvalidOperationException($"FCM push failed for Customer {customerEvent.CustomerId}");
                }
            }
            else
            {
                Log.Information("ℹ️ No device token registered for {Subject}. Notification logged only (no push sent).", subject);
            }

            Log.Debug("✅ CustomerCreated event processed successfully for Customer: {CustomerId}", customerEvent.CustomerId);
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
