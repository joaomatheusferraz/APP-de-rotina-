using Rotina.Domain;

namespace Rotina.Application;

public interface IHabitStore
{
    Task<IReadOnlyList<HabitHistory>> GetHabitsAsync(CancellationToken cancellationToken = default);
    Task CreateHabitAsync(HabitRevision initial, DateOnly createdOn, CancellationToken cancellationToken = default);
    Task ReviseHabitAsync(HabitRevision revision, CancellationToken cancellationToken = default);
    Task SetHabitArchivedAsync(Guid habitId, DateOnly? archivedOn, CancellationToken cancellationToken = default);
    Task RecordHabitAsync(Guid habitId, DateOnly date, bool completed, string? note,
        DateTimeOffset recordedAtUtc, CancellationToken cancellationToken = default);
}
