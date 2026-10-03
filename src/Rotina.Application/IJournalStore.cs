using Rotina.Domain;
namespace Rotina.Application;
public interface IJournalStore
{
    Task<IReadOnlyList<JournalEntry>> GetJournalEntriesAsync(DateOnly? date = null, CancellationToken cancellationToken = default);
    Task SaveJournalEntryAsync(JournalEntry entry, CancellationToken cancellationToken = default);
    Task DeleteJournalEntryAsync(Guid id, CancellationToken cancellationToken = default);
}
