using Microsoft.Data.Sqlite;
using Rotina.Domain;
using Rotina.Infrastructure;

namespace Rotina.Tests;

public sealed class DailyTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "rotina-daily", Guid.NewGuid().ToString());
    private string Db => Path.Combine(directory, "rotina.db");

    [Fact]
    public async Task NewDayDoesNotErasePreviousCheckInOrWater()
    {
        var store = new SqliteLocalStore(Db);
        var yesterday = DateOnly.FromDateTime(DateTime.Now).AddDays(-1);
        var today = yesterday.AddDays(1);
        await store.SaveDailyPreferencesAsync(new DailyPreferences(2000, 5));
        await store.SaveCheckInAsync(new DailyCheckIn(yesterday, new TimeOnly(7, 15), 450, 4, 3,
            120, "Dormiu cedo", 1800), DateTimeOffset.UtcNow);
        await store.AddWaterAsync(yesterday, 250, DateTimeOffset.UtcNow);
        var reopened = new SqliteLocalStore(Db);
        var previous = await reopened.GetDailyRecordAsync(yesterday);
        var current = await reopened.GetDailyRecordAsync(today);
        Assert.Equal(250, previous.WaterMl);
        Assert.Equal(1800, previous.CheckIn.WaterTargetMl);
        Assert.Equal(new TimeOnly(7, 15), previous.CheckIn.WokeAt);
        Assert.Equal("Dormiu cedo", previous.CheckIn.Note);
        Assert.Equal(0, current.WaterMl);
        Assert.Null(current.CheckIn.WokeAt);
        Assert.Equal(2000, current.CheckIn.WaterTargetMl);
        Assert.Single(await reopened.GetDailyHistoryAsync(yesterday, today));
    }

    [Fact]
    public async Task WaterQuickAddsAndCorrectionAreAuditable()
    {
        var store = new SqliteLocalStore(Db);
        var today = DateOnly.FromDateTime(DateTime.Now);
        await store.AddWaterAsync(today, 250, DateTimeOffset.UtcNow);
        await store.AddWaterAsync(today, 500, DateTimeOffset.UtcNow);
        await store.AddWaterAsync(today, -250, DateTimeOffset.UtcNow);
        var record = await new SqliteLocalStore(Db).GetDailyRecordAsync(today);
        Assert.Equal(500, record.WaterMl);
        Assert.Equal(new[] { 250, 500, -250 }, record.WaterEvents.Select(e => e.DeltaMl));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.AddWaterAsync(today, -750, DateTimeOffset.UtcNow));
        Assert.Equal(500, (await store.GetDailyRecordAsync(today)).WaterMl);
    }

    [Fact]
    public async Task WaterFirstRecordedTodayKeepsTargetWhenDefaultChanges()
    {
        var store = new SqliteLocalStore(Db);
        var today = DateOnly.FromDateTime(DateTime.Now);
        await store.SaveDailyPreferencesAsync(new DailyPreferences(1500, 5));
        await store.AddWaterAsync(today, 250, DateTimeOffset.UtcNow);
        await store.SaveDailyPreferencesAsync(new DailyPreferences(2500, 10));
        var record = await new SqliteLocalStore(Db).GetDailyRecordAsync(today);
        Assert.Equal(1500, record.CheckIn.WaterTargetMl);
        Assert.Equal(5, record.CheckIn.MoodScaleMax);
        Assert.Equal(250, record.WaterMl);
    }

    [Fact]
    public async Task EditingCheckInKeepsPreviousRevisionAndMoodScale()
    {
        var store = new SqliteLocalStore(Db);
        var today = DateOnly.FromDateTime(DateTime.Now);
        await store.SaveCheckInAsync(new DailyCheckIn(today, mood: 8, moodScaleMax: 10), DateTimeOffset.UtcNow);
        await store.SaveDailyPreferencesAsync(new DailyPreferences(null, 5));
        await store.SaveCheckInAsync(new DailyCheckIn(today, mood: 4, moodScaleMax: 5), DateTimeOffset.UtcNow);
        var record = await store.GetDailyRecordAsync(today);
        Assert.Equal(4, record.CheckIn.Mood);
        Assert.Equal(5, record.CheckIn.MoodScaleMax);
        using var connection = new SqliteConnection($"Data Source={Db};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM daily_checkin_revision WHERE local_date=$date;";
        command.Parameters.AddWithValue("$date", today.ToString("yyyy-MM-dd"));
        Assert.Equal(2L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task ClosingCapturesSummaryAndReopeningPreservesIt()
    {
        var store = new SqliteLocalStore(Db);
        var today = DateOnly.FromDateTime(DateTime.Now);
        await store.AddWaterAsync(today, 500, DateTimeOffset.UtcNow);
        await store.CloseDayAsync(new DailyClosure(0, today, true, 2, 3, 500, 2000,
            null, null, 4, "Dia bom", DateTimeOffset.UtcNow));
        await store.AddWaterAsync(today, 250, DateTimeOffset.UtcNow);
        await store.CloseDayAsync(new DailyClosure(0, today, false, 2, 3, 750, 2000,
            null, null, null, null, DateTimeOffset.UtcNow));
        var record = await new SqliteLocalStore(Db).GetDailyRecordAsync(today);
        Assert.Equal(750, record.WaterMl);
        Assert.False(record.IsClosed);
        Assert.Equal(2, record.Closures.Count);
        Assert.Equal(500, record.Closures[0].WaterMl);
        Assert.Equal("Dia bom", record.Closures[0].Note);
    }

    [Fact]
    public async Task VersionTwoMigrationPreservesExistingProfileAndHabit()
    {
        var store = new SqliteLocalStore(Db);
        var today = DateOnly.FromDateTime(DateTime.Now);
        await store.SaveProfileAsync(new UserProfile("Pessoa", ThemePreference.Dark));
        var id = Guid.NewGuid();
        await store.CreateHabitAsync(new HabitRevision(Guid.NewGuid(), id, "Estudar", null,
            today, HabitFrequency.Daily, 0, 0, false), today);
        using (var connection = new SqliteConnection($"Data Source={Db};Pooling=False"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DROP TABLE journal_entry; DROP TABLE dashboard_card_setting; DROP TABLE dream_board_item;
                DROP TABLE focus_session;
                DROP TABLE workout_set;
                DROP TABLE workout_session;
                DROP TABLE workout_phase;
                DROP TABLE workout_cycle;
                DROP TABLE workout_exercise;
                DROP TABLE workout_day;
                DROP TABLE workout_plan_version;
                DROP TABLE daily_close_event;
                DROP TABLE water_event;
                DROP TABLE daily_checkin_revision;
                DROP TABLE daily_preferences;
                DROP TABLE planned_task;
                DROP TABLE appointment;
                DROP TABLE action_move_event;
                DROP TABLE action_card;
                DROP TABLE action_lane;
                DROP TABLE goal_link;
                DROP TABLE goal_progress_event;
                DROP TABLE body_weight_record;
                DROP TABLE body_measurement;
                DROP TABLE goal;
                PRAGMA user_version=2;
                """;
            await command.ExecuteNonQueryAsync();
        }
        var migrated = new SqliteLocalStore(Db);
        Assert.Equal("Pessoa", (await migrated.GetProfileAsync()).DisplayName);
        Assert.Equal(id, Assert.Single(await migrated.GetHabitsAsync()).Id);
        Assert.Equal(DailyPreferences.Default, await migrated.GetDailyPreferencesAsync());
    }

    [Fact]
    public void DailyValuesAreValidated()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        Assert.Throws<ArgumentOutOfRangeException>(() => new DailyCheckIn(today, sleepMinutes: 1500));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DailyCheckIn(today, mood: 6, moodScaleMax: 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DailyCheckIn(today, waterTargetMl: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DailyPreferences(1000, 7));
    }

    [Fact]
    public async Task FutureCheckInAndClosureAreRejected()
    {
        var store = new SqliteLocalStore(Db);
        var tomorrow = DateOnly.FromDateTime(DateTime.Now).AddDays(1);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveCheckInAsync(new DailyCheckIn(tomorrow), DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<ArgumentException>(() => store.CloseDayAsync(new DailyClosure(0, tomorrow,
            true, 0, 0, 0, null, null, null, null, null, DateTimeOffset.UtcNow)));
        Assert.Empty(await store.GetDailyHistoryAsync(tomorrow, tomorrow));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
