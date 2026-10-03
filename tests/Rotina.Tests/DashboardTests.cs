using Microsoft.Data.Sqlite;
using Rotina.Domain;
using Rotina.Infrastructure;

namespace Rotina.Tests;

public sealed class DashboardTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "rotina-dashboard", Guid.NewGuid().ToString());
    private string Db => Path.Combine(directory, "rotina.db");

    [Fact]
    public async Task DefaultsHaveStableOrder()
    {
        var settings = await new SqliteLocalStore(Db).GetDashboardSettingsAsync();
        Assert.Equal(new[] { "habits", "planning", "workout", "water", "checkin" }, settings.Select(x => x.Key));
        Assert.All(settings, x => Assert.True(x.IsVisible));
    }

    [Fact]
    public async Task SavePersistsOrderAndVisibility()
    {
        var store = new SqliteLocalStore(Db);
        await store.SaveDashboardSettingsAsync([new("water", 0, false), new("habits", 1, true)]);
        var saved = await new SqliteLocalStore(Db).GetDashboardSettingsAsync();
        Assert.Equal(new[] { "water", "habits" }, saved.Select(x => x.Key)); Assert.False(saved[0].IsVisible);
    }

    [Fact]
    public async Task MigrationFromVersionNineCreatesDefaults()
    {
        var store = new SqliteLocalStore(Db); await store.GetDreamBoardItemsAsync();
        using var connection = new SqliteConnection($"Data Source={Db};Pooling=False"); await connection.OpenAsync(); using var command = connection.CreateCommand();
        command.CommandText = "DROP TABLE journal_entry; DROP TABLE dashboard_card_setting; PRAGMA user_version=9;"; await command.ExecuteNonQueryAsync();
        Assert.Equal(5, (await new SqliteLocalStore(Db).GetDashboardSettingsAsync()).Count);
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
