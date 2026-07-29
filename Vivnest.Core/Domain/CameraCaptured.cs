using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vivnest.Core.Interfaces;
using Vivnest.Core.Models.Camera;

namespace Vivnest.Core.Domain;

public record CameraCaptured(
    CaptureResult Result) : IMessage
{
    public Task PublishAsync<T>(T message)
    {
        throw new NotImplementedException();
    }
}