using NotificationService.DTOs;
using NotificationService.Services;
using Serilog;
using System.Text.Json;

namespace NotificationService.Consumers;

public class OrderCreatedConsumer
{
    private readonly IFcmService _fcmService;

    public OrderCreatedConsumer(IFcmService fcmService)
    {
        _fcmService = fcmService;
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

            // Try to send push notification if device token is available
            if (!string.IsNullOrWhiteSpace(orderEvent.DeviceToken))
            {
                var success = await _fcmService.SendNotificationAsync(
                    orderEvent.DeviceToken,
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

