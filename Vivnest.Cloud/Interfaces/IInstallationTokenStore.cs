using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Interfaces;

public interface IInstallationTokenStore
{
    Task<AgentInstallationTokenEntity?> GetByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    Task CreateAsync(
        AgentInstallationTokenEntity entity,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        AgentInstallationTokenEntity entity,
        CancellationToken cancellationToken = default);
}
