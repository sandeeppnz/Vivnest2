using Vivnest.Core.Models.Camera;

namespace Vivnest.Core.Interfaces;

public interface ICaptureStatusStore
{
    DeviceRuntimeState GetOrAdd(string deviceId);

    bool TryGet(
        string deviceId,
        out DeviceRuntimeState status);
}


