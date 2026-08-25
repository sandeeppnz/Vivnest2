namespace Vivnest.Agent.Bootstrap.ConfigurationLoading;

// Collects configuration-load failures across every phase of
// AgentConfigurationLoader so they can be published as
// ConfigurationLoadErrors at the end of the load - which
// AgentConfigMetadataOptions binds and AgentHeartbeatWorker sends to
// Cloud on every heartbeat. Everything here runs BEFORE the host is
// built, so there is no ILogger - which is why Report also talks to
// Console: that line is for whoever is watching the container.
//
// One instance per load, created by LoadAsync and passed to each phase.
// (This used to be a static List<string> cleared on entry; an instance
// says the same thing without the shared mutable state.)
internal sealed class StartupErrorSink
{
    private readonly List<string> _errors = [];

    public IReadOnlyList<string> Errors => _errors;

    // Reports a failure to both places: the console, for someone watching
    // the container, and the accumulator, for Cloud. Informational lines
    // ("... not set; skipping") deliberately stay plain Console.WriteLine -
    // a setting that was never configured is not a fault to report.
    public void Report(string message)
    {
        Console.WriteLine(message);

        _errors.Add(message.Replace("[Startup] ", ""));
    }

    // For call sites that print their own console line (or share one with
    // a neighbouring path) and only need the error recorded for Cloud.
    public void Add(string message)
    {
        _errors.Add(message);
    }
}
