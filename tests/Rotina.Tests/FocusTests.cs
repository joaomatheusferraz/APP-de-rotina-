using Microsoft.Data.Sqlite;
using Rotina.Domain;
using Rotina.Infrastructure;

namespace Rotina.Tests;

public sealed class FocusTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "rotina-focus", Guid.NewGuid().ToString());
    private string Db => Path.Combine(directory, "rotina.db");

    [Fact]
    public async Task FocusSessionPausesResumesAndStoresRealDuration()
    {
        var store = new SqliteLocalStore(Db);
        var start = DateTimeOffset.UtcNow.AddMinutes(-30);
        var session = new FocusSession(Guid.NewGuid(), "POO", "Faculdade", 45, start, null, null, 0, null, null, null);
        await store.StartFocusAsync(session);
        await store.PauseFocusAsync(session.Id, DateTimeOffset.UtcNow.AddMinutes(-20));
        await store.ResumeFocusAsync(session.Id, DateTimeOffset.UtcNow.AddMinutes(-10));
        await store.FinishFocusAsync(session.Id, DateTimeOffset.UtcNow, 5, "Bom foco");
        var saved = Assert.Single(await new SqliteLocalStore(Db).GetFocusSessionsAsync());
        Assert.Equal(FocusStatus.Finished, saved.Status);
        Assert.InRange(saved.PausedMinutes, 9, 11);
        Assert.InRange(saved.ElapsedMinutes(DateTimeOffset.UtcNow), 19, 22);
        Assert.Equal(5, saved.Rating);
    }

    [Fact]
    public async Task OnlyOneFocusSessionCanBeOpenAndFutureValuesAreRejected()
    {
        var store = new SqliteLocalStore(Db);
        var session = new FocusSession(Guid.NewGuid(), "C#", "Estudos", 25, DateTimeOffset.UtcNow, null, null, 0, null, null, null);
        await store.StartFocusAsync(session);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StartFocusAsync(session with { Id = Guid.NewGuid() }));
        await Assert.ThrowsAsync<ArgumentException>(() => store.FinishFocusAsync(session.Id, DateTimeOffset.UtcNow.AddDays(1), null, null));
        await store.FinishFocusAsync(session.Id, DateTimeOffset.UtcNow, null, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ResumeFocusAsync(session.Id, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task VersionSevenMigrationCreatesFocusTableWithoutLosingWorkouts()
    {
        var store = new SqliteLocalStore(Db);
        await store.GetWorkoutPlansAsync();
        using (var connection = new SqliteConnection($"Data Source={Db};Pooling=False"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE journal_entry; DROP TABLE dashboard_card_setting; DROP TABLE dream_board_item; DROP TABLE focus_session; PRAGMA user_version=7;";
            await command.ExecuteNonQueryAsync();
        }
        var migrated = new SqliteLocalStore(Db);
        var session = new FocusSession(Guid.NewGuid(), "Leitura", "Pessoal", 15, DateTimeOffset.UtcNow, null, null, 0, null, null, null);
        await migrated.StartFocusAsync(session);
        Assert.Equal(session.Id, Assert.Single(await migrated.GetFocusSessionsAsync()).Id);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
