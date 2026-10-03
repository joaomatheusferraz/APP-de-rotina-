using Rotina.Domain;
namespace Rotina.Infrastructure;
public sealed partial class SqliteLocalStore
{
    public async Task<IReadOnlyList<JournalEntry>> GetJournalEntriesAsync(DateOnly? date = null, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken); await using var connection = await OpenAsync(cancellationToken); using var command = connection.CreateCommand();
        command.CommandText = date is null ? "SELECT id,local_date,text,created_at,updated_at FROM journal_entry ORDER BY local_date DESC,created_at DESC;" : "SELECT id,local_date,text,created_at,updated_at FROM journal_entry WHERE local_date=$date ORDER BY created_at DESC;";
        if (date is not null) command.Parameters.AddWithValue("$date", date.Value.ToString("yyyy-MM-dd")); using var reader = await command.ExecuteReaderAsync(cancellationToken); var result=new List<JournalEntry>();
        while(await reader.ReadAsync(cancellationToken)) result.Add(new(Guid.Parse(reader.GetString(0)), DateOnly.Parse(reader.GetString(1)), reader.GetString(2), DateTimeOffset.Parse(reader.GetString(3)), reader.IsDBNull(4)?null:DateTimeOffset.Parse(reader.GetString(4)))); return result;
    }
    public async Task SaveJournalEntryAsync(JournalEntry entry, CancellationToken cancellationToken = default)
    { entry.Validate(); await InitializeAsync(cancellationToken); await using var connection=await OpenAsync(cancellationToken); using var command=connection.CreateCommand(); command.CommandText="INSERT INTO journal_entry(id,local_date,text,created_at,updated_at) VALUES($id,$date,$text,$created,$updated) ON CONFLICT(id) DO UPDATE SET local_date=$date,text=$text,updated_at=$updated;"; command.Parameters.AddWithValue("$id",entry.Id.ToString());command.Parameters.AddWithValue("$date",entry.LocalDate.ToString("yyyy-MM-dd"));command.Parameters.AddWithValue("$text",entry.Text.Trim());command.Parameters.AddWithValue("$created",entry.CreatedAt.ToUniversalTime().ToString("O"));command.Parameters.AddWithValue("$updated",entry.UpdatedAt?.ToUniversalTime().ToString("O")??(object)DBNull.Value);await command.ExecuteNonQueryAsync(cancellationToken); }
    public async Task DeleteJournalEntryAsync(Guid id, CancellationToken cancellationToken = default)
    { await InitializeAsync(cancellationToken); await using var connection=await OpenAsync(cancellationToken); using var command=connection.CreateCommand(); command.CommandText="DELETE FROM journal_entry WHERE id=$id;";command.Parameters.AddWithValue("$id",id.ToString());await command.ExecuteNonQueryAsync(cancellationToken); }
}
