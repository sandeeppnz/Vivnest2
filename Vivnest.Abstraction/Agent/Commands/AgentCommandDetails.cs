using System.Text.Json.Serialization;
namespace Vivnest.Abstraction.Agent.Commands;

// The Agent's own local copy of the command detail shape fetched via
// GET /agents/{agentId}/commands/{commandId} - mirrors Vivnest.Cloud's
// AgentCommandDto field-for-field, but declared independently since
// Vivnest.Agent doesn't (and shouldn't) reference Vivnest.Cloud. Only the
// fields handlers actually need are kept - Status/ExpiresUtc (decision-log.md
// ADR-082) exist purely for PlatformAgentCommandPollingWorker's own pre-execution
// check, not for any ICommandHandler implementation to read.
public sealed record AgentCommandDetails(
    string CommandId,
    string TargetAgentId,
    string? TargetDeviceId,
    // ADR-102 - the RUNTIME identity (camera.capture), not the catalogue
    // GUID: GetCommand translates on the way out, so by the time a command
    // reaches this record it is named the way the capability registry
    // names things. The property is renamed to match its value; a
    // CapabilityId holding "camera.capture" was actively misleading.
    //
    // The JSON name stays "capabilityId" because that is what Cloud's
    // AgentCommandDto still serializes. Without this attribute the rename
    // would bind nothing - PropertyNameCaseInsensitive does not bridge
    // CapabilityKey to capabilityId - and every command would arrive with
    // a null capability and fail as CAPABILITY_NOT_FOUND. The wire rename
    // is a separate step (1.10).
    [property: JsonPropertyName("capabilityId")]
    string? CapabilityKey,
    string CommandType,
    string? Payload,
    string Status,
    DateTime ExpiresUtc);
