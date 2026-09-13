using NotificationService.DTOs;
using NotificationService.Services;
using Serilog;
using System.Text.Json;

namespace NotificationService.Consumers;

public class QuoteCreatedConsumer
{
    private readonly IFcmService _fcmService;
    private readonly IDeviceTokenRegistry _tokens;

    public QuoteCreatedConsumer(IFcmService fcmService, IDeviceTokenRegistry tokens)
    {
        _fcmService = fcmService;
        _tokens = tokens;
    }

    public async Task HandleAsync(string message)
    {
        try
        {
            Log.Debug("📥 Received QuoteCreated event: {Message}", message);
            
            var quoteEvent = JsonSerializer.Deserialize<QuoteCreatedEvent>(message);
            if (quoteEvent == null)
            {
                Log.Warning("⚠️ Failed to deserialize QuoteCreatedEvent from message: {Message}", message);
                return;
            }

            Log.Information("📋 Quote #{QuoteId} - Customer: {CustomerId}, Price: {TotalBasePrice:N0} VND", 
                quoteEvent.QuoteId, quoteEvent.CustomerId, quoteEvent.TotalBasePrice);

            // Prepare notification content
            var title = "📝 Báo giá mới của bạn!";
            var body = $"Báo giá #{quoteEvent.QuoteId} cho xe của bạn đã được tạo. Tổng giá: {quoteEvent.TotalBasePrice:N0} VND.";
            var data = new Dictionary<string, string>
            {
                { "type", "quote" },
                { "quoteId", quoteEvent.QuoteId },
                { "customerId", quoteEvent.CustomerId.ToString() },
                { "totalPrice", quoteEvent.TotalBasePrice.ToString("F2") }
            };

            // Always log the notification
            Log.Information("📢 [THÔNG BÁO BÁO GIÁ] {Title} | {Body}", title, body);

            // Try to send push notification if a device token is available.
            // Registry fallback for the no-token producer (see
            // OrderCreatedConsumer for the full pattern); fans out to all
            // live tokens for the customer.
            List<string> deviceTokens;
            if (!string.IsNullOrWhiteSpace(quoteEvent.DeviceToken))
            {
                deviceTokens = new List<string> { quoteEvent.DeviceToken };
            }
            else
            {
                var registered = await _tokens.GetTokensAsync(
                    NotificationSubjects.Customer(quoteEvent.CustomerId));
                deviceTokens = registered.Count > 0 ? registered.ToList() : new List<string>();
            }

            if (deviceTokens.Count > 0)
            {
                var success = await _fcmService.SendMulticastAsync(
                    deviceTokens,
                    title,
                    body,
                    data
                );

                if (success)
                {
                    Log.Information("✅ Push notification sent successfully for Quote: {QuoteId}", quoteEvent.QuoteId);
                }
                else
                {
                    // IFcmService swallows send errors and returns false; throwing
                    // here lets the bus retry and eventually DLQ the delivery.
                    throw new InvalidOperationException($"FCM push failed for Quote {quoteEvent.QuoteId}");
                }
            }
            else
            {
                Log.Information("ℹ️ No device token for Quote {QuoteId}. Notification logged only (no push sent).", quoteEvent.QuoteId);
            }

            Log.Debug("✅ QuoteCreated event processed successfully for Quote: {QuoteId}", quoteEvent.QuoteId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Error processing QuoteCreatedEvent");
            // Rethrow: RabbitMQConsumerService decides retry-vs-DLQ from this
            // exception (EventRetryPolicy). Swallowing here used to ack the
            // delivery and lose the notification silently.
            throw;
        }
    }
}

