using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vivnest.Agent.Bootstrap.ConfigurationLoading;
using Vivnest.Domain.Agents;

namespace Vivnest.Agent.Bootstrap;

// The Agent's configuration load, which runs BEFORE the host is built.
// This class owns only the phase ordering; the phases themselves live in
// Bootstrap/ConfigurationLoading, one class per document family:
//
//   ConfigDecryptor     the credential-encryption key and enc:v1: handling
//   RemoteConfigLoader  shared + per-agent config from blob storage
//   LocalConfigLoader   the same documents from disk (LoadLocalSettings),
//                       plus the always-local *.secrets.json overlays
//   DeviceConfigLoader  the "Devices" section for a Low agent, with its
//                       last-known-good cache
//
// All of them push what they load through ConfigSourceInsertion (before
// the env-vars source, so env vars still win) and report failures into
// one StartupErrorSink, published here as ConfigurationLoadErrors -
// which AgentConfigMetadataOptions binds and AgentHeartbeatWorker sends
// to Cloud on every heartbeat.
public static class AgentConfigurationLoader
{
    public static async Task LoadAsync(
        ConfigurationManager configuration)
    {
        var errors = new StartupErrorSink();

        // The key must come from the bootstrap configuration that already
        // exists before remote configuration is loaded.
        var decryptor =
            ConfigDecryptor.FromConfiguration(configuration, errors);

        var localLoader =
            new LocalConfigLoader(configuration, decryptor, errors);

        // Load either local or remote public configuration.
        if (configuration.GetValue<bool>("LoadLocalSettings"))
        {
            localLoader.TryLoadSharedConfig();
            localLoader.TryLoadAgentConfig();
        }
        else
        {
            var remoteLoader =
                new RemoteConfigLoader(configuration, decryptor, errors);

            await remoteLoader.TryLoadSharedConfigAsync();
            await remoteLoader.TryLoadAgentConfigAsync();
        }

        // Secrets are always local-only.
        localLoader.TryLoadSharedSecrets();
        localLoader.TryLoadAgentSecrets(
            configuration["Agent:AgentId"] ?? "");

        // Determine agent type.
        //
        // Defaults to Low to preserve the existing behaviour.
        var agentType = GetAgentType(configuration);

        // Low agents load their device configuration.
        if (agentType == AgentType.Low)
        {
            await new DeviceConfigLoader(configuration, decryptor, errors)
                .TryLoadAsync(
                    configuration["Agent:AgentId"] ?? "");
        }

        PublishStartupErrors(configuration, errors);
    }

    // Last, so it sees every phase's failures. A startup problem that
    // used to exist only in the container's stdout now reaches the
    // dashboard via the heartbeat.
    private static void PublishStartupErrors(
        ConfigurationManager configuration,
        StartupErrorSink errors)
    {
        if (errors.Errors.Count == 0)
            return;

        var root =
            new JsonObject
            {
                ["ConfigurationLoadErrors"] =
                    new JsonArray(
                        errors.Errors
                            .Select(e => (JsonNode?)JsonValue.Create(e))
                            .ToArray())
            };

        ConfigSourceInsertion.InsertJsonBeforeEnvVars(
            configuration,
            JsonSerializer.SerializeToUtf8Bytes(root));

        Console.WriteLine(
            $"[Startup] {errors.Errors.Count} configuration load error(s) " +
            "will be reported to Cloud on the next heartbeat.");
    }

    private static AgentType GetAgentType(
        IConfiguration configuration)
    {
        return Enum.TryParse<AgentType>(
            configuration["Agent:Type"],
            out var parsedType)
                ? parsedType
                : AgentType.Low;
    }
}
