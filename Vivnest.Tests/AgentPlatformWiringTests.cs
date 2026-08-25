using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Agent.Commands;
using Vivnest.Agent.Shell;
using Vivnest.Agent.Shell.DeviceHealth;
using Vivnest.Core.DataStores;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Events;
using Vivnest.Core.Options;
using Vivnest.Core.Queues;
using Vivnest.Core.Storage;
using Vivnest.Domain.Agents;
using Vivnest.Domain.Devices;

namespace Vivnest.Tests;

// Three wiring defects from the 2026-08-24 review of Vivnest.Agent.
public class AgentPlatformWiringTests
{
    // ---- one error per failure, not two ----------------------------------

    // Both heartbeat handlers logged the exception AND rethrew it, and the
    // worker above them logged it again. Every Error becomes an
    // ErrorLogged AgentEvent via AgentLogBufferLoggerProvider, so one
    // failed write raised two events and two Cloud-side alerts.
    //
    // The worker's line is the one that survives: it names the same id and
    // carries the same exception.
    //
    // Both tests assert ThrowsAsync as well as the empty log, because
    // "not logged here" is only correct while it is still rethrown.
    // Swallowing would also strand the transition - DeviceHeartbeatWorker
    // sets LastReportedStatus before publishing, so a silent failure means
    // the status change is never retried.
    [Fact]
    public async Task AFailedAgentHeartbeatWriteIsNotLoggedByTheHandler()
    {
        var log = new RecordingLogger<AgentHeartbeatHandler>();

        var handler = new AgentHeartbeatHandler(
            log,
            Options.Create(new MessagingOptions()),
            new ThrowingAgentHeartbeatWriter(),
            new NullQueuePublisher());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(
                new AgentHeartbeatGeneratedEvent(new AgentHeartbeat { AgentId = "agent-1", TenantId = "tenant-1", SiteId = "site-1" }),
                CancellationToken.None));

        Assert.Empty(log.Errors);
    }

    [Fact]
    public async Task AFailedDeviceHeartbeatWriteIsNotLoggedByTheHandler()
    {
        var log = new RecordingLogger<DeviceHeartbeatHandler>();

        var handler = new DeviceHeartbeatHandler(
            log,
            Options.Create(new MessagingOptions()),
            new ThrowingDeviceHeartbeatWriter(),
            new NullQueuePublisher());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(
                new DeviceHeartbeatGeneratedEvent(new DeviceHeartbeat { DeviceId = "device-1", DeviceType = DeviceType.Camera, AgentId = "agent-1", TenantId = "tenant-1", SiteId = "site-1" }),
                CancellationToken.None));

        Assert.Empty(log.Errors);
    }

    // ---- the interface that exists to make this testable -----------------

    // IBlobStorageClient was extracted "so the code that writes runtime
    // configuration can be tested at all". These are that code, and they
    // took the concrete class anyway.
    [Theory]
    [InlineData(typeof(LogShippingWorker))]
    [InlineData(typeof(ApplyConfigurationCommandHandler))]
    [InlineData(typeof(RefreshConfigurationCommandHandler))]
    public void BlobConsumersDependOnTheInterfaceNotTheConcreteClient(Type type)
    {
        var parameters = type
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(p => p.ParameterType)
            .ToList();

        Assert.DoesNotContain(parameters, p => p.Name == "AzureBlobStorageClient");

        Assert.Contains(parameters, p => p == typeof(IBlobStorageClient));
    }

    // ---- pooled HTTP, not a socket per command ---------------------------

    [Theory]
    [InlineData(typeof(CommandPollingWorker))]
    [InlineData(typeof(AgentCommandPollingWorker))]
    public void CommandWorkersResolveTheirHttpClientFromTheFactory(Type worker)
    {
        var parameters = worker
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(p => p.ParameterType)
            .ToList();

        Assert.Contains(parameters, p => p == typeof(IHttpClientFactory));
    }

    // The tripwire for the next one. Scoped to Vivnest.Agent: the Updater
    // is a separate short-lived process with no host or container, where
    // newing one is defensible.
    [Fact]
    public void NothingInTheAgentNewsUpAnHttpClient()
    {
        var offenders = new List<string>();
        var root = FindRepoRoot();

        foreach (var file in Directory.EnumerateFiles(
            Path.Combine(root, "Vivnest.Agent"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var code = File.ReadAllLines(file)
                .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal));

            if (string.Join('\n', code).Contains("new HttpClient(", StringComparison.Ordinal))
                offenders.Add(Path.GetFileName(file));
        }

        Assert.True(
            offenders.Count == 0,
            "These files create an HttpClient directly:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders.Select(o => "  - " + o))
            + Environment.NewLine + Environment.NewLine
            + $"Use IHttpClientFactory.CreateClient(CloudApiHttpClient.Name) instead - "
            + "the project already resolves Home Assistant's client that way.");
    }

    // =======================================================================

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Vivnest.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    private sealed class ThrowingAgentHeartbeatWriter : IAgentHeartbeatWriter
    {
        public Task<AgentHeartbeatEntity> SaveAsync(
            AgentHeartbeat heartbeat, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the table is unavailable");
    }

    private sealed class ThrowingDeviceHeartbeatWriter : IDeviceHeartbeatWriter
    {
        public Task<DeviceHeartbeatEntity> SaveAsync(
            DeviceHeartbeat heartbeat, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the table is unavailable");
    }

    private sealed class NullQueuePublisher : IQueuePublisher
    {
        public Task PublishAsync<T>(
            string queueName, T message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
                Errors.Add(formatter(state, exception));
        }
    }
}
