using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Core.Options;

namespace Vivnest.Agent.Updater;

// The actual pull/stop/rm/run sequence, extracted out of
// DeployPollingWorker once a second real caller needed it: the queue-
// triggered path (a Cloud-originated DeployCommandQueueMessage) and the
// --install CLI flag (a local, operator-driven one-time bootstrap - see
// Program.cs) both just want "make the container match the latest image
// right now," with no reason to duplicate how that's done.
public sealed class AgentDeployer
{
    // Hardcoded, matching scripts/update-agent.ps1 exactly - both agent
    // roles share the exact same image (ADR-035), so there's never a
    // reason for this to differ. Split into two consts (not just Image)
    // so the login step below and the pull/run steps share one source of
    // truth for the registry hostname (ADR-039).
    private const string Registry = "vivnestagentacr.azurecr.io";
    private const string Image = $"{Registry}/vivnest-agent:latest";

    private readonly DeployOptions _deployOptions;
    private readonly ILogger<AgentDeployer> _logger;
    private readonly string _appSettingsPath;

    public AgentDeployer(
        IOptions<DeployOptions> deployOptions,
        ILogger<AgentDeployer> logger)
    {
        _deployOptions = deployOptions.Value;
        _logger = logger;

        // The same folder this executable is deployed into, not a
        // hardcoded C:\vivnest-agent path - so the exact same build works
        // wherever it's dropped (Windows today, a Raspberry Pi later),
        // since appsettings.json always sits right next to it.
        _appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    }

    // stop/rm are allowFailure: true deliberately - this is what makes a
    // first-ever install ("nothing running yet") and a routine update
    // ("recreate what's already there") the exact same code path, not two.
    public async Task DeployAsync(CancellationToken cancellationToken)
    {
        var containerName = _deployOptions.ContainerName;

        // Self-authenticate before every pull, rather than depending on a
        // prior manual `az acr login` on this host - that session is tied
        // to the Azure CLI's own token lifetime and expires if nobody's
        // been at the machine recently, which is exactly how a
        // queue-triggered deploy was found failing with no human present
        // to re-auth (ADR-039). Skipped entirely if not configured, so an
        // instance that hasn't set these yet behaves exactly as before.
        if (!string.IsNullOrWhiteSpace(_deployOptions.AcrUsername) &&
            !string.IsNullOrWhiteSpace(_deployOptions.AcrPassword))
        {
            await RunDockerLoginAsync(cancellationToken);
        }

        await RunDockerAsync(cancellationToken, allowFailure: false, "pull", Image);
        await RunDockerAsync(cancellationToken, allowFailure: true, "stop", containerName);
        await RunDockerAsync(cancellationToken, allowFailure: true, "rm", containerName);

        // Same flags as scripts/update-agent.ps1 - keep both in sync if
        // the container's run configuration ever changes.
        await RunDockerAsync(
            cancellationToken,
            allowFailure: false,
            "run", "-d",
            "--name", containerName,
            "--restart", "unless-stopped",
            "-v", $"{_appSettingsPath}:/app/appsettings.json",
            "-e", "HomeAssistant__BaseUrl=http://host.docker.internal:8123/",
            Image);

        _logger.LogInformation(
            "Deploy complete: {ContainerName} recreated from {Image}.",
            containerName,
            Image);
    }

    // Not built on RunDockerAsync below, deliberately - that method logs
    // "Running: docker {Arguments}" by joining the raw argument list, and
    // it has no stdin redirection. Neither is safe for a password: joining
    // arguments into a log line would leak it in plaintext, and passing it
    // as a `--password <value>` argument would put it on the process
    // command line, visible via `ps`/Task Manager on some hosts. Uses
    // Docker's own `--password-stdin` flag instead - the argument list
    // logged here carries zero secret material, and the password only
    // ever exists in this process's own stdin pipe (ADR-039).
    private async Task RunDockerLoginAsync(CancellationToken cancellationToken)
    {
        var arguments = new[] { "login", Registry, "--username", _deployOptions.AcrUsername, "--password-stdin" };

        _logger.LogInformation("Running: docker {Arguments}", string.Join(' ', arguments));

        var startInfo = new ProcessStartInfo("docker")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start docker process.");

        await process.StandardInput.WriteAsync(_deployOptions.AcrPassword);
        process.StandardInput.Close();

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;

        if (!string.IsNullOrWhiteSpace(stdOut))
            _logger.LogInformation("{Output}", stdOut.Trim());

        if (!string.IsNullOrWhiteSpace(stdErr))
            _logger.LogInformation("{Output}", stdErr.Trim());

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"docker login to {Registry} failed with exit code {process.ExitCode}.");
        }
    }

    private async Task RunDockerAsync(
        CancellationToken cancellationToken,
        bool allowFailure,
        params string[] arguments)
    {
        _logger.LogInformation("Running: docker {Arguments}", string.Join(' ', arguments));

        var startInfo = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start docker process.");

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;

        if (!string.IsNullOrWhiteSpace(stdOut))
            _logger.LogInformation("{Output}", stdOut.Trim());

        if (!string.IsNullOrWhiteSpace(stdErr))
            _logger.LogInformation("{Output}", stdErr.Trim());

        if (process.ExitCode != 0 && !allowFailure)
        {
            throw new InvalidOperationException(
                $"docker {string.Join(' ', arguments)} failed with exit code {process.ExitCode}.");
        }
    }
}
