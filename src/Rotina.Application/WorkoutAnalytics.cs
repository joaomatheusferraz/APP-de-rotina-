using Rotina.Domain;

namespace Rotina.Application;

public enum WorkoutDayState { Rest, Pending, Completed, Missed, Extra }
public sealed record WorkoutFrequency(int Planned, int Completed, int Missed, int Rest,
    IReadOnlyDictionary<DateOnly, WorkoutDayState> Days);
public sealed record ExerciseRecord(decimal MaxLoadKg, int MaxReps, decimal TotalVolumeKg, WorkoutSet? LastSet);

public static class WorkoutAnalytics
{
    public static WorkoutFrequency Frequency(IReadOnlyList<WorkoutPlanVersion> plans,
        IReadOnlyList<WorkoutSession> sessions, DateOnly today, int days)
    {
        if (days is < 1 or > 365) throw new ArgumentOutOfRangeException(nameof(days));
        var states = new Dictionary<DateOnly, WorkoutDayState>();
        var planned = 0;
        for (var date = today.AddDays(1 - days); date <= today; date = date.AddDays(1))
        {
            var plan = plans.Where(p => p.EffectiveFrom <= date && (p.SupersededOn is null || p.SupersededOn > date))
                .OrderByDescending(p => p.EffectiveFrom).ThenByDescending(p => p.Number).FirstOrDefault();
            var scheduled = plan?.DayOn(date) is not null;
            if (scheduled) planned++;
            var completed = sessions.Any(s => s.EndedAt is not null &&
                DateOnly.FromDateTime(s.StartedAt.ToLocalTime().DateTime) == date);
            states[date] = completed ? (scheduled ? WorkoutDayState.Completed : WorkoutDayState.Extra) : !scheduled ? WorkoutDayState.Rest :
                date == today ? WorkoutDayState.Pending : WorkoutDayState.Missed;
        }
        return new WorkoutFrequency(planned,
            states.Values.Count(s => s is WorkoutDayState.Completed or WorkoutDayState.Extra),
            states.Values.Count(s => s == WorkoutDayState.Missed),
            states.Values.Count(s => s == WorkoutDayState.Rest), states);
    }

    public static ExerciseRecord Record(Guid exerciseId, IReadOnlyList<WorkoutSession> sessions)
    {
        var sets = sessions.Where(s => s.EndedAt is not null).SelectMany(s => s.Sets)
            .Where(s => s.ExerciseId == exerciseId).OrderBy(s => s.CompletedAt).ToList();
        return new ExerciseRecord(sets.Count == 0 ? 0 : sets.Max(s => s.LoadKg),
            sets.Count == 0 ? 0 : sets.Max(s => s.ActualReps), sets.Sum(s => s.VolumeKg), sets.LastOrDefault());
    }
}
