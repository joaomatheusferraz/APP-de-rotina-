namespace Rotina.Domain;

public sealed record WorkoutExercise(Guid Id, string Name, string MuscleGroup, string? Note, int Order,
    int TargetSets, int RepMin, int RepMax, decimal? SuggestedLoadKg)
{
    public void Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 100) throw new ArgumentException("Informe o exercício.");
        if (string.IsNullOrWhiteSpace(MuscleGroup) || MuscleGroup.Length > 80 || Note?.Length > 1000 || Order < 0)
            throw new ArgumentException("Informe o grupo muscular do exercício.");
        if (TargetSets is < 1 or > 20 || RepMin is < 1 or > 100 || RepMax < RepMin || RepMax > 100)
            throw new ArgumentException("Informe séries e repetições válidas.");
        if (SuggestedLoadKg is < 0 or > 10000) throw new ArgumentException("Carga sugerida inválida.");
    }
}

public sealed record WorkoutDay(Guid Id, DayOfWeek Weekday, string Name, int Order, IReadOnlyList<WorkoutExercise> Exercises)
{
    public void Validate()
    {
        if (Id == Guid.Empty || !Enum.IsDefined(Weekday) || string.IsNullOrWhiteSpace(Name) || Name.Length > 100 || Order < 0)
            throw new ArgumentException("Dia de treino inválido.");
        if (Exercises.Count == 0 || Exercises.Select(e => e.Id).Distinct().Count() != Exercises.Count)
            throw new ArgumentException("Inclua exercícios únicos no dia.");
        foreach (var exercise in Exercises) exercise.Validate();
    }
}

public sealed record WorkoutPlanVersion(Guid Id, Guid PlanId, int Number, string Name, DateOnly EffectiveFrom,
    DateOnly? SupersededOn, IReadOnlyList<WorkoutDay> Days)
{
    public void Validate()
    {
        if (Id == Guid.Empty || PlanId == Guid.Empty || Number < 1 || string.IsNullOrWhiteSpace(Name) || Name.Length > 120)
            throw new ArgumentException("Plano de treino inválido.");
        if (Days.Count == 0 || Days.Select(d => d.Weekday).Distinct().Count() != Days.Count ||
            Days.Select(d => d.Id).Distinct().Count() != Days.Count)
            throw new ArgumentException("Selecione dias de treino diferentes.");
        foreach (var day in Days) day.Validate();
    }
    public WorkoutDay? DayOn(DateOnly date) => date >= EffectiveFrom &&
        (SupersededOn is null || date < SupersededOn) ? Days.FirstOrDefault(d => d.Weekday == date.DayOfWeek) : null;
}

public sealed record WorkoutSet(Guid Id, Guid ExerciseId, int Number, int TargetRepMin, int TargetRepMax,
    decimal LoadKg, int ActualReps, DateTimeOffset CompletedAt)
{
    public void Validate()
    {
        if (Id == Guid.Empty || ExerciseId == Guid.Empty || Number < 1 || TargetRepMin < 1 ||
            TargetRepMax < TargetRepMin || TargetRepMax > 100 || LoadKg is < 0 or > 10000 ||
            ActualReps is < 0 or > 100 || CompletedAt > DateTimeOffset.UtcNow.AddMinutes(1))
            throw new ArgumentException("Série inválida.");
    }
    public decimal VolumeKg => LoadKg * ActualReps;
}

public sealed record WorkoutSession(Guid Id, Guid PlanVersionId, Guid DayId, Guid? PhaseId,
    DateTimeOffset StartedAt, DateTimeOffset? EndedAt, string? Note, IReadOnlyList<WorkoutSet> Sets)
{
    public TimeSpan? Duration => EndedAt is { } end ? end - StartedAt : null;
    public decimal VolumeKg => Sets.Sum(s => s.VolumeKg);
}

public sealed record WorkoutPhase(Guid Id, string Name, int Order, int Weeks, int TargetSets,
    int RepMin, int RepMax, string? Note)
{
    public void Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || Order < 0 ||
            Weeks is < 1 or > 52 || TargetSets is < 1 or > 20 || RepMin is < 1 or > 100 ||
            RepMax < RepMin || RepMax > 100 || Note?.Length > 1000)
            throw new ArgumentException("Fase de periodização inválida.");
    }
}

public sealed record WorkoutCycle(Guid Id, string Name, DateOnly StartedOn, DateOnly? PausedOn,
    int PausedDays, DateOnly? ArchivedOn, IReadOnlyList<WorkoutPhase> Phases)
{
    public void Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 120 || PausedDays < 0 ||
            Phases.Count == 0 || Phases.Select(p => p.Id).Distinct().Count() != Phases.Count)
            throw new ArgumentException("Ciclo de treino inválido.");
        foreach (var phase in Phases) phase.Validate();
    }
    public WorkoutPhase? PhaseOn(DateOnly date)
    {
        if (date < StartedOn || (ArchivedOn is { } archived && date >= archived)) return null;
        var effective = PausedOn is { } paused && date > paused ? paused : date;
        var elapsed = effective.DayNumber - StartedOn.DayNumber - PausedDays;
        if (elapsed < 0) return null;
        var total = Phases.Sum(p => p.Weeks * 7);
        var day = elapsed % total;
        foreach (var phase in Phases.OrderBy(p => p.Order))
        {
            if (day < phase.Weeks * 7) return phase;
            day -= phase.Weeks * 7;
        }
        return null;
    }
}
