using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Agent.Interfaces;
using Vivnest.Agent.Runtime.Events;
using Vivnest.Core.Domain;
using Vivnest.Core.Options;

namespace Vivnest.Agent.Runtime.Workers;

public sealed class AgentHeartbeatWorker : BackgroundService
{
    private readonly ILogger<AgentHeartbeatWorker> _logger;
    private readonly IEventHandler<AgentHeartbeatGeneratedEvent> _handler;
    private readonly IHomeAssistantConnectionTracker _homeAssistantConnectionTracker;
    private readonly AgentOptions _agentOptions;
    private readonly AgentHeartbeatOptions _heartbeatOptions;

    private readonly DateTime _startedUtc = DateTime.UtcNow;

    public AgentHeartbeatWorker(
        IOptions<AgentOptions> agentOptions,
        IOptions<AgentHeartbeatOptions> heartbeatOptions,
        IEventHandler<AgentHeartbeatGeneratedEvent> handler,
        IHomeAssistantConnectionTracker homeAssistantConnectionTracker,
        ILogger<AgentHeartbeatWorker> logger)
    {
        _agentOptions = agentOptions.Value;
        _heartbeatOptions = heartbeatOptions.Value;
        _logger = logger;
        _handler = handler;
        _homeAssistantConnectionTracker = homeAssistantConnectionTracker;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_heartbeatOptions.Enabled)
        {
            _logger.LogInformation("Agent heartbeat disabled.");
            return;
        }

        _logger.LogInformation(
              "AgentHeartbeatWorker for {Delay}. Current: Local={NowLocal:yyyy-MM-dd HH:mm:ss}, UTC={NowUtc:yyyy-MM-dd HH:mm:ss}Z. Next heartbeat: Local={NextLocal:yyyy-MM-dd HH:mm:ss}, UTC={NextUtc:yyyy-MM-dd HH:mm:ss}Z",
              _heartbeatOptions.HeartbeatInterval,
              DateTime.Now,
              DateTime.UtcNow,
              DateTime.Now.Add(_heartbeatOptions.HeartbeatInterval),
              DateTime.UtcNow.Add(_heartbeatOptions.HeartbeatInterval));

        using var timer = new PeriodicTimer(_heartbeatOptions.HeartbeatInterval);

        do
        {
            try
            {
                var heartbeat = new AgentHeartbeat
                {
                    AgentId = _agentOptions.AgentId,
                    TenantId = _agentOptions.TenantId,
                    SiteId = _agentOptions.SiteId,
                    StartedUtc = _startedUtc,
                    LastHeartbeatUtc = DateTime.UtcNow,
                    HostName = Environment.MachineName,
                    FirmwareVersion = _agentOptions.FirmwareVersion,
                    Error = null,
                    HeartbeatInterval = _heartbeatOptions.HeartbeatInterval,
                    HomeAssistantLastConnectedUtc = _homeAssistantConnectionTracker.LastConnectedUtc
                };

                await _handler.HandleAsync(
                    new AgentHeartbeatGeneratedEvent(heartbeat),
                    stoppingToken);

                _logger.LogDebug(
                    "Agent heartbeat updated for {AgentId}",
                    heartbeat.AgentId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update agent heartbeat.");
            }

        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
