using Rotina.Domain;

namespace Rotina.Application;

public interface IWorkoutStore
{
    Task<IReadOnlyList<WorkoutPlanVersion>> GetWorkoutPlansAsync(CancellationToken cancellationToken = default);
    Task AddWorkoutPlanVersionAsync(WorkoutPlanVersion version, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkoutSession>> GetWorkoutSessionsAsync(CancellationToken cancellationToken = default);
    Task StartWorkoutAsync(WorkoutSession session, CancellationToken cancellationToken = default);
    Task AddWorkoutSetAsync(Guid sessionId, WorkoutSet set, CancellationToken cancellationToken = default);
    Task FinishWorkoutAsync(Guid sessionId, DateTimeOffset endedAt, string? note, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkoutCycle>> GetWorkoutCyclesAsync(CancellationToken cancellationToken = default);
    Task AddWorkoutCycleAsync(WorkoutCycle cycle, CancellationToken cancellationToken = default);
    Task PauseWorkoutCycleAsync(Guid id, DateOnly pausedOn, CancellationToken cancellationToken = default);
    Task ResumeWorkoutCycleAsync(Guid id, DateOnly resumedOn, CancellationToken cancellationToken = default);
}
