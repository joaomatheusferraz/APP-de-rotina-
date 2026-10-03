using Microsoft.Data.Sqlite;
using Rotina.Application;
using Rotina.Domain;
using Rotina.Infrastructure;

namespace Rotina.Tests;

public sealed class FoundationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "rotina-tests", Guid.NewGuid().ToString());
    private string DatabasePath => Path.Combine(directory, "rotina.db");

    [Fact]
    public async Task FreshDatabaseStartsWithoutInventedPersonalData()
    {
        var store = new SqliteLocalStore(DatabasePath);
        Assert.Equal(UserProfile.Default, await store.GetProfileAsync());
        Assert.Empty(await store.GetAreasAsync());
    }

    [Fact]
    public async Task ProfileSurvivesReopeningAndRepeatedInitialization()
    {
        var profile = new UserProfile(" Maria ", ThemePreference.Light);
        await new SqliteLocalStore(DatabasePath).SaveProfileAsync(profile);
        var reopened = new SqliteLocalStore(DatabasePath);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => reopened.InitializeAsync()));
        Assert.Equal(profile, await reopened.GetProfileAsync());
        Assert.Equal("Maria", (await reopened.GetProfileAsync()).DisplayName);
    }

    [Fact]
    public async Task EditingAndArchivingPreserveAreaIdentity()
    {
        var store = new SqliteLocalStore(DatabasePath);
        var id = Guid.NewGuid();
        await store.SaveAreaAsync(new(id, "Saúde", "#62d6a5", 0));
        await store.SaveAreaAsync(new(id, "Bem-estar", "#AABBCC", 2, true));
        var area = Assert.Single(await new SqliteLocalStore(DatabasePath).GetAreasAsync());
        Assert.Equal(id, area.Id);
        Assert.Equal("Bem-estar", area.Name);
        Assert.True(area.IsArchived);
    }

    [Fact]
    public async Task NamesAreStoredAsDataAndAreasAreOrdered()
    {
        var store = new SqliteLocalStore(DatabasePath);
        await store.SaveAreaAsync(new(Guid.NewGuid(), "'); DROP TABLE life_area; --", "#AABBCC", 2));
        await store.SaveAreaAsync(new(Guid.NewGuid(), "Estudos", "#FFFFFF", 0));
        var areas = await store.GetAreasAsync();
        Assert.Equal(2, areas.Count);
        Assert.Equal("Estudos", areas[0].Name);
    }

    [Fact]
    public async Task UnknownSchemaIsRejectedWithoutOverwritingData()
    {
        await new SqliteLocalStore(DatabasePath).SaveProfileAsync(new("Preservado", ThemePreference.Dark));
        using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version=99;";
        await command.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SqliteLocalStore(DatabasePath).InitializeAsync());
        command.CommandText = "SELECT display_name FROM user_profile;";
        Assert.Equal("Preservado", await command.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData("", "#123456")]
    [InlineData("   ", "#123456")]
    [InlineData("Saúde", "red")]
    [InlineData("Saúde", "#GGGGGG")]
    public void InvalidAreaIsRejected(string name, string color) =>
        Assert.Throws<ArgumentException>(() => new LifeArea(Guid.NewGuid(), name, color, 0));

    [Fact]
    public void LocalDayUsesUsersTimeZoneRatherThanUtcDate()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Manaus-test", TimeSpan.FromHours(-4), "Manaus", "Manaus");
        Assert.Equal(new DateOnly(2026, 9, 24), LocalDate.At(DateTimeOffset.Parse("2026-09-25T02:30:00Z"), zone));
        Assert.Equal(new DateOnly(2026, 9, 25), LocalDate.At(DateTimeOffset.Parse("2026-09-25T04:00:00Z"), zone));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
