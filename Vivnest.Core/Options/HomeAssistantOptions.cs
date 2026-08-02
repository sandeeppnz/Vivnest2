namespace Vivnest.Core.Options;

public class HomeAssistantOptions
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public List<HomeAssistantEntityOptions> Entities { get; set; } = [];
}
