using NotificationService.DTOs;
using Serilog;
using System.Text.Json;

namespace NotificationService.Consumers;

/// <summary>
/// payment.received (default exchange, routing key = queue name). The producer
/// payload carries no CustomerEmail/DeviceToken, so this is log-only today —
/// same degraded pattern as OrderCreated/QuoteCreated/ContractCreated. When
/// the API starts collecting device tokens, add the FCM push block (throw on
/// success == false so the bus retries; see TestDriveScheduledConsumer).
/// </summary>
public class PaymentReceivedConsumer
{
    public Task HandleAsync(string message)
    {
        try
        {
            Log.Debug("📥 Received PaymentReceived event: {Message}", message);

            var paymentEvent = JsonSerializer.Deserialize<PaymentReceivedEvent>(message);
            if (paymentEvent == null)
            {
                Log.Warning("⚠️ Failed to deserialize PaymentReceivedEvent from message: {Message}", message);
                return Task.CompletedTask;
            }

            var title = "💰 Thanh toán đã được ghi nhận!";
            var body = $"Thanh toán {paymentEvent.PaymentId} cho đơn hàng #{paymentEvent.OrderId} ({paymentEvent.Amount:N0} VND, {paymentEvent.PaymentMethod}) đã được ghi nhận.";

            // Always log the notification
            Log.Information("📢 [THÔNG BÁO THANH TOÁN] {Title} | {Body}", title, body);
            Log.Information("ℹ️ No device token in PaymentReceivedEvent payload. Notification logged only (no push sent).");

            Log.Debug("✅ PaymentReceived event processed successfully for Payment: {PaymentId}", paymentEvent.PaymentId);
            return Task.CompletedTask;
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
