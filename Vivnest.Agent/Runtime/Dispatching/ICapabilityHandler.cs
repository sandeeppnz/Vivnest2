using Microsoft.Azure.Amqp;
using System;
using System.Collections.Generic;
using System.Text;

namespace Vivnest.Agent.Runtime.Dispatching;

internal interface ICapabilityHandler<TEvent>
{
    Task HandleAsync(
        TEvent @event,
        CancellationToken cancellationToken);
}
