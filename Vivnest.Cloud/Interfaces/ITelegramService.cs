namespace Vivnest.Cloud.Interfaces;

public interface ITelegramService
{
    Task SendPhotoAsync(
        byte[] image,
        string caption,
        CancellationToken cancellationToken = default);

    Task SendPhotoAsync(
        Stream image,
        string caption,
        CancellationToken cancellationToken = default);

    Task SendMessageAsync(
        string message,
        CancellationToken cancellationToken = default);
}