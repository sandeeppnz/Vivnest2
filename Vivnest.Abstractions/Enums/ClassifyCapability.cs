namespace Vivnest.Abstractions.Enums;

// Discriminates which single capability a ClassifyCaptureQueueMessage
// carries - see decision-log.md ADR-036. SinkCleanliness and ObjectDetection
// route independently now, each to its own ExecutingAgentId, so a message
// only ever carries one of the two.
public enum ClassifyCapability
{
    SinkCleanliness,
    ObjectDetection
}
