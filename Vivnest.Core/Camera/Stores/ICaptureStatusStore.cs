namespace Vivnest.Core.Camera.Stores;

public interface ICaptureStatusStore
{
    DeviceRuntimeState GetOrAdd(string deviceId);

    bool TryGet(
        string deviceId,
        out DeviceRuntimeState status);
}


