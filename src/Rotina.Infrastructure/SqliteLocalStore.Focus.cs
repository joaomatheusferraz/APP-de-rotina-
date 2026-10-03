using Microsoft.Data.Sqlite;
using Rotina.Domain;

namespace Rotina.Infrastructure;

public sealed partial class SqliteLocalStore
{
    public async Task<IReadOnlyList<FocusSession>> GetFocusSessionsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,title,category,target_minutes,started_at,paused_at,ended_at,paused_minutes,rating,note,area_id FROM focus_session ORDER BY started_at,id;";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<FocusSession>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new FocusSession(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
                DateTimeOffset.Parse(reader.GetString(4)), InstantOrNull(reader, 5), InstantOrNull(reader, 6), reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8), TextOrNull(reader, 9), GuidOrNull(reader, 10)));
        return result;
    }

    public async Task StartFocusAsync(FocusSession session, CancellationToken cancellationToken = default)
    {
        session.Validate();
        if (session.PausedAt is not null || session.EndedAt is not null || session.PausedMinutes != 0)
            throw new ArgumentException("A sessão nova não pode estar pausada ou encerrada.");
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO focus_session (id,title,category,target_minutes,started_at,paused_at,ended_at,paused_minutes,rating,note,area_id) SELECT $id,$title,$category,$target,$start,NULL,NULL,0,NULL,NULL,$area WHERE NOT EXISTS (SELECT 1 FROM focus_session WHERE ended_at IS NULL);";
        command.Parameters.AddWithValue("$id", session.Id.ToString());
        command.Parameters.AddWithValue("$title", session.Title.Trim());
        command.Parameters.AddWithValue("$category", session.Category.Trim());
        command.Parameters.AddWithValue("$target", session.TargetMinutes);
        command.Parameters.AddWithValue("$start", session.StartedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$area", Db(session.AreaId?.ToString()));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("Já existe uma sessão de foco em andamento.");
    }

    public async Task PauseFocusAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        if (at > DateTimeOffset.UtcNow.AddMinutes(1)) throw new ArgumentException("A pausa não pode estar no futuro.");
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE focus_session SET paused_at=$at WHERE id=$id AND ended_at IS NULL AND paused_at IS NULL AND started_at<=$at;";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$at", at.ToUniversalTime().ToString("O"));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("A sessão não está em execução.");
    }

    public async Task ResumeFocusAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        if (at > DateTimeOffset.UtcNow.AddMinutes(1)) throw new ArgumentException("A retomada não pode estar no futuro.");
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE focus_session SET paused_minutes=paused_minutes+CAST((julianday($at)-julianday(paused_at))*1440 AS INTEGER),paused_at=NULL WHERE id=$id AND ended_at IS NULL AND paused_at IS NOT NULL AND paused_at<=$at;";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$at", at.ToUniversalTime().ToString("O"));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("A sessão não está pausada.");
    }

    public async Task FinishFocusAsync(Guid id, DateTimeOffset at, int? rating, string? note, CancellationToken cancellationToken = default)
    {
        if (at > DateTimeOffset.UtcNow.AddMinutes(1) || rating is < 1 or > 5 || note?.Length > 1000)
            throw new ArgumentException("Encerramento de foco inválido.");
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE focus_session SET ended_at=$at,paused_at=NULL,rating=$rating,note=$note WHERE id=$id AND ended_at IS NULL AND started_at<=$at;";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$at", at.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$rating", Db(rating?.ToString()));
        command.Parameters.AddWithValue("$note", Db(note));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("A sessão já foi encerrada ou não existe.");
    }
}
