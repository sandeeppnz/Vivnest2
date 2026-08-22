namespace Vivnest.Core.Enums;

// Only ever written, via .ToString() on DeviceEventEntity.ProcessingStatus,
// and never parsed back into this enum - so removing a member here can't
// break deserialization of an existing row. The former `Processing` member
// went with MarkProcessingAsync, which had no callers and wasn't on
// IDeviceEventReader, so nothing could ever enter that state.
public enum DeviceEventProcessingStatus
{
    Completed,
    Failed
}
