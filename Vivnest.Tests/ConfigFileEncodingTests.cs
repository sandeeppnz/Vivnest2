using System.Text;

namespace Vivnest.Tests;

// Exists because of a real incident, not as hygiene.
//
// An edit script rewrote Vivnest.Agent/common-config.json with a UTF-8 BOM,
// that file was committed, and it was then uploaded to the shared-config
// blob every deployed Agent reads at startup. Every Agent crash-looped.
//
// The failure was three layers from the cause: the BOM made JsonNode.Parse
// throw inside the credential-decryption step, so the config was used
// still-encrypted, and the Agent died on
// QueueServiceClient("enc:v1:...") reporting "No valid combination of
// account information found" - a message that mentions neither JSON, nor a
// BOM, nor the file. The one log line that did name it went past as a
// warning the Agent shrugged off.
//
// The Agent now tolerates a BOM (Program.cs StripUtf8Bom). This test stops
// one reaching the repo in the first place, whether from a script or from
// an editor that saves JSON as UTF-8-with-BOM by default - which several
// do on Windows.
public class ConfigFileEncodingTests
{
    // Config files the Agent parses as JSON at runtime, where a BOM is
    // fatal. appsettings.json files are excluded deliberately: those are
    // read by the .NET configuration provider, which handles a BOM fine.
    // Add a line here if another such file appears.
    [Theory]
    [InlineData("Vivnest.Agent/common-config.json")]
    public void AgentParsedConfigFilesHaveNoUtf8Bom(string relativePath)
    {
        var path = ResolveFromRepoRoot(relativePath);

        Assert.True(File.Exists(path), $"{relativePath} not found at {path}");

        var bytes = File.ReadAllBytes(path);

        Assert.False(
            bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            $"{relativePath} starts with a UTF-8 BOM. The Agent parses this file with "
            + "System.Text.Json, which rejects a BOM - and the resulting failure surfaces as an "
            + "unrelated storage-credential error. Save it as UTF-8 without BOM.");
    }

    [Theory]
    [InlineData("Vivnest.Agent/common-config.json")]
    public void AgentParsedConfigFilesAreValidJson(string relativePath)
    {
        var path = ResolveFromRepoRoot(relativePath);
        var json = File.ReadAllText(path, Encoding.UTF8);

        // Throws if not parseable, which is the assertion.
        using var _ = System.Text.Json.JsonDocument.Parse(json);
    }

    // The test binary runs from bin/Debug/net8.0, so walk up until the
    // solution file is in sight rather than hard-coding a depth.
    private static string ResolveFromRepoRoot(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Vivnest.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);

        return Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
