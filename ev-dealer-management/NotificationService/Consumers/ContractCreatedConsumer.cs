using NotificationService.DTOs;
using NotificationService.Services;
using Serilog;
using System.Text.Json;

namespace NotificationService.Consumers;

public class ContractCreatedConsumer
{
    private readonly IFcmService _fcmService;
    private readonly IDeviceTokenRegistry _tokens;

    public ContractCreatedConsumer(IFcmService fcmService, IDeviceTokenRegistry tokens)
    {
        _fcmService = fcmService;
        _tokens = tokens;
    }

    public async Task HandleAsync(string message)
    {
        try
        {
            Log.Debug("📥 Received ContractCreated event: {Message}", message);
            
            var contractEvent = JsonSerializer.Deserialize<ContractCreatedEvent>(message);
            if (contractEvent == null)
            {
                Log.Warning("⚠️ Failed to deserialize ContractCreatedEvent from message: {Message}", message);
                return;
            }

            Log.Information("📄 Contract #{ContractNumber} - Order: {OrderId}, Customer: {CustomerId}, Amount: {TotalAmount:N0} VND, Status: {Status}",
                contractEvent.ContractNumber, contractEvent.OrderId, contractEvent.CustomerId, contractEvent.TotalAmount, contractEvent.Status);

            // Prepare notification content
            var title = "📋 Hợp đồng mới đã được tạo!";
            var body = $"Hợp đồng #{contractEvent.ContractNumber} cho đơn hàng #{contractEvent.OrderId} đã được tạo. Tổng giá trị: {contractEvent.TotalAmount:N0} VND.";
            var data = new Dictionary<string, string>
            {
                { "type", "contract" },
                { "contractId", contractEvent.ContractId },
                { "contractNumber", contractEvent.ContractNumber },
                { "orderId", contractEvent.OrderId.ToString() },
                { "customerId", contractEvent.CustomerId.ToString() },
                { "totalAmount", contractEvent.TotalAmount.ToString("F2") },
                { "status", contractEvent.Status }
            };

            // Always log the notification
            Log.Information("📢 [THÔNG BÁO HỢP ĐỒNG] {Title} | {Body}", title, body);

            // Try to send push notification if a device token is available.
            // Registry fallback for the no-token producer (see
            // OrderCreatedConsumer for the full pattern); fans out to all
            // live tokens for the customer.
            List<string> deviceTokens;
            if (!string.IsNullOrWhiteSpace(contractEvent.DeviceToken))
            {
                deviceTokens = new List<string> { contractEvent.DeviceToken };
            }
            else
            {
                var registered = await _tokens.GetTokensAsync(
                    NotificationSubjects.Customer(contractEvent.CustomerId));
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
                    Log.Information("✅ Push notification sent successfully for Contract: {ContractNumber}", contractEvent.ContractNumber);
                }
                else
                {
                    // IFcmService swallows send errors and returns false; throwing
                    // here lets the bus retry and eventually DLQ the delivery.
                    throw new InvalidOperationException($"FCM push failed for Contract {contractEvent.ContractNumber}");
                }
            }
            else
            {
                Log.Information("ℹ️ No device token for Contract {ContractNumber}. Notification logged only (no push sent).", contractEvent.ContractNumber);
            }

            Log.Debug("✅ ContractCreated event processed successfully for Contract: {ContractNumber}", contractEvent.ContractNumber);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Error processing ContractCreatedEvent");
            // Rethrow: RabbitMQConsumerService decides retry-vs-DLQ from this
            // exception (EventRetryPolicy). Swallowing here used to ack the
            // delivery and lose the notification silently.
            throw;
        }
    }
}

