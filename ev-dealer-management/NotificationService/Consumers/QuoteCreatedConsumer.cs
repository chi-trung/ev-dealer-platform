using NotificationService.DTOs;

using Common.Events;
using NotificationService.Services;
using Serilog;
using System.Text.Json;

namespace NotificationService.Consumers;

/// <summary>
/// quote.created. Primary audience: the customer (customer:&lt;CustomerId&gt;
/// registry fallback, payload token wins — the #37 contract, unchanged).
/// Issue #56 adds a SECOND, preference-gated audience: the salesperson the
/// quote was assigned to. SalespersonId is a UserService id (the portal's
/// own accounts — frontend sends user.id for it, and OrderDetail resolves
/// it via GET /users/{id}), so their devices live under user:&lt;n&gt;;
/// that push is the one filtered by NotificationPreferencePolicy — muted
/// orders type or in-app channel ⇒ suppressed. The customer push is NOT
/// filtered: customer: subjects have no preferences surface (#51 auth
/// model), and gating them would let an id-space collision mute real
/// traffic.
/// </summary>
public class QuoteCreatedConsumer
{
    private readonly IFcmService _fcmService;
    private readonly IDeviceTokenRegistry _tokens;
    private readonly INotificationPreferencePolicy _prefs;

    public QuoteCreatedConsumer(IFcmService fcmService, IDeviceTokenRegistry tokens,
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
            string? registrySubject = null;
            if (!string.IsNullOrWhiteSpace(quoteEvent.DeviceToken))
            {
                deviceTokens = new List<string> { quoteEvent.DeviceToken };
            }
            else
            {
                registrySubject = NotificationSubjects.Customer(quoteEvent.CustomerId);
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
                    Log.Information("✅ Push notification sent successfully for Quote: {QuoteId}", quoteEvent.QuoteId);
                }
                else
                {
                    // The send layer swallows per-token errors and reports
                    // Success=false only when NO device was reached; throwing
                    // here lets the bus retry and eventually DLQ the delivery.
                    throw new InvalidOperationException($"FCM push failed for Quote {quoteEvent.QuoteId}");
                }
            }
            else
            {
                Log.Information("ℹ️ No device token for Quote {QuoteId}. Notification logged only (no push sent).", quoteEvent.QuoteId);
            }

            // Issue #56 — the user-targeted audience: the assigned
            // salesperson, keyed user:<SalespersonId>. This is the fan-out
            // point the preference flags were persisted for; the policy
            // above never consults the store for the customer push.
            //
            // Deliberately BEST-EFFORT (no throw on failure), unlike the
            // customer push's #37 contract: the customer has already been
            // reached by the time we get here, and rethrowing would make
            // the bus retry the WHOLE event and double-push the customer —
            // #56 must not change primary-delivery semantics. A lost
            // salesperson push is loud in the log and self-healing (the
            // quote is visible in their queue on next load).
            if (quoteEvent.SalespersonId > 0)
            {
                var salesSubject = NotificationSubjects.User(quoteEvent.SalespersonId);
                try
                {
                    if (await _prefs.ShouldDeliverAsync(salesSubject, "quote"))
                    {
                        var salesTokens = await _tokens.GetTokensAsync(salesSubject);
                        if (salesTokens.Count > 0)
                        {
                            var salesData = new Dictionary<string, string>
                            {
                                { "type", "quote" },
                                { "quoteId", quoteEvent.QuoteId },
                                { "customerId", quoteEvent.CustomerId.ToString() },
                                { "totalPrice", quoteEvent.TotalBasePrice.ToString("F2") },
                                { "audience", "salesperson" },
                            };
                            var salesResult = await _fcmService.SendMulticastAsync(
                                salesTokens.ToList(),
                                "📝 Báo giá mới bạn phụ trách!",
                                $"Báo giá #{quoteEvent.QuoteId} (khách hàng #{quoteEvent.CustomerId}) — tổng {quoteEvent.TotalBasePrice:N0} VND.",
                                salesData);
                            await _tokens.RevokeDeadTokensAsync(salesSubject, salesResult.DeadTokens);
                            if (salesResult.Success)
                                Log.Information("✅ Salesperson push sent for Quote: {QuoteId}", quoteEvent.QuoteId);
                            else
                                Log.Error("❌ Salesperson push FAILED (best-effort, not retried — see #56 note) for Quote {QuoteId}, user {Subject}",
                                    quoteEvent.QuoteId, salesSubject);
                        }
                        else
                        {
                            Log.Information("ℹ️ No device token for salesperson {Subject} (Quote {QuoteId}). Logged only.",
                                salesSubject, quoteEvent.QuoteId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "❌ Salesperson push errored (best-effort, not retried) for Quote {QuoteId}, user {Subject}",
                        quoteEvent.QuoteId, salesSubject);
                }
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

