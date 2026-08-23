

namespace Vivnest.Domain.Capabilities;

// Discriminates which single capability a ClassifyCaptureQueueMessage
// carries - see decision-log.md ADR-036. SinkCleanliness and ObjectDetection
// route independently now, each to its own ExecutingAgentId, so a message
// only ever carries one of the two.
// Values are deliberately NOT renumbered and no zero-valued "Unknown"
// member is added. AzureQueuePublisher serialises with no
// JsonSerializerOptions, so these travel as numbers: SinkCleanliness is 0
// and ObjectDetection is 1 on the wire, and shifting them would misroute
// every message already queued. "Absent" is expressed by
// ClassifyCaptureQueueMessage.Capability being nullable instead - which it
// is because a missing field used to deserialise to 0 and silently run
// sink cleanliness.
public enum ClassifyCapability
{
    SinkCleanliness,
    ObjectDetection
}
