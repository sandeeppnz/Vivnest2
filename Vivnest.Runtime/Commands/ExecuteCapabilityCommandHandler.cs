using Microsoft.Extensions.Logging;
using Vivnest.Abstractions.Commands;
using Vivnest.Abstractions.Constants;
using Vivnest.Abstractions.Enums;
using Vivnest.Abstractions.Events;
using Vivnest.Agent.Capabilities.Triggers;
using Vivnest.Core.Utils;

namespace Vivnest.Agent.Runtime.Commands;

// Decision-log.md ADR-081 (Phase 9 Pass 3) - scoped to ImageCapture only,
// matching every one of the spec's own worked examples. Reuses the
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
    private readonly IEventDispatcher _eventDispatcher;
    private readonly ILogger<ExecuteCapabilityCommandHandler> _logger;

    public ExecuteCapabilityCommandHandler(
        IDeviceRuntimeStore deviceRegistry,
        IEventDispatcher eventDispatcher,
        ILogger<ExecuteCapabilityCommandHandler> logger)
    {
        _deviceRegistry = deviceRegistry;
        _eventDispatcher = eventDispatcher;
        _logger = logger;
    }

    public string CommandType => AgentCommandTypes.ExecuteCapability;

    public async Task<CommandHandlerResult> HandleAsync(
        AgentCommandDetails command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.TargetDeviceId) || string.IsNullOrWhiteSpace(command.CapabilityId))
            return CommandHandlerResult.Failed("INVALID_REQUEST", "ExecuteCapability requires TargetDeviceId and CapabilityId.");

        if (!string.Equals(command.CapabilityId, AgentCommandTypes.ImageCaptureCapabilityId, StringComparison.Ordinal))
        {
            // Decision-log.md ADR-081 - Cloud already authorized this (a
            // real DeviceCapability assignment exists, ExecutingAgentId
            // matches), but no on-demand execution handler exists for a
            // Derived capability yet - this proves the full authorization
            // chain without a second real execution pipeline this pass.
            return CommandHandlerResult.Failed(
                "CAPABILITY_UNAVAILABLE", $"No execution handler for capability {command.CapabilityId} yet.");
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

        await _eventDispatcher.DispatchAsync(
            new DeviceTriggeredEvent(command.TargetDeviceId, DeviceType.Camera, "Command", DateTime.UtcNow),
            cancellationToken);

        _logger.LogInformation(
            "ExecuteCapability command {CommandId}: triggered ImageCapture for device {DeviceId}.",
            command.CommandId, command.TargetDeviceId);

        return CommandHandlerResult.Succeeded("Capture triggered.");
    }
}
