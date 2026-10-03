using Rotina.Domain;

namespace Rotina.Application;

public interface IDailyStore
{
    Task<DailyPreferences> GetDailyPreferencesAsync(CancellationToken cancellationToken = default);
    Task SaveDailyPreferencesAsync(DailyPreferences preferences, CancellationToken cancellationToken = default);
    Task<DailyRecord> GetDailyRecordAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DailyRecord>> GetDailyHistoryAsync(DateOnly from, DateOnly through,
        CancellationToken cancellationToken = default);
    Task SaveCheckInAsync(DailyCheckIn checkIn, DateTimeOffset recordedAtUtc,
        CancellationToken cancellationToken = default);
    Task AddWaterAsync(DateOnly date, int deltaMl, DateTimeOffset recordedAtUtc,
        CancellationToken cancellationToken = default);
    Task CloseDayAsync(DailyClosure closure, CancellationToken cancellationToken = default);
}
