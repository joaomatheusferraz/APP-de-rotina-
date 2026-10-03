using Microsoft.Data.Sqlite;
using Rotina.Domain;
using Rotina.Infrastructure;

namespace Rotina.Tests;

public sealed class GoalTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "rotina-goal", Guid.NewGuid().ToString());
    private string Db => Path.Combine(directory, "rotina.db");

    [Fact]
    public async Task FreshDatabaseDoesNotCreateProjectOrWeight()
    {
        var store = new SqliteLocalStore(Db);
        Assert.Empty(await store.GetGoalsAsync());
        Assert.Empty(await store.GetWeightsAsync());
        Assert.Empty(await store.GetActionCardsAsync());
    }

    [Fact]
    public async Task GeneralGoalKeepsProgressEventsAfterCompletionAndReopen()
    {
        var store = new SqliteLocalStore(Db);
        var goal = new Goal(Guid.NewGuid(), "Ler", null, DateOnly.FromDateTime(DateTime.Today), null,
            "livros", 0, 12, null, GoalKind.Numeric);
        await store.SaveGoalAsync(goal);
        await store.AddGoalProgressAsync(goal.Id, 3, DateTimeOffset.UtcNow.AddMinutes(-1), "Primeiro mês");
        await store.AddGoalProgressAsync(goal.Id, 5, DateTimeOffset.UtcNow, null);
        await store.SetGoalCompletedAsync(goal.Id, DateTimeOffset.UtcNow);
        var reopened = new SqliteLocalStore(Db);
        var saved = Assert.Single(await reopened.GetGoalsAsync());
        Assert.True(saved.IsArchived);
        Assert.NotNull(saved.CompletedAt);
        Assert.Equal(2, (await reopened.GetGoalProgressAsync(goal.Id)).Count);
        Assert.Equal(5m, (await reopened.GetGoalProgressAsync(goal.Id)).Last().Value);
        await reopened.SetGoalCompletedAsync(goal.Id, null);
        Assert.False(Assert.Single(await store.GetGoalsAsync()).IsArchived);
        Assert.Null(Assert.Single(await store.GetGoalsAsync()).CompletedAt);
    }

    [Fact]
    public async Task BodyWeightIsIndependentOfGoalAndSupportsProgressBothDirections()
    {
        var store = new SqliteLocalStore(Db);
        var project = new Goal(Guid.NewGuid(), "Projeto 80 kg", null, DateOnly.FromDateTime(DateTime.Today),
            null, "kg", 62, 80, null, GoalKind.BodyWeight);
        await store.SaveGoalAsync(project);
        await store.AddWeightAsync(new BodyWeightRecord(Guid.NewGuid(), 63.5m, DateTimeOffset.UtcNow, null));
        Assert.Equal(63.5m, Assert.Single(await new SqliteLocalStore(Db).GetWeightsAsync()).Kilograms);
        Assert.InRange(project.Progress(63.5m)!.Value, 0.08, 0.09);
        var loss = new Goal(Guid.NewGuid(), "Reduzir", null, DateOnly.FromDateTime(DateTime.Today), null,
            "kg", 90, 80, null, GoalKind.BodyWeight);
        Assert.Equal(0.5, loss.Progress(85));
        var withoutBaseline = new Goal(Guid.NewGuid(), "Peso", null, DateOnly.FromDateTime(DateTime.Today), null,
            "kg", null, 80, null, GoalKind.BodyWeight);
        Assert.Null(withoutBaseline.Progress(70));
        await store.SetGoalArchivedAsync(project.Id, true);
        Assert.Single(await store.GetWeightsAsync());
    }

    [Fact]
    public async Task LinksRequireExistingHabitOrTask()
    {
        var store = new SqliteLocalStore(Db);
        var date = DateOnly.FromDateTime(DateTime.Today);
        var goal = new Goal(Guid.NewGuid(), "Estudar", null, date, null, "horas", 0, 100,
            null, GoalKind.Numeric);
        await store.SaveGoalAsync(goal);
        var habitId = Guid.NewGuid();
        await store.CreateHabitAsync(new HabitRevision(Guid.NewGuid(), habitId, "Ler", null,
            date, HabitFrequency.Daily, 0, 0, false), date);
        var link = new GoalLink(goal.Id, GoalLinkKind.Habit, habitId);
        await store.AddGoalLinkAsync(link);
        await store.AddGoalLinkAsync(link);
        Assert.Single(await store.GetGoalLinksAsync(goal.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => store.AddGoalLinkAsync(new GoalLink(goal.Id, GoalLinkKind.Task, Guid.NewGuid())));
        await store.RemoveGoalLinkAsync(link);
        Assert.Empty(await store.GetGoalLinksAsync(goal.Id));
    }

    [Fact]
    public async Task KanbanMovesKeepHistoryAndEditsDoNotAddFalseMove()
    {
        var store = new SqliteLocalStore(Db);
        var lanes = await store.GetActionLanesAsync();
        Assert.Equal(new[] { "start", "doing", "done" }, lanes.Select(l => l.Id));
        var id = Guid.NewGuid();
        await store.SaveActionCardAsync(new ActionCard(id, "Revisar CV", null, "carreira", null, null, null, "start"));
        await store.SaveActionCardAsync(new ActionCard(id, "Revisar currículo", null, "carreira", null, null, null, "start"));
        await store.MoveActionCardAsync(id, "doing", DateTimeOffset.UtcNow);
        await store.MoveActionCardAsync(id, "doing", DateTimeOffset.UtcNow);
        await store.MoveActionCardAsync(id, "done", DateTimeOffset.UtcNow);
        var reopened = new SqliteLocalStore(Db);
        Assert.Equal("done", Assert.Single(await reopened.GetActionCardsAsync()).LaneId);
        Assert.Equal(new[] { "start", "doing", "done" }, (await reopened.GetActionMovesAsync(id)).Select(m => m.LaneId));
        await reopened.SetActionCardArchivedAsync(id, true);
        Assert.True(Assert.Single(await store.GetActionCardsAsync()).IsArchived);
    }

    [Fact]
    public async Task MigrationFromVersionFourPreservesExistingTasks()
    {
        var store = new SqliteLocalStore(Db);
        var task = new PlannedTask(Guid.NewGuid(), "Existente", null, null, TaskPriority.Normal, null, null);
        await store.SaveTaskAsync(task);
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
                PRAGMA user_version=4;
                """;
            await command.ExecuteNonQueryAsync();
        }
        var migrated = new SqliteLocalStore(Db);
        Assert.Equal(task.Id, Assert.Single(await migrated.GetTasksAsync()).Id);
        Assert.Empty(await migrated.GetGoalsAsync());
        Assert.Equal(3, (await migrated.GetActionLanesAsync()).Count);
    }

    [Fact]
    public async Task InvalidValuesAndFutureWeightDoNotPersist()
    {
        var store = new SqliteLocalStore(Db);
        Assert.Throws<ArgumentException>(() => new Goal(Guid.NewGuid(), "Meta", null,
            DateOnly.FromDateTime(DateTime.Today), null, "kg", 80, 80, null, GoalKind.BodyWeight));
        await Assert.ThrowsAsync<ArgumentException>(() => store.AddWeightAsync(new BodyWeightRecord(Guid.NewGuid(), 70,
            DateTimeOffset.UtcNow.AddDays(1), null)));
        Assert.Empty(await store.GetWeightsAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
