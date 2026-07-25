using System;
using System.Collections.Generic;
using System.Text;

namespace Vivnest.Core.Options;

public class StorageOptions
{
    public string ConnectionString { get; set; } = "";
    public string ContainerName { get; set; } = "";
}