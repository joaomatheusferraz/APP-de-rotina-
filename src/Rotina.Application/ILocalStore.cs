using Rotina.Domain;

namespace Rotina.Application;

public interface ILocalStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<UserProfile> GetProfileAsync(CancellationToken cancellationToken = default);
    Task SaveProfileAsync(UserProfile profile, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LifeArea>> GetAreasAsync(CancellationToken cancellationToken = default);
    Task SaveAreaAsync(LifeArea area, CancellationToken cancellationToken = default);
}
