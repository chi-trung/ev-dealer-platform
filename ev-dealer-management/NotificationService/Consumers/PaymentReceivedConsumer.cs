using NotificationService.DTOs;
using NotificationService.Services;
using Serilog;
using System.Text.Json;

namespace NotificationService.Consumers;

/// <summary>
/// payment.received (default exchange, routing key = queue name). Push-capable
/// via the device-token registry (Issue #37): the payload carries no
/// DeviceToken (the producer never has one), so tokens resolve out-of-band for
/// the subject customer:&lt;CustomerId&gt; and fan out to ALL live devices via
/// multicast. Zero registered tokens keeps the honest fallback: log-only, with
/// the exact subject string logged so a NotificationSubjects drift is visible
/// instead of failing silently. A push failure throws so the bus retries and
/// eventually DLQs the delivery (EventRetryPolicy).
/// </summary>
public class PaymentReceivedConsumer
{
    private readonly IFcmService _fcmService;
    private readonly IDeviceTokenRegistry _tokens;

    public PaymentReceivedConsumer(IFcmService fcmService, IDeviceTokenRegistry tokens)
    {
        _fcmService = fcmService;
        _tokens = tokens;
    }

    public async Task HandleAsync(string message)
    {
        try
        {
            Log.Debug("📥 Received PaymentReceived event: {Message}", message);

            var paymentEvent = JsonSerializer.Deserialize<PaymentReceivedEvent>(message);
            if (paymentEvent == null)
            {
                Log.Warning("⚠️ Failed to deserialize PaymentReceivedEvent from message: {Message}", message);
                return;
            }

            var title = "💰 Thanh toán đã được ghi nhận!";
            var body = $"Thanh toán {paymentEvent.PaymentId} cho đơn hàng #{paymentEvent.OrderId} ({paymentEvent.Amount:N0} VND, {paymentEvent.PaymentMethod}) đã được ghi nhận.";
            var data = new Dictionary<string, string>
            {
                { "type", "payment" },
                { "paymentId", paymentEvent.PaymentId },
                { "orderId", paymentEvent.OrderId },
                { "customerId", paymentEvent.CustomerId.ToString() },
                { "amount", paymentEvent.Amount.ToString("F2") }
            };

            // Always log the notification
            Log.Information("📢 [THÔNG BÁO THANH TOÁN] {Title} | {Body}", title, body);

            var subject = NotificationSubjects.Customer(paymentEvent.CustomerId);
            var registered = await _tokens.GetTokensAsync(subject);
            if (registered.Count > 0)
            {
                var success = await _fcmService.SendMulticastAsync(
                    registered.ToList(), title, body, data);
                if (success)
                {
                    Log.Information("✅ Push notification sent successfully for Payment: {PaymentId}", paymentEvent.PaymentId);
                }
                else
                {
                    // IFcmService swallows send errors and returns false; throwing
                    // here lets the bus retry and eventually DLQ the delivery.
                    throw new InvalidOperationException($"FCM push failed for Payment {paymentEvent.PaymentId}");
                }
            }
            else
            {
                Log.Information("ℹ️ No device token registered for {Subject}. Notification logged only (no push sent).", subject);
            }

            Log.Debug("✅ PaymentReceived event processed successfully for Payment: {PaymentId}", paymentEvent.PaymentId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Error processing PaymentReceivedEvent");
            // Rethrow: RabbitMQConsumerService decides retry-vs-DLQ from this
            // exception (EventRetryPolicy). Swallowing here used to ack the
            // delivery and lose the notification silently.
            throw;
        }
    }
}
