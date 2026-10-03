using Rotina.Application;
using Rotina.Domain;

namespace Rotina.Mobile;

public sealed class WorkoutViewModel(IWorkoutStore store)
{
    public IReadOnlyList<WorkoutPlanVersion> Plans { get; private set; } = [];
    public IReadOnlyList<WorkoutSession> Sessions { get; private set; } = [];
    public IReadOnlyList<WorkoutCycle> Cycles { get; private set; } = [];
    public string? Error { get; private set; }
    public WorkoutPlanVersion? CurrentPlan => Plans.Where(p => p.SupersededOn is null)
        .OrderByDescending(p => p.EffectiveFrom).ThenByDescending(p => p.Number).FirstOrDefault();
    public WorkoutCycle? CurrentCycle => Cycles.LastOrDefault(c => c.ArchivedOn is null);
    public WorkoutSession? OpenSession => Sessions.LastOrDefault(s => s.EndedAt is null);
    public WorkoutPhase? CurrentPhase => CurrentCycle?.PausedOn is null ?
        CurrentCycle?.PhaseOn(DateOnly.FromDateTime(DateTime.Today)) : null;

    public async Task LoadAsync()
    {
        try
        {
            Plans = await store.GetWorkoutPlansAsync();
            Sessions = await store.GetWorkoutSessionsAsync();
            Cycles = await store.GetWorkoutCyclesAsync();
            Error = null;
        }
        catch (Exception) { Error = "Não foi possível abrir treinos. Seus dados foram preservados."; }
    }
    public async Task SavePlanAsync(WorkoutPlanVersion plan) { await store.AddWorkoutPlanVersionAsync(plan); await LoadAsync(); }
    public async Task StartAsync(WorkoutPlanVersion plan, WorkoutDay day)
    {
        await store.StartWorkoutAsync(new WorkoutSession(Guid.NewGuid(), plan.Id, day.Id, CurrentPhase?.Id,
            DateTimeOffset.UtcNow, null, null, []));
        await LoadAsync();
    }
    public async Task AddSetAsync(WorkoutSession session, WorkoutExercise exercise, int number,
        int min, int max, decimal load, int reps)
    {
        await store.AddWorkoutSetAsync(session.Id, new WorkoutSet(Guid.NewGuid(), exercise.Id, number,
            min, max, load, reps, DateTimeOffset.UtcNow));
        await LoadAsync();
    }
    public async Task FinishAsync(WorkoutSession session, string? note)
    { await store.FinishWorkoutAsync(session.Id, DateTimeOffset.UtcNow, note); await LoadAsync(); }
    public async Task SaveCycleAsync(WorkoutCycle cycle) { await store.AddWorkoutCycleAsync(cycle); await LoadAsync(); }
    public async Task PauseCycleAsync(Guid id)
    { await store.PauseWorkoutCycleAsync(id, DateOnly.FromDateTime(DateTime.Today)); await LoadAsync(); }
    public async Task ResumeCycleAsync(Guid id)
    { await store.ResumeWorkoutCycleAsync(id, DateOnly.FromDateTime(DateTime.Today)); await LoadAsync(); }

    public WorkoutPlanVersion? PlanOf(WorkoutSession session) => Plans.FirstOrDefault(p => p.Id == session.PlanVersionId);
    public WorkoutDay? DayOf(WorkoutSession session) => PlanOf(session)?.Days.FirstOrDefault(d => d.Id == session.DayId);
    public WorkoutPhase? PhaseOf(WorkoutSession session) => Cycles.SelectMany(c => c.Phases).FirstOrDefault(p => p.Id == session.PhaseId);
    public IReadOnlyList<WorkoutSet> SetsForName(string name, Guid planId) =>
        (from plan in Plans where plan.PlanId == planId
         from day in plan.Days from exercise in day.Exercises
         where exercise.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
         from session in Sessions where session.PlanVersionId == plan.Id && session.EndedAt is not null
         from set in session.Sets where set.ExerciseId == exercise.Id
         orderby set.CompletedAt select set).ToList();
}
