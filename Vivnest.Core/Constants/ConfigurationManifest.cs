namespace Vivnest.Core.Constants;

// The current.json manifest shape (decision-log.md ADR-069) - a
// lightweight pointer at DeviceConfigBlob/AgentConfigBlob.ManifestBlobName(id)
// referencing the latest immutable version blob at
// DeviceConfigBlob/AgentConfigBlob.VersionBlobName(id, version). Shared
// between Vivnest.Cloud (writes it) and Vivnest.Agent (reads it) - both
// already reference Vivnest.Core, so this is the one place the wire shape
// is declared rather than two independent copies drifting apart.
public sealed record ConfigurationManifest(
    int ConfigurationVersion,
    string ConfigurationHash,
    string ConfigurationUri,
    DateTime PublishedUtc);
