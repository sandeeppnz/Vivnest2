using Vivnest.Core.DataStores.Entities;
using Vivnest.Cloud.Entities;

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
