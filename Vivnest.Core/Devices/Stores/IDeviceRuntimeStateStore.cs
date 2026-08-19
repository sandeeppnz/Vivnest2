namespace Vivnest.Core.Devices.Stores;

public interface IDeviceRuntimeStateStore
{
    DeviceRuntimeState GetOrAdd(string deviceId);
}


