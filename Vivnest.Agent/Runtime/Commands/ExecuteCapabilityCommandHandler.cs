using Microsoft.Extensions.Logging;
using Vivnest.Abstraction.Agent.Commands;
using Vivnest.Abstraction.Agent.Events;
using Vivnest.Agent.Capabilities.Triggers;
using Vivnest.Core.Constants;
using Vivnest.Core.Enums;
using Vivnest.Core.Utils;
using Vivnest.Runtime.Capabilities;

// Two enums are named CapabilityStatus: the catalogue's
// (Vivnest.Core.Enums - Active/Retired, admin lifecycle) and the runtime's
// (Vivnest.Abstraction.Agent.Capabilities - Registered/Starting/Running/...).
// This file needs both namespaces, so the runtime one is aliased rather
// than left to whichever using happens to win.
using RuntimeCapabilityStatus = Vivnest.Abstraction.Agent.Capabilities.CapabilityStatus;

namespace Vivnest.Agent.Runtime.Commands;

// Routes by capability, not by hard-coded identity (ADR-102, Command
// Routing 1.7). The handler names no capability: it looks one up in the
// registry by the runtime key the command carries, checks it is running,
// and checks the capability itself advertises the command. Adding a
// capability therefore needs no change here.
//
// It deliberately stops at SELECTION. Execution continues through the
// existing DeviceTriggeredEvent path, because CameraCaptureExecutor is
// already the one shared capture implementation - extracted precisely so
// CameraCaptureWorker and CaptureOnTriggerHandler would not drift into two
// copies. Invoking a capability directly here would recreate exactly that
// split.
//
// Original note, still true of the execution half: reuses the
// existing motion-triggered-capture path verbatim: publishing
// DeviceTriggeredEvent is enough - CaptureOnTriggerHandler (unchanged,
// already registered) does the real work (wakes CameraCaptureWorker,
// fires an immediate capture), which already flows into
// CameraCaptureCompletedEvent -> CameraCaptureHandler -> a persisted
// DeviceEvent: CameraCaptured. Reports Succeeded right after publishing,
// optimistically - actual capture completion is async and confirmed
// separately via that DeviceEvent, not by this command's own status.
public sealed class ExecuteCapabilityCommandHandler : ICommandHandler
{
    private readonly IDeviceRuntimeStore _deviceRegistry;
    private readonly ICapabilityRegistry _capabilityRegistry;
    private readonly IEventDispatcher _eventDispatcher;
    private readonly ILogger<ExecuteCapabilityCommandHandler> _logger;

    public ExecuteCapabilityCommandHandler(
        IDeviceRuntimeStore deviceRegistry,
        ICapabilityRegistry capabilityRegistry,
        IEventDispatcher eventDispatcher,
        ILogger<ExecuteCapabilityCommandHandler> logger)
    {
        _deviceRegistry = deviceRegistry;
        _capabilityRegistry = capabilityRegistry;
        _eventDispatcher = eventDispatcher;
        _logger = logger;
    }

    public string CommandType => AgentCommandTypes.ExecuteCapability;

    public async Task<CommandHandlerResult> HandleAsync(
        AgentCommandDetails command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.TargetDeviceId) || string.IsNullOrWhiteSpace(command.CapabilityKey))
            return CommandHandlerResult.Failed("INVALID_REQUEST", "ExecuteCapability requires TargetDeviceId and CapabilityKey.");

        // Capability checks come before the device check on purpose: a
        // command naming a capability this Agent does not have should fail
        // as a capability error, not fall through into device validation
        // and report something about cameras.
        var capability = _capabilityRegistry.Get(command.CapabilityKey);

        if (capability is null)
        {
            return CommandHandlerResult.Failed(
                "CAPABILITY_NOT_FOUND",
                $"Capability '{command.CapabilityKey}' is not registered in this Agent.");
        }

        // Registered is not the same as runnable. A capability that exists
        // but was never selected for startup (disabled assignment, failed
        // start) cannot service a command, and saying so distinctly is the
        // difference between "you asked for something I don't have" and
        // "I have it but it isn't running".
        if (capability.Status != RuntimeCapabilityStatus.Running)
        {
            return CommandHandlerResult.Failed(
                "CAPABILITY_UNAVAILABLE",
                $"Capability '{command.CapabilityKey}' is {capability.Status}, not Running.");
        }

        // The manifest is the routing table: a capability advertises what
        // it accepts, and the handler checks that rather than knowing it.
        //
        // The wire carries no command name of its own - ExecuteCapabilityRequest
        // is (TargetDeviceId, CapabilityId) - so the command being invoked
        // IS the capability key. When a command name is added to the
        // contract this comparison is where it goes, and nothing else here
        // changes.
        var supported = capability.Manifest.Commands.Any(descriptor =>
            string.Equals(descriptor.Name, command.CapabilityKey, StringComparison.OrdinalIgnoreCase));

        if (!supported)
        {
            return CommandHandlerResult.Failed(
                "COMMAND_NOT_SUPPORTED",
                $"Capability '{command.CapabilityKey}' does not declare command '{command.CapabilityKey}'.");
        }

        // Defense in depth, not the primary check - Cloud already
        // validated TargetAgentId == Device.AgentId at dispatch time.
        // This just confirms the device is actually one this process has
        // loaded before firing an event nothing would handle.
        try
        {
            _deviceRegistry.GetDevice(command.TargetDeviceId, DeviceType.Camera);
        }
        catch (KeyNotFoundException)
        {
            return CommandHandlerResult.Failed(
                "DEVICE_NOT_FOUND", $"Device {command.TargetDeviceId} is not a Camera this Agent owns.");
        }

        await _eventDispatcher.PublishAsync(
            new DeviceTriggeredEvent(command.TargetDeviceId, DeviceType.Camera, "Command", DateTime.UtcNow),
            cancellationToken);

        _logger.LogInformation(
            "ExecuteCapability command {CommandId}: routed to capability {CapabilityKey}, " +
            "triggered for device {DeviceId}.",
            command.CommandId, command.CapabilityKey, command.TargetDeviceId);

        return CommandHandlerResult.Succeeded("Capture triggered.");
    }
}
