using Microsoft.Data.Sqlite;
using Rotina.Domain;
using Rotina.Infrastructure;

namespace Rotina.Tests;

public sealed class DreamBoardTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "rotina-dreams", Guid.NewGuid().ToString());
    private string Db => Path.Combine(directory, "rotina.db");

    [Fact]
    public async Task DreamsKeepOptionalLinksAndCanBeReordered()
    {
        var store = new SqliteLocalStore(Db);
        var goal = new Goal(Guid.NewGuid(), "Viajar", null, DateOnly.FromDateTime(DateTime.Today), null, "vezes", 0, 1, null, GoalKind.Numeric);
        await store.SaveGoalAsync(goal);
        var first = new DreamBoardItem(Guid.NewGuid(), "dreams/a.jpg", "Praia", "Uma viagem tranquila", null, goal.Id, null, 0, DreamCardSize.Large);
        var second = new DreamBoardItem(Guid.NewGuid(), "dreams/b.jpg", "Casa", null, null, null, null, 1, DreamCardSize.Medium);
        await store.SaveDreamBoardItemAsync(first); await store.SaveDreamBoardItemAsync(second);
        await store.MoveDreamBoardItemAsync(second.Id, 0);
        var items = await new SqliteLocalStore(Db).GetDreamBoardItemsAsync();
        Assert.Equal(second.Id, items[0].Id); Assert.Equal(first.Id, items[1].Id); Assert.Equal(goal.Id, items[1].GoalId);
    }

    [Fact]
    public async Task ArchiveKeepsDreamAndMigrationFromVersionEightCreatesBoard()
    {
        var store = new SqliteLocalStore(Db);
        await store.GetFocusSessionsAsync();
        var item = new DreamBoardItem(Guid.NewGuid(), "dreams/vision.png", null, null, null, null, null, 0, DreamCardSize.Small);
        await store.SaveDreamBoardItemAsync(item); await store.SetDreamBoardArchivedAsync(item.Id, true);
        Assert.True(Assert.Single(await store.GetDreamBoardItemsAsync()).IsArchived);
        using (var connection = new SqliteConnection($"Data Source={Db};Pooling=False"))
        {
            await connection.OpenAsync(); using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE journal_entry; DROP TABLE dashboard_card_setting; DROP TABLE dream_board_item; PRAGMA user_version=8;"; await command.ExecuteNonQueryAsync();
        }
        var migrated = new SqliteLocalStore(Db);
        Assert.Empty(await migrated.GetDreamBoardItemsAsync());
    }

    [Fact]
    public void DreamValidationRejectsInvalidData()
    {
        Assert.Throws<ArgumentException>(() => new DreamBoardItem(Guid.NewGuid(), "", null, null, null, null, null, 0, DreamCardSize.Small).Validate());
        Assert.Throws<ArgumentException>(() => new DreamBoardItem(Guid.NewGuid(), "image", new string('x', 121), null, null, null, null, 0, DreamCardSize.Small).Validate());
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
