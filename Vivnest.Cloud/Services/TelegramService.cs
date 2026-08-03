using Microsoft.Extensions.Options;
using SkiaSharp;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Vivnest.Cloud.Interfaces;
using Vivnest.Cloud.Options;

namespace Vivnest.Cloud.Services;

public sealed class TelegramService : ITelegramService
{
    private const string TelegramApiBaseUrl = "https://api.telegram.org";

    // Telegram flood-controls per chat - a burst of alerts (e.g. several
    // devices recovering within seconds of each other) can trip a 429.
    // Telegram's own response tells us exactly how long to wait
    // (parameters.retry_after), so honor that rather than guessing.
    private const int MaxAttempts = 3;
    private static readonly TimeSpan FallbackRetryDelay = TimeSpan.FromSeconds(2);

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
            $"{TelegramApiBaseUrl}/bot{_options.BotToken}/sendPhoto";

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
            $"{TelegramApiBaseUrl}/bot{_options.BotToken}/sendPhoto";

        var compressed = CompressImage(image);

        await PostWithRetryAsync(
            url,
            () =>
            {
                var content = new MultipartFormDataContent
                {
                    { new StringContent(_options.ChatId), "chat_id" }
                };

                if (!string.IsNullOrWhiteSpace(caption))
                {
                    content.Add(new StringContent(caption), "caption");
                }

                var imageContent = new ByteArrayContent(compressed);

                imageContent.Headers.ContentType =
                    new MediaTypeHeaderValue("image/jpeg");

                content.Add(
                    imageContent,
                    "photo",
                    $"capture-{DateTime.UtcNow:yyyyMMddHHmmss}.jpg");

                return content;
            },
            cancellationToken);
    }


    public async Task SendMessageAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return;

        var url =
            $"{TelegramApiBaseUrl}/bot{_options.BotToken}/sendMessage";

        await PostWithRetryAsync(
            url,
            () => new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["chat_id"] = _options.ChatId,
                ["text"] = message
            }),
            cancellationToken);
    }

    // Recreates the request content per attempt (createContent) since
    // HttpContent can't safely be resent once consumed. Retries only on 429,
    // honoring Telegram's own parameters.retry_after when present.
    private async Task PostWithRetryAsync(
        string url,
        Func<HttpContent> createContent,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var content = createContent();

            using var response = await _httpClient.PostAsync(
                url,
                content,
                cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
                return;

            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt >= MaxAttempts)
            {
                throw new InvalidOperationException(
                    $"Telegram API returned {(int)response.StatusCode}: {body}");
            }

            var retryAfter = TryGetRetryAfter(body) ?? FallbackRetryDelay;

            await Task.Delay(retryAfter, cancellationToken);
        }
    }

    private static TimeSpan? TryGetRetryAfter(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("parameters", out var parameters) &&
                parameters.TryGetProperty("retry_after", out var retryAfter))
            {
                return TimeSpan.FromSeconds(retryAfter.GetInt32());
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static byte[] CompressImage(byte[] image, int maxWidth = 1024, int quality = 75)
    {
        using var sourceBitmap = SKBitmap.Decode(image);

        var width = sourceBitmap.Width;
        var height = sourceBitmap.Height;

        if (width > maxWidth)
        {
            var scale = (float)maxWidth / width;
            width = maxWidth;
            height = (int)(height * scale);
        }

        using var resizedBitmap = new SKBitmap(width, height);

        sourceBitmap.ScalePixels(
            resizedBitmap,
            SKSamplingOptions.Default);

        using var skImage = SKImage.FromBitmap(resizedBitmap);

        using var data = skImage.Encode(
            SKEncodedImageFormat.Jpeg,
            quality);

        return data.ToArray();
    }
}

