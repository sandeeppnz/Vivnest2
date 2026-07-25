namespace Vivnest.Core.Models;

public class CameraCapturedMessage
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
}