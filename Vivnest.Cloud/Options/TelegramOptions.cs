namespace Vivnest.Cloud.Options;

public class TelegramOptions
{
    public bool Enabled { get; set; }

    public string BotToken { get; set; } = string.Empty;

    public string ChatId { get; set; } = string.Empty;
}
