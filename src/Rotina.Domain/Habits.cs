namespace Rotina.Domain;

public enum HabitFrequency { Daily, Weekdays, TimesPerWeek }

public sealed record HabitRevision
{
    public Guid Id { get; }
    public Guid HabitId { get; }
    public string Name { get; }
    public Guid? AreaId { get; }
    public DateOnly EffectiveFrom { get; }
    public HabitFrequency Frequency { get; }
    public int WeekdayMask { get; }
    public int WeeklyTarget { get; }
    public bool IsPaused { get; }
    public long Sequence { get; }

    public HabitRevision(Guid id, Guid habitId, string name, Guid? areaId, DateOnly effectiveFrom,
        HabitFrequency frequency, int weekdayMask, int weeklyTarget, bool isPaused, long sequence = 0)
    {
        if (id == Guid.Empty || habitId == Guid.Empty) throw new ArgumentException("Identificador de hábito inválido.");
        name = name.Trim();
        if (name.Length is < 1 or > 100) throw new ArgumentException("O nome do hábito deve ter entre 1 e 100 caracteres.");
        if (!Enum.IsDefined(frequency)) throw new ArgumentOutOfRangeException(nameof(frequency));
        if (frequency == HabitFrequency.Weekdays && (weekdayMask < 1 || weekdayMask > 127))
            throw new ArgumentException("Selecione pelo menos um dia da semana.");
        if (frequency == HabitFrequency.TimesPerWeek && weeklyTarget is < 1 or > 7)
            throw new ArgumentException("A meta semanal deve ficar entre 1 e 7.");
        Id = id;
        HabitId = habitId;
        Name = name;
        AreaId = areaId;
        EffectiveFrom = effectiveFrom;
        Frequency = frequency;
        WeekdayMask = frequency == HabitFrequency.Weekdays ? weekdayMask : 0;
        WeeklyTarget = frequency == HabitFrequency.TimesPerWeek ? weeklyTarget : 0;
        IsPaused = isPaused;
        Sequence = sequence;
    }

    public bool ScheduledOn(DateOnly date) => !IsPaused && date >= EffectiveFrom && (Frequency switch
    {
        HabitFrequency.Daily => true,
        HabitFrequency.Weekdays => (WeekdayMask & (1 << (int)date.DayOfWeek)) != 0,
        HabitFrequency.TimesPerWeek => true,
        _ => false
    });
}

public sealed record HabitEvent(long Sequence, Guid HabitId, DateOnly Date, Guid RevisionId,
    bool IsCompleted, string? Note, DateTimeOffset RecordedAtUtc);

public sealed record HabitStatusEvent(long Sequence, DateOnly EffectiveOn, bool IsArchived);

public sealed record HabitHistory(Guid Id, DateOnly CreatedOn, DateOnly? ArchivedOn,
    IReadOnlyList<HabitRevision> Revisions, IReadOnlyList<HabitEvent> Events,
    IReadOnlyList<HabitStatusEvent>? StatusEvents = null)
{
    public HabitRevision? RevisionOn(DateOnly date) => Revisions
        .Where(r => r.EffectiveFrom <= date)
        .OrderByDescending(r => r.EffectiveFrom)
        .ThenByDescending(r => r.Sequence)
        .FirstOrDefault();

    public bool IsActiveOn(DateOnly date) => date >= CreatedOn &&
        StatusEvents?.Where(e => e.EffectiveOn <= date).OrderByDescending(e => e.EffectiveOn)
            .ThenByDescending(e => e.Sequence).FirstOrDefault()?.IsArchived != true;

    public bool CompletedOn(DateOnly date) => Events.Where(e => e.Date == date)
        .OrderByDescending(e => e.Sequence).FirstOrDefault()?.IsCompleted == true;
}

public enum HabitDayState { BeforeStart, Archived, Rest, Paused, ScheduledPending, Completed, Missed }

public sealed record HabitScore(int Done, int Planned)
{
    public double Fraction => Planned == 0 ? 0 : (double)Done / Planned;
}

public sealed record HabitReport(HabitScore Last7, HabitScore Last30, HabitScore Last90,
    int CurrentStreak, int BestStreak, IReadOnlyDictionary<DateOnly, HabitDayState> Days);

public static class HabitAnalytics
{
    public static HabitDayState Day(HabitHistory history, DateOnly date, DateOnly today)
    {
        if (!history.IsActiveOn(date)) return date < history.CreatedOn ? HabitDayState.BeforeStart : HabitDayState.Archived;
        var revision = history.RevisionOn(date);
        if (revision is null) return HabitDayState.BeforeStart;
        if (revision.IsPaused) return HabitDayState.Paused;
        if (revision.Frequency == HabitFrequency.TimesPerWeek)
        {
            var weekStart = StartOfWeek(date);
            var segmentStart = weekStart > revision.EffectiveFrom ? weekStart : revision.EffectiveFrom;
            var count = CountCompleted(history, segmentStart, date);
            var segmentEnd = WeeklySegmentEnd(history, segmentStart, revision, today);
            var target = Math.Min(revision.WeeklyTarget, segmentEnd.DayNumber - segmentStart.DayNumber + 1);
            if (history.CompletedOn(date)) return HabitDayState.Completed;
            if (count >= target) return HabitDayState.Rest;
            if (date == segmentEnd && date < today) return HabitDayState.Missed;
            return HabitDayState.ScheduledPending;
        }
        if (!revision.ScheduledOn(date)) return HabitDayState.Rest;
        if (history.CompletedOn(date)) return HabitDayState.Completed;
        return date < today ? HabitDayState.Missed : HabitDayState.ScheduledPending;
    }

