using System.Globalization;
using Vivnest.Core.DataStores;

namespace Vivnest.Tests;

// RowKey doubles as the time-range filter for
// IDeviceEventReader.GetByDeviceAndDateRangeAsync, which scans a RowKey
// range instead of a whole partition. So the format is not cosmetic: a
// drift in it silently breaks date-range queries for the affected rows
// rather than throwing anywhere.
public class EventRowKeyTests
{
    private static readonly DateTime Sample =
        new(2026, 8, 17, 9, 5, 3, 7, DateTimeKind.Utc);

    [Fact]
    public void FormatIsTimestampThenUniquifier()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");

        Assert.Equal($"20260817090503007-{id}", EventRowKey.For(Sample, id));
    }

    [Fact]
    public void KeysSortChronologically()
    {
        var earlier = EventRowKey.For(Sample, Guid.NewGuid());
        var later = EventRowKey.For(Sample.AddMilliseconds(1), Guid.NewGuid());

        Assert.True(string.CompareOrdinal(earlier, later) < 0);
    }

    // The format previously came from an interpolated
    // $"{t:yyyyMMddHHmmssfff}" at six separate call sites, which formats
    // under CurrentCulture - and "yyyy" is the year in that culture's own
    // calendar. On a th-TH host the same instant rendered as 2569 (Buddhist)
    // and on ar-SA as 1448 (Umm al-Qura), a different date entirely. Those
    // rows would sort and range-filter wrongly against every row written
    // elsewhere.
    [Theory]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    [InlineData("en-US")]
    [InlineData("")]
    public void FormatDoesNotDependOnHostCulture(string cultureName)
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);

            Assert.StartsWith("20260817090503007-", EventRowKey.For(Sample, Guid.NewGuid()));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void NewMintsADistinctKeyPerCall()
    {
        Assert.NotEqual(EventRowKey.New(Sample), EventRowKey.New(Sample));
    }
}
