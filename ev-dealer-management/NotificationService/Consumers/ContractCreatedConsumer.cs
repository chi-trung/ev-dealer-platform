using NotificationService.DTOs;

using Common.Events;
using NotificationService.DTOs;
using NotificationService.Services;
using Serilog;
using System.Text.Json;

namespace NotificationService.Consumers;

/// <summary>
/// contract.created. Primary audience: the customer (customer:&lt;id&gt;
/// registry fallback, payload token wins — unchanged #37 shape). Issue #56
/// adds the preference-gated salesperson audience user:&lt;SalespersonId&gt;,
/// with the same best-effort semantics as QuoteCreatedConsumer's #56 block
/// (never throws, never retries the already-delivered customer push).
/// </summary>
public class ContractCreatedConsumer
{
    private readonly IFcmService _fcmService;
    private readonly IDeviceTokenRegistry _tokens;
    private readonly INotificationPreferencePolicy _prefs;

    public ContractCreatedConsumer(IFcmService fcmService, IDeviceTokenRegistry tokens,
        INotificationPreferencePolicy prefs)
    {
        _fcmService = fcmService;
        _tokens = tokens;
        _prefs = prefs;
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
            string? registrySubject = null;
            if (!string.IsNullOrWhiteSpace(contractEvent.DeviceToken))
            {
                deviceTokens = new List<string> { contractEvent.DeviceToken };
            }
            else
            {
                registrySubject = NotificationSubjects.Customer(contractEvent.CustomerId);
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
                    Log.Information("✅ Push notification sent successfully for Contract: {ContractNumber}", contractEvent.ContractNumber);
                }
                else
                {
                    // The send layer swallows per-token errors and reports
                    // Success=false only when NO device was reached; throwing
                    // here lets the bus retry and eventually DLQ the delivery.
                    throw new InvalidOperationException($"FCM push failed for Contract {contractEvent.ContractNumber}");
                }
            }
            else
            {
                Log.Information("ℹ️ No device token for Contract {ContractNumber}. Notification logged only (no push sent).", contractEvent.ContractNumber);
            }

            // Issue #56 — preference-gated salesperson audience, user:<id>.
            // Best-effort by design; see QuoteCreatedConsumer's #56 block for
            // the full rationale (never throw: the customer push already
            // succeeded and a rethrow would double-send it via bus retry).
            if (contractEvent.SalespersonId > 0)
            {
                var salesSubject = NotificationSubjects.User(contractEvent.SalespersonId);
                try
                {
                    if (await _prefs.ShouldDeliverAsync(salesSubject, "contract"))
                    {
                        var salesTokens = await _tokens.GetTokensAsync(salesSubject);
                        if (salesTokens.Count > 0)
                        {
                            var salesData = new Dictionary<string, string>
                            {
                                { "type", "contract" },
                                { "contractId", contractEvent.ContractId },
                                { "contractNumber", contractEvent.ContractNumber },
                                { "orderId", contractEvent.OrderId.ToString() },
                                { "customerId", contractEvent.CustomerId.ToString() },
                                { "audience", "salesperson" },
                            };
                            var salesResult = await _fcmService.SendMulticastAsync(
                                salesTokens.ToList(),
                                "📋 Hợp đồng mới bạn phụ trách!",
                                $"Hợp đồng #{contractEvent.ContractNumber} cho đơn #{contractEvent.OrderId} đã được tạo. Tổng giá trị: {contractEvent.TotalAmount:N0} VND.",
                                salesData);
                            await _tokens.RevokeDeadTokensAsync(salesSubject, salesResult.DeadTokens);
                            if (salesResult.Success)
                                Log.Information("✅ Salesperson push sent for Contract: {ContractNumber}", contractEvent.ContractNumber);
                            else
                                Log.Error("❌ Salesperson push FAILED (best-effort, not retried — see #56 note) for Contract {ContractNumber}, user {Subject}",
                                    contractEvent.ContractNumber, salesSubject);
                        }
                        else
                        {
                            Log.Information("ℹ️ No device token for salesperson {Subject} (Contract {ContractNumber}). Logged only.",
                                salesSubject, contractEvent.ContractNumber);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "❌ Salesperson push errored (best-effort, not retried) for Contract {ContractNumber}, user {Subject}",
                        contractEvent.ContractNumber, salesSubject);
                }
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

