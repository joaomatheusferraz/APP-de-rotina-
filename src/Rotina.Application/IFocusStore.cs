using Rotina.Domain;

namespace Rotina.Application;

public interface IFocusStore
{
    Task<IReadOnlyList<FocusSession>> GetFocusSessionsAsync(CancellationToken cancellationToken = default);
    Task StartFocusAsync(FocusSession session, CancellationToken cancellationToken = default);
    Task PauseFocusAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken = default);
    Task ResumeFocusAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken = default);
    Task FinishFocusAsync(Guid id, DateTimeOffset at, int? rating, string? note, CancellationToken cancellationToken = default);
}
