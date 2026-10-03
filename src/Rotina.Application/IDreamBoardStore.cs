using Rotina.Domain;

namespace Rotina.Application;

public interface IDreamBoardStore
{
    Task<IReadOnlyList<DreamBoardItem>> GetDreamBoardItemsAsync(CancellationToken cancellationToken = default);
    Task SaveDreamBoardItemAsync(DreamBoardItem item, CancellationToken cancellationToken = default);
    Task SetDreamBoardArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default);
    Task MoveDreamBoardItemAsync(Guid id, int sortOrder, CancellationToken cancellationToken = default);
}
