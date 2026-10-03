using Microsoft.Data.Sqlite;
using Rotina.Application;
using Rotina.Domain;
using Rotina.Infrastructure;

namespace Rotina.Tests;

public sealed class WorkoutTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "rotina-workout", Guid.NewGuid().ToString());
    private string Db => Path.Combine(directory, "rotina.db");
    private static WorkoutPlanVersion Plan(DateOnly today, Guid? planId = null, int number = 1) =>
        new(Guid.NewGuid(), planId ?? Guid.NewGuid(), number, "Plano A", today, null,
            [new WorkoutDay(Guid.NewGuid(), today.DayOfWeek, "Treino A", 0,
                [new WorkoutExercise(Guid.NewGuid(), "Agachamento", "Pernas", null, 0, 3, 8, 12, 40)])]);

    [Fact]
    public async Task NewVersionPreservesOldPlanAndSessionSnapshot()
    {
        var store = new SqliteLocalStore(Db);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var first = Plan(today);
        await store.AddWorkoutPlanVersionAsync(first);
        var session = new WorkoutSession(Guid.NewGuid(), first.Id, first.Days[0].Id, null,
            DateTimeOffset.UtcNow.AddMinutes(-10), null, null, []);
        await store.StartWorkoutAsync(session);
        var set = new WorkoutSet(Guid.NewGuid(), first.Days[0].Exercises[0].Id, 1, 8, 12, 42.5m, 10, DateTimeOffset.UtcNow);
        await store.AddWorkoutSetAsync(session.Id, set);
        await store.FinishWorkoutAsync(session.Id, DateTimeOffset.UtcNow, "Bom treino");
        var second = Plan(today, first.PlanId, 2) with { Name = "Plano B" };
        await store.AddWorkoutPlanVersionAsync(second);
        var reopened = new SqliteLocalStore(Db);
        var versions = await reopened.GetWorkoutPlansAsync();
        Assert.Equal(2, versions.Count);
        Assert.Equal(today, versions[0].SupersededOn);
        Assert.Equal("Agachamento", versions[0].Days[0].Exercises[0].Name);
        Assert.Equal(first.Id, Assert.Single(await reopened.GetWorkoutSessionsAsync()).PlanVersionId);
        Assert.Equal(425m, Assert.Single(await reopened.GetWorkoutSessionsAsync()).VolumeKg);
    }

    [Fact]
    public async Task SetsRequireExerciseFromOpenSessionAndCannotDuplicate()
    {
        var store = new SqliteLocalStore(Db);
        var plan = Plan(DateOnly.FromDateTime(DateTime.Today));
        await store.AddWorkoutPlanVersionAsync(plan);
        var session = new WorkoutSession(Guid.NewGuid(), plan.Id, plan.Days[0].Id, null,
            DateTimeOffset.UtcNow.AddMinutes(-5), null, null, []);
        await store.StartWorkoutAsync(session);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StartWorkoutAsync(session with { Id = Guid.NewGuid() }));
        var set = new WorkoutSet(Guid.NewGuid(), Guid.NewGuid(), 1, 8, 12, 40, 10, DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddWorkoutSetAsync(session.Id, set));
        set = set with { ExerciseId = plan.Days[0].Exercises[0].Id };
        await store.AddWorkoutSetAsync(session.Id, set);
        await Assert.ThrowsAsync<SqliteException>(() => store.AddWorkoutSetAsync(session.Id, set with { Id = Guid.NewGuid() }));
        await store.FinishWorkoutAsync(session.Id, DateTimeOffset.UtcNow, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddWorkoutSetAsync(session.Id, set with { Id = Guid.NewGuid(), Number = 2 }));
    }

    [Fact]
    public async Task CyclePausesResumesAndReplacementKeepsHistory()
    {
        var store = new SqliteLocalStore(Db);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var start = today.AddDays(-28);
        var cycle = new WorkoutCycle(Guid.NewGuid(), "Ciclo", start, null, 0, null,
            [new WorkoutPhase(Guid.NewGuid(), "Hipertrofia", 0, 2, 3, 8, 12, null),
             new WorkoutPhase(Guid.NewGuid(), "Força", 1, 2, 4, 3, 5, null),
             new WorkoutPhase(Guid.NewGuid(), "Deload", 2, 1, 2, 10, 15, null)]);
        await store.AddWorkoutCycleAsync(cycle);
        Assert.Equal("Deload", (await store.GetWorkoutCyclesAsync())[0].PhaseOn(today)?.Name);
        await store.PauseWorkoutCycleAsync(cycle.Id, today);
        await store.ResumeWorkoutCycleAsync(cycle.Id, today.AddDays(2));
        Assert.Equal("Deload", (await store.GetWorkoutCyclesAsync())[0].PhaseOn(today.AddDays(2))?.Name);
        var replacement = cycle with { Id = Guid.NewGuid(), StartedOn = today,
            Phases = [new WorkoutPhase(Guid.NewGuid(), "Novo", 0, 1, 3, 6, 8, null)] };
        await store.AddWorkoutCycleAsync(replacement);
        var all = await new SqliteLocalStore(Db).GetWorkoutCyclesAsync();
        Assert.Equal(today, all[0].ArchivedOn);
        Assert.Equal("Novo", all[1].PhaseOn(today)?.Name);
    }

    [Fact]
    public void FrequencyDistinguishesMissedRestAndPending()
    {
        var today = new DateOnly(2026, 9, 25);
        var plan = Plan(today.AddDays(-7)) with
        {
            Days = [new WorkoutDay(Guid.NewGuid(), today.AddDays(-1).DayOfWeek, "Quinta", 0,
                [new WorkoutExercise(Guid.NewGuid(), "Remada", "Costas", null, 0, 3, 8, 12, null)]),
                new WorkoutDay(Guid.NewGuid(), today.DayOfWeek, "Sexta", 1,
                [new WorkoutExercise(Guid.NewGuid(), "Agachamento", "Pernas", null, 0, 3, 8, 12, null)])]
        };
        var report = WorkoutAnalytics.Frequency([plan], [], today, 7);
        Assert.Equal(2, report.Planned);
        Assert.Equal(WorkoutDayState.Missed, report.Days[today.AddDays(-1)]);
        Assert.Equal(WorkoutDayState.Pending, report.Days[today]);
        Assert.Equal(5, report.Rest);
    }

    [Fact]
    public async Task VersionSixMigrationPreservesMeasurements()
    {
        var store = new SqliteLocalStore(Db);
        var measure = new BodyMeasurement(Guid.NewGuid(), "Cintura", 80, "cm", DateTimeOffset.UtcNow, null);
        await store.AddMeasurementAsync(measure);
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
                PRAGMA user_version=6;
                """;
            await command.ExecuteNonQueryAsync();
        }
        var migrated = new SqliteLocalStore(Db);
        Assert.Equal(measure.Id, Assert.Single(await migrated.GetMeasurementsAsync()).Id);
        Assert.Empty(await migrated.GetWorkoutPlansAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
