namespace Vivnest.Core.Models;

public class CameraCapturedMessage
{
    public string PartitionKey { get; set; } = default!;
    public string RowKey { get; set; } = default!;
}