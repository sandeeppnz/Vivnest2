using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;

namespace Vivnest.Agent.Bootstrap.ConfigurationLoading;

// Every loaded document - shared config, per-agent config, device
// configs, secrets, the accumulated load errors - enters configuration
// the same way: as a JSON stream source inserted immediately BEFORE the
// environment-variables source, so that everything loaded here can
// override appsettings.json while environment variables still override
// everything. Successive insertions land in call order (each goes in at
// the env-vars index, pushing env vars down), so later phases win over
// earlier ones within the loaded block.
internal static class ConfigSourceInsertion
{
    public static void InsertJsonBeforeEnvVars(
        ConfigurationManager configuration,
        byte[] jsonBytes)
    {
        InsertBeforeEnvVars(
            configuration,
            new JsonStreamConfigurationSource
            {
                Stream = new ReusableMemoryStream(jsonBytes)
            });
    }

    private static void InsertBeforeEnvVars(
        ConfigurationManager configuration,
        IConfigurationSource source)
    {
        var sources =
            configuration.Sources;

        var envVarsSourceIndex = -1;

        for (var i = 0; i < sources.Count; i++)
        {
            if (sources[i] is
                EnvironmentVariablesConfigurationSource)
            {
                envVarsSourceIndex = i;
                break;
            }
        }

        if (envVarsSourceIndex >= 0)
        {
            sources.Insert(
                envVarsSourceIndex,
                source);
        }
        else
        {
            sources.Add(source);
        }
    }

    // ConfigurationManager re-reads its sources as the source list keeps
    // changing (each phase here inserts another one), so the stream behind
    // a JsonStreamConfigurationSource is consumed more than once and gets
    // disposed along the way. This one survives that by rewinding instead
    // of closing.
    private sealed class ReusableMemoryStream : MemoryStream
    {
        public ReusableMemoryStream(byte[] buffer)
            : base(buffer)
        {
        }

        protected override void Dispose(
            bool disposing)
        {
            Position = 0;
        }
    }
}
