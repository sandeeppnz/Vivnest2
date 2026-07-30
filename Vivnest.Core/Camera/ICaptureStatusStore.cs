using Vivnest.Core.Camera.Models;

namespace Vivnest.Core.Camera;

public interface ICaptureStatusStore
{
    DeviceRuntimeState GetOrAdd(string deviceId);

    bool TryGet(
        string deviceId,
        out DeviceRuntimeState status);
}


