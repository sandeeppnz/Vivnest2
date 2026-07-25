using System;
using System.Collections.Generic;
using System.Text;
using Vivnest.Core.Models;

namespace Vivnest.Cloud.Interfaces;

public interface ICameraCapturedHandler
{
    Task HandleAsync(
        CameraCapturedMessage message,
        CancellationToken cancellationToken = default);
}
