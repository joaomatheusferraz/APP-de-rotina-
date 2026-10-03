namespace Rotina.Domain;

public sealed record DailyPreferences
{
    public int? DefaultWaterTargetMl { get; }
    public int MoodScaleMax { get; }

    public DailyPreferences(int? defaultWaterTargetMl, int moodScaleMax)
    {
        if (defaultWaterTargetMl is < 100 or > 20000)
            throw new ArgumentOutOfRangeException(nameof(defaultWaterTargetMl), "A meta diária de água deve estar entre 100 e 20.000 ml.");
        if (moodScaleMax is not (3 or 5 or 10))
            throw new ArgumentOutOfRangeException(nameof(moodScaleMax), "Escolha uma escala de humor de 3, 5 ou 10 pontos.");
        DefaultWaterTargetMl = defaultWaterTargetMl;
        MoodScaleMax = moodScaleMax;
    }

    public static DailyPreferences Default => new(null, 5);
}

public sealed record DailyCheckIn
{
    public DateOnly Date { get; }
    public TimeOnly? WokeAt { get; }
    public int? SleepMinutes { get; }
    public int? SleepRating { get; }
    public int? Mood { get; }
    public int MoodScaleMax { get; }
    public int? PhoneMinutes { get; }
    public string? Note { get; }
    public int? WaterTargetMl { get; }

    public DailyCheckIn(DateOnly date, TimeOnly? wokeAt = null, int? sleepMinutes = null,
        int? sleepRating = null, int? mood = null, int? phoneMinutes = null,
        string? note = null, int? waterTargetMl = null, int moodScaleMax = 5)
    {
        if (sleepMinutes is < 0 or > 1440) throw new ArgumentOutOfRangeException(nameof(sleepMinutes));
        if (sleepRating is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(sleepRating));
        if (moodScaleMax is not (3 or 5 or 10)) throw new ArgumentOutOfRangeException(nameof(moodScaleMax));
        if (mood is < 1 || mood > moodScaleMax) throw new ArgumentOutOfRangeException(nameof(mood));
        if (phoneMinutes is < 0 or > 1440) throw new ArgumentOutOfRangeException(nameof(phoneMinutes));
        if (note?.Trim().Length > 500) throw new ArgumentException("A nota pode ter até 500 caracteres.");
        if (waterTargetMl is < 100 or > 20000) throw new ArgumentOutOfRangeException(nameof(waterTargetMl));
        Date = date;
        WokeAt = wokeAt;
        SleepMinutes = sleepMinutes;
        SleepRating = sleepRating;
        Mood = mood;
        MoodScaleMax = moodScaleMax;
        PhoneMinutes = phoneMinutes;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        WaterTargetMl = waterTargetMl;
    }

    public DailyCheckIn WithWaterTarget(int? target) => new(Date, WokeAt, SleepMinutes, SleepRating,
        Mood, PhoneMinutes, Note, target, MoodScaleMax);
}

public sealed record WaterEvent(long Sequence, DateOnly Date, int DeltaMl, DateTimeOffset RecordedAtUtc);

public sealed record DailyClosure(long Sequence, DateOnly Date, bool IsClosed, int HabitDone, int HabitPlanned,
    int WaterMl, int? WaterTargetMl, int? FocusMinutes, int? WorkoutCount,
    int? OverallRating, string? Note, DateTimeOffset RecordedAtUtc);

public sealed record DailyRecord(DailyCheckIn CheckIn, int WaterMl, IReadOnlyList<WaterEvent> WaterEvents,
    IReadOnlyList<DailyClosure> Closures)
{
    public DailyClosure? LatestClosure => Closures.OrderByDescending(c => c.Sequence).FirstOrDefault();
    public bool IsClosed => LatestClosure?.IsClosed == true;
    public double? WaterProgress => CheckIn.WaterTargetMl is > 0
        ? (double)WaterMl / CheckIn.WaterTargetMl.Value : null;
}
