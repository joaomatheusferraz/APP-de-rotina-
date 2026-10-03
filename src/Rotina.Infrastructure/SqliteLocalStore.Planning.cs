using Microsoft.Data.Sqlite;
using Rotina.Domain;

namespace Rotina.Infrastructure;

public sealed partial class SqliteLocalStore
{
    public async Task<IReadOnlyList<PlannedTask>> GetTasksAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,title,description,due_on,priority,area_id,reminder_at,completed_at,is_archived FROM planned_task ORDER BY is_archived,completed_at IS NOT NULL,due_on IS NULL,due_on,priority DESC,title,id;";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<PlannedTask>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new PlannedTask(Guid.Parse(reader.GetString(0)), reader.GetString(1), TextOrNull(reader, 2),
                reader.IsDBNull(3) ? null : DateOnly.Parse(reader.GetString(3)), (TaskPriority)reader.GetInt32(4),
                GuidOrNull(reader, 5), InstantOrNull(reader, 6), InstantOrNull(reader, 7), reader.GetBoolean(8)));
        return result;
    }

    public async Task SaveTaskAsync(PlannedTask item, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO planned_task (id,title,description,due_on,priority,area_id,reminder_at,completed_at,is_archived)
            VALUES ($id,$title,$description,$due,$priority,$area,$reminder,$completed,$archived)
            ON CONFLICT(id) DO UPDATE SET title=$title,description=$description,due_on=$due,priority=$priority,
                area_id=$area,reminder_at=$reminder,completed_at=$completed,is_archived=$archived;
            """;
        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$description", Db(item.Description));
        command.Parameters.AddWithValue("$due", Db(item.DueOn?.ToString("yyyy-MM-dd")));
        command.Parameters.AddWithValue("$priority", (int)item.Priority);
        command.Parameters.AddWithValue("$area", Db(item.AreaId?.ToString()));
        command.Parameters.AddWithValue("$reminder", Db(item.ReminderAt?.ToUniversalTime().ToString("O")));
        command.Parameters.AddWithValue("$completed", Db(item.CompletedAt?.ToUniversalTime().ToString("O")));
        command.Parameters.AddWithValue("$archived", item.IsArchived ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task SetTaskCompletedAsync(Guid id, DateTimeOffset? completedAt, CancellationToken cancellationToken = default) =>
        UpdatePlanningAsync("UPDATE planned_task SET completed_at=$value WHERE id=$id", id,
            Db(completedAt?.ToUniversalTime().ToString("O")), cancellationToken);

    public Task SetTaskArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default) =>
        UpdatePlanningAsync("UPDATE planned_task SET is_archived=$value WHERE id=$id", id, archived ? 1 : 0, cancellationToken);

    public async Task<IReadOnlyList<Appointment>> GetAppointmentsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,title,starts_at,duration_minutes,location,note,area_id,reminder_at,is_archived FROM appointment ORDER BY is_archived,starts_at,title,id;";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<Appointment>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new Appointment(Guid.Parse(reader.GetString(0)), reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2)), reader.GetInt32(3), TextOrNull(reader, 4),
                TextOrNull(reader, 5), GuidOrNull(reader, 6), InstantOrNull(reader, 7), reader.GetBoolean(8)));
        return result;
    }

    public async Task SaveAppointmentAsync(Appointment item, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO appointment (id,title,starts_at,duration_minutes,location,note,area_id,reminder_at,is_archived)
            VALUES ($id,$title,$starts,$duration,$location,$note,$area,$reminder,$archived)
            ON CONFLICT(id) DO UPDATE SET title=$title,starts_at=$starts,duration_minutes=$duration,
                location=$location,note=$note,area_id=$area,reminder_at=$reminder,is_archived=$archived;
            """;
        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$starts", item.StartsAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$duration", item.DurationMinutes);
        command.Parameters.AddWithValue("$location", Db(item.Location));
        command.Parameters.AddWithValue("$note", Db(item.Note));
        command.Parameters.AddWithValue("$area", Db(item.AreaId?.ToString()));
        command.Parameters.AddWithValue("$reminder", Db(item.ReminderAt?.ToUniversalTime().ToString("O")));
        command.Parameters.AddWithValue("$archived", item.IsArchived ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task SetAppointmentArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default) =>
        UpdatePlanningAsync("UPDATE appointment SET is_archived=$value WHERE id=$id", id, archived ? 1 : 0, cancellationToken);

    private async Task UpdatePlanningAsync(string sql, Guid id, object value, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$value", value);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new KeyNotFoundException("Item não encontrado.");
    }

    private static object Db(string? value) => value is null ? DBNull.Value : value;
    private static string? TextOrNull(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
    private static Guid? GuidOrNull(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : Guid.Parse(reader.GetString(index));
    private static DateTimeOffset? InstantOrNull(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : DateTimeOffset.Parse(reader.GetString(index));
}
