using Microsoft.Extensions.Configuration;
using Vivnest.Core.Constants;

namespace Vivnest.Agent.Bootstrap.ConfigurationLoading;

// Loads configuration and secrets from files beside the executable.
// The config half (shared + per-agent) is the LoadLocalSettings=true
// development path, mirroring what RemoteConfigLoader fetches from
// blob storage; the secrets half (*.secrets.json) runs on EVERY load -
// secrets are always local-only and never travel through blob storage.
internal sealed class LocalConfigLoader
{
    private readonly ConfigurationManager _configuration;
    private readonly ConfigDecryptor _decryptor;
    private readonly StartupErrorSink _errors;

    public LocalConfigLoader(
        ConfigurationManager configuration,
        ConfigDecryptor decryptor,
        StartupErrorSink errors)
    {
        _configuration = configuration;
        _decryptor = decryptor;
        _errors = errors;
    }

    public void TryLoadSharedConfig()
    {
        var path =
            Path.Combine(
                AppContext.BaseDirectory,
                SharedConfigBlob.BlobName);

        if (!File.Exists(path))
        {
            Console.WriteLine(
                $"[Startup] LoadLocalSettings is true but no local " +
                $"shared config file found at {path}; skipping.");

            return;
        }

        try
        {
            var configBytes =
                File.ReadAllBytes(path);

            configBytes =
                _decryptor.Decrypt(configBytes);

            ConfigSourceInsertion.InsertJsonBeforeEnvVars(
                _configuration,
                configBytes);

            Console.WriteLine(
                $"[Startup] Loaded local shared config from {path}.");
        }
        catch (Exception ex)
        {
            _errors.Report(
                $"[Startup] Failed to load local shared config from " +
                $"{path}, continuing without it: {ex.Message}");
        }
    }

    public void TryLoadAgentConfig()
    {
        var agentId =
            _configuration["Agent:AgentId"];

        if (string.IsNullOrWhiteSpace(agentId))
        {
            Console.WriteLine(
                "[Startup] LoadLocalSettings is true but " +
                "Agent:AgentId is not set; skipping local config load.");

            return;
        }

        var path =
            Path.Combine(
                AppContext.BaseDirectory,
                $"{agentId}.json");

        if (!File.Exists(path))
        {
            Console.WriteLine(
                $"[Startup] LoadLocalSettings is true but no local " +
                $"config file found at {path}; using local " +
                "appsettings only.");

            return;
        }

        try
        {
            var configBytes =
                File.ReadAllBytes(path);

            configBytes =
                _decryptor.Decrypt(configBytes);

            ConfigSourceInsertion.InsertJsonBeforeEnvVars(
                _configuration,
                configBytes);

            Console.WriteLine(
                $"[Startup] Loaded local config for agent " +
                $"{agentId} from {path}.");
        }
        catch (Exception ex)
        {
            _errors.Report(
                $"[Startup] Failed to load local config for agent " +
                $"{agentId} from {path}, continuing with local " +
                $"appsettings only: {ex.Message}");
        }
    }

    public void TryLoadSharedSecrets()
    {
        var path =
            Path.Combine(
                AppContext.BaseDirectory,
                "common-config.secrets.json");

        if (!File.Exists(path))
        {
            Console.WriteLine(
                "[Startup] No local shared secrets file found; " +
                "continuing without it.");

            return;
        }

        try
        {
            var secretsBytes =
                File.ReadAllBytes(path);

            ConfigSourceInsertion.InsertJsonBeforeEnvVars(
                _configuration,
                secretsBytes);

            Console.WriteLine(
                "[Startup] Loaded local shared secrets.");
        }
        catch (Exception ex)
        {
            _errors.Report(
                "[Startup] Failed to load local shared secrets, " +
                $"continuing without them: {ex.Message}");
        }
    }

    public void TryLoadAgentSecrets(
        string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            Console.WriteLine(
                "[Startup] Agent:AgentId is not set; " +
                "skipping local agent secrets load.");

            return;
        }

        var path =
            Path.Combine(
                AppContext.BaseDirectory,
                $"{agentId}.secrets.json");

        if (!File.Exists(path))
        {
            Console.WriteLine(
                $"[Startup] No local secrets file found for agent " +
                $"{agentId}; continuing without them.");

            return;
        }

        try
        {
            var secretsBytes =
                File.ReadAllBytes(path);

            ConfigSourceInsertion.InsertJsonBeforeEnvVars(
                _configuration,
                secretsBytes);

            Console.WriteLine(
                $"[Startup] Loaded local secrets for agent " +
                $"{agentId}.");
        }
        catch (Exception ex)
        {
            _errors.Report(
                $"[Startup] Failed to load local secrets for agent " +
                $"{agentId}, continuing without them: {ex.Message}");
        }
    }
}
