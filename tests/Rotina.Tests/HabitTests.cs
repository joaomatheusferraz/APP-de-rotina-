using Microsoft.Data.Sqlite;
using Rotina.Domain;
using Rotina.Infrastructure;

namespace Rotina.Tests;

public sealed class HabitTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "rotina-habits", Guid.NewGuid().ToString());
    private string PathToDb => Path.Combine(directory, "habits.db");
    private static readonly Guid HabitId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RevisionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateOnly Monday = new(2026, 1, 5);

    private static HabitRevision Revision(DateOnly start, HabitFrequency frequency, int mask = 0, int target = 0,
        bool paused = false, long sequence = 1) =>
        new(RevisionId, HabitId, "Treinar", null, start, frequency, mask, target, paused, sequence);

    private static HabitEvent Done(DateOnly date, long sequence = 1) =>
        new(sequence, HabitId, date, RevisionId, true, null, DateTimeOffset.UtcNow);

    [Fact]
    public void RestDaysDoNotBreakScheduledWorkoutStreak()
    {
        var mask = (1 << (int)DayOfWeek.Monday) | (1 << (int)DayOfWeek.Wednesday) | (1 << (int)DayOfWeek.Friday);
        var history = new HabitHistory(HabitId, Monday, null, [Revision(Monday, HabitFrequency.Weekdays, mask)],
            [Done(Monday), Done(Monday.AddDays(2), 2)]);
        var report = HabitAnalytics.Build(history, Monday.AddDays(3));
        Assert.Equal(HabitDayState.Rest, report.Days[Monday.AddDays(1)]);
        Assert.Equal(HabitDayState.Rest, report.Days[Monday.AddDays(3)]);
        Assert.Equal(2, report.CurrentStreak);
        Assert.Equal(new HabitScore(2, 2), report.Last7);
    }

    [Fact]
    public void OpenDayDoesNotBreakStreakButMissedScheduledDayDoes()
    {
        var history = new HabitHistory(HabitId, Monday, null, [Revision(Monday, HabitFrequency.Daily)], [Done(Monday)]);
        Assert.Equal(1, HabitAnalytics.Build(history, Monday.AddDays(1)).CurrentStreak);
        Assert.Equal(HabitDayState.ScheduledPending, HabitAnalytics.Day(history, Monday.AddDays(1), Monday.AddDays(1)));
        Assert.Equal(0, HabitAnalytics.Build(history, Monday.AddDays(2)).CurrentStreak);
        Assert.Equal(1, HabitAnalytics.Build(history, Monday.AddDays(2)).BestStreak);
    }

    [Fact]
    public void PausedDaysPreserveStreakAndAreNotOpportunities()
    {
        var paused = new HabitRevision(Guid.NewGuid(), HabitId, "Treinar", null, Monday.AddDays(1), HabitFrequency.Daily, 0, 0, true, 2);
        var resumed = new HabitRevision(Guid.NewGuid(), HabitId, "Treinar", null, Monday.AddDays(3), HabitFrequency.Daily, 0, 0, false, 3);
        var history = new HabitHistory(HabitId, Monday, null, [Revision(Monday, HabitFrequency.Daily), paused, resumed],
            [Done(Monday), Done(Monday.AddDays(3), 2)]);
        var report = HabitAnalytics.Build(history, Monday.AddDays(3));
        Assert.Equal(HabitDayState.Paused, report.Days[Monday.AddDays(2)]);
        Assert.Equal(new HabitScore(2, 2), report.Last7);
        Assert.Equal(2, report.CurrentStreak);
    }

    [Fact]
    public void FlexibleWeeklyGoalCountsWeeksAndWaitsUntilWeekEnds()
    {
        var history = new HabitHistory(HabitId, Monday, null, [Revision(Monday, HabitFrequency.TimesPerWeek, target: 2)],
            [Done(Monday), Done(Monday.AddDays(2), 2)]);
        Assert.Equal(HabitDayState.Rest, HabitAnalytics.Day(history, Monday.AddDays(3), Monday.AddDays(3)));
        Assert.Equal(1, HabitAnalytics.Build(history, Monday.AddDays(10)).CurrentStreak);
        Assert.Equal(new HabitScore(2, 4), HabitAnalytics.Build(history, Monday.AddDays(10)).Last30);
        var failure = new HabitHistory(HabitId, Monday, null, [Revision(Monday, HabitFrequency.TimesPerWeek, target: 2)], [Done(Monday)]);
        Assert.Equal(HabitDayState.Missed, HabitAnalytics.Day(failure, Monday.AddDays(6), Monday.AddDays(7)));
        Assert.Equal(0, HabitAnalytics.Build(failure, Monday.AddDays(7)).CurrentStreak);
    }

    [Fact]
    public void MidweekScheduleChangePreservesBothSegments()
    {
        var daily = new HabitRevision(Guid.NewGuid(), HabitId, "Treinar", null, Monday.AddDays(3),
            HabitFrequency.Daily, 0, 0, false, 2);
        var history = new HabitHistory(HabitId, Monday, null,
            [Revision(Monday, HabitFrequency.TimesPerWeek, target: 2), daily],
            [Done(Monday), Done(Monday.AddDays(2), 2), Done(Monday.AddDays(3), 3)]);
        var report = HabitAnalytics.Build(history, Monday.AddDays(3));
        Assert.Equal(new HabitScore(3, 3), report.Last7);
        Assert.Equal(2, report.CurrentStreak);
        Assert.Equal(HabitDayState.Completed, report.Days[Monday.AddDays(3)]);
    }

    [Fact]
    public async Task CompletionAndCorrectionAreSeparateEventsAndSurviveReopen()
    {
        var store = new SqliteLocalStore(PathToDb);
        var today = DateOnly.FromDateTime(DateTime.Now);
        await store.CreateHabitAsync(Revision(today, HabitFrequency.Daily), today);
        await store.RecordHabitAsync(HabitId, today, true, "Bom dia", DateTimeOffset.UtcNow);
        await store.RecordHabitAsync(HabitId, today, false, null, DateTimeOffset.UtcNow);
        await store.RecordHabitAsync(HabitId, today, false, null, DateTimeOffset.UtcNow);
        var history = Assert.Single(await new SqliteLocalStore(PathToDb).GetHabitsAsync());
        Assert.Equal(2, history.Events.Count);
        Assert.False(history.CompletedOn(today));
        Assert.Equal("Bom dia", history.Events[0].Note);
    }

    [Fact]
    public async Task CompletionNoteAddsAuditEventWithoutChangingCompletedState()
    {
        var store = new SqliteLocalStore(PathToDb);
        var today = DateOnly.FromDateTime(DateTime.Now);
        await store.CreateHabitAsync(Revision(today, HabitFrequency.Daily), today);
        await store.RecordHabitAsync(HabitId, today, true, null, DateTimeOffset.UtcNow);
        await store.RecordHabitAsync(HabitId, today, true, "Dormiu bem", DateTimeOffset.UtcNow);
        var history = Assert.Single(await store.GetHabitsAsync());
        Assert.True(history.CompletedOn(today));
        Assert.Equal(2, history.Events.Count);
        Assert.Equal("Dormiu bem", history.Events[^1].Note);
    }

    [Fact]
    public async Task FutureCompletionIsRejectedWithoutEvent()
    {
        var store = new SqliteLocalStore(PathToDb);
        var today = DateOnly.FromDateTime(DateTime.Now);
        await store.CreateHabitAsync(Revision(today, HabitFrequency.Daily), today);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.RecordHabitAsync(HabitId, today.AddDays(1), true, null, DateTimeOffset.UtcNow));
        Assert.Empty(Assert.Single(await store.GetHabitsAsync()).Events);
    }

    [Fact]
    public async Task EditingScheduleKeepsEarlierRevisionAndCompletion()
    {
        var store = new SqliteLocalStore(PathToDb);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var yesterday = today.AddDays(-1);
        await store.CreateHabitAsync(Revision(yesterday, HabitFrequency.Daily), yesterday);
        await store.RecordHabitAsync(HabitId, yesterday, true, null, DateTimeOffset.UtcNow);
        await store.ReviseHabitAsync(new(Guid.NewGuid(), HabitId, "Treinar melhor", null, today,
            HabitFrequency.Weekdays, 1 << (int)today.DayOfWeek, 0, false));
        var history = Assert.Single(await new SqliteLocalStore(PathToDb).GetHabitsAsync());
        Assert.Equal(2, history.Revisions.Count);
        Assert.Equal("Treinar", history.RevisionOn(yesterday)!.Name);
        Assert.Equal("Treinar melhor", history.RevisionOn(today)!.Name);
        Assert.True(history.CompletedOn(yesterday));
        Assert.Equal(1, HabitAnalytics.Build(history, today).CurrentStreak);
    }

    [Fact]
    public async Task ArchiveAndRestorePreserveInactiveInterval()
    {
        var store = new SqliteLocalStore(PathToDb);
        await store.CreateHabitAsync(Revision(Monday, HabitFrequency.Daily), Monday);
        await store.SetHabitArchivedAsync(HabitId, Monday.AddDays(2));
        await store.SetHabitArchivedAsync(HabitId, null);
        var history = Assert.Single(await new SqliteLocalStore(PathToDb).GetHabitsAsync());
        Assert.True(history.IsActiveOn(Monday));
        Assert.False(history.IsActiveOn(Monday.AddDays(3)));
        Assert.True(history.IsActiveOn(DateOnly.FromDateTime(DateTime.Now)));
        Assert.Equal(2, history.StatusEvents!.Count);
    }

    [Fact]
    public async Task ExistingVersionOneDatabaseMigratesWithoutLosingProfile()
    {
        Directory.CreateDirectory(directory);
        using (var connection = new SqliteConnection($"Data Source={PathToDb};Pooling=False"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE user_profile(id INTEGER PRIMARY KEY,display_name TEXT NOT NULL,theme INTEGER NOT NULL);
                INSERT INTO user_profile VALUES(1,'Pessoa',1);
                CREATE TABLE life_area(id TEXT PRIMARY KEY,name TEXT NOT NULL,color_hex TEXT NOT NULL,sort_order INTEGER NOT NULL,is_archived INTEGER NOT NULL);
                PRAGMA user_version=1;
                """;
            await command.ExecuteNonQueryAsync();
        }
        var store = new SqliteLocalStore(PathToDb);
        Assert.Equal("Pessoa", (await store.GetProfileAsync()).DisplayName);
        Assert.Empty(await store.GetHabitsAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
