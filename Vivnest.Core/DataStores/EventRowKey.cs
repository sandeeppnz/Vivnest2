using System.Globalization;

namespace Vivnest.Core.DataStores;

// Both event tables (tblDeviceEvents, tblAgentEvents) key rows by
// timestamp-then-uniquifier, so a RowKey range filter doubles as a time
// range filter - IDeviceEventReader.GetByDeviceAndDateRangeAsync depends
// on exactly this, scanning a RowKey range instead of a whole partition.
//
// That format was written out independently at six call sites across two
// assemblies (the two Agent-side writers in Vivnest.Infrastructure, the
// two audit writes in the Cloud config publishers, and two more in
// HealthMonitorService). All six agreed, but nothing made them agree -
// and a single site drifting on the format, or on the zero-padding of the
// timestamp, would silently break date-range queries for the rows it
// wrote rather than throwing anywhere.
//
// The uniquifier differs by caller on purpose: an Agent-originated event
// already carries its own EventId, so passing it through keeps the row
// traceable to the event object; Cloud-originated rows have no such id
// and mint one.
public static class EventRowKey
{
    private const string TimestampFormat = "yyyyMMddHHmmssfff";

    // InvariantCulture is load-bearing, not decoration. All six original
    // call sites used an interpolated $"{t:yyyyMMddHHmmssfff}", which
    // formats under CurrentCulture - and "yyyy" is the year *in the
    // culture's own calendar*. On a th-TH host (Buddhist calendar) the
    // same instant renders as 2569, not 2026, so those rows would sort
    // and range-filter wrongly against every row written elsewhere.
    // Verified directly: formatting 2026-08-17 under th-TH produces
    // "25690817...". Harmless on the Gregorian cultures this actually runs
    // under today (and identical output there, so this is a no-op for
    // every existing row), but there is no reason for a storage key to
    // depend on host culture at all.
    public static string For(DateTime occurredAtUtc, Guid eventId) =>
        $"{occurredAtUtc.ToString(TimestampFormat, CultureInfo.InvariantCulture)}-{eventId}";

    public static string New(DateTime occurredAtUtc) =>
        For(occurredAtUtc, Guid.NewGuid());
}
