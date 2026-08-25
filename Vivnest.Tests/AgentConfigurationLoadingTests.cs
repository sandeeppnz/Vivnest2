using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Vivnest.Agent.Bootstrap;
using Vivnest.Agent.Bootstrap.ConfigurationLoading;

namespace Vivnest.Tests;

// The pure parts of the Agent's configuration load, testable since the
// AgentConfigurationLoader split (2026-08-25). Everything here runs
// without storage, files or network - the phases that need those are
// exercised by the Agent itself.
public class AgentConfigurationLoadingTests
{
    // ---- the secrets deep-merge -------------------------------------------

    // A device secrets file carries only the secret fields - typically
    // {"Camera":{"Password":"..."}}. If the merge replaced the Camera
    // object wholesale instead of merging into it, the overlay would
    // erase Host/Username/everything else the remote document set, and
    // the device would fail in a way that points at its config, not at
    // the merge.
    [Fact]
    public void MergingSecretsIntoADeviceKeepsTheSiblingsOfTheSecret()
    {
        var device = (JsonObject)JsonNode.Parse(
            """
            {
              "DeviceId": "cam-1",
              "Camera": {
                "Host": "192.168.50.166",
                "Username": "admin",
                "Password": "enc:v1:placeholder"
              }
            }
            """)!;

        var secrets = (JsonObject)JsonNode.Parse(
            """{ "Camera": { "Password": "hunter2" } }""")!;

        DeviceConfigLoader.MergeJsonInto(device, secrets);

        Assert.Equal("hunter2", device["Camera"]?["Password"]?.GetValue<string>());
        Assert.Equal("192.168.50.166", device["Camera"]?["Host"]?.GetValue<string>());
        Assert.Equal("admin", device["Camera"]?["Username"]?.GetValue<string>());
        Assert.Equal("cam-1", device["DeviceId"]?.GetValue<string>());
    }

    [Fact]
    public void MergingReplacesArraysAndScalarsWholesale()
    {
        var target = (JsonObject)JsonNode.Parse(
            """{ "Tags": ["a", "b"], "Retries": 3 }""")!;

        var source = (JsonObject)JsonNode.Parse(
            """{ "Tags": ["c"], "Retries": 5 }""")!;

        DeviceConfigLoader.MergeJsonInto(target, source);

        Assert.Equal(5, target["Retries"]?.GetValue<int>());

        var tags = (JsonArray)target["Tags"]!;
        Assert.Single(tags);
        Assert.Equal("c", tags[0]?.GetValue<string>());
    }

    // ---- source ordering: loaded config beats defaults, env vars beat all --

    [Fact]
    public void InsertedJsonOverridesEarlierSourcesButNotEnvironmentVariables()
    {
        var envVarName =
            "VIVNEST_TEST_" + Guid.NewGuid().ToString("N");

        Environment.SetEnvironmentVariable(envVarName, "from-env");

        try
        {
            var configuration = new ConfigurationManager();

            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Overridden"] = "from-defaults",
                    [envVarName] = "from-defaults",
                });

            configuration.AddEnvironmentVariables();

            ConfigSourceInsertion.InsertJsonBeforeEnvVars(
                configuration,
                Encoding.UTF8.GetBytes(
                    $$"""{ "Overridden": "from-loaded", "{{envVarName}}": "from-loaded" }"""));

            // Loaded config overrides the in-memory "appsettings" layer...
            Assert.Equal("from-loaded", configuration["Overridden"]);

