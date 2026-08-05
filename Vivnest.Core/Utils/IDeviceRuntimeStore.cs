using Vivnest.Core.Enums;
using Vivnest.Core.Options;

namespace Vivnest.Core.Utils;

public interface IDeviceRuntimeStore
{
    // A DeviceId can now have more than one entry (one per capability it
    // exposes - see docs/architecture/dashboard-domain-model.md §3), so a
    // lookup that only cares about one specific capability must say which
    // one it wants.
    DeviceOptions GetDevice(string id, DeviceType type);

    // Every entry configured for this id, regardless of type - for callers
    // that don't know (or don't care) which capability a target belongs to,
    // e.g. resolving a trigger's target before knowing what it can do.
    IReadOnlyCollection<DeviceOptions> GetDevices(string id);

    IReadOnlyCollection<DeviceOptions> GetDevices();
}
