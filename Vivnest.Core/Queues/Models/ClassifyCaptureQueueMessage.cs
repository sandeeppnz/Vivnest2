using Vivnest.Core.Options;

namespace Vivnest.Core.Queues.Models;

// Flows unchanged through both legs of the Cloud-mediated cross-process
// AI-inference route (decision-log.md ADR-035, ADR-034's design 3): the
// capturing agent publishes this to ClassifyRequestQueue,
// ClassifyRequestFunction relays it byte-for-byte onto
// ClassifyCommandQueue, the Ai-role agent consumes it from there.
//
// Deliberately not following ADR-004's {PartitionKey, RowKey}-only shape -
// same exception RestartCommandQueueMessage already established: there is
// no persisted row to reference, this message *is* the payload.
//
// AgentId is the addressee (the Ai-role agent this is routed to, matching
// RestartCommandQueueMessage's own AgentId-as-addressee convention).
// OriginAgentId/OriginTenantId/OriginSiteId are the *capturing* agent's
// identity - needed because whichever agent classifies this capture must
// stamp the resulting DeviceEvent with the device's own agent, not its
// own, once classification no longer runs in the same process that
// captured the photo.
//
// SinkCleanlinessRoi/ObjectDetectionRoi carry only the camera-specific
// half of each capability's config (whether it's on, and where the ROI
// is) - the ADR-035 follow-up split. The receiving Ai-agent merges these
// with its own locally-configured SinkCleanlinessModelOptions/
// ObjectDetectionModelOptions (looked up by DeviceId) into the full
// SinkCleanlinessOptions/ObjectDetectionOptions the classifier/detector
// actually need - see AiClassificationOptions.
public sealed record ClassifyCaptureQueueMessage(
    string AgentId,
    string OriginAgentId,
    string OriginTenantId,
    string OriginSiteId,
    string DeviceId,
    string BlobContainer,
    string BlobName,
    DateTime CapturedAtUtc,
    SinkCleanlinessRoiOptions SinkCleanlinessRoi,
    ObjectDetectionRoiOptions? ObjectDetectionRoi,
    DateTime IssuedAtUtc);
