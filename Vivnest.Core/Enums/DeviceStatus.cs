namespace Vivnest.Core.Enums;

// Device lifecycle (decision-log.md ADR-058) - replaces the old Enabled
// bool. A retired device's identity must remain stable (its DeviceId may
// still be referenced by historical DeviceCapability assignments/events),
// same reasoning MachineStatus already established for Machine - so
// Device has no hard delete, only a terminal Retired state.
public enum DeviceStatus
{
    Active,
    Disabled,
    Retired
}
