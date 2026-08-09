namespace Vivnest.Agent.Runtime.Shell;

// TimeZoneInfo.Local.Id already resolves to an IANA name (e.g.
// "Pacific/Auckland") inside the container - see Dockerfile for the tzdata
// setup this depends on. Running the Agent directly on Windows (local dev)
// returns a Windows-style name instead (e.g. "New Zealand Standard Time"),
// which the dashboard's Intl.DateTimeFormat can't parse. Convert it so
// DeviceHeartbeat.Timezone is always IANA regardless of host OS.
public static class LocalTimeZone
{
    public static string IanaId { get; } = Resolve();

    private static string Resolve()
    {
        var id = TimeZoneInfo.Local.Id;

        return TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var ianaId)
            ? ianaId
            : id;
    }
}
