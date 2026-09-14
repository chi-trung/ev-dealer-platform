using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Serilog;

namespace NotificationService.Services;

public class FirebaseFcmService : IFcmService
{
    private readonly FirebaseMessaging _messaging;
    private readonly string _projectId;

    public FirebaseFcmService(IConfiguration configuration)
    {
        var credentialPath = configuration["Firebase:CredentialPath"] 
            ?? throw new ArgumentNullException("Firebase:CredentialPath not configured");
        
        _projectId = configuration["Firebase:ProjectId"] 
            ?? throw new ArgumentNullException("Firebase:ProjectId not configured");

        try
        {
            // Initialize Firebase App if not already initialized
            if (FirebaseApp.DefaultInstance == null)
            {
                FirebaseApp.Create(new AppOptions
                {
                    Credential = GoogleCredential.FromFile(credentialPath),
                    ProjectId = _projectId
                });

                Log.Information("Firebase App initialized successfully for project: {ProjectId}", _projectId);
            }

            _messaging = FirebaseMessaging.DefaultInstance;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to initialize Firebase App");
            throw;
        }
    }

    public async Task<bool> SendNotificationAsync(string deviceToken, string title, string body, Dictionary<string, string>? data = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(deviceToken))
            {
                Log.Warning("Device token is null or empty");
                return false;
            }

            var message = new Message
            {
                Token = deviceToken,
                Notification = new Notification
                {
                    Title = title,
                    Body = body
                },
                Data = data,
                // Add web push config for better browser support
                Webpush = new WebpushConfig
                {
                    Notification = new WebpushNotification
                    {
                        Title = title,
                        Body = body,
                        Icon = "/logo.png"
                    }
                }
            };

            var response = await _messaging.SendAsync(message);
            
            Log.Information("FCM notification sent successfully. MessageId: {MessageId}, Token: {Token}", 
                response, MaskToken(deviceToken));
            
