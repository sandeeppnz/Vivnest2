namespace Vivnest.Runtime.State;

public interface IDeviceRuntimeStateStore
{
    DeviceRuntimeState GetOrAdd(string deviceId);
}