    public static bool CanCompleteToday(HabitHistory history, DateOnly today)
    {
        if (!history.IsActiveOn(today)) return false;
        var revision = history.RevisionOn(today);
        if (revision is null || !revision.ScheduledOn(today)) return false;
        if (history.CompletedOn(today)) return false;
        if (revision.Frequency != HabitFrequency.TimesPerWeek) return true;
        var monday = StartOfWeek(today);
        var segmentStart = monday > revision.EffectiveFrom ? monday : revision.EffectiveFrom;
        var segmentEnd = WeeklySegmentEnd(history, segmentStart, revision, today);
        var target = Math.Min(revision.WeeklyTarget, segmentEnd.DayNumber - segmentStart.DayNumber + 1);
        return CountCompleted(history, segmentStart, segmentEnd) < target;
    }

    public static HabitReport Build(HabitHistory history, DateOnly today)
    {
        var first = history.CreatedOn > today.AddDays(-89) ? history.CreatedOn : today.AddDays(-89);
        var days = new Dictionary<DateOnly, HabitDayState>();
        for (var day = first; day <= today; day = day.AddDays(1)) days[day] = Day(history, day, today);
        var current = 0;
        var best = 0;
        var run = 0;
        // For flexible weekly goals, one completed week is one opportunity. Monday is the start of a week.
        var cursor = history.CreatedOn;
        while (cursor <= today)
        {
            var revision = history.RevisionOn(cursor);
            if (revision?.Frequency == HabitFrequency.TimesPerWeek)
            {
                var weekEnd = WeeklySegmentEnd(history, cursor, revision, today);
                var windowEnd = weekEnd < today ? weekEnd : today;
                var target = Math.Min(revision.WeeklyTarget, weekEnd.DayNumber - cursor.DayNumber + 1);
                var done = CountCompleted(history, cursor, windowEnd);
                if (revision.IsPaused || !history.IsActiveOn(cursor)) { }
                else if (done >= target) { run++; best = Math.Max(best, run); }
                else if (weekEnd < today) run = 0;
                cursor = weekEnd.AddDays(1);
                continue;
            }
            var state = Day(history, cursor, today);
            if (state == HabitDayState.Completed) { run++; best = Math.Max(best, run); }
            else if (state == HabitDayState.Missed) run = 0;
            cursor = cursor.AddDays(1);
        }
        current = run;
        return new(Score(history, today, 7), Score(history, today, 30), Score(history, today, 90), current, best, days);
    }

    public static HabitScore Score(HabitHistory history, DateOnly today, int days)
    {
        var start = today.AddDays(1 - days);
        if (history.CreatedOn > start) start = history.CreatedOn;
        var done = 0;
        var planned = 0;
        for (var date = start; date <= today;)
        {
            if (!history.IsActiveOn(date)) { date = date.AddDays(1); continue; }
            var revision = history.RevisionOn(date);
            if (revision is null || revision.IsPaused) { date = date.AddDays(1); continue; }
            if (revision.Frequency == HabitFrequency.TimesPerWeek)
            {
                var end = WeeklySegmentEnd(history, date, revision, today);
                var visibleEnd = end > today ? today : end;
                planned += Math.Min(revision.WeeklyTarget, end.DayNumber - date.DayNumber + 1);
                done += CountCompleted(history, date, visibleEnd);
                date = visibleEnd.AddDays(1);
            }
            else if (revision.ScheduledOn(date))
            { planned++; if (history.CompletedOn(date)) done++; date = date.AddDays(1); }
            else date = date.AddDays(1);
        }
        return new(Math.Min(done, planned), planned);
    }

    public static DateOnly StartOfWeek(DateOnly date) => date.AddDays(-((int)date.DayOfWeek + 6) % 7);

    private static int CountCompleted(HabitHistory history, DateOnly start, DateOnly end)
    {
        var count = 0;
        for (var date = start; date <= end; date = date.AddDays(1))
            if (history.CompletedOn(date)) count++;
        return count;
    }

    private static DateOnly WeeklySegmentEnd(HabitHistory history, DateOnly start, HabitRevision revision, DateOnly today)
    {
        var end = StartOfWeek(start).AddDays(6);
        var next = history.Revisions.Where(r => r.EffectiveFrom > start && r.EffectiveFrom <= end)
            .OrderBy(r => r.EffectiveFrom).FirstOrDefault();
        if (next is not null) end = next.EffectiveFrom.AddDays(-1);
        var status = history.StatusEvents?.Where(e => e.EffectiveOn > start && e.EffectiveOn <= end)
            .OrderBy(e => e.EffectiveOn).FirstOrDefault();
        if (status is not null) end = status.EffectiveOn.AddDays(-1);
        return end;
    }
}