            return true;
        }
        catch (FirebaseMessagingException ex)
        {
            Log.Error(ex, "Firebase Messaging error. Code: {ErrorCode}, Token: {Token}", 
                ex.MessagingErrorCode, MaskToken(deviceToken));
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to send FCM notification to token: {Token}", MaskToken(deviceToken));
            return false;
        }
    }

    public async Task<bool> SendToTopicAsync(string topic, string title, string body, Dictionary<string, string>? data = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(topic))
            {
                Log.Warning("Topic is null or empty");
                return false;
            }

            var message = new Message
            {
                Topic = topic,
                Notification = new Notification
                {
                    Title = title,
                    Body = body
                },
                Data = data,
                Webpush = new WebpushConfig
                {
                    Notification = new WebpushNotification
                    {
                        Title = title,
                        Body = body,
                        Icon = "/logo.png"
                    }
                }
            };

            var response = await _messaging.SendAsync(message);
            
            Log.Information("FCM topic notification sent successfully. MessageId: {MessageId}, Topic: {Topic}", 
                response, topic);
            
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to send FCM notification to topic: {Topic}", topic);
            return false;
        }
    }

    public async Task<MulticastResult> SendMulticastAsync(List<string> deviceTokens, string title, string body, Dictionary<string, string>? data = null)
    {
        try
        {
            if (deviceTokens == null || !deviceTokens.Any())
            {
                Log.Warning("Device tokens list is null or empty");
                return MulticastResult.Failed;
            }

            var message = new MulticastMessage
            {
                Tokens = deviceTokens,
                Notification = new Notification
                {
                    Title = title,
                    Body = body
                },
                Data = data,
                Webpush = new WebpushConfig
                {
                    Notification = new WebpushNotification
                    {
                        Title = title,
                        Body = body,
                        Icon = "/logo.png"
                    }
                }
            };

            var response = await _messaging.SendEachForMulticastAsync(message);

            Log.Information("FCM multicast sent. Success: {SuccessCount}, Failed: {FailureCount}, Total: {TotalCount}",
                response.SuccessCount, response.FailureCount, deviceTokens.Count);

            // Classify failures per token (Issue #44). SendEachForMulticastAsync
            // answers one response per token, in the order sent, so index i is
            // deviceTokens[i] — which is what lets a registry-backed caller
            // revoke the row a permanently-dead token belongs to.
            // SendResponse/BatchResponse have no public constructors, so the
            // classification below is factored into IsPermanentlyDead and
            // unit-tested by constructing the exception (public ctor) rather
            // than a fake batch response.
            var dead = new List<string>();
            for (var i = 0; i < response.Responses.Count; i++)
            {
                var send = response.Responses[i];
                if (send.IsSuccess) continue;

                var code = send.Exception?.MessagingErrorCode;
                if (IsPermanentlyDead(code))
                {
                    dead.Add(deviceTokens[i]);
                    Log.Warning("Token {TokenIndex} {Code}; reporting it dead for revocation", i, code);
                }
                else
                {
                    Log.Warning("Failed to send to token {TokenIndex}: {Code}: {Exception}",
                        i, code, send.Exception?.Message);
                }
            }

            return new MulticastResult(response.SuccessCount > 0, dead);
        }
        catch (Exception ex)
        {
            // Total send failure with nothing blamed: a dead-token report here
            // is unknowable, and guessing would mass-revoke on a bad credential
            // or a Firebase outage.
            Log.Error(ex, "Failed to send FCM multicast notification");
            return MulticastResult.Failed;
        }
    }

    /// <summary>
    /// Whether an FCM per-token error code means the token will NEVER work
    /// again, so its registry row should go (Issue #44).
    ///
    /// UNREGISTERED is the clear case: the browser unsubscribed or the
    /// subscription expired. INVALID_ARGUMENT is included per the issue's
    /// scope because FCM returns it for web-push registrations whose endpoint
    /// or p256dh/auth keys are no longer valid — the common fate of a stale
    /// service-worker token. Everything else stays: UNAVAILABLE / INTERNAL /
    /// QUOTA_EXCEEDED / SENDER_ID_MISMATCH / THIRD_PARTY_AUTH_ERROR and a
    /// null code (not a MessagingErrorCode-carrying failure at all) are all
    /// "maybe fine later" or "our side is broken", and revoking on them would
    /// delete live devices. SENDER_ID_MISMATCH is arguably permanent too, but
    /// it means the project config is wrong — a bad build would wipe every
    /// mailbox, which is the opposite of a stale-token cleanup.
    /// </summary>
    public static bool IsPermanentlyDead(MessagingErrorCode? code) =>
        code is MessagingErrorCode.Unregistered or MessagingErrorCode.InvalidArgument;

    public async Task<bool> SubscribeToTopicAsync(string deviceToken, string topic)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(deviceToken) || string.IsNullOrWhiteSpace(topic))
            {
                Log.Warning("Device token or topic is null or empty");
                return false;
            }

            var response = await _messaging.SubscribeToTopicAsync(new List<string> { deviceToken }, topic);
            
            if (response.SuccessCount > 0)
            {
                Log.Information("Device token subscribed to topic: {Topic}", topic);
                return true;
            }
            else
            {
                Log.Warning("Failed to subscribe to topic: {Topic}. Errors: {Errors}", 
                    topic, response.Errors?.FirstOrDefault()?.Reason);
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to subscribe device token to topic: {Topic}", topic);
            return false;
        }
    }

    public async Task<bool> UnsubscribeFromTopicAsync(string deviceToken, string topic)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(deviceToken) || string.IsNullOrWhiteSpace(topic))
            {
                Log.Warning("Device token or topic is null or empty");
                return false;
            }

            var response = await _messaging.UnsubscribeFromTopicAsync(new List<string> { deviceToken }, topic);
            
            if (response.SuccessCount > 0)
            {
                Log.Information("Device token unsubscribed from topic: {Topic}", topic);
                return true;
            }
            else
            {
                Log.Warning("Failed to unsubscribe from topic: {Topic}. Errors: {Errors}", 
                    topic, response.Errors?.FirstOrDefault()?.Reason);
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to unsubscribe device token from topic: {Topic}", topic);
            return false;
        }
    }

    /// <summary>
    /// Mask device token for logging (show first and last 8 chars only)
    /// </summary>
    private string MaskToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 20)
            return "***";
        
        return $"{token[..8]}...{token[^8..]}";
    }
}
