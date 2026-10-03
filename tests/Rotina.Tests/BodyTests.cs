using Microsoft.Data.Sqlite;
using Rotina.Domain;
using Rotina.Infrastructure;

namespace Rotina.Tests;

public sealed class BodyTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "rotina-body", Guid.NewGuid().ToString());
    private string Db => Path.Combine(directory, "rotina.db");

    [Fact]
    public async Task MeasurementsHaveIndependentChronologicalHistory()
    {
        var store = new SqliteLocalStore(Db);
        var now = DateTimeOffset.UtcNow;
        await store.AddMeasurementAsync(new BodyMeasurement(Guid.NewGuid(), "Cintura", 74.5m, "cm", now.AddDays(-2), null));
        await store.AddMeasurementAsync(new BodyMeasurement(Guid.NewGuid(), "Peito", 91m, "cm", now.AddDays(-1), null));
        await store.AddMeasurementAsync(new BodyMeasurement(Guid.NewGuid(), "Cintura", 75m, "cm", now, "Ao acordar"));
        var all = await new SqliteLocalStore(Db).GetMeasurementsAsync();
        Assert.Equal(new[] { "Cintura", "Peito", "Cintura" }, all.Select(m => m.Name));
        Assert.Equal(75m, all.Last().Value);
        Assert.Equal("Ao acordar", all.Last().Note);
    }

    [Fact]
    public async Task WeightCorrectionPreservesOriginalButExcludesItFromCurrent()
    {
        var store = new SqliteLocalStore(Db);
        var date = DateTimeOffset.UtcNow.AddDays(-1);
        var original = new BodyWeightRecord(Guid.NewGuid(), 63.5m, date, null);
        await store.AddWeightAsync(original);
        var replacement = new BodyWeightRecord(Guid.NewGuid(), 64m, date, "Valor corrigido", correctsId: original.Id);
        await store.CorrectWeightAsync(original.Id, replacement, DateTimeOffset.UtcNow);
        var history = await new SqliteLocalStore(Db).GetWeightsAsync();
        Assert.Equal(2, history.Count);
        Assert.NotNull(history.Single(w => w.Id == original.Id).VoidedAt);
        Assert.Equal(original.Id, history.Single(w => w.Id == replacement.Id).CorrectsId);
        Assert.Equal(64m, history.Last(w => w.VoidedAt is null).Kilograms);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CorrectWeightAsync(original.Id,
            new BodyWeightRecord(Guid.NewGuid(), 65m, date, null, correctsId: original.Id), DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task MeasurementCorrectionKeepsBothVersions()
    {
        var store = new SqliteLocalStore(Db);
        var date = DateTimeOffset.UtcNow.AddDays(-1);
        var original = new BodyMeasurement(Guid.NewGuid(), "Braço", 30m, "cm", date, null);
        await store.AddMeasurementAsync(original);
        var replacement = new BodyMeasurement(Guid.NewGuid(), "Braço", 31m, "cm", date, "Fita ajustada", correctsId: original.Id);
        await store.CorrectMeasurementAsync(original.Id, replacement,
            DateTimeOffset.UtcNow);
        var history = await store.GetMeasurementsAsync();
        Assert.Equal(2, history.Count);
        Assert.NotNull(history.Single(m => m.Id == original.Id).VoidedAt);
        Assert.Equal(31m, history.Single(m => m.Id == replacement.Id).Value);
        Assert.Equal(original.Id, history.Single(m => m.Id == replacement.Id).CorrectsId);
    }

    [Fact]
    public async Task VoidedRecordsStayInHistoryButLeaveCharts()
    {
        var store = new SqliteLocalStore(Db);
        var at = DateTimeOffset.UtcNow.AddDays(-1);
        var weight = new BodyWeightRecord(Guid.NewGuid(), 70m, at, null);
        var measure = new BodyMeasurement(Guid.NewGuid(), "Cintura", 80m, "cm", at, null);
        await store.AddWeightAsync(weight);
        await store.AddMeasurementAsync(measure);
        await store.VoidWeightAsync(weight.Id, DateTimeOffset.UtcNow);
        await store.VoidMeasurementAsync(measure.Id, DateTimeOffset.UtcNow);
        var today = DateOnly.FromDateTime(DateTime.Today);
        Assert.NotNull(Assert.Single(await store.GetWeightsAsync()).VoidedAt);
        Assert.NotNull(Assert.Single(await store.GetMeasurementsAsync()).VoidedAt);
        Assert.Empty(Rotina.Application.ProgressAnalytics.Weights(await store.GetWeightsAsync(), today, 7));
        Assert.Empty(Rotina.Application.ProgressAnalytics.Measurements(await store.GetMeasurementsAsync(), "Cintura", "cm", today, 7));
    }

    [Fact]
    public async Task VersionFiveMigrationPreservesGoalAndWeight()
    {
        var store = new SqliteLocalStore(Db);
        var goal = new Goal(Guid.NewGuid(), "Projeto 80 kg", null, DateOnly.FromDateTime(DateTime.Today), null,
            "kg", 62, 80, null, GoalKind.BodyWeight);
        await store.SaveGoalAsync(goal);
        await store.AddWeightAsync(new BodyWeightRecord(Guid.NewGuid(), 63m, DateTimeOffset.UtcNow, null));
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
                DROP TABLE body_measurement;
                ALTER TABLE body_weight_record DROP COLUMN corrects_id;
                ALTER TABLE body_weight_record DROP COLUMN voided_at;
                PRAGMA user_version=5;
                """;
            await command.ExecuteNonQueryAsync();
        }
        var migrated = new SqliteLocalStore(Db);
        Assert.Equal(goal.Id, Assert.Single(await migrated.GetGoalsAsync()).Id);
        Assert.Equal(63m, Assert.Single(await migrated.GetWeightsAsync()).Kilograms);
        Assert.Empty(await migrated.GetMeasurementsAsync());
    }

    [Fact]
    public async Task InvalidOrFutureMeasurementIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new BodyMeasurement(Guid.NewGuid(), " ", 10, "cm", DateTimeOffset.UtcNow, null));
        var store = new SqliteLocalStore(Db);
        await Assert.ThrowsAsync<ArgumentException>(() => store.AddMeasurementAsync(new BodyMeasurement(Guid.NewGuid(),
            "Cintura", 75, "cm", DateTimeOffset.UtcNow.AddDays(1), null)));
        Assert.Empty(await store.GetMeasurementsAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
