namespace Vivnest.Core.Options;

public class HomeAssistantOptions
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = string.Empty;

    // The ONLY credential the Home Assistant integration uses - both the
    // WebSocket handshake (HomeAssistantWorker.AuthenticateAsync) and the
    // REST client (HomeAssistantCommandSender) authenticate with a
    // long-lived access token. Username/Password fields used to sit
    // alongside this with no reader anywhere; a dead Password field is an
    // invitation to put a real password into shared config, where this
    // hand-authored section is not routed through EncryptFields and it
    // would sit in plaintext for nothing to ever consume.
    public string AccessToken { get; set; } = string.Empty;

    public List<HomeAssistantEntityOptions> Entities { get; set; } = [];
}
