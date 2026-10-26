namespace Vivnest.Cloud.Options;

// Push alerts to the Vivnest phone app through Expo's push service (ADR-129).
public class ExpoPushOptions
{
    // On by default: with no phones registered it sends nothing, so there is
    // no reason to make enabling it a second step after registering one.
    public bool Enabled { get; set; } = true;

    public string Endpoint { get; set; } = "https://exp.host/--/api/v2/push/send";

    // Optional. Only needed if "enhanced push security" is switched on for
    // the Expo project.
    public string AccessToken { get; set; } = string.Empty;
}
