using Microsoft.Data.Sqlite;
using Rotina.Domain;

namespace Rotina.Infrastructure;

public sealed partial class SqliteLocalStore
{
    public async Task<IReadOnlyList<DreamBoardItem>> GetDreamBoardItemsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,image_path,title,description,area_id,goal_id,action_card_id,sort_order,size,is_archived FROM dream_board_item ORDER BY is_archived,sort_order,id;";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<DreamBoardItem>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new DreamBoardItem(Guid.Parse(reader.GetString(0)), reader.GetString(1), TextOrNull(reader, 2), TextOrNull(reader, 3),
                GuidOrNull(reader, 4), GuidOrNull(reader, 5), GuidOrNull(reader, 6), reader.GetInt32(7), (DreamCardSize)reader.GetInt32(8), reader.GetBoolean(9)));
        return result;
    }

    public async Task SaveDreamBoardItemAsync(DreamBoardItem item, CancellationToken cancellationToken = default)
    {
        item.Validate();
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO dream_board_item (id,image_path,title,description,area_id,goal_id,action_card_id,sort_order,size,is_archived)
            VALUES ($id,$path,$title,$description,$area,$goal,$card,$order,$size,$archived)
            ON CONFLICT(id) DO UPDATE SET image_path=$path,title=$title,description=$description,area_id=$area,goal_id=$goal,
                action_card_id=$card,sort_order=$order,size=$size,is_archived=$archived;
            """;
        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$path", item.ImagePath);
        command.Parameters.AddWithValue("$title", Db(item.Title));
        command.Parameters.AddWithValue("$description", Db(item.Description));
        command.Parameters.AddWithValue("$area", Db(item.AreaId?.ToString()));
        command.Parameters.AddWithValue("$goal", Db(item.GoalId?.ToString()));
        command.Parameters.AddWithValue("$card", Db(item.ActionCardId?.ToString()));
        command.Parameters.AddWithValue("$order", item.SortOrder);
        command.Parameters.AddWithValue("$size", (int)item.Size);
        command.Parameters.AddWithValue("$archived", item.IsArchived ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task SetDreamBoardArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default) =>
        UpdatePlanningAsync("UPDATE dream_board_item SET is_archived=$value WHERE id=$id", id, archived ? 1 : 0, cancellationToken);

    public async Task MoveDreamBoardItemAsync(Guid id, int sortOrder, CancellationToken cancellationToken = default)
    {
        if (sortOrder < 0) throw new ArgumentOutOfRangeException(nameof(sortOrder));
        await InitializeAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        using var read = connection.CreateCommand(); read.Transaction = transaction;
        read.CommandText = "SELECT sort_order FROM dream_board_item WHERE id=$id AND is_archived=0;";
        read.Parameters.AddWithValue("$id", id.ToString());
        var old = await read.ExecuteScalarAsync(cancellationToken);
        if (old is null) throw new KeyNotFoundException("Cartão não encontrado.");
        var current = Convert.ToInt32(old);
        if (current != sortOrder)
        {
            using var shift = connection.CreateCommand(); shift.Transaction = transaction;
            shift.CommandText = current < sortOrder
                ? "UPDATE dream_board_item SET sort_order=sort_order-1 WHERE is_archived=0 AND sort_order>$from AND sort_order<=$to;"
                : "UPDATE dream_board_item SET sort_order=sort_order+1 WHERE is_archived=0 AND sort_order>=$to AND sort_order<$from;";
            shift.Parameters.AddWithValue("$from", current); shift.Parameters.AddWithValue("$to", sortOrder);
            await shift.ExecuteNonQueryAsync(cancellationToken);
            using var update = connection.CreateCommand(); update.Transaction = transaction;
            update.CommandText = "UPDATE dream_board_item SET sort_order=$order WHERE id=$id;";
            update.Parameters.AddWithValue("$order", sortOrder); update.Parameters.AddWithValue("$id", id.ToString());
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
        transaction.Commit();
    }
}
