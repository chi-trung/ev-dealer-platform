using NotificationService.DTOs;
using NotificationService.Services;
using Serilog;
using System.Text.Json;

namespace NotificationService.Consumers;

public class OrderCreatedConsumer
{
    private readonly IFcmService _fcmService;
    private readonly IDeviceTokenRegistry _tokens;

    public OrderCreatedConsumer(IFcmService fcmService, IDeviceTokenRegistry tokens)
    {
        _fcmService = fcmService;
        _tokens = tokens;
    }

    public async Task HandleAsync(string message)
    {
        try
        {
            Log.Debug("📥 Received OrderCreated event: {Message}", message);
            
            var orderEvent = JsonSerializer.Deserialize<OrderCreatedEvent>(message);
            if (orderEvent == null)
            {
                Log.Warning("⚠️ Failed to deserialize OrderCreatedEvent from message: {Message}", message);
                return;
            }

            Log.Information("🛒 Order #{OrderNumber} - Customer: {CustomerId}, Price: {TotalPrice:N0} VND, Status: {Status}", 
                orderEvent.OrderNumber, orderEvent.CustomerId, orderEvent.TotalPrice, orderEvent.Status);

            // Prepare notification content
            var title = "🎉 Đơn hàng mới của bạn!";
            var body = $"Đơn hàng #{orderEvent.OrderNumber} đã được tạo thành công. Tổng giá: {orderEvent.TotalPrice:N0} VND.";
            var data = new Dictionary<string, string>
            {
                { "type", "order" },
                { "orderId", orderEvent.OrderId },
                { "orderNumber", orderEvent.OrderNumber },
                { "customerId", orderEvent.CustomerId.ToString() },
                { "totalPrice", orderEvent.TotalPrice.ToString("F2") }
            };

            // Always log the notification
            Log.Information("📢 [THÔNG BÁO ĐƠN HÀNG] {Title} | {Body}", title, body);

            // Try to send push notification if a device token is available.
            // Producers carry none for this event (docs/EVENTS.md gap #4), so
            // fall back to the registry keyed by the customer the order
            // belongs to; a payload token (future producer sends one) still
            // wins and bypasses the lookup. Delivery fans out to ALL live
            // tokens for the subject via multicast.
            List<string> deviceTokens;
            if (!string.IsNullOrWhiteSpace(orderEvent.DeviceToken))
            {
                deviceTokens = new List<string> { orderEvent.DeviceToken };
            }
            else
            {
                var registered = await _tokens.GetTokensAsync(
                    NotificationSubjects.Customer(orderEvent.CustomerId));
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
                    Log.Information("✅ Push notification sent successfully for Order: {OrderNumber}", orderEvent.OrderNumber);
                }
                else
                {
                    // IFcmService swallows send errors and returns false; throwing
                    // here lets the bus retry and eventually DLQ the delivery.
                    throw new InvalidOperationException($"FCM push failed for Order {orderEvent.OrderNumber}");
                }
            }
            else
            {
                Log.Information("ℹ️ No device token for Order {OrderNumber}. Notification logged only (no push sent).", orderEvent.OrderNumber);
            }

            Log.Debug("✅ OrderCreated event processed successfully for Order: {OrderNumber}", orderEvent.OrderNumber);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Error processing OrderCreatedEvent");
            // Rethrow: RabbitMQConsumerService decides retry-vs-DLQ from this
            // exception (EventRetryPolicy). Swallowing here used to ack the
            // delivery and lose the notification silently.
            throw;
        }
    }
}

