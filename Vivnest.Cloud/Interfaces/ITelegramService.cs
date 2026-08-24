namespace Vivnest.Cloud.Interfaces;

public interface ITelegramService
{
    // byte[], deliberately not Stream. A Stream overload existed with no
    // caller, no 429 retry and no compression - the three things the real
    // photo path (TelegramNotificationChannel -> this) depends on. Content
    // must be re-creatable per retry attempt, which a consumed Stream is
    // not.
    Task SendPhotoAsync(
        byte[] image,
        string caption,
        CancellationToken cancellationToken = default);

    Task SendMessageAsync(
        string message,
        CancellationToken cancellationToken = default);
}