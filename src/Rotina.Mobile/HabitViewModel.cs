using Rotina.Application;
using Rotina.Domain;

namespace Rotina.Mobile;

public sealed class HabitViewModel(IHabitStore habits, ILocalStore localStore)
{
    public IReadOnlyList<HabitHistory> All { get; private set; } = [];
    public IReadOnlyList<LifeArea> Areas { get; private set; } = [];
    public string? Error { get; private set; }

    public async Task LoadAsync()
    {
        try
        {
            All = await habits.GetHabitsAsync();
            Areas = await localStore.GetAreasAsync();
            Error = null;
        }
        catch (Exception) { Error = "Não foi possível abrir os hábitos locais. Seus dados não foram apagados."; }
    }

    public IEnumerable<HabitHistory> ActiveOn(DateOnly date) => All.Where(h => h.IsActiveOn(date))
        .OrderBy(h => h.RevisionOn(date)?.Name);

    public IEnumerable<HabitHistory> Today(DateOnly date) => ActiveOn(date).Where(h =>
    {
        var revision = h.RevisionOn(date);
        return revision is not null && (HabitAnalytics.Day(h, date, date) == HabitDayState.Completed ||
            HabitAnalytics.CanCompleteToday(h, date));
    });

    public async Task ToggleTodayAsync(HabitHistory history, DateOnly today)
    {
        await habits.RecordHabitAsync(history.Id, today, !history.CompletedOn(today), null, DateTimeOffset.UtcNow);
        await LoadAsync();
    }

    public async Task AddNoteAsync(HabitHistory history, DateOnly date, string note)
    {
        await habits.RecordHabitAsync(history.Id, date, true, note, DateTimeOffset.UtcNow);
        await LoadAsync();
    }

    public async Task SaveAsync(Guid? habitId, string name, Guid? areaId, DateOnly effectiveFrom,
        HabitFrequency frequency, int mask, int target, bool paused)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (habitId is not null && effectiveFrom < today)
            throw new ArgumentException("Uma revisão do hábito não pode começar no passado.");
        var id = habitId ?? Guid.NewGuid();
        var revision = new HabitRevision(Guid.NewGuid(), id, name, areaId, effectiveFrom, frequency, mask, target, paused);
        if (habitId is null) await habits.CreateHabitAsync(revision, effectiveFrom);
        else await habits.ReviseHabitAsync(revision);
        await LoadAsync();
    }

    public async Task SetArchivedAsync(HabitHistory history, bool archive)
    {
        await habits.SetHabitArchivedAsync(history.Id, archive ? DateOnly.FromDateTime(DateTime.Now) : null);
        await LoadAsync();
    }
}
