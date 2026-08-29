namespace Vivnest.Core.Options;

public class StorageOptions
{
    public string ConnectionString { get; set; } = "";

    // Canonical default (ADR-120) - the capture container has been
    // "photos" in every environment since day one; absence in config
    // means the canonical name, not a broken empty string.
    public string BlobContainer { get; set; } = "photos";
}
