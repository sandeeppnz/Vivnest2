using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using Vivnest.Cloud.Interfaces;
using Vivnest.Cloud.Options;

public sealed class TelegramService : ITelegramService
{
    private readonly HttpClient _httpClient;
    private readonly TelegramOptions _options;

    public TelegramService(
        HttpClient httpClient,
        IOptions<TelegramOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task SendPhotoAsync(
        Stream image,
        string caption,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return;

        var requestUri =
            $"https://api.telegram.org/bot{_options.BotToken}/sendPhoto";

        using var content = new MultipartFormDataContent();

        content.Add(
            new StringContent(_options.ChatId),
            "chat_id");

        content.Add(
            new StringContent(caption),
            "caption");

        var imageContent = new StreamContent(image);

        imageContent.Headers.ContentType =
            new MediaTypeHeaderValue("image/jpeg");

        content.Add(
            imageContent,
            "photo",
            "camera.jpg");

        var response = await _httpClient.PostAsync(
            requestUri,
            content,
            cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task SendPhotoAsync(
           byte[] image,
           string caption,
           CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return;

        ArgumentNullException.ThrowIfNull(image);

        var url =
            $"https://api.telegram.org/bot{_options.BotToken}/sendPhoto";

        using var content = new MultipartFormDataContent();

        content.Add(
            new StringContent(_options.ChatId),
            "chat_id");

        if (!string.IsNullOrWhiteSpace(caption))
        {
            content.Add(
                new StringContent(caption),
                "caption");
        }

        var imageContent = new ByteArrayContent(image);
        imageContent.Headers.ContentType =
            new MediaTypeHeaderValue("image/jpeg");

        content.Add(
            imageContent,
            "photo",
            $"capture-{DateTime.UtcNow:yyyyMMddHHmmss}.jpg");

        using var response = await _httpClient.PostAsync(
            url,
            content,
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Telegram API returned {(int)response.StatusCode}: {body}");
        }
    }
}