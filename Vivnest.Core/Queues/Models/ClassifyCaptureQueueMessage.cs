using Vivnest.Core.Options;
using Vivnest.Domain.Capabilities;
using Vivnest.Domain.Devices;

namespace Vivnest.Core.Queues.Models;

// Flows unchanged through both legs of the Cloud-mediated cross-process
// AI-inference route (decision-log.md ADR-035, ADR-034's design 3): the
// capturing agent publishes this to ClassifyRequestQueue,
// ClassifyRequestFunction relays it byte-for-byte onto
// ClassifyCommandQueue, the High-type agent consumes it from there.
//
// Deliberately not following ADR-004's {PartitionKey, RowKey}-only shape -
// same exception RestartCommandQueueMessage already established: there is
// no persisted row to reference, this message *is* the payload.
//
// AgentId is the addressee (the High-type agent this is routed to, matching
// RestartCommandQueueMessage's own AgentId-as-addressee convention).
// OriginAgentId/OriginTenantId/OriginSiteId are the *capturing* agent's
// identity - needed because whichever agent classifies this capture must
// stamp the resulting DeviceEvent with the device's own agent, not its
// own, once classification no longer runs in the same process that
// captured the photo.
//
// One message now carries exactly one capability's work (ADR-036) - since
// SinkCleanliness and ObjectDetection can route to different High-type
// agents, there's no longer a single addressee for a combined message.
// Capability says which; only the matching Roi field is populated, the
// other is null. SinkCleanlinessRoi/ObjectDetectionRoi carry only the
// camera-specific half of that capability's config (whether it's on, and
// where the ROI is) - the ADR-035 follow-up split. The receiving High-type
// agent merges the
// populated one with its own locally-configured
// SinkCleanlinessModelOptions/ObjectDetectionModelOptions (looked up by
// DeviceId) into the full SinkCleanlinessOptions/ObjectDetectionOptions
// the classifier/detector actually need - see AiClassificationOptions.
public sealed record ClassifyCaptureQueueMessage(
    string AgentId,
    string OriginAgentId,
    string OriginTenantId,
    string OriginSiteId,
    string DeviceId,
    ClassifyCapability Capability,
    string BlobContainer,
    string BlobName,
    DateTime CapturedAtUtc,
    SinkCleanlinessRoiOptions? SinkCleanlinessRoi,
    ObjectDetectionRoiOptions? ObjectDetectionRoi,
    DateTime IssuedAtUtc);
