using Microsoft.AspNetCore.Mvc;
using NotificationService.Services;

namespace NotificationService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NotificationController : ControllerBase
{
    private readonly IFcmService _fcmService;

    public NotificationController(IFcmService fcmService)
    {
        _fcmService = fcmService;
    }

    /// <summary>
    /// Test FCM notification - Send a test push notification to a device
    /// </summary>
    [HttpPost("test-fcm")]
    public async Task<IActionResult> TestFcm([FromBody] TestFcmRequest request)
    {
        var success = await _fcmService.SendNotificationAsync(
            request.DeviceToken,
            request.Title,
            request.Body,
            request.Data
        );

        return success 
            ? Ok(new { message = "Push notification sent successfully" })
            : BadRequest(new { message = "Failed to send push notification" });
    }

    /// <summary>
    /// Subscribe a device token to a topic
    /// </summary>
    [HttpPost("subscribe-topic")]
    public async Task<IActionResult> SubscribeToTopic([FromBody] SubscribeTopicRequest request)
    {
        var success = await _fcmService.SubscribeToTopicAsync(
            request.DeviceToken,
            request.Topic
        );

        return success 
            ? Ok(new { message = $"Subscribed to topic: {request.Topic}" })
            : BadRequest(new { message = $"Failed to subscribe to topic: {request.Topic}" });
    }

    /// <summary>
    /// Unsubscribe a device token from a topic
    /// </summary>
    [HttpPost("unsubscribe-topic")]
    public async Task<IActionResult> UnsubscribeFromTopic([FromBody] SubscribeTopicRequest request)
    {
        var success = await _fcmService.UnsubscribeFromTopicAsync(
            request.DeviceToken,
            request.Topic
        );

        return success 
            ? Ok(new { message = $"Unsubscribed from topic: {request.Topic}" })
            : BadRequest(new { message = $"Failed to unsubscribe from topic: {request.Topic}" });
    }

    /// <summary>
    /// Send notification to a topic (broadcast)
    /// </summary>
    [HttpPost("send-to-topic")]
    public async Task<IActionResult> SendToTopic([FromBody] SendToTopicRequest request)
    {
        var success = await _fcmService.SendToTopicAsync(
            request.Topic,
            request.Title,
            request.Body,
            request.Data
        );

        return success 
            ? Ok(new { message = $"Notification sent to topic: {request.Topic}" })
            : BadRequest(new { message = $"Failed to send notification to topic: {request.Topic}" });
    }

    /// <summary>
    /// Send notification to multiple devices at once
    /// </summary>
    [HttpPost("send-multicast")]
    public async Task<IActionResult> SendMulticast([FromBody] SendMulticastRequest request)
    {
        // Raw caller-supplied tokens with no registry subject behind them, so
        // the dead-token report is unused here (revocation is the consumers'
        // job — Issue #44). Only Success matters for the HTTP outcome.
        var result = await _fcmService.SendMulticastAsync(
            request.DeviceTokens,
            request.Title,
            request.Body,
            request.Data
        );

        return result.Success
            ? Ok(new { message = $"Multicast notification sent to {request.DeviceTokens.Count} devices" })
            : BadRequest(new { message = "Failed to send multicast notification" });
    }
}

// Request DTOs
public record TestFcmRequest(
    string DeviceToken, 
    string Title, 
    string Body, 
    Dictionary<string, string>? Data = null
);

public record SubscribeTopicRequest(
    string DeviceToken, 
    string Topic
);

public record SendToTopicRequest(
    string Topic, 
    string Title, 
    string Body, 
    Dictionary<string, string>? Data = null
);

public record SendMulticastRequest(
    List<string> DeviceTokens, 
    string Title, 
    string Body, 
    Dictionary<string, string>? Data = null
);
