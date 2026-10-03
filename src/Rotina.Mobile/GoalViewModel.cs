using Rotina.Application;
using Rotina.Domain;

namespace Rotina.Mobile;

public sealed class GoalViewModel(IGoalStore store, ILocalStore local, IHabitStore habits, IPlanningStore planning, IBodyStore body)
{
    public IReadOnlyList<Goal> Goals { get; private set; } = [];
    public IReadOnlyList<BodyWeightRecord> Weights { get; private set; } = [];
    public IReadOnlyList<BodyMeasurement> Measurements { get; private set; } = [];
    public IReadOnlyList<ActionLane> Lanes { get; private set; } = [];
    public IReadOnlyList<ActionCard> Cards { get; private set; } = [];
    public IReadOnlyList<LifeArea> Areas { get; private set; } = [];
    public IReadOnlyList<HabitHistory> Habits { get; private set; } = [];
    public IReadOnlyList<PlannedTask> Tasks { get; private set; } = [];
    public string? Error { get; private set; }

    public async Task LoadAsync()
    {
        try
        {
            Goals = await store.GetGoalsAsync();
            Weights = await store.GetWeightsAsync();
            Measurements = await body.GetMeasurementsAsync();
            Lanes = await store.GetActionLanesAsync();
            Cards = await store.GetActionCardsAsync();
            Areas = await local.GetAreasAsync();
            Habits = await habits.GetHabitsAsync();
            Tasks = await planning.GetTasksAsync();
            Error = null;
        }
        catch (Exception) { Error = "Não foi possível abrir metas e Plano de Ação. Seus dados foram preservados."; }
    }

    public decimal? Current(Goal goal) => goal.Kind == GoalKind.BodyWeight
        ? Weights.LastOrDefault(w => w.VoidedAt is null)?.Kilograms
        : null;

    public async Task<decimal?> CurrentAsync(Goal goal)
    {
        if (goal.Kind == GoalKind.BodyWeight) return Current(goal);
        var history = await store.GetGoalProgressAsync(goal.Id);
        return history.LastOrDefault()?.Value ?? goal.StartValue;
    }

    public Task<IReadOnlyList<GoalProgressEvent>> ProgressAsync(Guid id) => store.GetGoalProgressAsync(id);
    public Task<IReadOnlyList<GoalLink>> LinksAsync(Guid id) => store.GetGoalLinksAsync(id);
    public Task<IReadOnlyList<ActionMoveEvent>> MovesAsync(Guid id) => store.GetActionMovesAsync(id);

    public async Task SaveGoalAsync(Goal goal) { await store.SaveGoalAsync(goal); await LoadAsync(); }
    public async Task CompleteGoalAsync(Goal goal, bool complete)
    { await store.SetGoalCompletedAsync(goal.Id, complete ? DateTimeOffset.UtcNow : null); await LoadAsync(); }
    public async Task ArchiveGoalAsync(Goal goal, bool archive)
    { await store.SetGoalArchivedAsync(goal.Id, archive); await LoadAsync(); }
    public async Task AddProgressAsync(Goal goal, decimal value, string? note)
    { await store.AddGoalProgressAsync(goal.Id, value, DateTimeOffset.UtcNow, note); await LoadAsync(); }
    public async Task AddWeightAsync(decimal kg, DateTimeOffset at, string? note)
    { await store.AddWeightAsync(new BodyWeightRecord(Guid.NewGuid(), kg, at, note)); await LoadAsync(); }
    public async Task CorrectWeightAsync(BodyWeightRecord original, decimal kg, DateTimeOffset at, string? note)
    { await body.CorrectWeightAsync(original.Id, new BodyWeightRecord(Guid.NewGuid(), kg, at, note, correctsId: original.Id), DateTimeOffset.UtcNow); await LoadAsync(); }
    public async Task VoidWeightAsync(BodyWeightRecord original)
    { await body.VoidWeightAsync(original.Id, DateTimeOffset.UtcNow); await LoadAsync(); }
    public async Task AddMeasurementAsync(string name, decimal value, string unit, DateTimeOffset at, string? note)
    { await body.AddMeasurementAsync(new BodyMeasurement(Guid.NewGuid(), name, value, unit, at, note)); await LoadAsync(); }
    public async Task CorrectMeasurementAsync(BodyMeasurement original, string name, decimal value, string unit, DateTimeOffset at, string? note)
    { await body.CorrectMeasurementAsync(original.Id, new BodyMeasurement(Guid.NewGuid(), name, value, unit, at, note, correctsId: original.Id), DateTimeOffset.UtcNow); await LoadAsync(); }
    public async Task VoidMeasurementAsync(BodyMeasurement original)
    { await body.VoidMeasurementAsync(original.Id, DateTimeOffset.UtcNow); await LoadAsync(); }
    public async Task AddLinkAsync(GoalLink link) { await store.AddGoalLinkAsync(link); await LoadAsync(); }
    public async Task RemoveLinkAsync(GoalLink link) { await store.RemoveGoalLinkAsync(link); await LoadAsync(); }
    public async Task SaveCardAsync(ActionCard card) { await store.SaveActionCardAsync(card); await LoadAsync(); }
    public async Task MoveCardAsync(ActionCard card, string lane)
    { await store.MoveActionCardAsync(card.Id, lane, DateTimeOffset.UtcNow); await LoadAsync(); }
    public async Task ArchiveCardAsync(ActionCard card, bool archive)
    { await store.SetActionCardArchivedAsync(card.Id, archive); await LoadAsync(); }
}
