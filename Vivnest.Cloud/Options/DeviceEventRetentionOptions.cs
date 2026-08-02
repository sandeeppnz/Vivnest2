namespace Vivnest.Cloud.Options;

public class DeviceEventRetentionOptions
{
    public bool Enabled { get; set; }

    // Keep in sync with devops/blob-lifecycle/blob-lifecycle-policy.json's
    // daysAfterModificationGreaterThan - two separate systems (Blob
    // Lifecycle Management vs this Table cleanup), no automatic link
    // between them, see devops/blob-lifecycle/README.md.
    public int RetentionDays { get; set; } = 30;
}
