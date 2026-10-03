using System.Globalization;
using Microsoft.Data.Sqlite;
using Rotina.Domain;

namespace Rotina.Infrastructure;

public sealed partial class SqliteLocalStore
{
    public async Task<IReadOnlyList<HabitHistory>> GetHabitsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        var habits = new Dictionary<Guid, (DateOnly created, DateOnly? archived)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id,created_on,archived_on FROM habit ORDER BY created_on,id;";
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                habits.Add(Guid.Parse(reader.GetString(0)), (DateOnly.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                    reader.IsDBNull(2) ? null : DateOnly.Parse(reader.GetString(2), CultureInfo.InvariantCulture)));
        }
        var revisions = habits.Keys.ToDictionary(id => id, _ => new List<HabitRevision>());
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT sequence,id,habit_id,name,area_id,effective_from,frequency,weekday_mask,weekly_target,is_paused FROM habit_revision ORDER BY sequence;";
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = Guid.Parse(reader.GetString(2));
                revisions[id].Add(new HabitRevision(Guid.Parse(reader.GetString(1)), id, reader.GetString(3),
                    reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4)), DateOnly.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                    (HabitFrequency)reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetBoolean(9), reader.GetInt64(0)));
            }
        }
        var events = habits.Keys.ToDictionary(id => id, _ => new List<HabitEvent>());
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT sequence,habit_id,occurrence_date,revision_id,is_completed,note,recorded_at_utc FROM habit_event ORDER BY sequence;";
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = Guid.Parse(reader.GetString(1));
                events[id].Add(new HabitEvent(reader.GetInt64(0), id, DateOnly.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                    Guid.Parse(reader.GetString(3)), reader.GetBoolean(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                    DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture)));
            }
        }
        var status = habits.Keys.ToDictionary(id => id, _ => new List<HabitStatusEvent>());
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT sequence,habit_id,effective_on,is_archived FROM habit_status_event ORDER BY sequence;";
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = Guid.Parse(reader.GetString(1));
                status[id].Add(new HabitStatusEvent(reader.GetInt64(0),
                    DateOnly.Parse(reader.GetString(2), CultureInfo.InvariantCulture), reader.GetBoolean(3)));
            }
        }
        return habits.Select(pair => new HabitHistory(pair.Key, pair.Value.created, pair.Value.archived,
            revisions[pair.Key], events[pair.Key], status[pair.Key])).ToList();
    }

    public async Task CreateHabitAsync(HabitRevision initial, DateOnly createdOn, CancellationToken cancellationToken = default)
    {
        if (initial.EffectiveFrom != createdOn) throw new ArgumentException("A primeira programação deve começar com o hábito.");
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO habit(id,created_on) VALUES ($id,$date);";
            command.Parameters.AddWithValue("$id", initial.HabitId.ToString());
            command.Parameters.AddWithValue("$date", createdOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await InsertRevisionAsync(connection, transaction, initial, cancellationToken);
        transaction.Commit();
    }

    public async Task ReviseHabitAsync(HabitRevision revision, CancellationToken cancellationToken = default)
    {
        var history = (await GetHabitsAsync(cancellationToken)).FirstOrDefault(h => h.Id == revision.HabitId)
            ?? throw new ArgumentException("Hábito não encontrado.");
        if (revision.EffectiveFrom < history.CreatedOn || revision.EffectiveFrom < DateOnly.FromDateTime(DateTime.Now))
            throw new ArgumentException("Alterações de programação valem a partir de hoje; o histórico anterior permanece.");
        await using var connection = await OpenAsync(cancellationToken);
        await InsertRevisionAsync(connection, null, revision, cancellationToken);
    }

    public async Task SetHabitArchivedAsync(Guid habitId, DateOnly? archivedOn, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "UPDATE habit SET archived_on=$date WHERE id=$id;";
            command.Parameters.AddWithValue("$date", archivedOn is null ? DBNull.Value : archivedOn.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$id", habitId.ToString());
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new ArgumentException("Hábito não encontrado.");
        }
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO habit_status_event(habit_id,effective_on,is_archived) VALUES($id,$date,$archived);";
            command.Parameters.AddWithValue("$id", habitId.ToString());
            command.Parameters.AddWithValue("$date", (archivedOn ?? DateOnly.FromDateTime(DateTime.Now)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$archived", archivedOn is null ? 0 : 1);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        transaction.Commit();
    }

    public async Task RecordHabitAsync(Guid habitId, DateOnly date, bool completed, string? note,
        DateTimeOffset recordedAtUtc, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(recordedAtUtc.ToLocalTime().DateTime);
        if (date > today) throw new ArgumentException("Não é possível concluir um hábito no futuro.");
        var history = (await GetHabitsAsync(cancellationToken)).FirstOrDefault(h => h.Id == habitId)
            ?? throw new ArgumentException("Hábito não encontrado.");
        var revision = history.RevisionOn(date);
        if (revision is null || !history.IsActiveOn(date)) throw new ArgumentException("Hábito não estava ativo nesta data.");
        if (completed && !HabitAnalytics.CanCompleteToday(history, date) &&
            !(history.CompletedOn(date) && !string.IsNullOrWhiteSpace(note)))
            throw new ArgumentException("Este hábito não está programado ou já foi concluído nesta data.");
        if (!completed && !history.CompletedOn(date)) return;
        if (note?.Length > 500) throw new ArgumentException("A nota pode ter até 500 caracteres.");
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO habit_event(habit_id,occurrence_date,revision_id,is_completed,note,recorded_at_utc)
            VALUES($id,$date,$revision,$completed,$note,$recorded);
            """;
        command.Parameters.AddWithValue("$id", habitId.ToString());
        command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$revision", revision.Id.ToString());
        command.Parameters.AddWithValue("$completed", completed ? 1 : 0);
        command.Parameters.AddWithValue("$note", note is null ? DBNull.Value : note.Trim());
        command.Parameters.AddWithValue("$recorded", recordedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertRevisionAsync(SqliteConnection connection, SqliteTransaction? transaction,
        HabitRevision revision, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO habit_revision(id,habit_id,name,area_id,effective_from,frequency,weekday_mask,weekly_target,is_paused)
            VALUES($id,$habit,$name,$area,$date,$frequency,$mask,$target,$paused);
            """;
        command.Parameters.AddWithValue("$id", revision.Id.ToString());
        command.Parameters.AddWithValue("$habit", revision.HabitId.ToString());
        command.Parameters.AddWithValue("$name", revision.Name);
        command.Parameters.AddWithValue("$area", revision.AreaId is null ? DBNull.Value : revision.AreaId.Value.ToString());
        command.Parameters.AddWithValue("$date", revision.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$frequency", (int)revision.Frequency);
        command.Parameters.AddWithValue("$mask", revision.WeekdayMask);
        command.Parameters.AddWithValue("$target", revision.WeeklyTarget);
        command.Parameters.AddWithValue("$paused", revision.IsPaused ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
