using Rotina.Domain;

namespace Rotina.Application;

public interface IGoalStore
{
    Task<IReadOnlyList<Goal>> GetGoalsAsync(CancellationToken cancellationToken = default);
    Task SaveGoalAsync(Goal goal, CancellationToken cancellationToken = default);
    Task SetGoalCompletedAsync(Guid id, DateTimeOffset? at, CancellationToken cancellationToken = default);
    Task SetGoalArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GoalProgressEvent>> GetGoalProgressAsync(Guid goalId, CancellationToken cancellationToken = default);
    Task AddGoalProgressAsync(Guid goalId, decimal value, DateTimeOffset at, string? note, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BodyWeightRecord>> GetWeightsAsync(CancellationToken cancellationToken = default);
    Task AddWeightAsync(BodyWeightRecord weight, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GoalLink>> GetGoalLinksAsync(Guid goalId, CancellationToken cancellationToken = default);
    Task AddGoalLinkAsync(GoalLink link, CancellationToken cancellationToken = default);
    Task RemoveGoalLinkAsync(GoalLink link, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActionLane>> GetActionLanesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActionCard>> GetActionCardsAsync(CancellationToken cancellationToken = default);
    Task SaveActionCardAsync(ActionCard card, CancellationToken cancellationToken = default);
    Task MoveActionCardAsync(Guid id, string laneId, DateTimeOffset at, CancellationToken cancellationToken = default);
    Task SetActionCardArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActionMoveEvent>> GetActionMovesAsync(Guid cardId, CancellationToken cancellationToken = default);
}
