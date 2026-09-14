using NotificationService.DTOs;
using NotificationService.Services;
using Serilog;
using System.Text.Json;

namespace NotificationService.Consumers;

public class TestDriveScheduledConsumer
{
    private readonly IFcmService _fcmService;
    private readonly IDeviceTokenRegistry _tokens;

    public TestDriveScheduledConsumer(IFcmService fcmService, IDeviceTokenRegistry tokens)
    {
        _fcmService = fcmService;
        _tokens = tokens;
    }

    public async Task HandleAsync(string message)
    {
        try
        {
            var testDriveEvent = JsonSerializer.Deserialize<TestDriveScheduledEvent>(message);
            if (testDriveEvent == null)
            {
                Log.Warning("Failed to deserialize TestDriveScheduledEvent from message: {Message}", message);
                return;
            }

            Log.Information("Processing TestDriveScheduledEvent for Customer: {CustomerName}", testDriveEvent.CustomerName);

            // VehicleModel may be empty when CustomerService can't resolve the name.
            var vehicleLabel = string.IsNullOrWhiteSpace(testDriveEvent.VehicleModel)
                ? $"#{testDriveEvent.VehicleId}"
                : testDriveEvent.VehicleModel;

            // Device token: the booking API still sends none (payload token
            // null), so fall back to the registry (Issue #33) and fan out to
            // all live tokens for the customer.
            List<string> deviceTokens;
            string? registrySubject = null;
            if (!string.IsNullOrWhiteSpace(testDriveEvent.DeviceToken))
            {
                deviceTokens = new List<string> { testDriveEvent.DeviceToken };
            }
            else
            {
                registrySubject = NotificationSubjects.Customer(testDriveEvent.CustomerId);
                var registered = await _tokens.GetTokensAsync(registrySubject);
                deviceTokens = registered.Count > 0 ? registered.ToList() : new List<string>();
            }

            if (deviceTokens.Count == 0)
            {
                Log.Warning("No device token for TestDrive event (customer {CustomerId}). Skipping push notification.", testDriveEvent.CustomerId);
                return;
            }

            // Send push notification
            var title = "📅 Test Drive đã được đặt!";
            var body = $"Lịch test drive xe {vehicleLabel} vào ngày {testDriveEvent.ScheduledDate:dd/MM/yyyy HH:mm}. Vui lòng đến đúng giờ!";
            var data = new Dictionary<string, string>
            {
                { "type", "testdrive" },
                { "vehicleModel", vehicleLabel },
                { "scheduledDate", testDriveEvent.ScheduledDate.ToString("yyyy-MM-dd HH:mm:ss") }
            };

            var result = await _fcmService.SendMulticastAsync(
                deviceTokens,
                title,
                body,
                data
            );
            // Issue #44: revoke rows FCM rejected permanently. Null key on the
            // payload-token path (no registry row to blame) makes the call a
            // no-op. Best-effort; never throws.
            await _tokens.RevokeDeadTokensAsync(registrySubject, result.DeadTokens);

            if (result.Success)
            {
                Log.Information("Test drive confirmation push notification sent for Customer: {CustomerName}", testDriveEvent.CustomerName);
            }
            else
            {
                // The send layer swallows per-token errors and reports
                // Success=false only when NO device was reached; throwing
                // here lets the bus retry and eventually DLQ the delivery.
                throw new InvalidOperationException($"FCM push failed for test drive of customer {testDriveEvent.CustomerName}");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing TestDriveScheduledEvent");
            // Rethrow: RabbitMQConsumerService decides retry-vs-DLQ from this
            // exception (EventRetryPolicy). Swallowing here used to ack the
            // delivery and lose the notification silently.
            throw;
        }
    }
}
