using System.Globalization;
using Microsoft.Data.Sqlite;
using Rotina.Domain;

namespace Rotina.Infrastructure;

public sealed partial class SqliteLocalStore
{
    private static string DecimalText(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    private static decimal ReadDecimal(SqliteDataReader reader, int index) =>
        decimal.Parse(reader.GetString(index), CultureInfo.InvariantCulture);

    public async Task<IReadOnlyList<Goal>> GetGoalsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,title,description,starts_on,due_on,unit,start_value,target_value,area_id,kind,completed_at,is_archived FROM goal ORDER BY is_archived,completed_at IS NOT NULL,due_on IS NULL,due_on,title,id;";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<Goal>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new Goal(Guid.Parse(reader.GetString(0)), reader.GetString(1), TextOrNull(reader, 2),
                DateOnly.Parse(reader.GetString(3)), reader.IsDBNull(4) ? null : DateOnly.Parse(reader.GetString(4)),
                reader.GetString(5), reader.IsDBNull(6) ? null : ReadDecimal(reader, 6), ReadDecimal(reader, 7),
                GuidOrNull(reader, 8), (GoalKind)reader.GetInt32(9), InstantOrNull(reader, 10), reader.GetBoolean(11)));
        return result;
    }

    public async Task SaveGoalAsync(Goal goal, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO goal (id,title,description,starts_on,due_on,unit,start_value,target_value,area_id,kind,completed_at,is_archived)
            VALUES ($id,$title,$description,$starts,$due,$unit,$startValue,$target,$area,$kind,$completed,$archived)
            ON CONFLICT(id) DO UPDATE SET title=$title,description=$description,starts_on=$starts,due_on=$due,
                unit=$unit,start_value=$startValue,target_value=$target,area_id=$area,kind=$kind,
                completed_at=$completed,is_archived=$archived;
            """;
        command.Parameters.AddWithValue("$id", goal.Id.ToString());
        command.Parameters.AddWithValue("$title", goal.Title);
        command.Parameters.AddWithValue("$description", Db(goal.Description));
        command.Parameters.AddWithValue("$starts", goal.StartsOn.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$due", Db(goal.DueOn?.ToString("yyyy-MM-dd")));
        command.Parameters.AddWithValue("$unit", goal.Unit);
        command.Parameters.AddWithValue("$startValue", Db(goal.StartValue is { } start ? DecimalText(start) : null));
        command.Parameters.AddWithValue("$target", DecimalText(goal.TargetValue));
        command.Parameters.AddWithValue("$area", Db(goal.AreaId?.ToString()));
        command.Parameters.AddWithValue("$kind", (int)goal.Kind);
        command.Parameters.AddWithValue("$completed", Db(goal.CompletedAt?.ToUniversalTime().ToString("O")));
        command.Parameters.AddWithValue("$archived", goal.IsArchived ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetGoalCompletedAsync(Guid id, DateTimeOffset? at, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE goal SET completed_at=$completed,is_archived=$archived WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$completed", Db(at?.ToUniversalTime().ToString("O")));
        command.Parameters.AddWithValue("$archived", at is null ? 0 : 1);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new KeyNotFoundException("Meta não encontrada.");
    }

    public Task SetGoalArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default) =>
        UpdatePlanningAsync("UPDATE goal SET is_archived=$value WHERE id=$id", id, archived ? 1 : 0, cancellationToken);

    public async Task<IReadOnlyList<GoalProgressEvent>> GetGoalProgressAsync(Guid goalId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sequence,goal_id,value,recorded_at,note FROM goal_progress_event WHERE goal_id=$id ORDER BY recorded_at,sequence;";
        command.Parameters.AddWithValue("$id", goalId.ToString());
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<GoalProgressEvent>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new GoalProgressEvent(reader.GetInt64(0), Guid.Parse(reader.GetString(1)), ReadDecimal(reader, 2),
                DateTimeOffset.Parse(reader.GetString(3)), TextOrNull(reader, 4)));
        return result;
    }

    public async Task AddGoalProgressAsync(Guid goalId, decimal value, DateTimeOffset at, string? note, CancellationToken cancellationToken = default)
    {
        if (value is < 0 or > 1000000000) throw new ArgumentException("Valor de progresso inválido.");
        if (at > DateTimeOffset.UtcNow.AddMinutes(1)) throw new ArgumentException("O progresso não pode ser registrado no futuro.");
        if (note?.Length > 1000) throw new ArgumentException("Nota muito longa.");
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO goal_progress_event (goal_id,value,recorded_at,note) SELECT id,$value,$at,$note FROM goal WHERE id=$id AND kind=0;";
        command.Parameters.AddWithValue("$id", goalId.ToString());
        command.Parameters.AddWithValue("$value", DecimalText(value));
        command.Parameters.AddWithValue("$at", at.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$note", Db(note));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("A meta não aceita progresso manual.");
    }

    public async Task<IReadOnlyList<BodyWeightRecord>> GetWeightsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,kilograms,measured_at,note,voided_at,corrects_id FROM body_weight_record ORDER BY measured_at,id;";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<BodyWeightRecord>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new BodyWeightRecord(Guid.Parse(reader.GetString(0)), ReadDecimal(reader, 1),
                DateTimeOffset.Parse(reader.GetString(2)), TextOrNull(reader, 3), InstantOrNull(reader, 4), GuidOrNull(reader, 5)));
        return result;
    }

    public async Task AddWeightAsync(BodyWeightRecord weight, CancellationToken cancellationToken = default)
    {
        if (weight.VoidedAt is not null || weight.CorrectsId is not null) throw new ArgumentException("Use a operação de correção para substituir uma pesagem.");
        if (weight.MeasuredAt > DateTimeOffset.UtcNow.AddMinutes(1)) throw new ArgumentException("A pesagem não pode ser registrada no futuro.");
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO body_weight_record (id,kilograms,measured_at,note,corrects_id) VALUES ($id,$kg,$at,$note,$corrects);";
        command.Parameters.AddWithValue("$id", weight.Id.ToString());
        command.Parameters.AddWithValue("$kg", DecimalText(weight.Kilograms));
        command.Parameters.AddWithValue("$at", weight.MeasuredAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$note", Db(weight.Note));
        command.Parameters.AddWithValue("$corrects", Db(weight.CorrectsId?.ToString()));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<GoalLink>> GetGoalLinksAsync(Guid goalId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT goal_id,kind,target_id FROM goal_link WHERE goal_id=$id ORDER BY kind,target_id;";
        command.Parameters.AddWithValue("$id", goalId.ToString());
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<GoalLink>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new GoalLink(Guid.Parse(reader.GetString(0)), (GoalLinkKind)reader.GetInt32(1), Guid.Parse(reader.GetString(2))));
        return result;
    }

    public async Task AddGoalLinkAsync(GoalLink link, CancellationToken cancellationToken = default)
    {
        if (link.GoalId == Guid.Empty || link.TargetId == Guid.Empty || !Enum.IsDefined(link.Kind)) throw new ArgumentException("Vínculo inválido.");
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = link.Kind == GoalLinkKind.Habit
            ? "INSERT OR IGNORE INTO goal_link (goal_id,kind,target_id) SELECT $goal,$kind,$target WHERE EXISTS(SELECT 1 FROM goal WHERE id=$goal) AND EXISTS(SELECT 1 FROM habit WHERE id=$target);"
            : "INSERT OR IGNORE INTO goal_link (goal_id,kind,target_id) SELECT $goal,$kind,$target WHERE EXISTS(SELECT 1 FROM goal WHERE id=$goal) AND EXISTS(SELECT 1 FROM planned_task WHERE id=$target);";
        command.Parameters.AddWithValue("$goal", link.GoalId.ToString());
        command.Parameters.AddWithValue("$kind", (int)link.Kind);
        command.Parameters.AddWithValue("$target", link.TargetId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
        using var check = connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM goal_link WHERE goal_id=$goal AND kind=$kind AND target_id=$target;";
        check.Parameters.AddWithValue("$goal", link.GoalId.ToString());
        check.Parameters.AddWithValue("$kind", (int)link.Kind);
        check.Parameters.AddWithValue("$target", link.TargetId.ToString());
        if (Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken)) != 1) throw new ArgumentException("Meta ou item vinculado não encontrado.");
    }

    public async Task RemoveGoalLinkAsync(GoalLink link, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM goal_link WHERE goal_id=$goal AND kind=$kind AND target_id=$target;";
        command.Parameters.AddWithValue("$goal", link.GoalId.ToString());
        command.Parameters.AddWithValue("$kind", (int)link.Kind);
        command.Parameters.AddWithValue("$target", link.TargetId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ActionLane>> GetActionLanesAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,title,sort_order FROM action_lane ORDER BY sort_order,id;";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ActionLane>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(new ActionLane(reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
        return result;
    }

    public async Task<IReadOnlyList<ActionCard>> GetActionCardsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,title,description,tags,area_id,due_on,goal_id,lane_id,is_archived FROM action_card ORDER BY is_archived,due_on IS NULL,due_on,title,id;";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ActionCard>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new ActionCard(Guid.Parse(reader.GetString(0)), reader.GetString(1), TextOrNull(reader, 2),
                TextOrNull(reader, 3), GuidOrNull(reader, 4), reader.IsDBNull(5) ? null : DateOnly.Parse(reader.GetString(5)),
                GuidOrNull(reader, 6), reader.GetString(7), reader.GetBoolean(8)));
        return result;
    }

    public async Task SaveActionCardAsync(ActionCard card, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        using var prior = connection.CreateCommand();
        prior.Transaction = transaction;
        prior.CommandText = "SELECT lane_id FROM action_card WHERE id=$id;";
        prior.Parameters.AddWithValue("$id", card.Id.ToString());
        var oldLane = (string?)await prior.ExecuteScalarAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO action_card (id,title,description,tags,area_id,due_on,goal_id,lane_id,is_archived)
            VALUES ($id,$title,$description,$tags,$area,$due,$goal,$lane,$archived)
            ON CONFLICT(id) DO UPDATE SET title=$title,description=$description,tags=$tags,area_id=$area,
                due_on=$due,goal_id=$goal,lane_id=$lane,is_archived=$archived;
            """;
        command.Parameters.AddWithValue("$id", card.Id.ToString());
        command.Parameters.AddWithValue("$title", card.Title);
        command.Parameters.AddWithValue("$description", Db(card.Description));
        command.Parameters.AddWithValue("$tags", Db(card.Tags));
        command.Parameters.AddWithValue("$area", Db(card.AreaId?.ToString()));
        command.Parameters.AddWithValue("$due", Db(card.DueOn?.ToString("yyyy-MM-dd")));
        command.Parameters.AddWithValue("$goal", Db(card.GoalId?.ToString()));
        command.Parameters.AddWithValue("$lane", card.LaneId);
        command.Parameters.AddWithValue("$archived", card.IsArchived ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (oldLane != card.LaneId) await InsertMoveAsync(connection, transaction, card.Id, card.LaneId, DateTimeOffset.UtcNow, cancellationToken);
        transaction.Commit();
    }

    public async Task MoveActionCardAsync(Guid id, string laneId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE action_card SET lane_id=$lane WHERE id=$id AND lane_id<>$lane;";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$lane", laneId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 1)
            await InsertMoveAsync(connection, transaction, id, laneId, at, cancellationToken);
        else
        {
            using var check = connection.CreateCommand();
            check.Transaction = transaction;
            check.CommandText = "SELECT COUNT(*) FROM action_card WHERE id=$id;";
            check.Parameters.AddWithValue("$id", id.ToString());
            if (Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken)) == 0) throw new KeyNotFoundException("Cartão não encontrado.");
        }
        transaction.Commit();
    }

    private static async Task InsertMoveAsync(SqliteConnection connection, SqliteTransaction transaction,
        Guid id, string laneId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO action_move_event (card_id,lane_id,recorded_at) VALUES ($id,$lane,$at);";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$lane", laneId);
        command.Parameters.AddWithValue("$at", at.ToUniversalTime().ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task SetActionCardArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default) =>
        UpdatePlanningAsync("UPDATE action_card SET is_archived=$value WHERE id=$id", id, archived ? 1 : 0, cancellationToken);

    public async Task<IReadOnlyList<ActionMoveEvent>> GetActionMovesAsync(Guid cardId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sequence,card_id,lane_id,recorded_at FROM action_move_event WHERE card_id=$id ORDER BY sequence;";
        command.Parameters.AddWithValue("$id", cardId.ToString());
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ActionMoveEvent>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new ActionMoveEvent(reader.GetInt64(0), Guid.Parse(reader.GetString(1)), reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3))));
        return result;
    }
}
