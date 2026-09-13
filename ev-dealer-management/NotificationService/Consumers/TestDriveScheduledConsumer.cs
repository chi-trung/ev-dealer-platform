using NotificationService.DTOs;
using NotificationService.Services;
using Serilog;
using System.Text.Json;

namespace NotificationService.Consumers;

public class TestDriveScheduledConsumer
{
    private readonly IFcmService _fcmService;

    public TestDriveScheduledConsumer(IFcmService fcmService)
    {
        _fcmService = fcmService;
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

            // Check if device token is available
            if (string.IsNullOrWhiteSpace(testDriveEvent.DeviceToken))
            {
                Log.Warning("No device token found for TestDrive event. Skipping push notification.");
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

            var success = await _fcmService.SendNotificationAsync(
                testDriveEvent.DeviceToken,
                title,
                body,
                data
            );

            if (success)
            {
                Log.Information("Test drive confirmation push notification sent for Customer: {CustomerName}", testDriveEvent.CustomerName);
            }
            else
            {
                // IFcmService swallows send errors and returns false; throwing
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
