using Rotina.Domain;

namespace Rotina.Application;

public interface IPlanningStore
{
    Task<IReadOnlyList<PlannedTask>> GetTasksAsync(CancellationToken cancellationToken = default);
    Task SaveTaskAsync(PlannedTask item, CancellationToken cancellationToken = default);
    Task SetTaskCompletedAsync(Guid id, DateTimeOffset? completedAt, CancellationToken cancellationToken = default);
    Task SetTaskArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Appointment>> GetAppointmentsAsync(CancellationToken cancellationToken = default);
    Task SaveAppointmentAsync(Appointment item, CancellationToken cancellationToken = default);
    Task SetAppointmentArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default);
}