            // ...but the environment variable still wins over it.
            Assert.Equal("from-env", configuration[envVarName]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, null);
        }
    }

    // Phases insert in order and each goes in just before env vars, so a
    // later phase (per-agent config, secrets) overrides an earlier one
    // (shared config). The whole layering story depends on this.
    [Fact]
    public void ALaterInsertionOverridesAnEarlierOne()
    {
        var configuration = new ConfigurationManager();
        configuration.AddEnvironmentVariables();

        ConfigSourceInsertion.InsertJsonBeforeEnvVars(
            configuration,
            Encoding.UTF8.GetBytes("""{ "Value": "from-shared" }"""));

        ConfigSourceInsertion.InsertJsonBeforeEnvVars(
            configuration,
            Encoding.UTF8.GetBytes("""{ "Value": "from-agent" }"""));

        Assert.Equal("from-agent", configuration["Value"]);
    }

    [Fact]
    public void WithoutAnEnvVarsSourceTheJsonIsStillAdded()
    {
        var configuration = new ConfigurationManager();

        ConfigSourceInsertion.InsertJsonBeforeEnvVars(
            configuration,
            Encoding.UTF8.GetBytes("""{ "Value": "loaded" }"""));

        Assert.Equal("loaded", configuration["Value"]);
    }

    // ---- the enc:v1: no-key warning ---------------------------------------

    // The likelier misconfiguration is the unset key, not the wrong one:
    // without this warning an RTSP password stays the literal "enc:v1:..."
    // string and the failure surfaces as the camera refusing credentials -
    // nowhere near the missing environment variable that caused it.
    [Fact]
    public void EncryptedValuesWithNoKeyAreReportedAndPassedThroughUnchanged()
    {
        var errors = new StartupErrorSink();
        var decryptor =
            ConfigDecryptor.FromConfiguration(
                new ConfigurationManager(), errors);

        var bytes = Encoding.UTF8.GetBytes(
            """{ "Password": "enc:v1:AAAA" }""");

        var result = decryptor.Decrypt(bytes);

        Assert.Same(bytes, result);
        Assert.Contains(errors.Errors, e => e.Contains("UNDECRYPTED"));
    }

    [Fact]
    public void PlainConfigWithNoKeyPassesThroughSilently()
    {
        var errors = new StartupErrorSink();
        var decryptor =
            ConfigDecryptor.FromConfiguration(
                new ConfigurationManager(), errors);

        var bytes = Encoding.UTF8.GetBytes("""{ "Host": "10.0.0.1" }""");

        Assert.Same(bytes, decryptor.Decrypt(bytes));
        Assert.Empty(errors.Errors);
    }

    // The device-document path warns too - it used to be silently
    // undecrypted while the whole-document path warned, so a fleet whose
    // key went missing pointed every diagnosis at the cameras instead of
    // the key. The device id in the message is what makes the heartbeat
    // error actionable.
    [Fact]
    public void ADeviceDocumentWithEncryptedValuesAndNoKeyIsReportedByName()
    {
        var errors = new StartupErrorSink();
        var decryptor =
            ConfigDecryptor.FromConfiguration(
                new ConfigurationManager(), errors);

        var device = (JsonObject)JsonNode.Parse(
            """{ "DeviceId": "cam-1", "Password": "enc:v1:AAAA" }""")!;

        decryptor.TryDecryptInPlace(device, "config for device cam-1");

        // Untouched, and loudly so.
        Assert.Equal("enc:v1:AAAA", device["Password"]?.GetValue<string>());
        Assert.Contains(
            errors.Errors,
            e => e.Contains("UNDECRYPTED") && e.Contains("cam-1"));
    }

    [Fact]
    public void ADeviceDocumentWithNoEncryptedValuesStaysSilent()
    {
        var errors = new StartupErrorSink();
        var decryptor =
            ConfigDecryptor.FromConfiguration(
                new ConfigurationManager(), errors);

        var device = (JsonObject)JsonNode.Parse(
            """{ "DeviceId": "cam-1", "Host": "10.0.0.1" }""")!;

        decryptor.TryDecryptInPlace(device, "config for device cam-1");

        Assert.Empty(errors.Errors);
    }

    [Fact]
    public void AUtf8BomIsStrippedAndItsAbsenceIsHarmless()
    {
        byte[] withBom = [0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}'];
        byte[] withoutBom = [(byte)'{', (byte)'}'];

        Assert.Equal(withoutBom, ConfigDecryptor.StripUtf8Bom(withBom));
        Assert.Same(withoutBom, ConfigDecryptor.StripUtf8Bom(withoutBom));
    }

    // ---- error publication end-to-end -------------------------------------

    // An invalid CredentialEncryption:Key is a failure the load can
    // produce without touching storage or disk, so it drives the whole
    // sink -> PublishStartupErrors -> configuration path: the error a
    // phase reports must come out as ConfigurationLoadErrors, which is
    // what AgentConfigMetadataOptions binds and the heartbeat ships.
    [Fact]
    public async Task AReportedLoadErrorComesOutAsConfigurationLoadErrors()
    {
        var configuration = new ConfigurationManager();

        configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["LoadLocalSettings"] = "true",
                ["CredentialEncryption:Key"] = "not-a-valid-key",
            });

        await AgentConfigurationLoader.LoadAsync(configuration);

        Assert.Contains(
            "CredentialEncryption:Key is invalid",
            configuration["ConfigurationLoadErrors:0"]);
    }

    [Fact]
    public async Task ACleanLoadPublishesNoConfigurationLoadErrors()
    {
        var configuration = new ConfigurationManager();

        configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["LoadLocalSettings"] = "true",
            });

        await AgentConfigurationLoader.LoadAsync(configuration);

        Assert.Null(configuration["ConfigurationLoadErrors:0"]);
    }
}
