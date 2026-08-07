using Vivnest.Core.Options;

namespace Vivnest.Agent.Capabilities.Camera;

// Handed from SinkCleanlinessHandler to SinkCleanlinessWorker over an
// in-process Channel<T> (ADR-034) - just enough to download and classify
// the capture without SinkCleanlinessWorker needing DeviceRuntimeStore
// or DeviceOptions itself.
public sealed record SinkCleanlinessWorkItem(
    string DeviceId,
    string BlobContainer,
    string BlobName,
    DateTime CapturedAtUtc,
    SinkCleanlinessOptions Options);
