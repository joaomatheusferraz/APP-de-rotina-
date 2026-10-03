using Rotina.Domain;
namespace Rotina.Application;
public interface IDashboardStore
{
    Task<IReadOnlyList<DashboardCardSetting>> GetDashboardSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveDashboardSettingsAsync(IReadOnlyList<DashboardCardSetting> settings, CancellationToken cancellationToken = default);
}
