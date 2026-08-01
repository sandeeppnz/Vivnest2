namespace Vivnest.Core.Storage;

/// <summary>
/// Azure.Data.Tables writes TimeSpan entity properties as ISO-8601 duration
/// strings (e.g. "PT1M") but fails to parse its own output back on read -
/// TimeSpan.Parse doesn't understand ISO-8601 duration syntax, so the SDK's
/// deserializer silently defaults the property to zero instead of throwing.
/// TimeSpan properties on table entities are stored as plain strings
/// (TimeSpan.ToString()) instead, converted at the read/write boundary here.
/// </summary>
public static class TableTimeSpan
{
    public static string ToStorageString(TimeSpan value) => value.ToString();

    public static TimeSpan Parse(string? value)
    {
        return TimeSpan.TryParse(value, out var result) ? result : TimeSpan.Zero;
    }
}
