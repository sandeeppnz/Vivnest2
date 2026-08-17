namespace Vivnest.Core.Camera.Stores;

public interface ICaptureStatusStore
{
    DeviceRuntimeState GetOrAdd(string deviceId);
}


