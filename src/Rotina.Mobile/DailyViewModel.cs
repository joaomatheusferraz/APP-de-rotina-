using Rotina.Application;
using Rotina.Domain;

namespace Rotina.Mobile;

public sealed class DailyViewModel(IDailyStore store, HabitViewModel habits, WorkoutViewModel workouts, FocusViewModel focus)
{
    public DailyRecord? Today { get; private set; }
    public DailyPreferences Preferences { get; private set; } = DailyPreferences.Default;
    public string? Error { get; private set; }
    public DateOnly LocalToday => DateOnly.FromDateTime(DateTime.Now);
    public int WorkoutsToday => workouts.Sessions.Count(s => s.EndedAt is not null &&
        DateOnly.FromDateTime(s.StartedAt.ToLocalTime().DateTime) == LocalToday);
    public int FocusMinutesToday => focus.Sessions.Where(s => s.EndedAt is not null &&
        DateOnly.FromDateTime(s.StartedAt.ToLocalTime().DateTime) == LocalToday)
        .Sum(s => s.ElapsedMinutes(s.EndedAt!.Value));

    public async Task LoadAsync()
    {
        try
        {
            Preferences = await store.GetDailyPreferencesAsync();
            Today = await store.GetDailyRecordAsync(LocalToday);
            await workouts.LoadAsync();
            await focus.LoadAsync();
            Error = null;
        }
        catch (Exception) { Error = "Não foi possível abrir os dados do dia. Eles não foram apagados."; }
    }

    public async Task AddWaterAsync(int deltaMl)
    {
        await store.AddWaterAsync(LocalToday, deltaMl, DateTimeOffset.UtcNow);
        await LoadAsync();
    }

    public async Task SetWaterTotalAsync(int totalMl)
    {
        var current = Today ?? await store.GetDailyRecordAsync(LocalToday);
        if (totalMl < 0) throw new ArgumentOutOfRangeException(nameof(totalMl), "O consumo não pode ser negativo.");
        var delta = totalMl - current.WaterMl;
        if (delta != 0) await AddWaterAsync(delta);
    }

    public async Task SetTodayWaterTargetAsync(int? targetMl)
    {
        var current = Today ?? await store.GetDailyRecordAsync(LocalToday);
        await store.SaveCheckInAsync(current.CheckIn.WithWaterTarget(targetMl), DateTimeOffset.UtcNow);
        await LoadAsync();
    }

    public async Task SaveCheckInAsync(DailyCheckIn checkIn)
    {
        await store.SaveCheckInAsync(checkIn, DateTimeOffset.UtcNow);
        await LoadAsync();
    }

    public async Task SavePreferencesAsync(DailyPreferences preferences)
    {
        await store.SaveDailyPreferencesAsync(preferences);
        await LoadAsync();
    }

    public async Task CloseAsync(bool close, int? rating, string? note)
    {
        var today = LocalToday;
        await habits.LoadAsync();
        await workouts.LoadAsync();
        await focus.LoadAsync();
        var scheduled = habits.Today(today).ToList();
        var record = Today ?? await store.GetDailyRecordAsync(today);
        var closure = new DailyClosure(0, today, close, scheduled.Count(h => h.CompletedOn(today)), scheduled.Count,
            record.WaterMl, record.CheckIn.WaterTargetMl, FocusMinutesToday, WorkoutsToday, rating, note, DateTimeOffset.UtcNow);
        await store.CloseDayAsync(closure);
        await LoadAsync();
    }

    public Task<IReadOnlyList<DailyRecord>> HistoryAsync(DateOnly from, DateOnly through) =>
        store.GetDailyHistoryAsync(from, through);
}
