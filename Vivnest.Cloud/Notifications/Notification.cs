namespace Vivnest.Cloud.Notifications;

public sealed class Notification
{
    public required string Type { get; init; }
    public string? Title { get; init; }
    public required string Message { get; init; }
    public IReadOnlyList<byte[]>? Images { get; init; }
    public NotificationPriority Priority { get; init; } = NotificationPriority.Normal;
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
}
