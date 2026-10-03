using Microsoft.Data.Sqlite;
using Rotina.Domain;
using Rotina.Infrastructure;

namespace Rotina.Tests;

public sealed class PlanningTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "rotina-planning", Guid.NewGuid().ToString());
    private string Db => Path.Combine(directory, "rotina.db");

    [Fact]
    public async Task TaskIsOneOffAndCanBeCompletedReopenedAndEdited()
    {
        var store = new SqliteLocalStore(Db);
        var id = Guid.NewGuid();
        var due = DateOnly.FromDateTime(DateTime.Today.AddDays(2));
        var task = new PlannedTask(id, "Consulta", "Levar exames", due, TaskPriority.High, null, null);
        await store.SaveTaskAsync(task);
        await store.SetTaskCompletedAsync(id, DateTimeOffset.UtcNow);
        var done = Assert.Single(await new SqliteLocalStore(Db).GetTasksAsync());
        Assert.NotNull(done.CompletedAt);
        Assert.Equal(due, done.DueOn);
        await store.SaveTaskAsync(new PlannedTask(id, "Consulta alterada", null, due, TaskPriority.Normal,
            null, null, done.CompletedAt));
        Assert.Equal("Consulta alterada", Assert.Single(await store.GetTasksAsync()).Title);
        await store.SetTaskCompletedAsync(id, null);
        Assert.Null(Assert.Single(await store.GetTasksAsync()).CompletedAt);
        await store.SetTaskArchivedAsync(id, true);
        Assert.True(Assert.Single(await store.GetTasksAsync()).IsArchived);
        await store.SetTaskArchivedAsync(id, false);
        Assert.False(Assert.Single(await store.GetTasksAsync()).IsArchived);
    }

    [Fact]
    public async Task AppointmentKeepsInstantAcrossReopenAndArchive()
    {
        var store = new SqliteLocalStore(Db);
        var start = new DateTimeOffset(2026, 10, 12, 16, 30, 0, TimeSpan.FromHours(-4));
        var item = new Appointment(Guid.NewGuid(), "Dentista", start, 45, "Clínica", "Retorno",
            null, start.AddHours(-1));
        await store.SaveAppointmentAsync(item);
        var reopened = new SqliteLocalStore(Db);
        var saved = Assert.Single(await reopened.GetAppointmentsAsync());
        Assert.Equal(start, saved.StartsAt);
        Assert.Equal(start.AddHours(-1), saved.ReminderAt);
        await reopened.SetAppointmentArchivedAsync(item.Id, true);
        Assert.True(Assert.Single(await store.GetAppointmentsAsync()).IsArchived);
        await reopened.SetAppointmentArchivedAsync(item.Id, false);
        Assert.False(Assert.Single(await store.GetAppointmentsAsync()).IsArchived);
    }

    [Fact]
    public async Task VersionThreeMigrationPreservesDailyAndHabitData()
    {
        var store = new SqliteLocalStore(Db);
        var today = DateOnly.FromDateTime(DateTime.Today);
        await store.SaveProfileAsync(new UserProfile("Pessoa", ThemePreference.Dark));
        await store.AddWaterAsync(today, 250, DateTimeOffset.UtcNow);
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
                DROP TABLE action_move_event;
                DROP TABLE action_card;
                DROP TABLE action_lane;
                DROP TABLE goal_link;
                DROP TABLE goal_progress_event;
                DROP TABLE body_weight_record;
                DROP TABLE body_measurement;
                DROP TABLE goal;
                DROP TABLE planned_task;
                DROP TABLE appointment;
                PRAGMA user_version=3;
                """;
            await command.ExecuteNonQueryAsync();
        }
        var migrated = new SqliteLocalStore(Db);
        Assert.Equal("Pessoa", (await migrated.GetProfileAsync()).DisplayName);
        Assert.Equal(250, (await migrated.GetDailyRecordAsync(today)).WaterMl);
        Assert.Empty(await migrated.GetTasksAsync());
        Assert.Empty(await migrated.GetAppointmentsAsync());
    }

    [Fact]
    public void InvalidRemindersAndDurationsAreRejected()
    {
        var start = DateTimeOffset.Now.AddDays(2);
        Assert.Throws<ArgumentException>(() => new PlannedTask(Guid.NewGuid(), "Tarefa", null, null,
            TaskPriority.Normal, null, start));
        Assert.Throws<ArgumentException>(() => new Appointment(Guid.NewGuid(), "Evento", start, 0,
            null, null, null, null));
        Assert.Throws<ArgumentException>(() => new Appointment(Guid.NewGuid(), "Evento", start, 60,
            null, null, null, start.AddMinutes(1)));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
